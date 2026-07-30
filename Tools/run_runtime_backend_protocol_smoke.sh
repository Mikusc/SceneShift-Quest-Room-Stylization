#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

PORT="${SCENESHIFT_SMOKE_BACKEND_PORT:-8789}"
REPORT_ROOT="Library/RuntimeBackendSmokeReports"
mkdir -p "$REPORT_ROOT"

timestamp="$(date -u +%Y%m%d_%H%M%S)"
report="${REPORT_ROOT}/runtime_backend_protocol_smoke_${timestamp}.md"
log="${REPORT_ROOT}/runtime_backend_protocol_smoke_${timestamp}.server.log"
json_out="${REPORT_ROOT}/runtime_backend_protocol_smoke_${timestamp}.json"

cleanup() {
  if [[ -n "${SERVER_PID:-}" ]]; then
    kill "$SERVER_PID" >/dev/null 2>&1 || true
    wait "$SERVER_PID" >/dev/null 2>&1 || true
  fi
}
trap cleanup EXIT

SCENESHIFT_BACKEND_PROVIDER=fixed-url \
SCENESHIFT_FIXED_MODEL_URL="https://raw.githubusercontent.com/KhronosGroup/glTF-Sample-Models/main/2.0/Box/glTF-Binary/Box.glb" \
SCENESHIFT_BACKEND_HOST=127.0.0.1 \
SCENESHIFT_BACKEND_PORT="$PORT" \
python3 Backend/sceneshift_runtime_backend.py >"$log" 2>&1 &
SERVER_PID=$!

python3 - "$PORT" "$json_out" <<'PY'
from __future__ import annotations

import hashlib
import http.client
import json
import sys
import time
import uuid

port = int(sys.argv[1])
output_path = sys.argv[2]

deadline = time.time() + 10
while time.time() < deadline:
    try:
        conn = http.client.HTTPConnection("127.0.0.1", port, timeout=1)
        conn.request("GET", "/not-found")
        conn.getresponse().read()
        break
    except OSError:
        time.sleep(0.1)
else:
    raise SystemExit("backend did not start")

boundary = "----SceneShiftSmoke" + uuid.uuid4().hex
image = b"sceneshift runtime backend protocol smoke image bytes"
request_id = "smoke_table_runtime_backend"
metadata = {
    "RequestId": request_id,
    "ObjectId": "TABLE_SMOKE",
    "RoomId": "runtime_backend_smoke_room",
    "ThemeId": "future_research_lab",
    "StyleVariantId": "preset",
    "SourceRequestJson": json.dumps({"RequestId": request_id, "ObjectId": "TABLE_SMOKE"}),
    "PromptText": "future research lab table, preserve tabletop support",
    "SourceImageFileName": "smoke.png",
    "SourceImageMimeType": "image/png",
    "SourceImageSha256": hashlib.sha256(image).hexdigest(),
    "SourceImageByteLength": len(image),
    "TargetLengthMeters": 1.2,
    "TargetWidthMeters": 0.7,
    "TargetHeightMeters": 0.75,
}
parts: list[bytes] = []


def add_field(name: str, value: str, ctype: str = "text/plain; charset=utf-8") -> None:
    parts.append(
        f"--{boundary}\r\nContent-Disposition: form-data; name=\"{name}\"\r\nContent-Type: {ctype}\r\n\r\n".encode()
        + value.encode()
        + b"\r\n"
    )


def add_file(name: str, filename: str, data: bytes, ctype: str) -> None:
    parts.append(
        f"--{boundary}\r\nContent-Disposition: form-data; name=\"{name}\"; filename=\"{filename}\"\r\nContent-Type: {ctype}\r\n\r\n".encode()
        + data
        + b"\r\n"
    )


add_field("metadata", json.dumps(metadata), "application/json")
add_field("request_json", metadata["SourceRequestJson"], "application/json")
add_field("prompt_text", metadata["PromptText"])
add_file("image", "smoke.png", image, "image/png")
body = b"".join(parts) + f"--{boundary}--\r\n".encode()

conn = http.client.HTTPConnection("127.0.0.1", port, timeout=10)
conn.request(
    "POST",
    "/v1/runtime-generations",
    body,
    {"Content-Type": f"multipart/form-data; boundary={boundary}", "Accept": "application/json"},
)
submit_response = conn.getresponse()
submit_text = submit_response.read().decode()
submit_json = json.loads(submit_text)

status_path = "/" + submit_json["RuntimeBackendStatusUrl"].split("/", 3)[3]
poll_json = {}
for _ in range(20):
    time.sleep(0.2)
    conn = http.client.HTTPConnection("127.0.0.1", port, timeout=10)
    conn.request("GET", status_path, headers={"Accept": "application/json"})
    poll_response = conn.getresponse()
    poll_text = poll_response.read().decode()
    poll_json = json.loads(poll_text)
    if poll_json.get("OutputState") == 10 or poll_json.get("FailureReason"):
        break

result = {
    "submit_status": submit_response.status,
    "submit": submit_json,
    "poll": poll_json,
    "pass": (
        submit_response.status == 202
        and submit_json.get("OutputState") == 9
        and poll_json.get("OutputState") == 10
        and bool(poll_json.get("RuntimeModelUrl"))
    ),
}
with open(output_path, "w", encoding="utf-8") as handle:
    json.dump(result, handle, indent=2)
if not result["pass"]:
    raise SystemExit(json.dumps(result, indent=2))
print(json.dumps(result, indent=2))
PY

overall="Pass"
cat >"$report" <<REPORT
# SceneShift Runtime Backend Protocol Smoke

- Created UTC: \`$(date -u +%Y-%m-%dT%H:%M:%SZ)\`
- Overall: \`${overall}\`
- Provider: \`fixed-url protocol smoke only\`
- Server log: \`${log}\`
- Result JSON: \`${json_out}\`

This smoke test validates multipart submit and polling contract shape only. It is not true 3D generation evidence.
REPORT

echo "$report"
