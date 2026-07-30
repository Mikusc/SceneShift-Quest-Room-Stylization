#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

REPORT=""
ALLOW_BLOCKED=false

usage() {
  cat <<'USAGE'
Usage:
  bash Tools/verify_mqdh_package_build_report.sh [options] [report]

Options:
  --allow-blocked   Accept a blocked pre-build report as a valid current-state
                    record. Without this, the report must be BuiltAndVerified.
  -h, --help        Show this help.

When report is omitted, verifies the latest
Library/MQDHPackageBuildReports/mqdh_package_build_*.md report.
USAGE
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --allow-blocked)
      ALLOW_BLOCKED=true
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      if [[ -n "$REPORT" ]]; then
        echo "Unexpected extra argument: $1" >&2
        usage >&2
        exit 2
      fi
      REPORT="$1"
      shift
      ;;
  esac
done

latest_file() {
  local pattern="$1"
  compgen -G "$pattern" >/dev/null || return 0
  ls -t $pattern | head -n 1
}

extract_markdown_value() {
  local path="$1"
  local field="$2"
  [[ -f "$path" ]] || return 0
  sed -nE "s/^- ${field}: \`([^\`]*)\`.*/\\1/p" "$path" | head -n 1
}

extract_check_status() {
  local path="$1"
  local check="$2"
  [[ -f "$path" ]] || return 0
  awk -F'|' -v check="$check" '
    $2 ~ check {
      gsub(/^[[:space:]]+|[[:space:]]+$/, "", $3)
      gsub(/`/, "", $3)
      print $3
      exit
    }
  ' "$path"
}

if [[ -z "$REPORT" ]]; then
  REPORT="$(latest_file 'Library/MQDHPackageBuildReports/mqdh_package_build_*.md')"
fi

if [[ -z "$REPORT" || ! -f "$REPORT" ]]; then
  echo "No MQDH package build report found. Run SceneShift/Validation/Build MQDH Test Package first." >&2
  exit 1
fi

latest_readiness="$(latest_file 'Library/PreDeviceBuildReadinessReports/predevice_build_readiness_*.md')"
overall="$(extract_markdown_value "$REPORT" "Overall")"
artifact_path="$(extract_markdown_value "$REPORT" "Artifact path")"
artifact_exists_field="$(extract_markdown_value "$REPORT" "Artifact exists")"
artifact_bytes="$(extract_markdown_value "$REPORT" "Artifact bytes")"
readiness_report="$(extract_markdown_value "$REPORT" "Readiness report")"
readiness_overall="$(extract_markdown_value "$REPORT" "Readiness overall")"
unity_build_result="$(extract_markdown_value "$REPORT" "Unity build result")"

status=0

echo "# Verify MQDH Package Build Report"
echo
echo "- Report: ${REPORT}"
echo "- Overall: ${overall:-unknown}"
echo "- Artifact path: ${artifact_path:-missing}"
echo "- Artifact exists field: ${artifact_exists_field:-missing}"
echo "- Artifact bytes: ${artifact_bytes:-missing}"
echo "- Readiness report: ${readiness_report:-missing}"
echo "- Latest readiness: ${latest_readiness:-missing}"
echo "- Readiness overall: ${readiness_overall:-unknown}"
echo "- Unity build result: ${unity_build_result:-unknown}"
echo "- Allow blocked: ${ALLOW_BLOCKED}"
echo

case "$overall" in
  BuiltAndVerified|BlockedAndroidSupport|BlockedReadiness|BlockedBuildTarget|BlockedPreBuild|BuildFailed|BuiltButFinalGateFailed)
    ;;
  *)
    echo "FAIL: Unknown Overall value: ${overall:-missing}"
    status=1
    ;;
esac

if [[ -n "$latest_readiness" && "$readiness_report" != "$ROOT_DIR/$latest_readiness" && "$readiness_report" != "$latest_readiness" ]]; then
  echo "STALE: Package build report does not reference the latest readiness report."
  status=1
fi

if [[ "$overall" == "BuiltAndVerified" ]]; then
  if [[ "$artifact_exists_field" != "True" && "$artifact_exists_field" != "true" ]]; then
    echo "FAIL: BuiltAndVerified report does not record Artifact exists=True."
    status=1
  fi

  if [[ -z "$artifact_path" || ! -f "$artifact_path" ]]; then
    echo "FAIL: Package artifact is missing on disk: ${artifact_path:-missing}"
    status=1
  fi

  if [[ "${artifact_bytes:-0}" == "0" ]]; then
    echo "FAIL: Package artifact byte size is zero or missing."
    status=1
  fi

  if [[ "$(extract_check_status "$REPORT" "unity_build_succeeded")" != "Pass" ]]; then
    echo "FAIL: unity_build_succeeded check is not Pass."
    status=1
  fi

  if [[ "$(extract_check_status "$REPORT" "final_local_gate_with_package_artifact")" != "Pass" ]]; then
    echo "FAIL: final_local_gate_with_package_artifact check is not Pass."
    status=1
  fi

  if [[ "$(extract_check_status "$REPORT" "final_local_gate_package_required_verification")" != "Pass" ]]; then
    echo "FAIL: final_local_gate_package_required_verification check is not Pass."
    status=1
  fi
else
  if [[ "$ALLOW_BLOCKED" != true ]]; then
    echo "FAIL: Package build report is not BuiltAndVerified."
    status=1
  fi

  case "$overall" in
    BlockedAndroidSupport|BlockedReadiness|BlockedBuildTarget|BlockedPreBuild)
      if [[ "$artifact_exists_field" == "True" || "$artifact_exists_field" == "true" ]]; then
        echo "FAIL: Blocked pre-build report unexpectedly records an artifact."
        status=1
      fi
      ;;
    BuildFailed|BuiltButFinalGateFailed)
      echo "FAIL: Package build or final gate failed; this is not an acceptable blocked pre-build state."
      status=1
      ;;
  esac
fi

if [[ "$status" -eq 0 ]]; then
  echo "MQDH package build report verification: Pass"
else
  echo "MQDH package build report verification: Fail"
fi

exit "$status"
