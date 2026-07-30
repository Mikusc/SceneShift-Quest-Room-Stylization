#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

PACKAGE_ID="com.mikusc.sceneshiftroom.comp4145"
APK_PATH=""
ADB_BIN="${ADB:-}"
OUTPUT_ROOT="Library/MQDHHeadsetEvidence"
TEMPLATE_PATH=""
RECORD_SECONDS="0"
WAIT_BEFORE_COLLECT_SECONDS="8"
INSTALL_APP=true
LAUNCH_APP=true
CLEAR_LOGCAT=true

usage() {
  cat <<'USAGE'
Usage:
  bash Tools/install_launch_collect_mqdh_headset_evidence.sh [options]

Options:
  --apk <path>                 APK to install. Default: latest Builds/MQDH/*.apk
  --package <id>               Android package id. Default: com.mikusc.sceneshiftroom.comp4145
  --adb <path>                 adb binary. Default: $ADB, PATH, Android SDK env, or Unity AndroidPlayer SDK.
  --output-root <path>         Evidence output root. Default: Library/MQDHHeadsetEvidence
  --template <path>            Optional MQDH evidence template path for the collected summary.
  --record-seconds <n>         Optional screenrecord duration passed to collector. Default: 0
  --wait-before-collect <n>    Seconds to wait after launch before collection. Default: 8
  --no-install                 Do not install; use the already installed package.
  --no-launch                  Do not launch; collect current headset state.
  --keep-logcat                Do not clear logcat before launch.
  -h, --help                   Show this help.

Installs the latest gated APK to a connected Quest over ADB, launches it, then
delegates to Tools/collect_mqdh_headset_evidence.sh and
Tools/verify_mqdh_headset_evidence.sh. This captures install/launch evidence
only; the user still needs to complete the in-headset style/capture/backend
flow before the evidence can count as true 3D generation closure.
USAGE
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --apk)
      APK_PATH="${2:?missing APK path}"
      shift 2
      ;;
    --package)
      PACKAGE_ID="${2:?missing package id}"
      shift 2
      ;;
    --adb)
      ADB_BIN="${2:?missing adb path}"
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
    --wait-before-collect)
      WAIT_BEFORE_COLLECT_SECONDS="${2:?missing wait seconds}"
      shift 2
      ;;
    --no-install)
      INSTALL_APP=false
      shift
      ;;
    --no-launch)
      LAUNCH_APP=false
      shift
      ;;
    --keep-logcat)
      CLEAR_LOGCAT=false
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

latest_file() {
  local pattern="$1"
  compgen -G "$pattern" >/dev/null || return 0
  ls -t $pattern | head -n 1
}

find_adb() {
  if [[ -n "$ADB_BIN" && -x "$ADB_BIN" ]]; then
    printf '%s\n' "$ADB_BIN"
    return 0
  fi
  if command -v adb >/dev/null 2>&1; then
    command -v adb
    return 0
  fi
  if [[ -n "${ANDROID_HOME:-}" && -x "${ANDROID_HOME}/platform-tools/adb" ]]; then
    printf '%s\n' "${ANDROID_HOME}/platform-tools/adb"
    return 0
  fi
  if [[ -n "${ANDROID_SDK_ROOT:-}" && -x "${ANDROID_SDK_ROOT}/platform-tools/adb" ]]; then
    printf '%s\n' "${ANDROID_SDK_ROOT}/platform-tools/adb"
    return 0
  fi

  local readiness android_player sdk
  readiness="$(latest_file 'Library/PreDeviceBuildReadinessReports/predevice_build_readiness_*.md')"
  if [[ -n "$readiness" && -f "$readiness" ]]; then
    android_player="$(sed -nE 's/^.*android_build_support_installed.*path=([^|]+).*$/\1/p' "$readiness" | head -n 1 | sed 's/[[:space:]]*$//')"
    android_player="${android_player%%,*}"
    sdk="${android_player}/SDK"
    if [[ -x "${sdk}/platform-tools/adb" ]]; then
      printf '%s\n' "${sdk}/platform-tools/adb"
      return 0
    fi
  fi

  return 1
}

if [[ -z "$APK_PATH" ]]; then
  APK_PATH="$(latest_file 'Builds/MQDH/*.apk')"
fi

if [[ "$INSTALL_APP" == true ]]; then
  if [[ -z "$APK_PATH" || ! -f "$APK_PATH" ]]; then
    echo "No APK found. Build with SceneShift/Validation/Build MQDH Test Package or pass --apk <path>." >&2
    exit 1
  fi
fi

if ! [[ "$RECORD_SECONDS" =~ ^[0-9]+$ && "$WAIT_BEFORE_COLLECT_SECONDS" =~ ^[0-9]+$ ]]; then
  echo "--record-seconds and --wait-before-collect must be non-negative integers." >&2
  exit 2
fi

ADB_BIN="$(find_adb || true)"
if [[ -z "$ADB_BIN" ]]; then
  echo "adb not found. Pass --adb /path/to/adb or install Android SDK Platform Tools." >&2
  exit 1
fi

devices_output="$("$ADB_BIN" devices -l)"
if ! printf '%s\n' "$devices_output" | awk 'BEGIN { ok=0 } /^[^[:space:]]+[[:space:]]+device([[:space:]]|$)/ { ok=1 } END { exit ok ? 0 : 1 }'; then
  echo "$devices_output"
  echo "No connected ADB device in 'device' state. Connect/unlock the Quest and accept the USB debugging prompt." >&2
  exit 1
fi

echo "# SceneShift MQDH Install/Launch/Collect"
echo
echo "- ADB: \`${ADB_BIN}\`"
echo "- Package: \`${PACKAGE_ID}\`"
if [[ "$INSTALL_APP" == true ]]; then
  echo "- APK: \`${APK_PATH}\`"
fi
echo

if [[ "$CLEAR_LOGCAT" == true ]]; then
  "$ADB_BIN" logcat -c || true
fi

if [[ "$INSTALL_APP" == true ]]; then
  echo "## Install"
  "$ADB_BIN" install -r "$APK_PATH"
  echo
fi

if [[ "$LAUNCH_APP" == true ]]; then
  echo "## Launch"
  "$ADB_BIN" shell monkey -p "$PACKAGE_ID" -c android.intent.category.LAUNCHER 1
  if [[ "$WAIT_BEFORE_COLLECT_SECONDS" -gt 0 ]]; then
    sleep "$WAIT_BEFORE_COLLECT_SECONDS"
  fi
  echo
fi

collect_cmd=(bash Tools/collect_mqdh_headset_evidence.sh --package "$PACKAGE_ID" --adb "$ADB_BIN" --output-root "$OUTPUT_ROOT" --record-seconds "$RECORD_SECONDS")
if [[ -n "$TEMPLATE_PATH" ]]; then
  collect_cmd+=(--template "$TEMPLATE_PATH")
fi

echo "## Collect"
collect_output="$("${collect_cmd[@]}")"
printf '%s\n' "$collect_output"
evidence_dir="$(printf '%s\n' "$collect_output" | sed -nE 's/^MQDH evidence collected: (.*)$/\1/p' | tail -n 1)"
if [[ -z "$evidence_dir" ]]; then
  echo "Could not determine evidence directory from collector output." >&2
  exit 1
fi
echo

echo "## Verify"
bash Tools/verify_mqdh_headset_evidence.sh --package "$PACKAGE_ID" "$evidence_dir"
