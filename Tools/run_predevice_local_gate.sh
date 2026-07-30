#!/usr/bin/env bash
set -uo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

OUTPUT_ROOT="Library/MQDHHeadsetEvidence"
WRITE_REPORT=true
PACKAGE_ARTIFACT=""
PACKAGE_ID="com.mikusc.sceneshiftroom.comp4145"
PACKAGE_VERSION_CODE=""
PACKAGE_VERSION_NAME=""
PACKAGE_MIN_SIZE=""

usage() {
  cat <<'USAGE'
Usage:
  bash Tools/run_predevice_local_gate.sh [options]

Options:
  --no-report                  Print results without writing a report file.
  --package-artifact <path>    Also verify the built APK/AAB artifact.
  --package <id>               Expected Android package id for package checks.
                               Default: com.mikusc.sceneshiftroom.comp4145
  --version-code <code>        Expected Android bundle version code.
  --version-name <name>        Expected bundle version string.
  --package-min-size <bytes>   Minimum APK/AAB size for package checks.
  -h, --help                   Show this help.

Runs the terminal-side checks that should be true before switching/building for
MQDH or a test release-channel headset run:
  - credential scan
  - handoff bundle freshness/hash verification
  - Unity Android Build Support filesystem check
  - consolidated MQDH handoff status
  - optional APK/AAB package artifact verification

This script does not modify Unity scenes or project settings.
USAGE
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --no-report)
      WRITE_REPORT=false
      shift
      ;;
    --package-artifact)
      PACKAGE_ARTIFACT="${2:?missing package artifact path}"
      shift 2
      ;;
    --package)
      PACKAGE_ID="${2:?missing package id}"
      shift 2
      ;;
    --version-code)
      PACKAGE_VERSION_CODE="${2:?missing version code}"
      shift 2
      ;;
    --version-name)
      PACKAGE_VERSION_NAME="${2:?missing version name}"
      shift 2
      ;;
    --package-min-size)
      PACKAGE_MIN_SIZE="${2:?missing package min size}"
      shift 2
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
  report="${OUTPUT_ROOT}/predevice_local_gate_${timestamp}.md"
fi

CHECK_KEYS=()
CHECK_TITLES=()
CHECK_STATUS=()
CHECK_EXIT_CODES=()
CHECK_OUTPUTS=()

run_check() {
  local key="$1"
  local title="$2"
  shift 2

  local output
  local exit_code
  output="$("$@" 2>&1)"
  exit_code=$?

  CHECK_KEYS+=("$key")
  CHECK_TITLES+=("$title")
  CHECK_EXIT_CODES+=("$exit_code")
  CHECK_OUTPUTS+=("$output")
  if [[ "$exit_code" -eq 0 ]]; then
    CHECK_STATUS+=("Pass")
  else
    CHECK_STATUS+=("Fail")
  fi
}

run_check "secret_scan" "Pre-device secret scan" bash Tools/scan_predevice_secrets.sh --summary-only
run_check "handoff_bundle" "MQDH handoff bundle verification" bash Tools/verify_mqdh_handoff_bundle.sh
run_check "android_support" "Unity Android Build Support filesystem check" bash Tools/check_unity_android_support.sh
run_check "handoff_status" "MQDH handoff status" env SCENESHIFT_SUPPRESS_LOCAL_GATE=1 bash Tools/show_mqdh_handoff_status.sh

if [[ -n "$PACKAGE_ARTIFACT" ]]; then
  package_check_cmd=(bash Tools/verify_mqdh_package_artifact.sh --package "$PACKAGE_ID")
  if [[ -n "$PACKAGE_VERSION_CODE" ]]; then
    package_check_cmd+=(--version-code "$PACKAGE_VERSION_CODE")
  fi
  if [[ -n "$PACKAGE_VERSION_NAME" ]]; then
    package_check_cmd+=(--version-name "$PACKAGE_VERSION_NAME")
  fi
  if [[ -n "$PACKAGE_MIN_SIZE" ]]; then
    package_check_cmd+=(--min-size "$PACKAGE_MIN_SIZE")
  fi
  package_check_cmd+=("$PACKAGE_ARTIFACT")
  run_check "package_artifact" "MQDH package artifact verification" "${package_check_cmd[@]}"
fi

overall="Pass"
has_local_failure=false
has_android_blocker=false
for index in "${!CHECK_KEYS[@]}"; do
  if [[ "${CHECK_STATUS[$index]}" == "Pass" ]]; then
    continue
  fi

  case "${CHECK_KEYS[$index]}" in
    android_support|handoff_status)
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

print_report() {
  echo "# SceneShift Pre-Device Local Gate"
  echo
  echo "- Created UTC: \`${created_utc}\`"
  echo "- Overall: \`${overall}\`"
  if [[ -n "$PACKAGE_ARTIFACT" ]]; then
    echo "- Package artifact: \`${PACKAGE_ARTIFACT}\`"
  else
    echo "- Package artifact: \`not provided (pre-package gate)\`"
  fi
  if [[ -n "$report" ]]; then
    echo "- Report path: \`${report}\`"
  fi
  echo
  echo "## Summary"
  echo
  local index
  for index in "${!CHECK_KEYS[@]}"; do
    echo "- ${CHECK_TITLES[$index]}: \`${CHECK_STATUS[$index]}\` (exit=${CHECK_EXIT_CODES[$index]})"
  done
  echo
  echo "## Interpretation"
  echo
  case "$overall" in
    Pass)
      if [[ -n "$PACKAGE_ARTIFACT" ]]; then
        echo "- Terminal-side local gate passed, including APK/AAB package artifact verification."
      else
        echo "- Terminal-side pre-package local gate passed. After building the APK/AAB, rerun this gate with \`--package-artifact <apk-or-aab-path>\` before MQDH/test-channel upload."
      fi
      ;;
    BlockedAndroidSupport)
      echo "- Do not package yet. Local evidence and secret scanning are current, but Unity Android Build Support or the handoff status is still blocking the package path."
      echo "- Install Android Build Support for the exact Unity editor, including Android SDK & NDK Tools and OpenJDK, then rerun this gate."
      ;;
    *)
      echo "- Do not package yet. One or more local evidence checks failed and must be fixed before Android switching or MQDH/test-channel packaging."
      ;;
  esac
  echo
  echo "## Command Output"

  for index in "${!CHECK_KEYS[@]}"; do
    echo
    echo "### ${CHECK_TITLES[$index]}"
    echo
    echo '```text'
    printf '%s\n' "${CHECK_OUTPUTS[$index]}"
    echo '```'
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
