#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

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

readiness="$(latest_file 'Library/PreDeviceBuildReadinessReports/predevice_build_readiness_*.md')"
template="$(latest_file 'Library/MQDHHeadsetEvidence/mqdh_headset_evidence_*.md')"
handoff="$(latest_file 'Library/MQDHHeadsetEvidence/mqdh_handoff_preflight_*.md')"
if [[ -n "${SCENESHIFT_CURRENT_TERMINAL_SUITE:-}" ]]; then
  terminal_suite="$SCENESHIFT_CURRENT_TERMINAL_SUITE"
else
  terminal_suite="$(latest_file 'Library/MQDHHeadsetEvidence/mqdh_terminal_prepackage_suite_*.md')"
fi
handoff_bundle="$(latest_file 'Library/MQDHHeadsetEvidence/handoff_bundle_*/manifest.md')"
package_build_report="$(latest_file 'Library/MQDHPackageBuildReports/mqdh_package_build_*.md')"
local_gate=""
local_gate_verify_status="skipped"
local_gate_verify_detail="suppressed"
if [[ "${SCENESHIFT_SUPPRESS_LOCAL_GATE:-}" != "1" ]]; then
  local_gate="$(latest_file 'Library/MQDHHeadsetEvidence/predevice_local_gate_*.md')"
  if [[ -f "Tools/verify_predevice_local_gate.sh" ]]; then
    if local_gate_verify_output="$(bash Tools/verify_predevice_local_gate.sh 2>&1)"; then
      local_gate_verify_status="Pass"
    else
      local_gate_verify_status="Fail"
    fi
    local_gate_verify_detail="$(printf '%s\n' "$local_gate_verify_output" | tail -n 1)"
  else
    local_gate_verify_status="missing"
    local_gate_verify_detail="Tools/verify_predevice_local_gate.sh missing"
  fi
fi
smoke="$(latest_file 'Library/PreDeviceSmokeReports/predevice_smoke_*.md')"
visual_review="$(latest_file 'Library/PreDeviceVisualEvidence/predevice_visual_review_*.md')"
headset_evidence_dir="$(ls -dt Library/MQDHHeadsetEvidence/adb_* 2>/dev/null | head -n 1 || true)"

readiness_overall="$(extract_markdown_value "$readiness" "Overall")"
handoff_overall="$(extract_markdown_value "$handoff" "Overall")"
terminal_suite_overall="$(extract_markdown_value "$terminal_suite" "Overall")"
if [[ -n "${SCENESHIFT_CURRENT_TERMINAL_SUITE:-}" && -z "$terminal_suite_overall" ]]; then
  terminal_suite_overall="Running"
fi
package_build_overall="$(extract_markdown_value "$package_build_report" "Overall")"
package_build_artifact="$(extract_markdown_value "$package_build_report" "Artifact path")"
local_gate_overall="$(extract_markdown_value "$local_gate" "Overall")"
local_gate_package_artifact="$(extract_markdown_value "$local_gate" "Package artifact")"
local_gate_package_status="not provided"
if [[ -n "$local_gate_package_artifact" && "$local_gate_package_artifact" != "not provided (pre-package gate)" ]]; then
  if grep -Fq "MQDH package artifact verification: \`Pass\`" "$local_gate"; then
    local_gate_package_status="Pass"
  elif grep -Fq "MQDH package artifact verification: \`Fail\`" "$local_gate"; then
    local_gate_package_status="Fail"
  else
    local_gate_package_status="unknown"
  fi
fi
template_readiness=""
packaging_allowed=""
if [[ -f "$template" ]]; then
  template_readiness="$(sed -nE 's/^- Latest build readiness: `([^`]*)`.*/\1/p' "$template" | head -n 1)"
  packaging_allowed="$(sed -nE 's/^- Packaging allowed now: `([^`]*)`.*/\1/p' "$template" | head -n 1)"
fi

android_status="$(extract_check_status "$readiness" "android_build_support_installed")"
android_detail="$(extract_check_detail "$readiness" "android_build_support_installed")"
active_target_status="$(extract_check_status "$readiness" "active_build_target")"
artifact_set_status="$(extract_check_status "$readiness" "active_predevice_runtime_artifact_set")"
android_player_reported_path="$(printf '%s\n' "$android_detail" | sed -nE 's/^.*path=([^[:space:]]+.*)$/\1/p' | sed 's/[[:space:]]*$//' | head -n 1)"
android_player_path="$(resolve_android_player_path "$android_player_reported_path")"
android_player_fs_exists="unknown"
if [[ -n "$android_player_path" ]]; then
  if [[ -d "$android_player_path" ]]; then
    android_player_fs_exists="true"
  else
    android_player_fs_exists="false"
  fi
fi

secret_scan_status="missing"
secret_scan_detail="Tools/scan_predevice_secrets.sh missing"
if [[ -f "Tools/scan_predevice_secrets.sh" ]]; then
  if secret_scan_output="$(bash Tools/scan_predevice_secrets.sh --summary-only 2>&1)"; then
    secret_scan_status="Pass"
  else
    secret_scan_status="Fail"
  fi

  secret_packaged="$(printf '%s\n' "$secret_scan_output" | sed -nE 's/^- Packaged files scanned: ([0-9]+).*/\1/p' | head -n 1)"
  secret_generated="$(printf '%s\n' "$secret_scan_output" | sed -nE 's/^- Generated records scanned: ([0-9]+).*/\1/p' | head -n 1)"
  secret_findings="$(printf '%s\n' "$secret_scan_output" | sed -nE 's/^- Findings: ([0-9]+).*/\1/p' | head -n 1)"
  secret_scan_detail="packaged=${secret_packaged:-unknown}, generated=${secret_generated:-unknown}, findings=${secret_findings:-unknown}"
fi

headset_evidence_verify_status="missing"
headset_evidence_verify_detail="No Library/MQDHHeadsetEvidence/adb_* directory found yet"
if [[ -n "$headset_evidence_dir" && -d "$headset_evidence_dir" ]]; then
  if [[ -f "Tools/verify_mqdh_headset_evidence.sh" ]]; then
    if headset_evidence_verify_output="$(bash Tools/verify_mqdh_headset_evidence.sh "$headset_evidence_dir" 2>&1)"; then
      headset_evidence_verify_status="Pass"
    else
      headset_evidence_verify_status="Fail"
    fi
    headset_evidence_verify_detail="$(printf '%s\n' "$headset_evidence_verify_output" | tail -n 1)"
  else
    headset_evidence_verify_status="missing"
    headset_evidence_verify_detail="Tools/verify_mqdh_headset_evidence.sh missing"
  fi
fi

package_build_verify_status="missing"
package_build_verify_detail="No Library/MQDHPackageBuildReports/mqdh_package_build_*.md report found yet"
if [[ -n "$package_build_report" && -f "$package_build_report" ]]; then
  if [[ -f "Tools/verify_mqdh_package_build_report.sh" ]]; then
    if package_build_verify_output="$(bash Tools/verify_mqdh_package_build_report.sh --allow-blocked "$package_build_report" 2>&1)"; then
      package_build_verify_status="Pass"
    else
      package_build_verify_status="Fail"
    fi
    package_build_verify_detail="$(printf '%s\n' "$package_build_verify_output" | tail -n 1)"
  else
    package_build_verify_status="missing"
    package_build_verify_detail="Tools/verify_mqdh_package_build_report.sh missing"
  fi
fi

echo "# MQDH Handoff Status"
echo
echo "- Latest readiness: ${readiness:-missing}"
echo "- Readiness overall: ${readiness_overall:-unknown}"
echo "- Android Build Support: ${android_status:-unknown}"
echo "- Android detail: ${android_detail:-missing}"
echo "- AndroidPlayer filesystem exists now: ${android_player_fs_exists}"
echo "- Active build target check: ${active_target_status:-unknown}"
echo "- Active pre-device runtime artifact set: ${artifact_set_status:-unknown}"
echo "- Latest MQDH template: ${template:-missing}"
echo "- Template readiness reference: ${template_readiness:-missing}"
echo "- Template packaging allowed: ${packaging_allowed:-unknown}"
echo "- Latest handoff preflight: ${handoff:-missing}"
echo "- Handoff overall: ${handoff_overall:-unknown}"
echo "- Latest terminal pre-package suite: ${terminal_suite:-missing}"
echo "- Terminal suite overall: ${terminal_suite_overall:-unknown}"
echo "- Latest handoff bundle: ${handoff_bundle:-missing}"
echo "- Latest MQDH package build report: ${package_build_report:-missing}"
echo "- Package build overall: ${package_build_overall:-unknown}"
echo "- Package build artifact: ${package_build_artifact:-missing}"
echo "- Package build report verification: ${package_build_verify_status}"
echo "- Package build report verification detail: ${package_build_verify_detail}"
if [[ "${SCENESHIFT_SUPPRESS_LOCAL_GATE:-}" != "1" ]]; then
  echo "- Latest pre-device local gate: ${local_gate:-missing}"
  echo "- Local gate overall: ${local_gate_overall:-unknown}"
  echo "- Local gate package artifact: ${local_gate_package_artifact:-missing}"
  echo "- Local gate package verification: ${local_gate_package_status}"
  echo "- Local gate verification: ${local_gate_verify_status}"
  echo "- Local gate verification detail: ${local_gate_verify_detail}"
fi
echo "- Latest smoke: ${smoke:-missing}"
echo "- Latest visual review: ${visual_review:-missing}"
echo "- ADB collection script: Tools/collect_mqdh_headset_evidence.sh"
echo "- Latest headset ADB evidence: ${headset_evidence_dir:-missing}"
echo "- Headset evidence verification: ${headset_evidence_verify_status}"
echo "- Headset evidence verification detail: ${headset_evidence_verify_detail}"
echo "- Pre-device secret scan: ${secret_scan_status}"
echo "- Secret scan detail: ${secret_scan_detail}"
echo

if [[ -n "$readiness" && -n "$template_readiness" && "$template_readiness" != "$ROOT_DIR/$readiness" && "$template_readiness" != "$readiness" ]]; then
  echo "WARNING: MQDH template does not reference the latest readiness report. Regenerate it from Unity:"
  echo "  SceneShift/Validation/Create MQDH Headset Evidence Template"
  echo
fi

if [[ "$readiness_overall" == "Fail" ]]; then
  echo "BLOCKED: Build readiness is still Fail. Do not package or start MQDH/headset validation yet."
  if [[ "$android_status" == "Fail" ]]; then
    if [[ "$android_player_fs_exists" == "true" ]]; then
      echo "AndroidPlayer now exists on disk, but the latest readiness report is stale or still failing. Reopen Unity and rerun readiness."
    else
      echo "Primary blocker: install Android Build Support for the exact Unity editor, including SDK/NDK Tools and OpenJDK."
      echo "Check the filesystem state with:"
      echo "  bash Tools/check_unity_android_support.sh"
      echo "After Unity Hub installation, run the recovery check before reopening Unity:"
      echo "  bash Tools/check_android_support_recovery.sh"
      echo "After reopening Unity and rerunning the Unity suite, refresh terminal evidence with:"
      echo "  bash Tools/run_mqdh_terminal_prepackage_suite.sh"
      if [[ -n "$handoff_bundle" ]]; then
        echo "Verify the frozen local evidence bundle with:"
        echo "  bash Tools/verify_mqdh_handoff_bundle.sh"
      fi
    fi
  fi
  exit 1
fi

if [[ "$handoff_overall" == "Fail" ]]; then
  echo "BLOCKED: MQDH handoff preflight is Fail. Regenerate the evidence template and rerun handoff preflight."
  exit 1
fi

if [[ "$secret_scan_status" == "Fail" ]]; then
  echo "BLOCKED: Terminal pre-device secret scan failed. Inspect and remove likely credentials before packaging:"
  echo "  bash Tools/scan_predevice_secrets.sh"
  exit 1
fi

if [[ "$readiness_overall" == "PassWithWarnings" ]]; then
  echo "READY WITH WARNINGS: If the only warning is active_build_target=StandaloneOSX, switch Unity to Android and rerun readiness before packaging."
  if [[ "$terminal_suite_overall" != "Pass" ]]; then
    echo "Before switching/building, refresh terminal evidence with:"
    echo "  bash Tools/run_mqdh_terminal_prepackage_suite.sh"
  fi
  exit 0
fi

if [[ "$readiness_overall" == "Pass" && "$handoff_overall" == "Pass" ]]; then
  if [[ "$local_gate_package_status" == "Pass" ]]; then
    echo "READY: Pre-device reports, MQDH handoff artifacts, terminal suite, and APK/AAB package artifact verification are aligned."
  else
    echo "READY FOR PACKAGE BUILD: Pre-device reports and MQDH handoff artifacts are aligned."
    if [[ "$terminal_suite_overall" != "Pass" ]]; then
      echo "Before building, refresh terminal evidence with:"
      echo "  bash Tools/run_mqdh_terminal_prepackage_suite.sh"
    fi
    echo "Preferred package command:"
    echo "  SceneShift/Validation/Build MQDH Test Package"
    echo "After building APK/AAB, run:"
    echo "  bash Tools/run_predevice_local_gate.sh --package-artifact <apk-or-aab-path>"
    echo "  bash Tools/verify_predevice_local_gate.sh --require-package-artifact"
  fi
  exit 0
fi

echo "UNKNOWN: Reports are present but status is not a known ready state. Inspect the files above."
exit 1
