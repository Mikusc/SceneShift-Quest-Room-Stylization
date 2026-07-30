#!/usr/bin/env bash
set -euo pipefail

PACKAGE_ID="com.mikusc.sceneshiftroom.comp4145"
OUTPUT_ROOT="Library/MQDHHeadsetEvidence"
TEMPLATE_PATH=""
RECORD_SECONDS="0"
ADB_BIN="${ADB:-}"

usage() {
  cat <<'USAGE'
Usage:
  bash Tools/collect_mqdh_headset_evidence.sh [options]

Options:
  --package <id>          Android package id. Default: com.mikusc.sceneshiftroom.comp4145
  --output-root <path>    Output root. Default: Library/MQDHHeadsetEvidence
  --template <path>       Optional MQDH evidence template path to reference in summary.
  --record-seconds <n>    Optional screenrecord duration in seconds. Default: 0
  --adb <path>            Optional adb binary path. Default: $ADB or PATH lookup.
  -h, --help              Show this help.

This script collects headset-side evidence after a MQDH/test-channel install.
It does not install, uninstall, or modify the app.
USAGE
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --package)
      PACKAGE_ID="${2:?missing package id}"
      shift 2
      ;;
    --output-root)
      OUTPUT_ROOT="${2:?missing output root}"
      shift 2
      ;;
    --template)
      TEMPLATE_PATH="${2:?missing template path}"
      shift 2
      ;;
    --record-seconds)
      RECORD_SECONDS="${2:?missing record seconds}"
      shift 2
      ;;
    --adb)
      ADB_BIN="${2:?missing adb path}"
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

if [[ -z "$ADB_BIN" ]]; then
  if command -v adb >/dev/null 2>&1; then
    ADB_BIN="$(command -v adb)"
  elif [[ -n "${ANDROID_HOME:-}" && -x "${ANDROID_HOME}/platform-tools/adb" ]]; then
    ADB_BIN="${ANDROID_HOME}/platform-tools/adb"
  elif [[ -n "${ANDROID_SDK_ROOT:-}" && -x "${ANDROID_SDK_ROOT}/platform-tools/adb" ]]; then
    ADB_BIN="${ANDROID_SDK_ROOT}/platform-tools/adb"
  else
    echo "adb not found. Install Android SDK Platform Tools or pass --adb /path/to/adb." >&2
    exit 1
  fi
fi

timestamp="$(date -u +%Y%m%d_%H%M%S)"
output_dir="${OUTPUT_ROOT}/adb_${timestamp}"
mkdir -p "$output_dir"

run_capture() {
  local name="$1"
  shift
  {
    echo "$ $*"
    "$@"
  } >"${output_dir}/${name}" 2>&1 || true
}

run_capture "adb_devices.txt" "$ADB_BIN" devices -l
run_capture "device_model.txt" "$ADB_BIN" shell getprop ro.product.model
run_capture "device_build.txt" "$ADB_BIN" shell getprop ro.build.display.id
run_capture "device_os.txt" "$ADB_BIN" shell getprop ro.build.version.release
run_capture "package_dump.txt" "$ADB_BIN" shell dumpsys package "$PACKAGE_ID"
run_capture "package_path.txt" "$ADB_BIN" shell pm path "$PACKAGE_ID"
run_capture "unity_logcat.txt" "$ADB_BIN" logcat -d -v time Unity ActivityManager AndroidRuntime DEBUG "*:S"

"$ADB_BIN" exec-out screencap -p >"${output_dir}/screenshot.png" 2>"${output_dir}/screenshot.err" || true

sdcard_app_dir="/sdcard/Android/data/${PACKAGE_ID}/files"
run_capture "persistent_sdcard_ls.txt" "$ADB_BIN" shell ls -la "$sdcard_app_dir"
"$ADB_BIN" pull "$sdcard_app_dir" "${output_dir}/persistent_sdcard_files" >"${output_dir}/persistent_sdcard_pull.txt" 2>&1 || true

run_capture "run_as_files_ls.txt" "$ADB_BIN" shell run-as "$PACKAGE_ID" sh -c "pwd; find files -maxdepth 4 -type f -print"
"$ADB_BIN" exec-out run-as "$PACKAGE_ID" tar -C files -cf - . >"${output_dir}/persistent_run_as_files.tar" 2>"${output_dir}/persistent_run_as_files.err" || true

if [[ "$RECORD_SECONDS" =~ ^[0-9]+$ && "$RECORD_SECONDS" -gt 0 ]]; then
  remote_video="/sdcard/sceneshift_mqdh_${timestamp}.mp4"
  "$ADB_BIN" shell screenrecord --time-limit "$RECORD_SECONDS" "$remote_video" >"${output_dir}/screenrecord.txt" 2>&1 || true
  "$ADB_BIN" pull "$remote_video" "${output_dir}/screenrecord.mp4" >>"${output_dir}/screenrecord.txt" 2>&1 || true
  "$ADB_BIN" shell rm "$remote_video" >>"${output_dir}/screenrecord.txt" 2>&1 || true
fi

summary="${output_dir}/summary.md"
{
  echo "# MQDH ADB Evidence ${timestamp}"
  echo
  echo "- Package id: \`${PACKAGE_ID}\`"
  echo "- ADB: \`${ADB_BIN}\`"
  echo "- Output directory: \`${output_dir}\`"
  if [[ -n "$TEMPLATE_PATH" ]]; then
    echo "- Evidence template: \`${TEMPLATE_PATH}\`"
  fi
  echo
  echo "## Files"
  echo
  find "$output_dir" -maxdepth 2 -type f | sort | sed 's#^#- `#; s#$#`#'
  echo
  echo "## Notes"
  echo
  echo "- If \`persistent_run_as_files.tar\` is empty or has an error file, the build is probably not debuggable. Use MQDH file tools or a development build when persistent app files must be pulled."
  echo "- If \`persistent_sdcard_pull.txt\` reports permission denial, keep the error as evidence and rely on logcat/MQDH recording for that run."
} >"$summary"

echo "MQDH evidence collected: $output_dir"
