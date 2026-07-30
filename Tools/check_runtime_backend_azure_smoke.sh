#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

BACKEND_URL="${SCENESHIFT_RUNTIME_BACKEND_URL:-https://www.mikusc.top/api/v1/runtime-generations}"
OUTPUT_ROOT="Library/RuntimeBackendSmokeReports"
WRITE_REPORT=true

usage() {
  cat <<'USAGE'
Usage:
  bash Tools/check_runtime_backend_azure_smoke.sh [options]

Options:
  --url <url>          Runtime backend submit URL.
                       Default: $SCENESHIFT_RUNTIME_BACKEND_URL or
                       https://www.mikusc.top/api/v1/runtime-generations
  --output-root <dir>  Report output root. Default: Library/RuntimeBackendSmokeReports
  --no-report          Print result JSON only.
  -h, --help           Show this help.

Performs a deployed Azure Static Web Apps runtime-backend smoke check without
uploading an image. The expected pass condition is a clean HTTP response whose
failure reason is "Quest upload did not include a readable image file." That
proves the deployed function, storage connection, provider selection, and
server-side provider keys are reachable, but it does not create a paid provider
task and does not prove true 3D generation.

When report writing is enabled, the script writes
Library/RuntimeBackendSmokeReports/runtime_backend_azure_smoke_*.md/json.
USAGE
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --url)
      BACKEND_URL="${2:?missing backend URL}"
      shift 2
      ;;
    --output-root)
      OUTPUT_ROOT="${2:?missing output root}"
      shift 2
      ;;
    --no-report)
      WRITE_REPORT=false
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Unknown argument: $1" >&2
      usage >&2
      exit 2
      ;;
  esac
done

timestamp="$(date -u +%Y%m%d_%H%M%S)"
created_utc="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
mkdir -p "$OUTPUT_ROOT"
json_path="${OUTPUT_ROOT}/runtime_backend_azure_smoke_${timestamp}.json"
report_path="${OUTPUT_ROOT}/runtime_backend_azure_smoke_${timestamp}.md"

set +e
python3 - "$BACKEND_URL" "$json_path" <<'PY'
from __future__ import annotations

import json
import ssl
import sys
import time
import urllib.error
import urllib.parse
import urllib.request

backend_url = sys.argv[1]
output_path = sys.argv[2]
parsed = urllib.parse.urlparse(backend_url)
checks: list[dict[str, str]] = []


def add(name: str, passed: bool, detail: str) -> None:
    checks.append({"name": name, "status": "Pass" if passed else "Fail", "detail": detail})


def request(method: str, url: str, body: bytes | None = None, content_type: str = "application/json") -> tuple[int, str, dict[str, str]]:
    headers = {"Accept": "application/json"}
    if body is not None:
        headers["Content-Type"] = content_type
    req = urllib.request.Request(url, data=body, headers=headers, method=method)
    try:
        with urllib.request.urlopen(req, timeout=20, context=ssl.create_default_context()) as resp:
            return resp.status, resp.read().decode("utf-8", "replace"), dict(resp.headers)
    except urllib.error.HTTPError as exc:
        return exc.code, exc.read().decode("utf-8", "replace"), dict(exc.headers)


add("backend_url_https", parsed.scheme == "https", backend_url)
add("backend_url_no_query_secret", not parsed.query, "query is empty" if not parsed.query else "query string is present")

get_status, get_body, _ = request("GET", backend_url)
add("get_collection_rejected", get_status in (404, 405), f"HTTP {get_status}")

request_id = f"azure-smoke-noimage-{int(time.time())}"
metadata = {
    "RequestId": request_id,
    "ObjectId": "TABLE_AZURE_SMOKE",
    "RoomId": "azure_smoke_room",
    "ThemeId": "future_research_lab",
    "StyleVariantId": "preset",
    "SemanticLabel": "table",
    "PromptText": "azure smoke only; no image is uploaded and no provider task should be created",
    "SourceRequestJson": json.dumps({"RequestId": request_id, "ObjectId": "TABLE_AZURE_SMOKE", "SemanticLabel": "table"}),
}
post_status, post_body, post_headers = request("POST", backend_url, json.dumps(metadata).encode("utf-8"))

post_json = {}
post_parse_error = ""
try:
    post_json = json.loads(post_body)
except json.JSONDecodeError as exc:
    post_parse_error = str(exc)

failure_reason = str(post_json.get("FailureReason", ""))
status_note = str(post_json.get("StatusNote", ""))
status_url = str(post_json.get("RuntimeBackendStatusUrl", ""))
output_state = post_json.get("OutputState")

add("post_no_image_http_200", post_status == 200, f"HTTP {post_status}")
add("post_response_json", bool(post_json), post_parse_error or "parsed")
add("post_output_state_failed", output_state == 5, f"OutputState={output_state}")
provider_reached = (
    "readable image file" in failure_reason
    and (
        "Seed3D provider requires" in status_note
        or "Full-chain provider requires" in status_note
    )
)
add(
    "post_provider_config_reached_before_paid_task",
    provider_reached,
    f"failure={failure_reason!r}; status={status_note!r}",
)
add("post_status_url_https", status_url.startswith("https://"), status_url or "missing")
add("post_no_runtime_model_url", not post_json.get("RuntimeModelUrl"), "RuntimeModelUrl empty as expected")

overall = "Pass" if all(check["status"] == "Pass" for check in checks) else "Fail"
result = {
    "overall": overall,
    "backend_url": backend_url,
    "created_request_id": request_id,
    "get": {"status": get_status, "body": get_body[:500]},
    "post": {
        "status": post_status,
        "headers": {k: v for k, v in post_headers.items() if k.lower() in {"content-type", "cache-control"}},
        "json": post_json,
        "body": post_body[:2000],
    },
    "checks": checks,
    "note": "No image was uploaded, so this smoke does not create a paid provider task and is not true 3D generation evidence.",
}

with open(output_path, "w", encoding="utf-8") as handle:
    json.dump(result, handle, indent=2, ensure_ascii=False)

print(json.dumps(result, indent=2, ensure_ascii=False))
raise SystemExit(0 if overall == "Pass" else 1)
PY
exit_code=$?
set -e

overall="$(python3 - "$json_path" <<'PY'
import json, sys
print(json.load(open(sys.argv[1], encoding="utf-8"))["overall"])
PY
)"

if [[ "$WRITE_REPORT" == true ]]; then
  {
    echo "# SceneShift Runtime Backend Azure Smoke"
    echo
    echo "- Created UTC: \`${created_utc}\`"
    echo "- Overall: \`${overall}\`"
    echo "- Backend URL: \`${BACKEND_URL}\`"
    echo "- JSON: \`${json_path}\`"
    echo "- Paid provider task: \`not triggered\`"
    echo
    echo "## Checks"
    echo
    python3 - "$json_path" <<'PY'
import json, sys
report = json.load(open(sys.argv[1], encoding="utf-8"))
for check in report["checks"]:
    print(f"- {check['name']}: `{check['status']}` - {check['detail']}")
PY
    echo
    echo "## Interpretation"
    echo
    if [[ "$overall" == "Pass" ]]; then
      echo "- The deployed Azure runtime endpoint is reachable and reaches the configured provider boundary before rejecting the intentionally missing image."
      echo "- This is backend-readiness evidence only. True closure still requires a headset capture that uploads an image and returns a real GLB."
    else
      echo "- The deployed Azure runtime endpoint is not ready for a true headset run. Inspect the failed checks and Azure app settings."
    fi
  } >"$report_path"
fi

if [[ "$WRITE_REPORT" == true ]]; then
  echo "$report_path"
fi

exit "$exit_code"
