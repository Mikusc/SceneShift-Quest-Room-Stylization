#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

UNITY_HUB_BIN="/Applications/Unity Hub.app/Contents/MacOS/Unity Hub"
ANDROID_PLAYER_PATH=""
EDITOR_VERSION=""
RUN_INSTALL=false
ALLOW_RUNNING=false
WAIT_FOR_CLOSE=false
WAIT_TIMEOUT_SECONDS=900
WAIT_INTERVAL_SECONDS=5
LOG_ROOT="Library/AndroidSupportInstallLogs"

usage() {
  cat <<'USAGE'
Usage:
  bash Tools/install_unity_android_support.sh [options]

Options:
  --run                     Run Unity Hub CLI install-modules.
  --dry-run                 Print the resolved command without installing. Default.
  --version <version>       Unity editor version, for example 6000.4.3f1.
  --android-player <path>   Explicit Unity AndroidPlayer directory.
  --wait-for-close          Wait until Unity Editor and Unity Hub are closed, then install.
  --wait-timeout <seconds>  Max wait for --wait-for-close. Default: 900.
  --wait-interval <seconds> Poll interval for --wait-for-close. Default: 5.
  --allow-running           Do not block when Unity Editor or Unity Hub is running.
  -h, --help                Show this help.

This helper is intentionally conservative. In --run mode it refuses to install
while Unity Editor or Unity Hub is already running unless --wait-for-close is
used. It never closes apps for you; close Unity Editor and Unity Hub manually.
The guard avoids UnityHub profile locks and module installation conflicts with
an active editor process.
USAGE
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --run)
      RUN_INSTALL=true
      shift
      ;;
    --dry-run)
      RUN_INSTALL=false
      shift
      ;;
    --version|--editor-version)
      EDITOR_VERSION="${2:?missing editor version}"
      shift 2
      ;;
    --android-player)
      ANDROID_PLAYER_PATH="${2:?missing AndroidPlayer path}"
      shift 2
      ;;
    --wait-for-close)
      WAIT_FOR_CLOSE=true
      shift
      ;;
    --wait-timeout)
      WAIT_TIMEOUT_SECONDS="${2:?missing wait timeout seconds}"
      shift 2
      ;;
    --wait-interval)
      WAIT_INTERVAL_SECONDS="${2:?missing wait interval seconds}"
      shift 2
      ;;
    --allow-running)
      ALLOW_RUNNING=true
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

extract_android_player_path() {
  local readiness="$1"
  [[ -f "$readiness" ]] || return 0
  sed -nE 's/^.*android_build_support_installed.*path=([^|]+).*$/\1/p' "$readiness" | head -n 1 | sed 's/[[:space:]]*$//'
}

extract_editor_version() {
  local android_player="$1"
  printf '%s\n' "$android_player" | sed -nE 's#^.*/Editor/([^/]+)/(Unity\.app/Contents/)?PlaybackEngines/AndroidPlayer$#\1#p' | head -n 1
}

resolve_android_player_path() {
  local android_player="$1"
  local version candidate external_candidate

  if [[ -n "$android_player" && -d "$android_player" ]]; then
    printf '%s\n' "$android_player"
    return 0
  fi

  if [[ -n "$android_player" ]]; then
    external_candidate="$(printf '%s\n' "$android_player" | sed -nE 's#^(.*)/Unity\.app/Contents/PlaybackEngines/AndroidPlayer$#\1/PlaybackEngines/AndroidPlayer#p' | head -n 1)"
    if [[ -n "$external_candidate" && -d "$external_candidate" ]]; then
      printf '%s\n' "$external_candidate"
      return 0
    fi
  fi

  version="$(extract_editor_version "$android_player")"
  if [[ -n "$version" ]]; then
    for candidate in \
      "/Applications/Unity/Hub/Editor/${version}/PlaybackEngines/AndroidPlayer" \
      "/Applications/Unity/Hub/Editor/${version}/Unity.app/Contents/PlaybackEngines/AndroidPlayer"
    do
      if [[ -d "$candidate" ]]; then
        printf '%s\n' "$candidate"
        return 0
      fi
    done
  fi

  printf '%s\n' "$android_player"
}

bool_from_pgrep() {
  local pattern="$1"
  if pgrep -f "$pattern" >/dev/null 2>&1; then
    printf 'true'
  else
    printf 'false'
  fi
}

refresh_running_state() {
  editor_running="$(bool_from_pgrep "$editor_process_pattern")"
  hub_running="$(bool_from_pgrep "$hub_process_pattern")"
}

wait_for_unity_and_hub_to_close() {
  local elapsed=0

  echo
  echo "Waiting for Unity Editor and Unity Hub to close. Close them manually to continue."
  echo "- Timeout seconds: ${WAIT_TIMEOUT_SECONDS}"
  echo "- Poll interval seconds: ${WAIT_INTERVAL_SECONDS}"

  while true; do
    refresh_running_state
    if [[ "$editor_running" != true && "$hub_running" != true ]]; then
      echo "Unity Editor and Unity Hub are closed. Continuing installation."
      return 0
    fi

    if [[ "$elapsed" -ge "$WAIT_TIMEOUT_SECONDS" ]]; then
      echo
      echo "Overall: WaitForCloseTimedOut"
      echo
      echo "Unity Editor running: ${editor_running}"
      echo "Unity Hub running: ${hub_running}"
      echo "Rerun after closing both apps, or increase --wait-timeout."
      exit 1
    fi

    sleep "$WAIT_INTERVAL_SECONDS"
    elapsed=$((elapsed + WAIT_INTERVAL_SECONDS))
  done
}

print_install_command() {
  local version="$1"
  echo "\"$UNITY_HUB_BIN\" -- --headless install-modules --version $version -m android android-sdk-ndk-tools android-open-jdk"
}

readiness="$(latest_file 'Library/PreDeviceBuildReadinessReports/predevice_build_readiness_*.md')"

if [[ -z "$ANDROID_PLAYER_PATH" && -n "$readiness" ]]; then
  ANDROID_PLAYER_PATH="$(extract_android_player_path "$readiness")"
fi

if [[ -z "$EDITOR_VERSION" && -n "$ANDROID_PLAYER_PATH" ]]; then
  EDITOR_VERSION="$(extract_editor_version "$ANDROID_PLAYER_PATH")"
fi

if [[ -z "$ANDROID_PLAYER_PATH" && -n "$EDITOR_VERSION" ]]; then
  ANDROID_PLAYER_PATH="/Applications/Unity/Hub/Editor/${EDITOR_VERSION}/PlaybackEngines/AndroidPlayer"
fi

ANDROID_PLAYER_PATH="$(resolve_android_player_path "$ANDROID_PLAYER_PATH")"

if [[ -z "$EDITOR_VERSION" ]]; then
  echo "Could not determine Unity editor version. Run Unity readiness first or pass --version." >&2
  exit 2
fi

if [[ ! -x "$UNITY_HUB_BIN" ]]; then
  echo "Unity Hub CLI not found or not executable: $UNITY_HUB_BIN" >&2
  exit 2
fi

editor_process_pattern="/Applications/Unity/Hub/Editor/${EDITOR_VERSION}/Unity.app/Contents/MacOS/Unity"
hub_process_pattern="/Applications/Unity Hub.app/Contents/MacOS/Unity Hub"
refresh_running_state

echo "# Unity Android Support Installer"
echo
echo "- Editor version: \`${EDITOR_VERSION}\`"
echo "- AndroidPlayer: \`${ANDROID_PLAYER_PATH:-unknown}\`"
echo "- Unity Editor running for this version: \`${editor_running}\`"
echo "- Unity Hub running: \`${hub_running}\`"
echo "- Mode: \`$([[ "$RUN_INSTALL" == true ]] && printf 'run' || printf 'dry-run')\`"
echo "- Wait for close: \`${WAIT_FOR_CLOSE}\`"
echo
echo "## Command"
echo
echo "\`\`\`bash"
print_install_command "$EDITOR_VERSION"
echo "\`\`\`"

if [[ "$RUN_INSTALL" != true ]]; then
  echo
  echo "Overall: DryRun"
  echo
  echo "Recommended command:"
  echo "  bash Tools/install_unity_android_support.sh --run --wait-for-close"
  exit 0
fi

if [[ "$ALLOW_RUNNING" != true && ( "$editor_running" == true || "$hub_running" == true ) ]]; then
  if [[ "$WAIT_FOR_CLOSE" == true ]]; then
    wait_for_unity_and_hub_to_close
  else
    echo
    echo "Overall: RefusedRunningUnityOrHub"
    echo
    echo "Close Unity Editor and Unity Hub, then rerun:"
    echo "  bash Tools/install_unity_android_support.sh --run"
    echo
    echo "Or start the helper now and close both apps manually while it waits:"
    echo "  bash Tools/install_unity_android_support.sh --run --wait-for-close"
    echo
    echo "Use --allow-running only if you intentionally accept Unity Hub profile-lock/module-install risk."
    exit 1
  fi
fi

if [[ "$ALLOW_RUNNING" != true && ( "$editor_running" == true || "$hub_running" == true ) ]]; then
  echo
  echo "Overall: RefusedRunningUnityOrHub"
  echo
  echo "Close Unity Editor and Unity Hub, then rerun:"
  echo "  bash Tools/install_unity_android_support.sh --run"
  echo
  echo "Use --allow-running only if you intentionally accept Unity Hub profile-lock/module-install risk."
  exit 1
fi

mkdir -p "$LOG_ROOT"
timestamp="$(date -u +%Y%m%d_%H%M%S)"
log_path="${LOG_ROOT}/android_support_install_${timestamp}.log"

echo
echo "Writing install log to: $log_path"
echo

install_cmd=(
  "$UNITY_HUB_BIN"
  "--"
  "--headless"
  "install-modules"
  "--version"
  "$EDITOR_VERSION"
  "-m"
  "android"
  "android-sdk-ndk-tools"
  "android-open-jdk"
)

set +e
"${install_cmd[@]}" 2>&1 | tee "$log_path"
install_exit=${PIPESTATUS[0]}
set -e

if [[ "$install_exit" -ne 0 ]]; then
  echo
  echo "Overall: InstallFailed"
  echo "- Exit code: $install_exit"
  echo "- Log: $log_path"
  exit "$install_exit"
fi

echo
echo "Unity Hub module install command completed. Verifying Android support files..."
echo
bash Tools/check_unity_android_support.sh --android-player "$ANDROID_PLAYER_PATH"
