#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

OUTPUT_ROOT="Library/RuntimeBackendSmokeReports"
WRITE_REPORT=true

usage() {
  cat <<'USAGE'
Usage:
  bash Tools/check_runtime_backend_seed3d_preflight.sh [--no-report]

Checks whether the secure runtime backend is configured for a real Seed3D run.
This script does not call Seed3D and does not print API key values.

Required for a real Quest backend test:
  SCENESHIFT_BACKEND_PROVIDER=seed3d
  ARK_API_KEY=<server-side only>
  SCENESHIFT_PUBLIC_BASE_URL=https://<public backend base>

Optional:
  SEED3D_TASK_ENDPOINT=https://...
USAGE
}

while [[ $# -gt 0 ]]; do
  case "$1" in
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
json_path="${OUTPUT_ROOT}/runtime_backend_seed3d_preflight_${timestamp}.json"
report_path="${OUTPUT_ROOT}/runtime_backend_seed3d_preflight_${timestamp}.md"

set +e
SCENESHIFT_BACKEND_PROVIDER="${SCENESHIFT_BACKEND_PROVIDER:-seed3d}" \
python3 Backend/sceneshift_runtime_backend.py --preflight >"$json_path"
exit_code=$?
set -e

overall="$(python3 - "$json_path" <<'PY'
import json, sys
print(json.load(open(sys.argv[1]))["overall"])
PY
)"

if [[ "$WRITE_REPORT" == true ]]; then
  {
    echo "# SceneShift Runtime Backend Seed3D Preflight"
    echo
    echo "- Created UTC: \`${created_utc}\`"
    echo "- Overall: \`${overall}\`"
    echo "- JSON: \`${json_path}\`"
    echo
    echo "## Checks"
    echo
    python3 - "$json_path" <<'PY'
import json, sys
report = json.load(open(sys.argv[1]))
for check in report["checks"]:
    print(f"- {check['name']}: `{check['status']}` - {check['detail']}")
PY
    echo
    echo "## Interpretation"
    echo
    if [[ "$overall" == "Pass" ]]; then
      echo "- Backend environment is ready to start a real Seed3D runtime test service."
    else
      echo "- Backend environment is not ready for true 3D generation closure. Fix failed checks before building an HttpBackend Quest test package."
    fi
  } >"$report_path"
fi

cat "$json_path"
echo
if [[ "$WRITE_REPORT" == true ]]; then
  echo "$report_path"
fi

exit "$exit_code"
