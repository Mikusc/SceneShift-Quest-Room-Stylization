#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

OUTPUT_ROOT="Library/MQDHHeadsetEvidence"
WRITE_REPORT=true
REQUIRE_READY=false
UNITY_HUB_BIN="/Applications/Unity Hub.app/Contents/MacOS/Unity Hub"

usage() {
  cat <<'USAGE'
Usage:
  bash Tools/audit_true_device_preflight.sh [options]

Options:
  --no-report       Print the audit without writing Library evidence.
  --require-ready   Exit nonzero unless the audit reaches a package/upload-ready state.
  -h, --help        Show this help.

Summarizes the current true-device preflight state across Unity readiness,
MQDH handoff evidence, terminal local gates, package build reports, package
artifact verification, and headset evidence collection.

This script does not modify Unity scenes, project settings, or packages.
USAGE
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --no-report)
      WRITE_REPORT=false
      shift
      ;;
    --require-ready)
      REQUIRE_READY=true
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

extract_check_detail() {
  local path="$1"
  local check="$2"
  [[ -f "$path" ]] || return 0
  awk -F'|' -v check="$check" '
    $2 ~ check {
      gsub(/^[[:space:]]+|[[:space:]]+$/, "", $4)
      print $4
      exit
    }
  ' "$path"
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

find_adb() {
  local android_player="$1"
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
  if [[ -n "$android_player" && -x "${android_player}/SDK/platform-tools/adb" ]]; then
    printf '%s\n' "${android_player}/SDK/platform-tools/adb"
    return 0
  fi
  return 1
}

print_unity_hub_install_command() {
  local version="$1"
  if [[ -f "Tools/install_unity_android_support.sh" ]]; then
    if [[ -n "$version" ]]; then
      echo "  - \`bash Tools/install_unity_android_support.sh --run --wait-for-close --version $version\`"
    else
      echo "  - \`bash Tools/install_unity_android_support.sh --run --wait-for-close --version <editor-version>\`"
    fi
  fi
  if [[ -x "$UNITY_HUB_BIN" && -n "$version" ]]; then
    echo "  - Raw Unity Hub CLI: \`\"$UNITY_HUB_BIN\" -- --headless install-modules --version $version -m android android-sdk-ndk-tools android-open-jdk\`"
  elif [[ -n "$version" ]]; then
    echo "  - Raw Unity Hub CLI: \`Unity Hub -- --headless install-modules --version $version -m android android-sdk-ndk-tools android-open-jdk\`"
  else
    echo "  - Install Android Build Support, Android SDK & NDK Tools, and OpenJDK for the exact Unity editor."
  fi
}

run_probe() {
  local __status_var="$1"
  local __exit_var="$2"
  local __detail_var="$3"
  shift 3

  local output exit_code detail
  set +e
  output="$("$@" 2>&1)"
  exit_code=$?
  set -e

  detail="$(printf '%s\n' "$output" | sed '/^[[:space:]]*$/d' | tail -n 1)"
  if [[ -z "$detail" ]]; then
    detail="exit=${exit_code}"
  fi

  if [[ "$exit_code" -eq 0 ]]; then
    printf -v "$__status_var" '%s' "Pass"
  else
    printf -v "$__status_var" '%s' "Fail"
  fi
  printf -v "$__exit_var" '%s' "$exit_code"
  printf -v "$__detail_var" '%s' "$detail"
}

path_or_missing() {
  [[ -n "$1" ]] && printf '%s' "$1" || printf 'missing'
}

mtime_seconds() {
  local path="$1"
  [[ -e "$path" ]] || return 1
  if stat -f %m "$path" >/dev/null 2>&1; then
    stat -f %m "$path"
  else
    stat -c %Y "$path"
  fi
}

created_utc="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
timestamp="$(date -u +%Y%m%d_%H%M%S)"
report_path=""
if [[ "$WRITE_REPORT" == true ]]; then
  mkdir -p "$OUTPUT_ROOT"
  report_path="${OUTPUT_ROOT}/true_device_preflight_audit_${timestamp}.md"
fi

readiness="$(latest_file 'Library/PreDeviceBuildReadinessReports/predevice_build_readiness_*.md')"
smoke="$(latest_file 'Library/PreDeviceSmokeReports/predevice_smoke_*.md')"
visual_review="$(latest_file 'Library/PreDeviceVisualEvidence/predevice_visual_review_*.md')"
template="$(latest_file 'Library/MQDHHeadsetEvidence/mqdh_headset_evidence_*.md')"
handoff="$(latest_file 'Library/MQDHHeadsetEvidence/mqdh_handoff_preflight_*.md')"
terminal_suite="$(latest_file 'Library/MQDHHeadsetEvidence/mqdh_terminal_prepackage_suite_*.md')"
handoff_bundle="$(latest_file 'Library/MQDHHeadsetEvidence/handoff_bundle_*/manifest.md')"
local_gate="$(latest_file 'Library/MQDHHeadsetEvidence/predevice_local_gate_*.md')"
package_build_report="$(latest_file 'Library/MQDHPackageBuildReports/mqdh_package_build_*.md')"
headset_evidence_dir="$(ls -dt Library/MQDHHeadsetEvidence/adb_* 2>/dev/null | head -n 1 || true)"
runtime_backend_smoke="$(latest_file 'Library/RuntimeBackendSmokeReports/runtime_backend_azure_smoke_*.md')"

readiness_overall="$(extract_markdown_value "$readiness" "Overall")"
smoke_overall="$(extract_markdown_value "$smoke" "Overall")"
handoff_overall="$(extract_markdown_value "$handoff" "Overall")"
terminal_suite_overall="$(extract_markdown_value "$terminal_suite" "Overall")"
local_gate_overall="$(extract_markdown_value "$local_gate" "Overall")"
local_gate_package_artifact="$(extract_markdown_value "$local_gate" "Package artifact")"
package_build_overall="$(extract_markdown_value "$package_build_report" "Overall")"
package_build_artifact="$(extract_markdown_value "$package_build_report" "Artifact path")"
runtime_backend_smoke_overall="$(extract_markdown_value "$runtime_backend_smoke" "Overall")"
android_status="$(extract_check_status "$readiness" "android_build_support_installed")"
android_detail="$(extract_check_detail "$readiness" "android_build_support_installed")"
active_target_status="$(extract_check_status "$readiness" "active_build_target")"
artifact_set_status="$(extract_check_status "$readiness" "active_predevice_runtime_artifact_set")"
android_player_reported_path="$(printf '%s\n' "$android_detail" | sed -nE 's/^.*path=([^[:space:]]+.*)$/\1/p' | sed 's/[[:space:]]*$//' | head -n 1)"
android_player_path="$(resolve_android_player_path "$android_player_reported_path")"
editor_version="$(extract_editor_version "$android_player_path")"
android_player_fs_exists="unknown"
if [[ -n "$android_player_path" ]]; then
  if [[ -d "$android_player_path" ]]; then
    android_player_fs_exists="true"
  else
    android_player_fs_exists="false"
  fi
fi
adb_path=""
adb_status="Fail"
if adb_path="$(find_adb "$android_player_path")"; then
  adb_status="Pass"
fi

adb_device_status="NotRun"
adb_device_detail="adb binary is missing"
if [[ "$adb_status" == "Pass" ]]; then
  adb_devices_output="$("$adb_path" devices -l 2>&1 || true)"
  if printf '%s\n' "$adb_devices_output" | awk 'BEGIN { ok=0 } /^[^[:space:]]+[[:space:]]+device([[:space:]]|$)/ { ok=1 } END { exit ok ? 0 : 1 }'; then
    adb_device_status="Pass"
    adb_device_detail="$(printf '%s\n' "$adb_devices_output" | awk '/^[^[:space:]]+[[:space:]]+device([[:space:]]|$)/ { print; exit }')"
  else
    adb_device_status="Fail"
    adb_device_detail="no connected ADB device in device state"
  fi
fi

secret_scan_status="missing"
secret_scan_exit=""
secret_scan_detail="Tools/scan_predevice_secrets.sh missing"
if [[ -f "Tools/scan_predevice_secrets.sh" ]]; then
  run_probe secret_scan_status secret_scan_exit secret_scan_detail bash Tools/scan_predevice_secrets.sh --summary-only
fi

bundle_verify_status="missing"
bundle_verify_exit=""
bundle_verify_detail="Tools/verify_mqdh_handoff_bundle.sh missing"
if [[ -f "Tools/verify_mqdh_handoff_bundle.sh" ]]; then
  run_probe bundle_verify_status bundle_verify_exit bundle_verify_detail bash Tools/verify_mqdh_handoff_bundle.sh
fi

local_gate_verify_status="missing"
local_gate_verify_exit=""
local_gate_verify_detail="Tools/verify_predevice_local_gate.sh missing"
final_gate_verify_status="missing"
final_gate_verify_exit=""
final_gate_verify_detail="Tools/verify_predevice_local_gate.sh missing"
if [[ -f "Tools/verify_predevice_local_gate.sh" ]]; then
  run_probe local_gate_verify_status local_gate_verify_exit local_gate_verify_detail bash Tools/verify_predevice_local_gate.sh
  run_probe final_gate_verify_status final_gate_verify_exit final_gate_verify_detail bash Tools/verify_predevice_local_gate.sh --require-package-artifact
fi

package_report_allow_status="missing"
package_report_allow_exit=""
package_report_allow_detail="Tools/verify_mqdh_package_build_report.sh missing"
package_report_final_status="missing"
package_report_final_exit=""
package_report_final_detail="Tools/verify_mqdh_package_build_report.sh missing"
if [[ -f "Tools/verify_mqdh_package_build_report.sh" ]]; then
  run_probe package_report_allow_status package_report_allow_exit package_report_allow_detail bash Tools/verify_mqdh_package_build_report.sh --allow-blocked
  run_probe package_report_final_status package_report_final_exit package_report_final_detail bash Tools/verify_mqdh_package_build_report.sh
fi

headset_verify_status="NotRun"
headset_verify_exit=""
headset_verify_detail="No Library/MQDHHeadsetEvidence/adb_* directory found yet"
if [[ -n "$headset_evidence_dir" && -d "$headset_evidence_dir" ]]; then
  if [[ -f "Tools/verify_mqdh_headset_evidence.sh" ]]; then
    run_probe headset_verify_status headset_verify_exit headset_verify_detail bash Tools/verify_mqdh_headset_evidence.sh "$headset_evidence_dir"
  else
    headset_verify_status="missing"
    headset_verify_detail="Tools/verify_mqdh_headset_evidence.sh missing"
  fi
fi

headset_freshness_status="NotRun"
headset_freshness_detail="No package build report or headset evidence to compare"
if [[ -n "$headset_evidence_dir" && -d "$headset_evidence_dir" && -n "$package_build_report" && -f "$package_build_report" ]]; then
  headset_summary="${headset_evidence_dir}/summary.md"
  headset_mtime_path="$headset_evidence_dir"
  if [[ -f "$headset_summary" ]]; then
    headset_mtime_path="$headset_summary"
  fi
  headset_mtime="$(mtime_seconds "$headset_mtime_path" || echo 0)"
  package_mtime="$(mtime_seconds "$package_build_report" || echo 0)"
  if [[ "$headset_mtime" -ge "$package_mtime" ]]; then
    headset_freshness_status="Pass"
    headset_freshness_detail="headset evidence is newer than or equal to latest package build report"
  else
    headset_freshness_status="Stale"
    headset_freshness_detail="headset evidence predates latest package build report"
  fi
fi

hard_failure=false
for required_status in "$secret_scan_status" "$bundle_verify_status" "$local_gate_verify_status" "$package_report_allow_status"; do
  if [[ "$required_status" != "Pass" ]]; then
    hard_failure=true
  fi
done

overall="NeedsReview"
if [[ "$hard_failure" == true ]]; then
  overall="Fail"
elif [[ "$readiness_overall" == "Fail" && "$android_status" == "Fail" && "$android_player_fs_exists" == "false" ]]; then
  overall="BlockedAndroidSupport"
elif [[ "$readiness_overall" == "Fail" ]]; then
  overall="NeedsUnityEvidenceRefresh"
elif [[ "$readiness_overall" == "PassWithWarnings" ]]; then
  overall="ReadyForAndroidSwitchGate"
elif [[ "$readiness_overall" == "Pass" && "$handoff_overall" == "Pass" && "$package_report_final_status" == "Pass" && "$final_gate_verify_status" == "Pass" ]]; then
  overall="ReadyForMQDHUpload"
elif [[ "$readiness_overall" == "Pass" && "$handoff_overall" == "Pass" ]]; then
  overall="ReadyForPackageBuild"
fi

print_audit() {
  echo "# SceneShift True-Device Preflight Audit"
  echo
  echo "- Created UTC: \`${created_utc}\`"
  echo "- Overall: \`${overall}\`"
  if [[ -n "$report_path" ]]; then
    echo "- Report path: \`${report_path}\`"
  fi
  echo
  echo "## Latest Evidence"
  echo
  echo "- Readiness: \`$(path_or_missing "$readiness")\`"
  echo "- Smoke: \`$(path_or_missing "$smoke")\`"
  echo "- Visual review: \`$(path_or_missing "$visual_review")\`"
  echo "- MQDH template: \`$(path_or_missing "$template")\`"
  echo "- Handoff preflight: \`$(path_or_missing "$handoff")\`"
  echo "- Terminal pre-package suite: \`$(path_or_missing "$terminal_suite")\`"
  echo "- Handoff bundle: \`$(path_or_missing "$handoff_bundle")\`"
  echo "- Pre-device local gate: \`$(path_or_missing "$local_gate")\`"
  echo "- MQDH package build report: \`$(path_or_missing "$package_build_report")\`"
  echo "- Latest headset ADB evidence: \`$(path_or_missing "$headset_evidence_dir")\`"
  echo "- Runtime backend Azure smoke: \`$(path_or_missing "$runtime_backend_smoke")\`"
  echo
  echo "## Gate Matrix"
  echo
  echo "| Gate | Status | Detail |"
  echo "| --- | --- | --- |"
  echo "| Smoke report | \`${smoke_overall:-missing}\` | $(path_or_missing "$smoke") |"
  echo "| Build readiness | \`${readiness_overall:-missing}\` | android=${android_status:-unknown}, activeTarget=${active_target_status:-unknown}, artifactSet=${artifact_set_status:-unknown} |"
  echo "| Android Support files | \`${android_status:-unknown}\` | AndroidPlayerExists=${android_player_fs_exists}, path=${android_player_path:-unknown} |"
  echo "| ADB binary availability | \`${adb_status}\` | ${adb_path:-missing} |"
  echo "| ADB connected device | \`${adb_device_status}\` | ${adb_device_detail} |"
  echo "| Runtime backend Azure smoke | \`${runtime_backend_smoke_overall:-missing}\` | $(path_or_missing "$runtime_backend_smoke") |"
  echo "| MQDH handoff preflight | \`${handoff_overall:-missing}\` | $(path_or_missing "$handoff") |"
  echo "| Terminal pre-package suite | \`${terminal_suite_overall:-missing}\` | $(path_or_missing "$terminal_suite") |"
  echo "| Handoff bundle verification | \`${bundle_verify_status}\` | ${bundle_verify_detail} |"
  echo "| Pre-device secret scan | \`${secret_scan_status}\` | ${secret_scan_detail} |"
  echo "| Pre-package local gate verifier | \`${local_gate_verify_status}\` | ${local_gate_verify_detail} |"
  echo "| Final package local gate verifier | \`${final_gate_verify_status}\` | ${final_gate_verify_detail} |"
  echo "| MQDH package build report | \`${package_build_overall:-missing}\` | artifact=$(path_or_missing "$package_build_artifact") |"
  echo "| Package build report current-state verifier | \`${package_report_allow_status}\` | ${package_report_allow_detail} |"
  echo "| Package build report final verifier | \`${package_report_final_status}\` | ${package_report_final_detail} |"
  echo "| Headset ADB evidence verifier | \`${headset_verify_status}\` | ${headset_verify_detail} |"
  echo "| Headset evidence freshness | \`${headset_freshness_status}\` | ${headset_freshness_detail} |"
  echo
  echo "## Interpretation"
  echo
  case "$overall" in
    BlockedAndroidSupport)
      echo "- Local evidence is internally consistent, but packaging is blocked because Android Build Support is missing for this Unity editor."
      echo "- Install Android Build Support, Android SDK & NDK Tools, and OpenJDK for Unity ${editor_version:-6000.4.3f1}. Close Unity Editor and Unity Hub before using the terminal route:"
      print_unity_hub_install_command "$editor_version"
      echo "- After installation, run:"
      echo "  - \`bash Tools/check_android_support_recovery.sh\`"
      echo "  - \`SceneShift/Validation/Run MQDH Pre-Package Evidence Suite\`"
      echo "  - \`bash Tools/run_mqdh_terminal_prepackage_suite.sh\`"
      ;;
    ReadyForAndroidSwitchGate)
      echo "- Unity evidence is close to package-ready. If the only warning is \`active_build_target=StandaloneOSX\`, switch the Editor build target to Android and rerun readiness."
      ;;
    ReadyForPackageBuild)
      echo "- Pre-device evidence is aligned. Build the APK/AAB through \`SceneShift/Validation/Build MQDH Test Package\`, or run the final local gate manually with \`--package-artifact\`."
      ;;
    ReadyForMQDHUpload)
      echo "- Package build report and final local gate are verified. Continue with MQDH/test-channel upload/install, then collect headset evidence."
      echo "- For the Azure backend path, run \`bash Tools/check_runtime_backend_azure_smoke.sh\` before the paid headset generation attempt if the latest runtime backend smoke row is missing or stale."
      if [[ "$adb_device_status" != "Pass" ]]; then
        echo "- No connected ADB device is currently available; connect/unlock the Quest and accept USB debugging before running \`Tools/install_launch_collect_mqdh_headset_evidence.sh\`."
      fi
      if [[ "$headset_freshness_status" == "Stale" ]]; then
        echo "- Existing headset evidence predates the latest package; do not use it as proof for the current \`HttpBackend\` APK."
      fi
      ;;
    NeedsUnityEvidenceRefresh)
      echo "- Android Support may have changed or Unity evidence is stale/failing. Reopen Unity, let scripts compile, rerun the Unity MQDH suite, then refresh terminal evidence."
      ;;
    Fail)
      echo "- One or more local evidence verifiers failed outside the expected Android Support blocker. Inspect the failed gate rows above before packaging."
      ;;
    *)
      echo "- The current state is not one of the known ready/blocker states. Inspect the latest evidence files listed above."
      ;;
  esac
}

audit_text="$(print_audit)"

if [[ -n "$report_path" ]]; then
  printf '%s\n' "$audit_text" > "$report_path"
fi

printf '%s\n' "$audit_text"

if [[ "$REQUIRE_READY" == true ]]; then
  case "$overall" in
    ReadyForPackageBuild|ReadyForMQDHUpload)
      exit 0
      ;;
    *)
      exit 1
      ;;
  esac
fi

exit 0
