#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

EVIDENCE_DIR=""
EXPECTED_PACKAGE="com.mikusc.sceneshiftroom.comp4145"

usage() {
  cat <<'USAGE'
Usage:
  bash Tools/verify_mqdh_headset_evidence.sh [options] [evidence_dir]

Options:
  --package <id>    Expected Android package id. Default: com.mikusc.sceneshiftroom.comp4145
  -h, --help        Show this help.

When evidence_dir is omitted, verifies the latest
Library/MQDHHeadsetEvidence/adb_* directory created by
Tools/collect_mqdh_headset_evidence.sh.

This verifies that the headset evidence directory contains the core ADB outputs,
an installed package path, connected-device evidence, a screenshot, and either
pulled persistent files or an explicit pull/run-as error artifact.
USAGE
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --package)
      EXPECTED_PACKAGE="${2:?missing package id}"
      shift 2
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      if [[ -n "$EVIDENCE_DIR" ]]; then
        echo "Unexpected extra argument: $1" >&2
        usage >&2
        exit 2
      fi
      EVIDENCE_DIR="$1"
      shift
      ;;
  esac
done

if [[ -z "$EVIDENCE_DIR" ]]; then
  EVIDENCE_DIR="$(ls -dt Library/MQDHHeadsetEvidence/adb_* 2>/dev/null | head -n 1 || true)"
fi

if [[ -z "$EVIDENCE_DIR" || ! -d "$EVIDENCE_DIR" ]]; then
  echo "No MQDH ADB evidence directory found. Run Tools/collect_mqdh_headset_evidence.sh after the headset app is installed and open." >&2
  exit 1
fi

status=0
warn_count=0

require_file() {
  local path="$1"
  local label="$2"

  if [[ ! -f "$path" ]]; then
    echo "FAIL: Missing ${label}: ${path}"
    status=1
    return 1
  fi

  if [[ ! -s "$path" ]]; then
    echo "FAIL: Empty ${label}: ${path}"
    status=1
    return 1
  fi

  return 0
}

warn() {
  echo "WARN: $*"
  warn_count=$((warn_count + 1))
}

echo "# Verify MQDH Headset Evidence"
echo
echo "- Evidence directory: ${EVIDENCE_DIR}"
echo "- Expected package: ${EXPECTED_PACKAGE}"
echo

summary="${EVIDENCE_DIR}/summary.md"
adb_devices="${EVIDENCE_DIR}/adb_devices.txt"
device_model="${EVIDENCE_DIR}/device_model.txt"
device_build="${EVIDENCE_DIR}/device_build.txt"
device_os="${EVIDENCE_DIR}/device_os.txt"
package_dump="${EVIDENCE_DIR}/package_dump.txt"
package_path="${EVIDENCE_DIR}/package_path.txt"
unity_logcat="${EVIDENCE_DIR}/unity_logcat.txt"
screenshot="${EVIDENCE_DIR}/screenshot.png"
screenshot_err="${EVIDENCE_DIR}/screenshot.err"
sdcard_pull="${EVIDENCE_DIR}/persistent_sdcard_pull.txt"
run_as_ls="${EVIDENCE_DIR}/run_as_files_ls.txt"
run_as_tar="${EVIDENCE_DIR}/persistent_run_as_files.tar"
run_as_err="${EVIDENCE_DIR}/persistent_run_as_files.err"

require_file "$summary" "summary"
require_file "$adb_devices" "adb devices output"
require_file "$device_model" "device model output"
require_file "$device_build" "device build output"
require_file "$device_os" "device OS output"
require_file "$package_dump" "package dump"
require_file "$package_path" "package path"
require_file "$unity_logcat" "Unity logcat"

if [[ -f "$adb_devices" ]]; then
  if ! awk 'BEGIN { ok=0 } /^[^[:space:]]+[[:space:]]+device([[:space:]]|$)/ { ok=1 } END { exit ok ? 0 : 1 }' "$adb_devices"; then
    echo "FAIL: adb_devices.txt does not show a connected device in 'device' state."
    status=1
  fi
fi

if [[ -f "$package_path" ]]; then
  if ! grep -Fq "package:" "$package_path"; then
    echo "FAIL: package_path.txt does not show an installed package path for ${EXPECTED_PACKAGE}."
    status=1
  fi
fi

if [[ -f "$package_dump" ]]; then
  if ! grep -Fq "$EXPECTED_PACKAGE" "$package_dump"; then
    echo "FAIL: package_dump.txt does not reference ${EXPECTED_PACKAGE}."
    status=1
  fi
fi

if [[ ! -s "$screenshot" ]]; then
  echo "FAIL: screenshot.png is missing or empty."
  status=1
else
  screenshot_size="$(wc -c < "$screenshot" | tr -d '[:space:]')"
  if [[ "$screenshot_size" -lt 1024 ]]; then
    warn "screenshot.png is very small (${screenshot_size} bytes); inspect it manually."
  fi
fi

if [[ -s "$screenshot_err" ]]; then
  warn "screenshot.err is non-empty; inspect capture warnings/errors."
fi

if [[ -f "$sdcard_pull" ]]; then
  if grep -Eiq 'pulled|files? pulled|error|denied|No such file|failed' "$sdcard_pull"; then
    :
  else
    warn "persistent_sdcard_pull.txt does not contain an obvious pull result or error."
  fi
else
  warn "persistent_sdcard_pull.txt is missing."
fi

if [[ -s "$run_as_tar" ]]; then
  echo "- Persistent run-as tar: present"
elif [[ -s "$run_as_err" || -s "$run_as_ls" ]]; then
  warn "run-as persistent pull did not produce a non-empty tar; keeping error/list files as evidence."
else
  echo "FAIL: No persistent app-file evidence or explicit run-as error/list artifact found."
  status=1
fi

if [[ -f "$summary" ]]; then
  if ! grep -Fq "$EXPECTED_PACKAGE" "$summary"; then
    echo "FAIL: summary.md does not reference expected package ${EXPECTED_PACKAGE}."
    status=1
  fi
fi

echo
echo "Warnings: ${warn_count}"
if [[ "$status" -eq 0 ]]; then
  echo "MQDH headset evidence verification: Pass"
else
  echo "MQDH headset evidence verification: Fail"
fi

exit "$status"
