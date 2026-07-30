#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

OUTPUT_ROOT="Library/MQDHHeadsetEvidence"

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

extract_line_value() {
  local path="$1"
  local label="$2"
  [[ -f "$path" ]] || return 0
  sed -nE "s/^- ${label}: \`([^\`]*)\`.*/\\1/p" "$path" | head -n 1
}

copy_file() {
  local source="$1"
  local dest_dir="$2"
  [[ -n "$source" && -f "$source" ]] || return 0
  mkdir -p "$dest_dir"
  cp -p "$source" "$dest_dir/"
}

copy_dir() {
  local source="$1"
  local dest_dir="$2"
  [[ -n "$source" && -d "$source" ]] || return 0
  mkdir -p "$dest_dir"
  cp -R "$source" "$dest_dir/"
}

hash_file() {
  local file="$1"
  [[ -f "$file" ]] || return 0
  shasum -a 256 "$file" | awk '{print $1}'
}

timestamp="$(date -u +%Y%m%d_%H%M%S)"
bundle_dir="${OUTPUT_ROOT}/handoff_bundle_${timestamp}"
mkdir -p "$bundle_dir/files"

readiness_md="$(latest_file 'Library/PreDeviceBuildReadinessReports/predevice_build_readiness_*.md')"
readiness_json="${readiness_md%.md}.json"
smoke_md="$(latest_file 'Library/PreDeviceSmokeReports/predevice_smoke_*.md')"
smoke_json="${smoke_md%.md}.json"
visual_review="$(latest_file 'Library/PreDeviceVisualEvidence/predevice_visual_review_*.md')"
visual_image="$(latest_file 'Library/PreDeviceVisualEvidence/*.png')"
template_md="$(latest_file 'Library/MQDHHeadsetEvidence/mqdh_headset_evidence_*.md')"
handoff_md="$(latest_file 'Library/MQDHHeadsetEvidence/mqdh_handoff_preflight_*.md')"
handoff_json="${handoff_md%.md}.json"
runtime_backend_azure_smoke_md="$(latest_file 'Library/RuntimeBackendSmokeReports/runtime_backend_azure_smoke_*.md')"
runtime_backend_azure_smoke_json="${runtime_backend_azure_smoke_md%.md}.json"

readiness_overall="$(extract_markdown_value "$readiness_md" "Overall")"
template_packaging_allowed="$(extract_line_value "$template_md" "Packaging allowed now")"
active_request="$(extract_line_value "$template_md" "Active request")"
runtime_model_folder="$(extract_line_value "$template_md" "Runtime model folder")"

copy_file "$readiness_md" "$bundle_dir/files/readiness"
copy_file "$readiness_json" "$bundle_dir/files/readiness"
copy_file "$smoke_md" "$bundle_dir/files/smoke"
copy_file "$smoke_json" "$bundle_dir/files/smoke"
copy_file "$visual_review" "$bundle_dir/files/visual"
copy_file "$visual_image" "$bundle_dir/files/visual"
copy_file "$template_md" "$bundle_dir/files/mqdh_template"
copy_file "$handoff_md" "$bundle_dir/files/handoff_preflight"
copy_file "$handoff_json" "$bundle_dir/files/handoff_preflight"
copy_file "$runtime_backend_azure_smoke_md" "$bundle_dir/files/runtime_backend"
copy_file "$runtime_backend_azure_smoke_json" "$bundle_dir/files/runtime_backend"

secret_scan_output="$bundle_dir/files/secret_scan/secret_scan.md"
mkdir -p "$(dirname "$secret_scan_output")"
if [[ -f "Tools/scan_predevice_secrets.sh" ]]; then
  if bash Tools/scan_predevice_secrets.sh >"$secret_scan_output"; then
    secret_scan_status="Pass"
  else
    secret_scan_status="Fail"
  fi
else
  secret_scan_status="Missing"
  {
    echo "# Pre-Device Secret Scan"
    echo
    echo "- Findings: unknown"
    echo
    echo "FAIL: Tools/scan_predevice_secrets.sh is missing."
  } >"$secret_scan_output"
fi
secret_scan_findings="$(sed -nE 's/^- Findings: ([0-9]+).*/\1/p' "$secret_scan_output" | head -n 1)"

if [[ -n "$active_request" ]]; then
  mkdir -p "$bundle_dir/files/generated_jobs"
  for artifact in Library/GeneratedObjectJobs/"${active_request}".*; do
    [[ -e "$artifact" ]] || continue
    cp -p "$artifact" "$bundle_dir/files/generated_jobs/"
  done
fi

copy_dir "$runtime_model_folder" "$bundle_dir/files/runtime_models"

manifest="$bundle_dir/manifest.md"
{
  echo "# MQDH Handoff Bundle ${timestamp}"
  echo
  echo "## Summary"
  echo
  echo "- Created UTC: \`$(date -u +%Y-%m-%dT%H:%M:%SZ)\`"
  echo "- Readiness overall: \`${readiness_overall:-unknown}\`"
  echo "- Template packaging allowed: \`${template_packaging_allowed:-unknown}\`"
  echo "- Active request: \`${active_request:-missing}\`"
  echo "- Runtime model folder: \`${runtime_model_folder:-missing}\`"
  echo "- Runtime backend Azure smoke: \`${runtime_backend_azure_smoke_md:-missing}\`"
  echo "- Secret scan status: \`${secret_scan_status:-unknown}\`"
  echo "- Secret scan findings: \`${secret_scan_findings:-unknown}\`"
  echo "- Bundle directory: \`${bundle_dir}\`"
  echo
  echo "## Source Files"
  echo
  for source in \
    "$readiness_md" "$readiness_json" \
    "$smoke_md" "$smoke_json" \
    "$visual_review" "$visual_image" \
    "$template_md" "$handoff_md" "$handoff_json" \
    "$runtime_backend_azure_smoke_md" "$runtime_backend_azure_smoke_json"; do
    if [[ -n "$source" ]]; then
      echo "- \`${source}\`"
    fi
  done
  echo "- \`Tools/scan_predevice_secrets.sh\`"
  echo
  echo "## Bundle Files"
  echo
  while IFS= read -r file; do
    rel="${file#"$bundle_dir"/}"
    digest="$(hash_file "$file")"
    echo "- \`${rel}\` sha256=\`${digest:-n/a}\`"
  done < <(find "$bundle_dir/files" -type f | sort)
  echo
  echo "## Current Blocker Interpretation"
  echo
  if [[ "$secret_scan_status" != "Pass" || "${secret_scan_findings:-unknown}" != "0" ]]; then
    echo "- Do not package yet. The terminal pre-device secret scan did not pass with zero findings."
  elif [[ "$readiness_overall" == "Fail" ]]; then
    echo "- Do not package yet. The latest readiness report is still \`Fail\`."
    echo "- On this machine the known blocker is Android Build Support until a newer readiness report proves otherwise."
  elif [[ "$readiness_overall" == "PassWithWarnings" ]]; then
    echo "- Proceed only if the remaining warning is the deliberate pre-switch \`StandaloneOSX\` active build target warning."
  elif [[ "$readiness_overall" == "Pass" ]]; then
    echo "- Local pre-device reports are ready for the next MQDH/test-channel packaging step."
  else
    echo "- Readiness status is unknown. Inspect the source reports before packaging."
  fi
} >"$manifest"

echo "MQDH handoff bundle written: $bundle_dir"
echo "Manifest: $manifest"
