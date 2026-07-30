#!/usr/bin/env bash
set -uo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

OUTPUT_ROOT="Library/MQDHHeadsetEvidence"
WRITE_REPORT=true

usage() {
  cat <<'USAGE'
Usage:
  bash Tools/run_mqdh_terminal_prepackage_suite.sh [--no-report]

Runs the terminal-side MQDH pre-package sequence after the Unity-side
SceneShift/Validation/Run MQDH Pre-Package Evidence Suite menu:
  - write the latest MQDH handoff bundle
  - verify the latest MQDH handoff bundle
  - run the pre-device local gate
  - verify the latest pre-device local gate
  - show the current MQDH handoff status

This script does not modify Unity scenes, prefabs, ProjectSettings, or packages.
It writes only timestamped reports under Library/MQDHHeadsetEvidence.
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
report=""
if [[ "$WRITE_REPORT" == true ]]; then
  mkdir -p "$OUTPUT_ROOT"
  report="${OUTPUT_ROOT}/mqdh_terminal_prepackage_suite_${timestamp}.md"
fi

STEP_KEYS=()
STEP_TITLES=()
STEP_STATUS=()
STEP_EXIT_CODES=()
STEP_OUTPUTS=()

classify_step() {
  local key="$1"
  local exit_code="$2"
  local output="$3"

  if [[ "$exit_code" -eq 0 ]]; then
    echo "Pass"
    return 0
  fi

  case "$key" in
    predevice_local_gate)
      if printf '%s\n' "$output" | grep -Fq 'Overall: `BlockedAndroidSupport`'; then
        echo "BlockedAndroidSupport"
      else
        echo "Fail"
      fi
      ;;
    handoff_status)
      if printf '%s\n' "$output" | grep -Eiq 'Android Build Support|AndroidPlayer|BLOCKED: Build readiness is still Fail'; then
        echo "BlockedAndroidSupport"
      else
        echo "Fail"
      fi
      ;;
    *)
      echo "Fail"
      ;;
  esac
}

run_step() {
  local key="$1"
  local title="$2"
  shift 2

  local output
  local exit_code
  local status
  output="$("$@" 2>&1)"
  exit_code=$?
  status="$(classify_step "$key" "$exit_code" "$output")"

  STEP_KEYS+=("$key")
  STEP_TITLES+=("$title")
  STEP_EXIT_CODES+=("$exit_code")
  STEP_STATUS+=("$status")
  STEP_OUTPUTS+=("$output")
}

latest_file() {
  local pattern="$1"
  compgen -G "$pattern" >/dev/null || return 0
  ls -t $pattern | head -n 1
}

extract_report_path() {
  local output="$1"
  printf '%s\n' "$output" | sed -nE 's/^- Report path: `([^`]*)`.*/\1/p' | tail -n 1
}

run_step "write_handoff_bundle" "Write MQDH handoff bundle" bash Tools/write_mqdh_handoff_bundle.sh
run_step "verify_handoff_bundle" "Verify MQDH handoff bundle" bash Tools/verify_mqdh_handoff_bundle.sh
run_step "predevice_local_gate" "Run pre-device local gate" env SCENESHIFT_CURRENT_TERMINAL_SUITE="$report" bash Tools/run_predevice_local_gate.sh
run_step "verify_predevice_local_gate" "Verify pre-device local gate" bash Tools/verify_predevice_local_gate.sh
run_step "handoff_status" "Show MQDH handoff status" env SCENESHIFT_CURRENT_TERMINAL_SUITE="$report" bash Tools/show_mqdh_handoff_status.sh

overall="Pass"
has_local_failure=false
has_android_blocker=false
for index in "${!STEP_KEYS[@]}"; do
  case "${STEP_STATUS[$index]}" in
    Pass)
      ;;
    BlockedAndroidSupport)
      has_android_blocker=true
      ;;
    *)
      has_local_failure=true
      ;;
  esac
done

if [[ "$has_local_failure" == true ]]; then
  overall="Fail"
elif [[ "$has_android_blocker" == true ]]; then
  overall="BlockedAndroidSupport"
fi

handoff_bundle_manifest="$(latest_file 'Library/MQDHHeadsetEvidence/handoff_bundle_*/manifest.md')"
local_gate_report=""
for index in "${!STEP_KEYS[@]}"; do
  if [[ "${STEP_KEYS[$index]}" == "predevice_local_gate" ]]; then
    local_gate_report="$(extract_report_path "${STEP_OUTPUTS[$index]}")"
    break
  fi
done
if [[ -z "$local_gate_report" ]]; then
  local_gate_report="$(latest_file 'Library/MQDHHeadsetEvidence/predevice_local_gate_*.md')"
fi

print_report() {
  echo "# SceneShift MQDH Terminal Pre-Package Suite"
  echo
  echo "- Created UTC: \`${created_utc}\`"
  echo "- Overall: \`${overall}\`"
  if [[ -n "$report" ]]; then
    echo "- Report path: \`${report}\`"
  fi
  echo "- Handoff bundle manifest: \`${handoff_bundle_manifest:-missing}\`"
  echo "- Pre-device local gate report: \`${local_gate_report:-missing}\`"
  echo
  echo "## Summary"
  echo
  local index
  for index in "${!STEP_KEYS[@]}"; do
    echo "- ${STEP_TITLES[$index]}: \`${STEP_STATUS[$index]}\` (exit=${STEP_EXIT_CODES[$index]})"
  done
  echo
  echo "## Interpretation"
  echo
  case "$overall" in
    Pass)
      echo "- Terminal-side MQDH pre-package evidence is current and verified."
      echo "- If the Unity suite is also clean and only the deliberate pre-switch active build target warning remains, switch Unity to Android before building the headset package."
      ;;
    BlockedAndroidSupport)
      echo "- Local evidence generation and verification completed, but the package path is blocked by missing Unity Android Build Support or stale readiness after installing it."
      echo "- Install Android Build Support for the exact Unity editor, including Android SDK & NDK Tools and OpenJDK, then rerun the Unity suite and this terminal suite."
      ;;
    *)
      echo "- Do not package yet. One or more terminal-side evidence steps failed for a local reason; inspect the command output below."
      ;;
  esac
  echo
  echo "## Final Package Gate"
  echo
  echo "After building APK/AAB, run:"
  echo
  echo '```bash'
  echo "bash Tools/run_predevice_local_gate.sh --package-artifact <apk-or-aab-path>"
  echo "bash Tools/verify_predevice_local_gate.sh --require-package-artifact"
  echo '```'
  echo
  echo "Use the package-only verifier directly only when debugging package-specific failures:"
  echo
  echo '```bash'
  echo "bash Tools/verify_mqdh_package_artifact.sh <apk-or-aab-path>"
  echo '```'
  echo
  echo "## Command Output"

  for index in "${!STEP_KEYS[@]}"; do
    echo
    echo "### ${STEP_TITLES[$index]}"
    echo
    echo '````text'
    printf '%s\n' "${STEP_OUTPUTS[$index]}"
    echo '````'
  done
}

if [[ -n "$report" ]]; then
  print_report > "$report"
fi

print_report

if [[ "$overall" == "Pass" ]]; then
  exit 0
fi

exit 1
