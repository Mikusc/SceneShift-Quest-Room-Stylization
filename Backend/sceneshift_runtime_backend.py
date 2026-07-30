#!/usr/bin/env python3
"""Minimal SceneShift runtime generation backend.

This service is intentionally outside the Unity APK. It accepts Quest runtime
generation submissions, owns provider credentials through environment variables,
polls the 3D provider server-side, and returns only model URLs/results to Quest.
"""

from __future__ import annotations

import base64
import hashlib
import http.server
import json
import mimetypes
import os
import re
import secrets
import shutil
import sys
import threading
import time
import traceback
import urllib.error
import urllib.parse
import urllib.request
import zipfile
from datetime import datetime, timezone
from email.parser import BytesParser
from email.policy import default
from pathlib import Path
from typing import Any


STATE_FAILED = 5
STATE_RUNTIME_BACKEND_SUBMITTED = 9
STATE_RUNTIME_MODEL_READY = 10

DEFAULT_ENDPOINT = "https://ark.cn-beijing.volces.com/api/v3/contents/generations/tasks"
DEFAULT_MODEL = "doubao-seed3d-2-0-260328"


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")


def env(name: str, default: str = "") -> str:
    return os.environ.get(name, default).strip()


def project_root() -> Path:
    return Path(__file__).resolve().parents[1]


def job_root() -> Path:
    configured = env("SCENESHIFT_BACKEND_JOB_DIR")
    root = Path(configured).expanduser() if configured else project_root() / "Library" / "RuntimeBackendJobs"
    root.mkdir(parents=True, exist_ok=True)
    return root


def safe_id(value: str) -> str:
    cleaned = re.sub(r"[^A-Za-z0-9_.-]+", "-", value or "job").strip("-._")
    return cleaned[:80] or "job"


def read_json(path: Path) -> dict[str, Any]:
    if not path.exists():
        return {}
    return json.loads(path.read_text(encoding="utf-8"))


def write_json(path: Path, payload: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")


def update_job(job_dir: Path, **changes: Any) -> dict[str, Any]:
    job = read_json(job_dir / "job.json")
    job.update(changes)
    job["updated_at"] = utc_now()
    write_json(job_dir / "job.json", job)
    return job


def public_base_url(handler: http.server.BaseHTTPRequestHandler | None = None) -> str:
    configured = env("SCENESHIFT_PUBLIC_BASE_URL")
    if configured:
        return configured.rstrip("/")
    host = handler.headers.get("Host", "") if handler else f"127.0.0.1:{env('SCENESHIFT_BACKEND_PORT', '8787')}"
    return f"http://{host}".rstrip("/")


def status_url(job_id: str, handler: http.server.BaseHTTPRequestHandler | None = None) -> str:
    return f"{public_base_url(handler)}/v1/runtime-generations/{urllib.parse.quote(job_id)}"


def file_url(job_id: str, filename: str, handler: http.server.BaseHTTPRequestHandler | None = None) -> str:
    quoted_job = urllib.parse.quote(job_id)
    quoted_name = urllib.parse.quote(filename)
    return f"{public_base_url(handler)}/v1/runtime-generations/{quoted_job}/files/{quoted_name}"


def result_from_job(job: dict[str, Any], handler: http.server.BaseHTTPRequestHandler | None = None) -> dict[str, Any]:
    job_id = job.get("job_id", "")
    return {
        "RequestId": job.get("request_id", ""),
        "ObjectId": job.get("object_id", ""),
        "ThemeId": job.get("theme_id", ""),
        "StyleVariantId": job.get("style_variant_id", "preset"),
        "RuntimeBackendJobId": job_id,
        "RuntimeBackendStatusUrl": status_url(job_id, handler) if job_id else "",
        "RuntimeModelUrl": job.get("model_url", ""),
        "RuntimeModelMimeType": job.get("model_mime_type", ""),
        "RuntimeModelHash": job.get("model_hash", ""),
        "Progress01": float(job.get("progress01", 0.0)),
        "OutputState": int(job.get("output_state", STATE_RUNTIME_BACKEND_SUBMITTED)),
        "FailureReason": job.get("failure_reason", ""),
        "StatusNote": job.get("status_note", ""),
        "CreatedAtIsoUtc": job.get("updated_at", utc_now()),
    }


def parse_request_body(content_type: str, body: bytes) -> tuple[dict[str, str], dict[str, tuple[str, bytes, str]]]:
    fields: dict[str, str] = {}
    files: dict[str, tuple[str, bytes, str]] = {}
    if content_type.lower().startswith("multipart/"):
        header = f"Content-Type: {content_type}\r\nMIME-Version: 1.0\r\n\r\n".encode("utf-8")
        message = BytesParser(policy=default).parsebytes(header + body)
        for part in message.iter_parts():
            disposition = part.get("Content-Disposition", "")
            params = dict(part.get_params(header="content-disposition", unquote=True) or [])
            name = params.get("name")
            if not name:
                continue
            payload = part.get_payload(decode=True) or b""
            filename = params.get("filename")
            if filename:
                files[name] = (Path(filename).name, payload, part.get_content_type() or "application/octet-stream")
            else:
                fields[name] = payload.decode(part.get_content_charset() or "utf-8", errors="replace")
        return fields, files

    if content_type.lower().startswith("application/json") or body:
        fields["metadata"] = body.decode("utf-8", errors="replace")
    return fields, files


def load_metadata(fields: dict[str, str]) -> dict[str, Any]:
    metadata_text = fields.get("metadata") or "{}"
    metadata = json.loads(metadata_text)
    if fields.get("request_json") and not metadata.get("SourceRequestJson"):
        metadata["SourceRequestJson"] = fields["request_json"]
    if fields.get("prompt_text") and not metadata.get("PromptText"):
        metadata["PromptText"] = fields["prompt_text"]
    return metadata


def create_job(fields: dict[str, str], files: dict[str, tuple[str, bytes, str]]) -> tuple[str, dict[str, Any]]:
    metadata = load_metadata(fields)
    request_id = metadata.get("RequestId") or f"request-{secrets.token_hex(4)}"
    job_id = f"{safe_id(request_id)}-{secrets.token_hex(4)}"
    directory = job_root() / job_id
    directory.mkdir(parents=True, exist_ok=False)

    write_json(directory / "metadata.json", metadata)
    if metadata.get("SourceRequestJson"):
        (directory / "request.json").write_text(metadata["SourceRequestJson"], encoding="utf-8")
    if metadata.get("PromptText"):
        (directory / "prompt.txt").write_text(metadata["PromptText"], encoding="utf-8")

    image_info = {}
    if "image" in files:
        filename, data, mime = files["image"]
        image_path = directory / safe_id(filename)
        image_path.write_bytes(data)
        image_hash = hashlib.sha256(data).hexdigest()
        expected_hash = metadata.get("SourceImageSha256", "")
        image_info = {
            "image_path": str(image_path),
            "image_file_name": image_path.name,
            "image_mime_type": mime,
            "image_sha256": image_hash,
            "image_byte_length": len(data),
        }
        if expected_hash and expected_hash.lower() != image_hash:
            write_json(
                directory / "job.json",
                build_job_record(
                    job_id,
                    metadata,
                    output_state=STATE_FAILED,
                    failure_reason=f"Uploaded image hash mismatch: expected {expected_hash}, got {image_hash}.",
                    status_note="Runtime backend rejected a corrupted image upload.",
                    extra=image_info,
                ),
            )
            return job_id, read_json(directory / "job.json")

    job = build_job_record(
        job_id,
        metadata,
        output_state=STATE_RUNTIME_BACKEND_SUBMITTED,
        status_note="Runtime backend accepted the Quest upload and queued generation.",
        progress01=0.05,
        extra=image_info,
    )
    write_json(directory / "job.json", job)
    write_json(directory / "submission.snapshot.json", {"metadata": metadata, "fields": fields, "image": image_info})
    return job_id, job


def build_job_record(
    job_id: str,
    metadata: dict[str, Any],
    output_state: int,
    status_note: str,
    failure_reason: str = "",
    progress01: float = 0.0,
    extra: dict[str, Any] | None = None,
) -> dict[str, Any]:
    now = utc_now()
    job = {
        "job_id": job_id,
        "provider": env("SCENESHIFT_BACKEND_PROVIDER", "manual").lower(),
        "request_id": metadata.get("RequestId", ""),
        "object_id": metadata.get("ObjectId", ""),
        "theme_id": metadata.get("ThemeId", ""),
        "style_variant_id": metadata.get("StyleVariantId", "preset") or "preset",
        "output_state": output_state,
        "status_note": status_note,
        "failure_reason": failure_reason,
        "progress01": progress01,
        "created_at": now,
        "updated_at": now,
    }
    if extra:
        job.update(extra)
    return job


def run_provider_worker(job_id: str) -> None:
    directory = job_root() / job_id
    try:
        provider = env("SCENESHIFT_BACKEND_PROVIDER", "manual").lower()
        if provider == "manual":
            write_manual_template(directory)
            update_job(
                directory,
                status_note="Manual provider waiting for manual-result.json. This is for protocol testing, not true generation.",
                progress01=0.1,
            )
            return
        if provider == "fixed-url":
            run_fixed_url_provider(directory)
            return
        if provider == "seed3d":
            run_seed3d_provider(directory)
            return
        update_job(
            directory,
            output_state=STATE_FAILED,
            failure_reason=f"Unknown SCENESHIFT_BACKEND_PROVIDER '{provider}'.",
            status_note="Runtime backend provider configuration failed.",
        )
    except Exception as exc:  # noqa: BLE001 - top-level worker must never crash server
        update_job(
            directory,
            output_state=STATE_FAILED,
            failure_reason=str(exc),
            status_note="Runtime backend worker crashed.",
            traceback=traceback.format_exc(),
        )


def write_manual_template(directory: Path) -> None:
    template = {
        "RuntimeModelUrl": "https://example.com/generated-model.glb",
        "RuntimeModelMimeType": "model/gltf-binary",
        "RuntimeModelHash": "",
        "StatusNote": "Replace this file with a real model URL to complete a manual backend job.",
    }
    write_json(directory / "manual-result.template.json", template)


def apply_manual_result_if_present(directory: Path) -> None:
    result_path = directory / "manual-result.json"
    if not result_path.exists():
        return
    manual = read_json(result_path)
    model_url = manual.get("RuntimeModelUrl", "")
    if not model_url:
        return
    update_job(
        directory,
        output_state=STATE_RUNTIME_MODEL_READY,
        model_url=model_url,
        model_mime_type=manual.get("RuntimeModelMimeType", "model/gltf-binary"),
        model_hash=manual.get("RuntimeModelHash", ""),
        status_note=manual.get("StatusNote", "Manual backend result supplied a runtime model URL."),
        failure_reason="",
        progress01=1.0,
    )


def run_fixed_url_provider(directory: Path) -> None:
    model_url = env("SCENESHIFT_FIXED_MODEL_URL")
    if not model_url:
        update_job(
            directory,
            output_state=STATE_FAILED,
            failure_reason="SCENESHIFT_FIXED_MODEL_URL is empty.",
            status_note="Fixed-url provider failed.",
        )
        return
    update_job(
        directory,
        output_state=STATE_RUNTIME_MODEL_READY,
        model_url=model_url,
        model_mime_type=env("SCENESHIFT_FIXED_MODEL_MIME", "model/gltf-binary"),
        model_hash=env("SCENESHIFT_FIXED_MODEL_SHA256"),
        status_note="Fixed-url provider returned a configured model URL. This is protocol test evidence only.",
        failure_reason="",
        progress01=1.0,
    )


def run_seed3d_provider(directory: Path) -> None:
    api_key = env("ARK_API_KEY")
    if not api_key:
        update_job(
            directory,
            output_state=STATE_FAILED,
            failure_reason="ARK_API_KEY is not set in the backend process environment.",
            status_note="Seed3D provider could not start.",
        )
        return

    job = read_json(directory / "job.json")
    image_path = Path(job.get("image_path", ""))
    if not image_path.exists():
        update_job(
            directory,
            output_state=STATE_FAILED,
            failure_reason="Quest upload did not include a readable image file.",
            status_note="Seed3D provider requires the captured crop image.",
        )
        return

    metadata = read_json(directory / "metadata.json")
    prompt = build_seed3d_prompt(metadata)
    image_mime = job.get("image_mime_type") or mimetypes.guess_type(image_path.name)[0] or "image/png"
    image_data_uri = "data:{};base64,{}".format(
        image_mime,
        base64.b64encode(image_path.read_bytes()).decode("ascii"),
    )

    request_payload = {
        "model": env("SEED3D_MODEL", DEFAULT_MODEL),
        "content": [
            {"type": "text", "text": prompt},
            {"type": "image_url", "image_url": {"url": image_data_uri}},
        ],
    }
    write_json(directory / "seed3d.request.json", request_payload)
    update_job(directory, status_note="Seed3D task create request sent.", progress01=0.18)

    endpoint = env("SEED3D_TASK_ENDPOINT", DEFAULT_ENDPOINT)
    create_response = http_json("POST", endpoint, request_payload, api_key)
    write_json(directory / "seed3d.create.response.json", create_response)
    task_id = find_first_key(create_response, ("task_id", "id"))
    if not task_id:
        raise RuntimeError("Seed3D create response did not include task_id/id.")

    update_job(directory, seed3d_task_id=task_id, status_note=f"Seed3D task {task_id} submitted; polling.", progress01=0.25)
    poll_seed3d_until_ready(directory, endpoint, str(task_id), api_key)


def build_seed3d_prompt(metadata: dict[str, Any]) -> str:
    flags = (
        f"--subdivisionlevel {safe_command_token(env('SEED3D_SUBDIVISION_LEVEL', 'low'))} "
        f"--fileformat {safe_command_token(env('SEED3D_FILE_FORMAT', 'glb'))}"
    )
    return flags


def safe_command_token(value: str) -> str:
    return re.sub(r"[^A-Za-z0-9_.-]", "", value) or "low"


def poll_seed3d_until_ready(directory: Path, endpoint: str, task_id: str, api_key: str) -> None:
    timeout_seconds = int(env("SEED3D_TIMEOUT_SECONDS", "900"))
    interval_seconds = max(1, int(env("SEED3D_POLL_INTERVAL_SECONDS", "5")))
    deadline = time.time() + timeout_seconds
    poll_url = f"{endpoint.rstrip('/')}/{urllib.parse.quote(task_id)}"
    attempt = 0
    last_response: dict[str, Any] = {}
    while time.time() < deadline:
        time.sleep(interval_seconds)
        attempt += 1
        response = http_json("GET", poll_url, None, api_key)
        last_response = response
        write_json(directory / "seed3d.poll.response.json", response)

        status = str(find_first_key(response, ("status", "state")) or "").lower()
        update_job(
            directory,
            status_note=f"Seed3D polling attempt {attempt}; status={status or 'unknown'}.",
            progress01=min(0.9, 0.25 + attempt * 0.03),
        )
        if status in {"failed", "error", "cancelled", "canceled"}:
            raise RuntimeError(f"Seed3D task failed with status '{status}'.")
        if status not in {"succeeded", "success", "completed"}:
            continue

        model_url = find_model_url(response)
        if not model_url:
            raise RuntimeError("Seed3D task succeeded but no model URL was found.")
        cache_model_and_finish(directory, model_url)
        return

    write_json(directory / "seed3d.timeout.last-response.json", last_response)
    raise TimeoutError(f"Seed3D polling timed out after {timeout_seconds}s for task {task_id}.")


def cache_model_and_finish(directory: Path, source_url: str) -> None:
    job = read_json(directory / "job.json")
    job_id = job["job_id"]
    model_dir = directory / "model"
    model_dir.mkdir(parents=True, exist_ok=True)
    parsed = urllib.parse.urlparse(source_url)
    filename = Path(urllib.parse.unquote(parsed.path)).name or f"{job_id}.glb"
    raw_path = model_dir / safe_id(filename)
    try:
        download_file(source_url, raw_path)
        model_path = extract_model_if_needed(raw_path, model_dir)
        digest = hashlib.sha256(model_path.read_bytes()).hexdigest()
        update_job(
            directory,
            output_state=STATE_RUNTIME_MODEL_READY,
            model_url=file_url(job_id, model_path.name),
            model_mime_type=mimetypes.guess_type(model_path.name)[0] or "model/gltf-binary",
            model_hash=digest,
            source_model_url=source_url,
            cached_model_path=str(model_path),
            status_note="Seed3D generated model cached by backend and ready for Quest runtime download.",
            failure_reason="",
            progress01=1.0,
        )
    except Exception as exc:  # Keep provider URL as a recoverable handoff if caching fails.
        update_job(
            directory,
            output_state=STATE_RUNTIME_MODEL_READY,
            model_url=source_url,
            model_mime_type="model/gltf-binary",
            status_note=f"Seed3D returned a model URL, but backend caching failed: {exc}",
            failure_reason="",
            progress01=1.0,
        )


def extract_model_if_needed(raw_path: Path, model_dir: Path) -> Path:
    if raw_path.suffix.lower() != ".zip":
        return raw_path
    with zipfile.ZipFile(raw_path) as archive:
        candidates = [name for name in archive.namelist() if name.lower().endswith((".glb", ".gltf"))]
        if not candidates:
            raise RuntimeError("Seed3D zip did not contain .glb or .gltf.")
        selected = sorted(candidates, key=len)[0]
        output = model_dir / Path(selected).name
        with archive.open(selected) as source, output.open("wb") as target:
            shutil.copyfileobj(source, target)
        return output


def http_json(method: str, url: str, payload: dict[str, Any] | None, api_key: str) -> dict[str, Any]:
    data = json.dumps(payload).encode("utf-8") if payload is not None else None
    request = urllib.request.Request(
        url,
        data=data,
        method=method,
        headers={
            "Accept": "application/json",
            "Authorization": f"Bearer {api_key}",
            **({"Content-Type": "application/json"} if payload is not None else {}),
        },
    )
    try:
        with urllib.request.urlopen(request, timeout=60) as response:
            text = response.read().decode("utf-8", errors="replace")
    except urllib.error.HTTPError as exc:
        body = exc.read().decode("utf-8", errors="replace")
        raise RuntimeError(f"{method} {url} failed: {exc.code} {body}") from exc
    return json.loads(text) if text.strip() else {}


def download_file(url: str, path: Path) -> None:
    request = urllib.request.Request(url, headers={"User-Agent": "SceneShiftRuntimeBackend/1.0"})
    with urllib.request.urlopen(request, timeout=120) as response, path.open("wb") as target:
        shutil.copyfileobj(response, target)


def find_first_key(payload: Any, keys: tuple[str, ...]) -> Any:
    if isinstance(payload, dict):
        for key in keys:
            if payload.get(key):
                return payload[key]
        for value in payload.values():
            found = find_first_key(value, keys)
            if found:
                return found
    elif isinstance(payload, list):
        for value in payload:
            found = find_first_key(value, keys)
            if found:
                return found
    return None


def find_model_url(payload: Any) -> str:
    found = find_first_key(payload, ("file_url", "model_url", "download_url", "url"))
    if isinstance(found, str) and found.startswith("http"):
        return found
    text = json.dumps(payload)
    match = re.search(r"https?://[^\"\\\s]+", text)
    return match.group(0) if match else ""


def backend_preflight() -> dict[str, Any]:
    provider = env("SCENESHIFT_BACKEND_PROVIDER", "manual").lower()
    public_url = env("SCENESHIFT_PUBLIC_BASE_URL")
    endpoint = env("SEED3D_TASK_ENDPOINT", DEFAULT_ENDPOINT)
    checks: list[dict[str, Any]] = []

    def add(name: str, passed: bool, detail: str) -> None:
        checks.append({"name": name, "status": "Pass" if passed else "Fail", "detail": detail})

    add("provider_supported", provider in {"manual", "fixed-url", "seed3d"}, provider)
    add("job_root_writable", is_directory_writable(job_root()), str(job_root()))
    add(
        "public_base_url_https",
        provider != "seed3d" or (is_https_url(public_url) and not url_query_has_secret(public_url)),
        public_url or "missing; required for Quest to download backend-cached models in real seed3d validation",
    )
    add("seed3d_api_key_env", provider != "seed3d" or bool(env("ARK_API_KEY")), "set" if env("ARK_API_KEY") else "missing")
    add("seed3d_endpoint_https", provider != "seed3d" or is_https_url(endpoint), endpoint)
    add(
        "fixed_url_configured",
        provider != "fixed-url" or is_http_url(env("SCENESHIFT_FIXED_MODEL_URL")),
        env("SCENESHIFT_FIXED_MODEL_URL") or "missing",
    )
    add(
        "true_generation_provider",
        provider == "seed3d",
        "Only seed3d mode can count toward true 3D generation closure; manual/fixed-url are protocol tests.",
    )

    overall = "Pass" if all(check["status"] == "Pass" for check in checks) else "Fail"
    return {
        "overall": overall,
        "created_at": utc_now(),
        "provider": provider,
        "public_base_url": public_url,
        "seed3d_endpoint": endpoint,
        "job_root": str(job_root()),
        "checks": checks,
    }


def is_directory_writable(path: Path) -> bool:
    try:
        path.mkdir(parents=True, exist_ok=True)
        probe = path / f".write_probe_{secrets.token_hex(4)}"
        probe.write_text("ok", encoding="utf-8")
        probe.unlink(missing_ok=True)
        return True
    except OSError:
        return False


def is_http_url(value: str) -> bool:
    if not value:
        return False
    parsed = urllib.parse.urlparse(value)
    return parsed.scheme in {"http", "https"} and bool(parsed.netloc)


def is_https_url(value: str) -> bool:
    if not value:
        return False
    parsed = urllib.parse.urlparse(value)
    return parsed.scheme == "https" and bool(parsed.netloc)


def url_query_has_secret(value: str) -> bool:
    query = urllib.parse.urlparse(value).query
    return bool(re.search(r"(key|api_key|apikey|token|secret|signature|sig|authorization|bearer)=", query, re.IGNORECASE))


def run_preflight_cli() -> int:
    report = backend_preflight()
    print(json.dumps(report, ensure_ascii=False, indent=2))
    return 0 if report["overall"] == "Pass" else 1


class RuntimeGenerationHandler(http.server.BaseHTTPRequestHandler):
    server_version = "SceneShiftRuntimeBackend/0.1"

    def do_POST(self) -> None:  # noqa: N802 - BaseHTTPRequestHandler API
        if self.path.rstrip("/") != "/v1/runtime-generations":
            self.send_error(404)
            return
        length = int(self.headers.get("Content-Length", "0") or "0")
        body = self.rfile.read(length)
        try:
            fields, files = parse_request_body(self.headers.get("Content-Type", ""), body)
            job_id, job = create_job(fields, files)
            if job.get("output_state") != STATE_FAILED:
                threading.Thread(target=run_provider_worker, args=(job_id,), daemon=True).start()
            self.write_json_response(202, result_from_job(job, self))
        except Exception as exc:  # noqa: BLE001
            self.write_json_response(
                400,
                {
                    "OutputState": STATE_FAILED,
                    "FailureReason": str(exc),
                    "StatusNote": "Runtime backend rejected the submission.",
                    "CreatedAtIsoUtc": utc_now(),
                },
            )

    def do_GET(self) -> None:  # noqa: N802 - BaseHTTPRequestHandler API
        if self.path in {"/health", "/v1/backend-preflight"}:
            self.write_json_response(200, backend_preflight())
            return

        match = re.match(r"^/v1/runtime-generations/([^/]+)(?:/files/(.+))?$", self.path)
        if not match:
            self.send_error(404)
            return
        job_id = urllib.parse.unquote(match.group(1))
        filename = urllib.parse.unquote(match.group(2) or "")
        directory = job_root() / job_id
        if not directory.exists():
            self.send_error(404)
            return
        if filename:
            self.send_file(directory / "model" / filename)
            return
        apply_manual_result_if_present(directory)
        self.write_json_response(200, result_from_job(read_json(directory / "job.json"), self))

    def write_json_response(self, status: int, payload: dict[str, Any]) -> None:
        data = json.dumps(payload, ensure_ascii=False, indent=2).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def send_file(self, path: Path) -> None:
        if not path.exists() or not path.is_file():
            self.send_error(404)
            return
        mime = mimetypes.guess_type(path.name)[0] or "application/octet-stream"
        data = path.read_bytes()
        self.send_response(200)
        self.send_header("Content-Type", mime)
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def log_message(self, format: str, *args: Any) -> None:
        print(f"[{utc_now()}] {self.address_string()} {format % args}")


def main() -> None:
    if "--preflight" in sys.argv:
        raise SystemExit(run_preflight_cli())

    host = env("SCENESHIFT_BACKEND_HOST", "127.0.0.1")
    port = int(env("SCENESHIFT_BACKEND_PORT", "8787"))
    print(f"SceneShift runtime backend listening on http://{host}:{port}")
    print(f"Provider: {env('SCENESHIFT_BACKEND_PROVIDER', 'manual')}")
    print(f"Job dir: {job_root()}")
    if not env("SCENESHIFT_PUBLIC_BASE_URL"):
        print("Warning: SCENESHIFT_PUBLIC_BASE_URL is empty; Quest builds usually need an HTTPS public/tunnel URL.")
    http.server.ThreadingHTTPServer((host, port), RuntimeGenerationHandler).serve_forever()


if __name__ == "__main__":
    main()
