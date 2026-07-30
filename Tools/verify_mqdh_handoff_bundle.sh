#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

BUNDLE_DIR=""

usage() {
  cat <<'USAGE'
Usage:
  bash Tools/verify_mqdh_handoff_bundle.sh [bundle_dir]

When bundle_dir is omitted, verifies the latest
Library/MQDHHeadsetEvidence/handoff_bundle_* directory.
USAGE
}

if [[ $# -gt 1 ]]; then
  usage >&2
  exit 2
fi

if [[ $# -eq 1 ]]; then
  case "$1" in
    -h|--help)
      usage
      exit 0
      ;;
    *)
      BUNDLE_DIR="$1"
      ;;
  esac
else
  BUNDLE_DIR="$(ls -dt Library/MQDHHeadsetEvidence/handoff_bundle_* 2>/dev/null | head -n 1 || true)"
fi

if [[ -z "$BUNDLE_DIR" || ! -d "$BUNDLE_DIR" ]]; then
  echo "No MQDH handoff bundle found. Run Tools/write_mqdh_handoff_bundle.sh first." >&2
  exit 1
fi

MANIFEST="${BUNDLE_DIR}/manifest.md"
if [[ ! -f "$MANIFEST" ]]; then
  echo "Bundle manifest missing: $MANIFEST" >&2
  exit 1
fi

latest_file() {
  local pattern="$1"
  compgen -G "$pattern" >/dev/null || return 0
  ls -t $pattern | head -n 1
}

manifest_contains() {
  local value="$1"
  [[ -n "$value" ]] && grep -Fq "$value" "$MANIFEST"
}

verify_hashes() {
  local failures=0
  while IFS= read -r line; do
    local rel expected actual file
    rel="$(printf '%s\n' "$line" | sed -nE 's/^- `([^`]*)` sha256=`([^`]*)`.*/\1/p')"
    expected="$(printf '%s\n' "$line" | sed -nE 's/^- `([^`]*)` sha256=`([^`]*)`.*/\2/p')"
    [[ -n "$rel" && -n "$expected" && "$expected" != "n/a" ]] || continue
    file="${BUNDLE_DIR}/${rel}"
    if [[ ! -f "$file" ]]; then
      echo "HASH FAIL missing: $rel"
      failures=$((failures + 1))
      continue
    fi
    actual="$(shasum -a 256 "$file" | awk '{print $1}')"
    if [[ "$actual" != "$expected" ]]; then
      echo "HASH FAIL mismatch: $rel expected=$expected actual=$actual"
      failures=$((failures + 1))
    fi
  done < "$MANIFEST"
  return "$failures"
}

latest_readiness="$(latest_file 'Library/PreDeviceBuildReadinessReports/predevice_build_readiness_*.md')"
latest_smoke="$(latest_file 'Library/PreDeviceSmokeReports/predevice_smoke_*.md')"
latest_visual="$(latest_file 'Library/PreDeviceVisualEvidence/predevice_visual_review_*.md')"
latest_template="$(latest_file 'Library/MQDHHeadsetEvidence/mqdh_headset_evidence_*.md')"
latest_handoff="$(latest_file 'Library/MQDHHeadsetEvidence/mqdh_handoff_preflight_*.md')"
latest_runtime_backend_azure_smoke="$(latest_file 'Library/RuntimeBackendSmokeReports/runtime_backend_azure_smoke_*.md')"
secret_scan_file="${BUNDLE_DIR}/files/secret_scan/secret_scan.md"

status=0

echo "# Verify MQDH Handoff Bundle"
echo
echo "- Bundle: ${BUNDLE_DIR}"
echo "- Manifest: ${MANIFEST}"
echo "- Latest readiness: ${latest_readiness:-missing}"
echo "- Latest smoke: ${latest_smoke:-missing}"
echo "- Latest visual review: ${latest_visual:-missing}"
echo "- Latest template: ${latest_template:-missing}"
echo "- Latest handoff preflight: ${latest_handoff:-missing}"
echo "- Latest runtime backend Azure smoke: ${latest_runtime_backend_azure_smoke:-missing}"
echo "- Bundle secret scan: ${secret_scan_file}"
echo

for required in "$latest_readiness" "$latest_smoke" "$latest_visual" "$latest_template" "$latest_handoff"; do
  if [[ -z "$required" ]]; then
    echo "STALE: Missing latest source file for one evidence category."
    status=1
    continue
  fi
  if ! manifest_contains "$required"; then
    echo "STALE: Manifest does not reference latest file: $required"
    status=1
  fi
done

if [[ -n "$latest_runtime_backend_azure_smoke" ]] && ! manifest_contains "$latest_runtime_backend_azure_smoke"; then
  echo "STALE: Manifest does not reference latest runtime backend Azure smoke: $latest_runtime_backend_azure_smoke"
  status=1
fi

if [[ ! -f "$secret_scan_file" ]]; then
  echo "STALE: Bundle does not include files/secret_scan/secret_scan.md."
  status=1
else
  secret_findings="$(sed -nE 's/^- Findings: ([0-9]+).*/\1/p' "$secret_scan_file" | head -n 1)"
  if [[ "$secret_findings" != "0" ]]; then
    echo "SECRET SCAN FAIL: Bundle secret scan findings=${secret_findings:-unknown}."
    status=1
  fi

  if ! grep -Fq 'files/secret_scan/secret_scan.md' "$MANIFEST"; then
    echo "STALE: Manifest does not list the bundled secret scan file."
    status=1
  fi
fi

if verify_hashes; then
  echo "Hashes: Pass"
else
  echo "Hashes: Fail"
  status=1
fi

if [[ "$status" -eq 0 ]]; then
  echo "Bundle verification: Pass"
else
  echo "Bundle verification: Fail"
fi

exit "$status"
