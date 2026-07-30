#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

ANDROID_PLAYER_PATH=""
UNITY_HUB_BIN="/Applications/Unity Hub.app/Contents/MacOS/Unity Hub"

usage() {
  cat <<'USAGE'
Usage:
  bash Tools/check_unity_android_support.sh [options]

Options:
  --android-player <path>   Explicit Unity AndroidPlayer directory.
  -h, --help                Show this help.

By default this script reads the latest pre-device build readiness report and
checks the AndroidPlayer path reported by Unity. It is useful immediately after
installing Android Build Support from Unity Hub, before reopening Unity.
USAGE
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --android-player)
      ANDROID_PLAYER_PATH="${2:?missing AndroidPlayer path}"
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

print_install_hint() {
  local version="$1"

  echo "Install modules through Unity Hub UI, or use Unity Hub CLI if you want a terminal route:"
  if [[ -f "Tools/install_unity_android_support.sh" ]]; then
    if [[ -n "$version" ]]; then
      echo "  bash Tools/install_unity_android_support.sh --run --wait-for-close --version $version"
    else
      echo "  bash Tools/install_unity_android_support.sh --run --wait-for-close --version <editor-version>"
    fi
    echo "The project helper waits for you to close Unity Editor and Unity Hub before installing."
  fi
  if [[ -x "$UNITY_HUB_BIN" && -n "$version" ]]; then
    echo "  \"$UNITY_HUB_BIN\" -- --headless install-modules --version $version -m android android-sdk-ndk-tools android-open-jdk"
  elif [[ -n "$version" ]]; then
    echo "  Unity Hub -- --headless install-modules --version $version -m android android-sdk-ndk-tools android-open-jdk"
  else
    echo "  Unity Hub -- --headless install-modules --version <editor-version> -m android android-sdk-ndk-tools android-open-jdk"
  fi
  echo "Module IDs: android, android-sdk-ndk-tools, android-open-jdk"
}

if [[ -z "$ANDROID_PLAYER_PATH" ]]; then
  readiness="$(latest_file 'Library/PreDeviceBuildReadinessReports/predevice_build_readiness_*.md')"
  ANDROID_PLAYER_PATH="$(extract_android_player_path "$readiness")"
fi

if [[ -z "$ANDROID_PLAYER_PATH" ]]; then
  echo "Could not determine AndroidPlayer path. Run Unity readiness first or pass --android-player." >&2
  exit 2
fi

REQUESTED_ANDROID_PLAYER_PATH="$ANDROID_PLAYER_PATH"
ANDROID_PLAYER_PATH="$(resolve_android_player_path "$ANDROID_PLAYER_PATH")"

SDK_PATH="${ANDROID_PLAYER_PATH}/SDK"
NDK_PATH="${ANDROID_PLAYER_PATH}/NDK"
OPENJDK_PATH="${ANDROID_PLAYER_PATH}/OpenJDK"
ADB_PATH="${SDK_PATH}/platform-tools/adb"
EDITOR_VERSION="$(extract_editor_version "$ANDROID_PLAYER_PATH")"

android_player_exists=false
sdk_exists=false
ndk_exists=false
openjdk_exists=false
adb_exists=false

[[ -d "$ANDROID_PLAYER_PATH" ]] && android_player_exists=true
[[ -d "$SDK_PATH" ]] && sdk_exists=true
[[ -d "$NDK_PATH" ]] && ndk_exists=true
[[ -d "$OPENJDK_PATH" ]] && openjdk_exists=true
[[ -x "$ADB_PATH" ]] && adb_exists=true

echo "# Unity Android Support Check"
echo
if [[ "$REQUESTED_ANDROID_PLAYER_PATH" != "$ANDROID_PLAYER_PATH" ]]; then
  echo "- AndroidPlayer requested: \`${REQUESTED_ANDROID_PLAYER_PATH}\`"
fi
echo "- AndroidPlayer: \`${ANDROID_PLAYER_PATH}\`"
echo "- Editor version: \`${EDITOR_VERSION:-unknown}\`"
echo "- AndroidPlayer exists: \`${android_player_exists}\`"
echo "- SDK exists: \`${sdk_exists}\`"
echo "- NDK exists: \`${ndk_exists}\`"
echo "- OpenJDK exists: \`${openjdk_exists}\`"
echo "- adb exists: \`${adb_exists}\`"

if [[ "$android_player_exists" == true && "$sdk_exists" == true && "$ndk_exists" == true && "$openjdk_exists" == true && "$adb_exists" == true ]]; then
  echo
  echo "READY: Unity Android Build Support files are present. Reopen Unity and rerun:"
  echo "  SceneShift/Validation/Run Pre-Device Build Readiness Report"
  exit 0
fi

echo
echo "MISSING: Install Android Build Support for this exact Unity editor through Unity Hub, including Android SDK & NDK Tools and OpenJDK."
print_install_hint "$EDITOR_VERSION"
exit 1
