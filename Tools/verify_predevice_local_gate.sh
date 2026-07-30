#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

GATE_REPORT=""
REQUIRE_PACKAGE_ARTIFACT=false

usage() {
  cat <<'USAGE'
Usage:
  bash Tools/verify_predevice_local_gate.sh [options] [gate_report]

Options:
  --require-package-artifact   Fail unless the gate report includes a passing
                               APK/AAB package artifact verification.
  -h, --help                   Show this help.

When gate_report is omitted, verifies the latest
Library/MQDHHeadsetEvidence/predevice_local_gate_*.md report.

This verifies that the stored local gate report still references the latest
pre-device readiness, smoke, visual, MQDH template, handoff preflight, and
handoff bundle evidence, recorded a zero-finding secret scan, and is either
Pass or the current pre-package BlockedAndroidSupport state. Overall=Fail is
always rejected.

Use --require-package-artifact for the final post-build/pre-upload gate. That
mode also requires Overall=Pass.
USAGE
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --require-package-artifact)
      REQUIRE_PACKAGE_ARTIFACT=true
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      if [[ -n "$GATE_REPORT" ]]; then
        echo "Unexpected extra argument: $1" >&2
        usage >&2
        exit 2
      fi
      GATE_REPORT="$1"
      shift
      ;;
  esac
done

if [[ -z "$GATE_REPORT" ]]; then
  GATE_REPORT="$(ls -t Library/MQDHHeadsetEvidence/predevice_local_gate_*.md 2>/dev/null | head -n 1 || true)"
fi

if [[ -z "$GATE_REPORT" || ! -f "$GATE_REPORT" ]]; then
  echo "No pre-device local gate report found. Run Tools/run_predevice_local_gate.sh first." >&2
  exit 1
fi

latest_file() {
  local pattern="$1"
  compgen -G "$pattern" >/dev/null || return 0
  ls -t $pattern | head -n 1
}

extract_markdown_value() {
  local path="$1"
  local field="$2"
  sed -nE "s/^- ${field}: \`([^\`]*)\`.*/\\1/p" "$path" | head -n 1
}

report_contains() {
  local value="$1"
  [[ -n "$value" ]] && grep -Fq "$value" "$GATE_REPORT"
}

latest_readiness="$(latest_file 'Library/PreDeviceBuildReadinessReports/predevice_build_readiness_*.md')"
latest_smoke="$(latest_file 'Library/PreDeviceSmokeReports/predevice_smoke_*.md')"
latest_visual="$(latest_file 'Library/PreDeviceVisualEvidence/predevice_visual_review_*.md')"
latest_template="$(latest_file 'Library/MQDHHeadsetEvidence/mqdh_headset_evidence_*.md')"
latest_handoff="$(latest_file 'Library/MQDHHeadsetEvidence/mqdh_handoff_preflight_*.md')"
latest_bundle_manifest="$(latest_file 'Library/MQDHHeadsetEvidence/handoff_bundle_*/manifest.md')"

overall="$(extract_markdown_value "$GATE_REPORT" "Overall")"
package_artifact="$(extract_markdown_value "$GATE_REPORT" "Package artifact")"
status=0

echo "# Verify Pre-Device Local Gate"
echo
echo "- Gate report: ${GATE_REPORT}"
echo "- Overall: ${overall:-unknown}"
echo "- Package artifact: ${package_artifact:-missing}"
echo "- Latest readiness: ${latest_readiness:-missing}"
echo "- Latest smoke: ${latest_smoke:-missing}"
echo "- Latest visual review: ${latest_visual:-missing}"
echo "- Latest template: ${latest_template:-missing}"
echo "- Latest handoff preflight: ${latest_handoff:-missing}"
echo "- Latest handoff bundle: ${latest_bundle_manifest:-missing}"
echo

case "$overall" in
  Pass)
    ;;
  BlockedAndroidSupport)
    if [[ "$REQUIRE_PACKAGE_ARTIFACT" == true ]]; then
      echo "FAIL: Final package gate must have Overall=Pass, but this report is BlockedAndroidSupport."
      status=1
    fi
    ;;
  Fail)
    echo "FAIL: Gate report Overall is Fail."
    status=1
    ;;
  *)
    echo "FAIL: Gate report has unknown Overall value: ${overall:-missing}"
    status=1
    ;;
esac

for required in "$latest_readiness" "$latest_smoke" "$latest_visual" "$latest_template" "$latest_handoff" "$latest_bundle_manifest"; do
  if [[ -z "$required" ]]; then
    echo "STALE: Missing latest source file for one evidence category."
    status=1
    continue
  fi

  if ! report_contains "$required"; then
    echo "STALE: Gate report does not reference latest evidence: $required"
    status=1
  fi
done

if ! grep -Fq "Pre-device secret scan: \`Pass\`" "$GATE_REPORT"; then
  echo "FAIL: Gate report does not record a passing pre-device secret scan."
  status=1
fi

if ! grep -Fq -- "- Findings: 0" "$GATE_REPORT"; then
  echo "FAIL: Gate report does not record zero secret-scan findings."
  status=1
fi

if ! grep -Fq "Bundle verification: Pass" "$GATE_REPORT"; then
  echo "FAIL: Gate report does not record passing handoff bundle verification."
  status=1
fi

if [[ -z "$package_artifact" ]]; then
  echo "FAIL: Gate report does not include a package artifact field."
  status=1
elif [[ "$package_artifact" == "not provided (pre-package gate)" ]]; then
  if [[ "$REQUIRE_PACKAGE_ARTIFACT" == true ]]; then
    echo "FAIL: Final package gate requires --package-artifact, but this report is a pre-package gate."
    status=1
  fi
else
  if [[ ! -f "$package_artifact" ]]; then
    echo "STALE: Package artifact recorded by gate report is missing: $package_artifact"
    status=1
  fi

  if ! grep -Fq "MQDH package artifact verification: \`Pass\`" "$GATE_REPORT"; then
    echo "FAIL: Gate report does not record a passing package artifact summary."
    status=1
  fi

  if ! grep -Fq "MQDH package artifact verification: Pass" "$GATE_REPORT"; then
    echo "FAIL: Gate report does not include passing package artifact command output."
    status=1
  fi
fi

if [[ "$status" -eq 0 ]]; then
  echo "Local gate verification: Pass"
else
  echo "Local gate verification: Fail"
fi

exit "$status"
