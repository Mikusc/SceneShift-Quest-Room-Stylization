#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

SCAN_PACKAGED=true
SCAN_GENERATED=true
SUMMARY_ONLY=false
MAX_HITS=50

usage() {
  cat <<'USAGE'
Usage:
  bash Tools/scan_predevice_secrets.sh [options]

Options:
  --packaged-only       Scan packaged project config/assets only.
  --generated-only      Scan generated job JSON records only.
  --summary-only        Print only the summary and pass/fail result.
  --max-hits <count>    Maximum findings to print before truncating.
  -h, --help            Show this help.

This is a terminal-side companion to the Unity pre-device readiness secret
checks. It does not print matching line contents, only file paths, line numbers,
and the pattern category that matched.
USAGE
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --packaged-only)
      SCAN_PACKAGED=true
      SCAN_GENERATED=false
      shift
      ;;
    --generated-only)
      SCAN_PACKAGED=false
      SCAN_GENERATED=true
      shift
      ;;
    --summary-only)
      SUMMARY_ONLY=true
      shift
      ;;
    --max-hits)
      MAX_HITS="${2:?missing max hit count}"
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

if ! [[ "$MAX_HITS" =~ ^[0-9]+$ ]]; then
  echo "--max-hits must be a non-negative integer." >&2
  exit 2
fi

PACKAGED_PATHS=()
GENERATED_PATHS=()

add_if_exists() {
  local path="$1"
  if [[ -f "$path" ]]; then
    PACKAGED_PATHS+=("$path")
  fi
}

add_files() {
  local target_array="$1"
  local directory="$2"
  shift 2

  [[ -d "$directory" ]] || return 0

  local find_args=()
  local first=true
  for extension in "$@"; do
    if [[ "$first" == true ]]; then
      first=false
    else
      find_args+=(-o)
    fi
    find_args+=(-name "*${extension}")
  done

  while IFS= read -r -d '' path; do
    case "$target_array" in
      packaged)
        PACKAGED_PATHS+=("$path")
        ;;
      generated)
        GENERATED_PATHS+=("$path")
        ;;
    esac
  done < <(find "$directory" -type f \( "${find_args[@]}" \) -print0)
}

if [[ "$SCAN_PACKAGED" == true ]]; then
  add_if_exists "$ROOT_DIR/Packages/manifest.json"
  add_if_exists "$ROOT_DIR/Packages/packages-lock.json"
  add_files packaged "$ROOT_DIR/ProjectSettings" .asset .json
  add_files packaged "$ROOT_DIR/Assets" .unity .prefab .asset .json .asmdef .inputactions
fi

if [[ "$SCAN_GENERATED" == true ]]; then
  add_files generated "$ROOT_DIR/Library/GeneratedObjectJobs" .json
  add_files generated "$ROOT_DIR/Library/SurfaceTextureJobs" .json
fi

scan_file() {
  local group="$1"
  local path="$2"
  local relative_path="${path#$ROOT_DIR/}"

  perl -ne '
    if (/sk-[A-Za-z0-9_\-]{20,}/) {
      print "$.:openai-style-key\n";
    } elsif (/Bearer\s+[A-Za-z0-9_\-\.]{20,}/i) {
      print "$.:bearer-token-value\n";
    } elsif (/(APIMART_API_KEY|ARK_API_KEY|DEEPSEEK_API_KEY|SCENESHIFT_UPLOAD_TOKEN)\s*=\s*[^\s"'"'"']{8,}/i) {
      print "$.:service-env-assignment\n";
    } elsif (/(?<![A-Za-z])(api[_-]?key|authToken|auth[_-]?token|accessToken|access[_-]?token|uploadToken|upload[_-]?token|token|secret|authorization)\s*[:=]\s*(?!"?(APIMART_API_KEY|ARK_API_KEY|DEEPSEEK_API_KEY|SCENESHIFT_UPLOAD_TOKEN)"?,?\s*$)(?!"?(Bearer\s*)"?\,?\s*$)"?[A-Za-z0-9_\-\.]{20,}/i) {
      print "$.:serialized-secret-value\n";
    }
  ' "$path" | while IFS=: read -r line_number pattern_name; do
    [[ -n "${line_number:-}" && -n "${pattern_name:-}" ]] || continue
    printf '%s\t%s\t%s\t%s\n' "$group" "$relative_path" "$line_number" "$pattern_name"
  done
}

scan_paths() {
  local group="$1"
  shift

  local path
  for path in "$@"; do
    [[ -f "$path" ]] || continue
    scan_file "$group" "$path"
  done
}

hits_file="$(mktemp "${TMPDIR:-/tmp}/sceneshift_secret_scan.XXXXXX")"
trap 'rm -f "$hits_file"' EXIT

if [[ "$SCAN_PACKAGED" == true && "${#PACKAGED_PATHS[@]}" -gt 0 ]]; then
  scan_paths "packaged" "${PACKAGED_PATHS[@]}" >>"$hits_file"
fi

if [[ "$SCAN_GENERATED" == true && "${#GENERATED_PATHS[@]}" -gt 0 ]]; then
  scan_paths "generated" "${GENERATED_PATHS[@]}" >>"$hits_file"
fi

hit_count="$(wc -l <"$hits_file" | tr -d '[:space:]')"
packaged_count="${#PACKAGED_PATHS[@]}"
generated_count="${#GENERATED_PATHS[@]}"

echo "# Pre-Device Secret Scan"
echo
echo "- Packaged files scanned: ${packaged_count}"
echo "- Generated records scanned: ${generated_count}"
echo "- Findings: ${hit_count}"

if [[ "$SUMMARY_ONLY" != true && "$hit_count" -gt 0 ]]; then
  echo
  echo "Findings:"
  printed=0
  while IFS=$'\t' read -r group relative_path line_number pattern_name; do
    [[ -n "${relative_path:-}" ]] || continue
    if [[ "$printed" -ge "$MAX_HITS" ]]; then
      remaining=$((hit_count - printed))
      echo "- ... ${remaining} more finding(s) omitted"
      break
    fi

    echo "- [${group}] ${relative_path}:${line_number} (${pattern_name})"
    printed=$((printed + 1))
  done <"$hits_file"
fi

echo
if [[ "$hit_count" -eq 0 ]]; then
  echo "PASS: No likely long-lived service credentials were found in scanned pre-device inputs."
  exit 0
fi

echo "FAIL: Remove likely credentials from scanned files before packaging or MQDH/headset validation."
exit 1
