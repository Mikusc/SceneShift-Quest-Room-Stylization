#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

tmpdir="$(mktemp -d "${TMPDIR:-/tmp}/sceneshift_gate_selftest.XXXXXX")"
cleanup() {
  rm -rf "$tmpdir"
}
trap cleanup EXIT

fail() {
  echo "FAIL: $*" >&2
  exit 1
}

expect_exit() {
  local expected="$1"
  local label="$2"
  local output="$3"
  shift 3

  set +e
  "$@" >"$output" 2>&1
  local actual=$?
  set -e

  if [[ "$actual" -ne "$expected" ]]; then
    echo "## ${label}"
    cat "$output"
    fail "${label} exit=${actual}, expected=${expected}"
  fi
}

expect_nonzero() {
  local label="$1"
  local output="$2"
  shift 2

  set +e
  "$@" >"$output" 2>&1
  local actual=$?
  set -e

  if [[ "$actual" -eq 0 ]]; then
    echo "## ${label}"
    cat "$output"
    fail "${label} unexpectedly passed"
  fi
}

assert_contains() {
  local file="$1"
  local text="$2"
  grep -Fq "$text" "$file" || {
    echo "## Missing expected text"
    echo "- File: $file"
    echo "- Text: $text"
    cat "$file"
    exit 1
  }
}

latest_file() {
  local pattern="$1"
  compgen -G "$pattern" >/dev/null || return 0
  ls -t $pattern | head -n 1
}

make_fixture_apk() {
  local path="$1"
  local include_secret="${2:-false}"
  local source_dir="${path}.src"

  mkdir -p "$source_dir/lib/arm64-v8a"
  printf 'manifest' > "$source_dir/AndroidManifest.xml"
  printf 'il2cpp' > "$source_dir/lib/arm64-v8a/libil2cpp.so"
  printf 'unity' > "$source_dir/lib/arm64-v8a/libunity.so"

  if [[ "$include_secret" == true ]]; then
    mkdir -p "$source_dir/assets"
    printf 'Authorization: Bearer abcdefghijklmnopqrstuvwxyz1234567890\n' > "$source_dir/assets/config.txt"
  fi

  (cd "$source_dir" && zip -qr "$path" .)
}

echo "# SceneShift Pre-Device Gate Self-Test"
echo
echo "- Temp dir: $tmpdir"

bundle_verify_before="$tmpdir/bundle_verify_before.out"
if bash Tools/verify_mqdh_handoff_bundle.sh >"$bundle_verify_before" 2>&1; then
  echo "- Existing handoff bundle verification: Pass"
else
  echo "- Existing handoff bundle verification: stale; writing a fresh bundle for self-test"
  bash Tools/write_mqdh_handoff_bundle.sh >"$tmpdir/write_bundle.out" 2>&1
  bundle_verify_after="$tmpdir/bundle_verify_after.out"
  expect_exit 0 "fresh handoff bundle verification" "$bundle_verify_after" bash Tools/verify_mqdh_handoff_bundle.sh
fi

android_support_check="$tmpdir/android_support_check.out"
android_support_available=false
if bash Tools/check_unity_android_support.sh >"$android_support_check" 2>&1; then
  android_support_available=true
  echo "- Android Build Support filesystem check: Pass"
else
  echo "- Android Build Support filesystem check: Blocked"
fi

current_gate="$tmpdir/current_prepackage_gate.md"
if [[ "$android_support_available" == true ]]; then
  expect_exit 0 "fresh pre-package local gate" "$current_gate" bash Tools/run_predevice_local_gate.sh --no-report
  echo "- Fresh pre-package local gate generated and passed for self-test"
else
  expect_nonzero "fresh pre-package local gate" "$current_gate" bash Tools/run_predevice_local_gate.sh --no-report
  echo "- Fresh pre-package local gate generated with Android Support blocker for self-test"
fi
assert_contains "$current_gate" "Package artifact: \`not provided (pre-package gate)\`"

normal_verify="$tmpdir/verify_current_gate.out"
expect_exit 0 "current gate normal verification" "$normal_verify" bash Tools/verify_predevice_local_gate.sh "$current_gate"
assert_contains "$normal_verify" "Local gate verification: Pass"
echo "- Current gate normal verification: Pass"

require_no_package="$tmpdir/verify_current_gate_require_package.out"
expect_nonzero "current gate package-required verification" "$require_no_package" bash Tools/verify_predevice_local_gate.sh --require-package-artifact "$current_gate"
assert_contains "$require_no_package" "Final package gate requires --package-artifact"
echo "- Current pre-package gate is rejected by --require-package-artifact: Pass"

latest_readiness="$(latest_file 'Library/PreDeviceBuildReadinessReports/predevice_build_readiness_*.md')"
latest_smoke="$(latest_file 'Library/PreDeviceSmokeReports/predevice_smoke_*.md')"
latest_visual="$(latest_file 'Library/PreDeviceVisualEvidence/predevice_visual_review_*.md')"
latest_template="$(latest_file 'Library/MQDHHeadsetEvidence/mqdh_headset_evidence_*.md')"
latest_handoff="$(latest_file 'Library/MQDHHeadsetEvidence/mqdh_handoff_preflight_*.md')"
latest_bundle="$(latest_file 'Library/MQDHHeadsetEvidence/handoff_bundle_*/manifest.md')"
for required_latest in "$latest_readiness" "$latest_smoke" "$latest_visual" "$latest_template" "$latest_handoff" "$latest_bundle"; do
  if [[ -z "$required_latest" ]]; then
    fail "latest readiness/smoke/visual/template/handoff/bundle evidence is required for local gate verifier self-test"
  fi
done

failed_gate="$tmpdir/failed_gate.md"
cat >"$failed_gate" <<EOF
# failed_gate

- Overall: \`Fail\`
- Package artifact: \`not provided (pre-package gate)\`
- Latest readiness: $latest_readiness
- Latest smoke: $latest_smoke
- Latest visual review: $latest_visual
- Latest template: $latest_template
- Latest handoff preflight: $latest_handoff
- Latest handoff bundle: $latest_bundle

## Command Output

### Pre-device secret scan

\`\`\`text
# Pre-Device Secret Scan
- Findings: 0
Pre-device secret scan: \`Pass\`
\`\`\`

### MQDH handoff bundle verification

\`\`\`text
Bundle verification: Pass
\`\`\`
EOF

failed_gate_verify="$tmpdir/failed_gate_verify.out"
expect_nonzero "failed local gate verification" "$failed_gate_verify" bash Tools/verify_predevice_local_gate.sh "$failed_gate"
assert_contains "$failed_gate_verify" "Gate report Overall is Fail"
echo "- Local gate verifier rejects Overall=Fail reports: Pass"

pass_apk="$tmpdir/pass.apk"
make_fixture_apk "$pass_apk" false
if [[ "$android_support_available" == true ]]; then
  package_gate="$tmpdir/package_gate.md"
  expect_exit 0 "local gate with clean fixture package" "$package_gate" bash Tools/run_predevice_local_gate.sh --no-report --package-artifact "$pass_apk" --package-min-size 1
  assert_contains "$package_gate" "MQDH package artifact verification: \`Pass\`"
  package_gate_verify="$tmpdir/package_gate_verify.out"
  expect_exit 0 "package-required verification accepts clean fixture gate" "$package_gate_verify" bash Tools/verify_predevice_local_gate.sh --require-package-artifact "$package_gate"
  assert_contains "$package_gate_verify" "Local gate verification: Pass"
  echo "- Package-required verification accepts a clean fixture gate: Pass"
else
  blocked_pass_gate="$tmpdir/blocked_pass_gate.md"
  expect_nonzero "local gate with clean fixture package while Android support is blocked" "$blocked_pass_gate" bash Tools/run_predevice_local_gate.sh --no-report --package-artifact "$pass_apk" --package-min-size 1
  assert_contains "$blocked_pass_gate" "MQDH package artifact verification: \`Pass\`"
  blocked_pass_verify="$tmpdir/blocked_pass_verify.out"
  expect_nonzero "package-required verification rejects blocked clean fixture gate" "$blocked_pass_verify" bash Tools/verify_predevice_local_gate.sh --require-package-artifact "$blocked_pass_gate"
  assert_contains "$blocked_pass_verify" "Final package gate must have Overall=Pass"
  echo "- Package-required verification rejects package gates while Android Support is blocked: Pass"
fi

pass_gate="$tmpdir/pass_gate.md"
cat >"$pass_gate" <<EOF
# pass_gate

- Overall: \`Pass\`
- Package artifact: \`$pass_apk\`
- Latest readiness: $latest_readiness
- Latest smoke: $latest_smoke
- Latest visual review: $latest_visual
- Latest template: $latest_template
- Latest handoff preflight: $latest_handoff
- Latest handoff bundle: $latest_bundle

## Summary

- Pre-device secret scan: \`Pass\` (exit=0)
- MQDH handoff bundle verification: \`Pass\` (exit=0)
- MQDH package artifact verification: \`Pass\` (exit=0)

## Command Output

### Pre-device secret scan

\`\`\`text
# Pre-Device Secret Scan
- Findings: 0
\`\`\`

### MQDH handoff bundle verification

\`\`\`text
Bundle verification: Pass
\`\`\`

### MQDH package artifact verification

\`\`\`text
MQDH package artifact verification: Pass
\`\`\`
EOF

pass_verify="$tmpdir/pass_verify.out"
expect_exit 0 "package-required verification with clean fixture" "$pass_verify" bash Tools/verify_predevice_local_gate.sh --require-package-artifact "$pass_gate"
echo "- Package-required verification accepts a clean fixture APK gate: Pass"

secret_apk="$tmpdir/secret.apk"
make_fixture_apk "$secret_apk" true
secret_gate="$tmpdir/secret_gate.md"
expect_nonzero "local gate with credential fixture package" "$secret_gate" bash Tools/run_predevice_local_gate.sh --no-report --package-artifact "$secret_apk" --package-min-size 1
assert_contains "$secret_gate" "MQDH package artifact verification: \`Fail\`"
secret_verify="$tmpdir/secret_verify.out"
expect_nonzero "package-required verification with credential fixture" "$secret_verify" bash Tools/verify_predevice_local_gate.sh --require-package-artifact "$secret_gate"
assert_contains "$secret_verify" "Gate report does not record a passing package artifact summary"
echo "- Package-required verification rejects a credential-bearing fixture APK gate: Pass"

if [[ -z "$latest_readiness" ]]; then
  fail "latest readiness report is required for package build report verifier self-test"
fi

blocked_package_build_report="$tmpdir/blocked_package_build.md"
cat >"$blocked_package_build_report" <<EOF
# blocked_package_build

- Overall: \`BlockedAndroidSupport\`
- Artifact path: \`$tmpdir/missing.apk\`
- Artifact exists: \`False\`
- Artifact bytes: \`0\`
- Readiness report: \`$latest_readiness\`
- Readiness overall: \`Fail\`
- Unity build result: \`unknown\`

## Checks

| Check | Status | Detail |
| --- | --- | --- |
| android_support_files_present | \`Fail\` | AndroidPlayerPathExists=False |
| readiness_not_fail | \`Fail\` | overall=Fail |
| active_build_target_android | \`Fail\` | active=StandaloneOSX |
EOF

blocked_package_allow="$tmpdir/blocked_package_allow.out"
expect_exit 0 "blocked package build report allowed verification" "$blocked_package_allow" bash Tools/verify_mqdh_package_build_report.sh --allow-blocked "$blocked_package_build_report"
blocked_package_default="$tmpdir/blocked_package_default.out"
expect_nonzero "blocked package build report default verification" "$blocked_package_default" bash Tools/verify_mqdh_package_build_report.sh "$blocked_package_build_report"
assert_contains "$blocked_package_default" "Package build report is not BuiltAndVerified"
echo "- Package build verifier accepts blocked reports only with --allow-blocked: Pass"

built_package_build_report="$tmpdir/built_package_build.md"
pass_apk_bytes="$(wc -c < "$pass_apk" | tr -d ' ')"
cat >"$built_package_build_report" <<EOF
# built_package_build

- Overall: \`BuiltAndVerified\`
- Artifact path: \`$pass_apk\`
- Artifact exists: \`True\`
- Artifact bytes: \`$pass_apk_bytes\`
- Readiness report: \`$latest_readiness\`
- Readiness overall: \`Pass\`
- Unity build result: \`Succeeded\`

## Checks

| Check | Status | Detail |
| --- | --- | --- |
| unity_build_succeeded | \`Pass\` | result=Succeeded |
| final_local_gate_with_package_artifact | \`Pass\` | exit=0 |
| final_local_gate_package_required_verification | \`Pass\` | exit=0 |
EOF

built_package_verify="$tmpdir/built_package_verify.out"
expect_exit 0 "built package build report verification" "$built_package_verify" bash Tools/verify_mqdh_package_build_report.sh "$built_package_build_report"
echo "- Package build verifier accepts a BuiltAndVerified fixture report: Pass"

preflight_audit="$tmpdir/true_device_preflight_audit.out"
expect_exit 0 "true-device preflight audit" "$preflight_audit" bash Tools/audit_true_device_preflight.sh --no-report
assert_contains "$preflight_audit" "SceneShift True-Device Preflight Audit"
assert_contains "$preflight_audit" "Final package local gate verifier"
if [[ "$android_support_available" == true ]]; then
  assert_contains "$preflight_audit" "Android Support files | \`Pass\`"
else
  assert_contains "$preflight_audit" "install_unity_android_support.sh --run --wait-for-close"
fi
echo "- True-device preflight audit command runs: Pass"

android_install_dry_run="$tmpdir/android_install_dry_run.out"
expect_exit 0 "Android Support installer dry-run" "$android_install_dry_run" bash Tools/install_unity_android_support.sh --dry-run
assert_contains "$android_install_dry_run" "Unity Android Support Installer"
assert_contains "$android_install_dry_run" "Overall: DryRun"
assert_contains "$android_install_dry_run" "install_unity_android_support.sh --run --wait-for-close"
echo "- Android Support installer dry-run runs: Pass"

azure_smoke_help="$tmpdir/azure_smoke_help.out"
expect_exit 0 "Azure runtime backend smoke help" "$azure_smoke_help" bash Tools/check_runtime_backend_azure_smoke.sh --help
assert_contains "$azure_smoke_help" "runtime_backend_azure_smoke"
assert_contains "$azure_smoke_help" "does not create a paid"
echo "- Azure runtime backend smoke help runs: Pass"

headset_install_help="$tmpdir/headset_install_help.out"
expect_exit 0 "headset install/launch collector help" "$headset_install_help" bash Tools/install_launch_collect_mqdh_headset_evidence.sh --help
assert_contains "$headset_install_help" "install_launch_collect_mqdh_headset_evidence.sh"
assert_contains "$headset_install_help" "Do not install"
echo "- Headset install/launch collector help runs: Pass"

echo
echo "Pre-device gate self-test: Pass"
