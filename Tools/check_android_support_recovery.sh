#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

ANDROID_PLAYER_PATH=""
UNITY_HUB_BIN="/Applications/Unity Hub.app/Contents/MacOS/Unity Hub"

usage() {
  cat <<'USAGE'
Usage:
  bash Tools/check_android_support_recovery.sh [options]

Options:
  --android-player <path>   Explicit Unity AndroidPlayer directory.
  -h, --help                Show this help.

Use this immediately after installing Android Build Support from Unity Hub.
It checks whether AndroidPlayer/SDK/NDK/OpenJDK/adb exist and whether the
current readiness/template/handoff/terminal-suite/local-gate evidence must be regenerated.
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

  echo "Optional Unity Hub CLI route:"
  if [[ -x "$UNITY_HUB_BIN" && -n "$version" ]]; then
    echo "  \"$UNITY_HUB_BIN\" -- --headless install-modules --version $version -m android android-sdk-ndk-tools android-open-jdk"
  elif [[ -n "$version" ]]; then
    echo "  Unity Hub -- --headless install-modules --version $version -m android android-sdk-ndk-tools android-open-jdk"
  else
    echo "  Unity Hub -- --headless install-modules --version <editor-version> -m android android-sdk-ndk-tools android-open-jdk"
  fi
  echo "Module IDs: android, android-sdk-ndk-tools, android-open-jdk"
}

readiness="$(latest_file 'Library/PreDeviceBuildReadinessReports/predevice_build_readiness_*.md')"
template="$(latest_file 'Library/MQDHHeadsetEvidence/mqdh_headset_evidence_*.md')"
handoff="$(latest_file 'Library/MQDHHeadsetEvidence/mqdh_handoff_preflight_*.md')"
terminal_suite="$(latest_file 'Library/MQDHHeadsetEvidence/mqdh_terminal_prepackage_suite_*.md')"
package_build_report="$(latest_file 'Library/MQDHPackageBuildReports/mqdh_package_build_*.md')"
bundle="$(latest_file 'Library/MQDHHeadsetEvidence/handoff_bundle_*/manifest.md')"
local_gate="$(latest_file 'Library/MQDHHeadsetEvidence/predevice_local_gate_*.md')"

if [[ -z "$ANDROID_PLAYER_PATH" ]]; then
  ANDROID_PLAYER_PATH="$(extract_android_player_path "$readiness")"
fi
requested_android_player_path="$ANDROID_PLAYER_PATH"
ANDROID_PLAYER_PATH="$(resolve_android_player_path "$ANDROID_PLAYER_PATH")"

echo "# Android Support Recovery Check"
echo
echo "- Latest readiness: ${readiness:-missing}"
echo "- Latest MQDH template: ${template:-missing}"
echo "- Latest handoff preflight: ${handoff:-missing}"
echo "- Latest terminal pre-package suite: ${terminal_suite:-missing}"
echo "- Latest MQDH package build report: ${package_build_report:-missing}"
echo "- Latest handoff bundle: ${bundle:-missing}"
echo "- Latest local gate: ${local_gate:-missing}"
if [[ "$requested_android_player_path" != "$ANDROID_PLAYER_PATH" ]]; then
  echo "- AndroidPlayer requested: ${requested_android_player_path:-unknown}"
fi
echo "- AndroidPlayer: ${ANDROID_PLAYER_PATH:-unknown}"
editor_version="$(extract_editor_version "$ANDROID_PLAYER_PATH")"

if [[ -z "$ANDROID_PLAYER_PATH" ]]; then
  echo
  echo "Overall: UnknownAndroidPlayerPath"
  echo
  echo "Run Unity readiness first or pass --android-player."
  exit 2
fi

set +e
android_check_output="$(bash Tools/check_unity_android_support.sh --android-player "$ANDROID_PLAYER_PATH" 2>&1)"
android_check_exit=$?
set -e
echo
echo "## Android Support Files"
echo
printf '%s\n' "$android_check_output"

if [[ "$android_check_exit" -ne 0 ]]; then
  echo
  echo "Overall: MissingAndroidSupport"
  echo
  echo "Next steps:"
  echo "1. Install Android Build Support for Unity ${editor_version:-6000.4.3f1} from Unity Hub."
  echo "2. Include Android SDK & NDK Tools and OpenJDK."
  echo "3. Rerun this script before reopening Unity."
  echo
  echo "Use bash Tools/install_unity_android_support.sh --run --wait-for-close or the Unity Hub CLI command printed in the Android Support Files section above if you prefer terminal installation."
  exit 1
fi

readiness_overall="$(extract_markdown_value "$readiness" "Overall")"
android_readiness_status="$(extract_check_status "$readiness" "android_build_support_installed")"
template_readiness=""
if [[ -f "$template" ]]; then
  template_readiness="$(sed -nE 's/^- Latest build readiness: `([^`]*)`.*/\1/p' "$template" | head -n 1)"
fi
handoff_overall="$(extract_markdown_value "$handoff" "Overall")"
terminal_suite_overall="$(extract_markdown_value "$terminal_suite" "Overall")"
local_gate_overall="$(extract_markdown_value "$local_gate" "Overall")"

needs_regen=0
echo
echo "## Evidence State"
echo
echo "- Readiness overall: ${readiness_overall:-unknown}"
echo "- Readiness android_build_support_installed: ${android_readiness_status:-unknown}"
echo "- Template readiness reference: ${template_readiness:-missing}"
echo "- Handoff overall: ${handoff_overall:-unknown}"
echo "- Terminal suite overall: ${terminal_suite_overall:-unknown}"
echo "- Local gate overall: ${local_gate_overall:-unknown}"

if [[ "$android_readiness_status" != "Pass" || "$readiness_overall" == "Fail" || -z "$readiness_overall" ]]; then
  needs_regen=1
  echo "- Stale readiness: yes"
else
  echo "- Stale readiness: no"
fi

if [[ -n "$readiness" && -n "$template_readiness" && "$template_readiness" != "$ROOT_DIR/$readiness" && "$template_readiness" != "$readiness" ]]; then
  needs_regen=1
  echo "- Stale MQDH template: yes"
else
  echo "- Stale MQDH template: no"
fi

if [[ "$handoff_overall" != "Pass" ]]; then
  needs_regen=1
  echo "- Handoff preflight needs rerun: yes"
else
  echo "- Handoff preflight needs rerun: no"
fi

if [[ "$terminal_suite_overall" != "Pass" ]]; then
  needs_regen=1
  echo "- Terminal pre-package suite needs rerun: yes"
else
  echo "- Terminal pre-package suite needs rerun: no"
fi

if [[ "$local_gate_overall" != "Pass" ]]; then
  needs_regen=1
  echo "- Local gate needs rerun: yes"
else
  echo "- Local gate needs rerun: no"
fi

echo
if [[ "$needs_regen" -ne 0 ]]; then
  echo "Overall: NeedsUnityEvidenceRefresh"
  echo
  echo "Run these after reopening Unity and waiting for compile/import:"
  echo "1. SceneShift/Validation/Run MQDH Pre-Package Evidence Suite"
  echo "2. bash Tools/run_mqdh_terminal_prepackage_suite.sh"
  exit 1
fi

echo "Overall: ReadyForAndroidSwitchGate"
echo
echo "Android Support files are present and local evidence is current. Continue with the Android build-target switch and rerun Unity readiness afterward."
exit 0
