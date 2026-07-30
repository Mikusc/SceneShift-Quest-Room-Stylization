#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

EXPECTED_PACKAGE="com.mikusc.sceneshiftroom.comp4145"
EXPECTED_VERSION_CODE=""
EXPECTED_VERSION_NAME=""
MIN_SIZE_BYTES=1048576
ARTIFACT=""

usage() {
  cat <<'USAGE'
Usage:
  bash Tools/verify_mqdh_package_artifact.sh [options] <apk_or_aab>

Options:
  --package <id>          Expected Android package id. Default: com.mikusc.sceneshiftroom.comp4145
  --version-code <code>   Expected Android bundle version code.
  --version-name <name>   Expected bundle version string.
  --min-size <bytes>      Minimum artifact size. Default: 1048576
  -h, --help              Show this help.

Verifies the package artifact before MQDH/test-channel upload. It checks file
presence, ZIP structure, Quest-relevant ARM64 Unity libraries, optional Android
metadata through aapt when available, and obvious long-lived credential strings.
USAGE
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --package)
      EXPECTED_PACKAGE="${2:?missing package id}"
      shift 2
      ;;
    --version-code)
      EXPECTED_VERSION_CODE="${2:?missing version code}"
      shift 2
      ;;
    --version-name)
      EXPECTED_VERSION_NAME="${2:?missing version name}"
      shift 2
      ;;
    --min-size)
      MIN_SIZE_BYTES="${2:?missing min size}"
      shift 2
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      if [[ -n "$ARTIFACT" ]]; then
        echo "Unexpected extra argument: $1" >&2
        usage >&2
        exit 2
      fi
      ARTIFACT="$1"
      shift
      ;;
  esac
done

if [[ -z "$ARTIFACT" ]]; then
  usage >&2
  exit 2
fi

if ! [[ "$MIN_SIZE_BYTES" =~ ^[0-9]+$ ]]; then
  echo "--min-size must be a non-negative integer." >&2
  exit 2
fi

status=0
warnings=0

warn() {
  echo "WARN: $*"
  warnings=$((warnings + 1))
}

fail() {
  echo "FAIL: $*"
  status=1
}

latest_file() {
  local pattern="$1"
  compgen -G "$pattern" >/dev/null || return 0
  ls -t $pattern | head -n 1
}

find_android_tool() {
  local tool_name="$1"
  if command -v "$tool_name" >/dev/null 2>&1; then
    command -v "$tool_name"
    return 0
  fi

  local readiness android_player sdk
  readiness="$(latest_file 'Library/PreDeviceBuildReadinessReports/predevice_build_readiness_*.md')"
  if [[ -n "$readiness" && -f "$readiness" ]]; then
    android_player="$(sed -nE 's/^.*android_build_support_installed.*path=([^|]+).*$/\1/p' "$readiness" | head -n 1 | sed 's/[[:space:]]*$//')"
    android_player="${android_player%%,*}"
    sdk="${android_player}/SDK"
    if [[ -d "$sdk/build-tools" ]]; then
      find "$sdk/build-tools" -type f -name "$tool_name" -perm -111 | sort | tail -n 1
      return 0
    fi
  fi

  if [[ -n "${ANDROID_HOME:-}" && -d "${ANDROID_HOME}/build-tools" ]]; then
    find "${ANDROID_HOME}/build-tools" -type f -name "$tool_name" -perm -111 | sort | tail -n 1
    return 0
  fi

  if [[ -n "${ANDROID_SDK_ROOT:-}" && -d "${ANDROID_SDK_ROOT}/build-tools" ]]; then
    find "${ANDROID_SDK_ROOT}/build-tools" -type f -name "$tool_name" -perm -111 | sort | tail -n 1
    return 0
  fi
}

zip_list() {
  local path="$1"
  if command -v zipinfo >/dev/null 2>&1; then
    zipinfo -1 "$path"
  else
    unzip -Z1 "$path"
  fi
}

contains_zip_entry() {
  local listing="$1"
  local pattern="$2"
  printf '%s\n' "$listing" | grep -Eq "$pattern"
}

scan_secret_stream() {
  perl -ne '
    if (/sk-[A-Za-z0-9_\-]{20,}/) {
      print "openai-style-key\n";
    } elsif (/Bearer\s+[A-Za-z0-9_\-\.]{20,}/i) {
      print "bearer-token-value\n";
    } elsif (/(APIMART_API_KEY|ARK_API_KEY|DEEPSEEK_API_KEY|SCENESHIFT_UPLOAD_TOKEN)\s*=\s*[^\s"'"'"']{8,}/i) {
      print "service-env-assignment\n";
    } elsif (/(?<![A-Za-z])(api[_-]?key|authToken|auth[_-]?token|accessToken|access[_-]?token|uploadToken|upload[_-]?token|token|secret|authorization)\s*[:=]\s*(?!"?(APIMART_API_KEY|ARK_API_KEY|DEEPSEEK_API_KEY|SCENESHIFT_UPLOAD_TOKEN)"?,?\s*$)(?!"?(Bearer\s*)"?\,?\s*$)"?[A-Za-z0-9_\-\.]{20,}/i) {
      print "serialized-secret-value\n";
    }
  '
}

scan_artifact_for_secret_strings() {
  local path="$1"
  local listing="${2:-}"
  local hits_file
  hits_file="$(mktemp "${TMPDIR:-/tmp}/sceneshift_package_secret_hits.XXXXXX")"

  strings "$path" 2>/dev/null | scan_secret_stream >> "$hits_file" || true

  if [[ -n "$listing" ]] && command -v unzip >/dev/null 2>&1; then
    while IFS= read -r entry; do
      [[ -n "$entry" ]] || continue
      unzip -p "$path" "$entry" 2>/dev/null | strings 2>/dev/null | scan_secret_stream >> "$hits_file" || true
    done <<EOF
$listing
EOF
  fi

  sort "$hits_file" | uniq -c
  rm -f "$hits_file"
}

echo "# Verify MQDH Package Artifact"
echo
echo "- Artifact: ${ARTIFACT}"
echo "- Expected package: ${EXPECTED_PACKAGE}"
echo "- Expected version code: ${EXPECTED_VERSION_CODE:-not checked}"
echo "- Expected version name: ${EXPECTED_VERSION_NAME:-not checked}"
echo "- Minimum size bytes: ${MIN_SIZE_BYTES}"
echo

if [[ ! -f "$ARTIFACT" ]]; then
  fail "Artifact does not exist: ${ARTIFACT}"
  echo
  echo "MQDH package artifact verification: Fail"
  exit 1
fi

artifact_size="$(wc -c < "$ARTIFACT" | tr -d '[:space:]')"
echo "- Artifact size bytes: ${artifact_size}"
if [[ "$artifact_size" -lt "$MIN_SIZE_BYTES" ]]; then
  fail "Artifact is smaller than the minimum expected size."
fi

extension="${ARTIFACT##*.}"
case "$extension" in
  apk|APK)
    artifact_type="apk"
    ;;
  aab|AAB)
    artifact_type="aab"
    ;;
  *)
    artifact_type="unknown"
    fail "Artifact extension should be .apk or .aab."
    ;;
esac
echo "- Artifact type: ${artifact_type}"

zip_entries=""
if zip_entries="$(zip_list "$ARTIFACT" 2>/dev/null)"; then
  echo "- ZIP entries: $(printf '%s\n' "$zip_entries" | wc -l | tr -d '[:space:]')"
else
  fail "Artifact is not a readable ZIP/APK/AAB file."
fi

if [[ -n "$zip_entries" ]]; then
  if [[ "$artifact_type" == "apk" ]]; then
    contains_zip_entry "$zip_entries" '^AndroidManifest\.xml$' || fail "APK is missing AndroidManifest.xml."
    contains_zip_entry "$zip_entries" '^lib/arm64-v8a/.*\.so$' || fail "APK is missing arm64-v8a native libraries."
    contains_zip_entry "$zip_entries" '^lib/arm64-v8a/libil2cpp\.so$' || fail "APK is missing lib/arm64-v8a/libil2cpp.so."
    contains_zip_entry "$zip_entries" '^lib/arm64-v8a/libunity\.so$' || fail "APK is missing lib/arm64-v8a/libunity.so."
    if contains_zip_entry "$zip_entries" '^lib/(armeabi-v7a|x86|x86_64)/'; then
      warn "APK contains non-ARM64 native library folders; confirm Player Settings still target ARM64-only for Quest."
    fi
  elif [[ "$artifact_type" == "aab" ]]; then
    contains_zip_entry "$zip_entries" '^base/manifest/AndroidManifest\.xml$' || fail "AAB is missing base/manifest/AndroidManifest.xml."
    contains_zip_entry "$zip_entries" '^base/lib/arm64-v8a/.*\.so$' || fail "AAB is missing base/lib/arm64-v8a native libraries."
    contains_zip_entry "$zip_entries" '^base/lib/arm64-v8a/libil2cpp\.so$' || fail "AAB is missing base/lib/arm64-v8a/libil2cpp.so."
    contains_zip_entry "$zip_entries" '^base/lib/arm64-v8a/libunity\.so$' || fail "AAB is missing base/lib/arm64-v8a/libunity.so."
    if contains_zip_entry "$zip_entries" '^base/lib/(armeabi-v7a|x86|x86_64)/'; then
      warn "AAB contains non-ARM64 native library folders; confirm Player Settings still target ARM64-only for Quest."
    fi
  fi
fi

secret_hits="$(scan_artifact_for_secret_strings "$ARTIFACT" "$zip_entries" || true)"
if [[ -n "$secret_hits" ]]; then
  echo
  echo "Likely credential string categories:"
  printf '%s\n' "$secret_hits" | sed 's/^/- /'
  fail "Artifact strings contain likely long-lived credentials."
else
  echo "- Credential string scan: Pass"
fi

aapt_path="$(find_android_tool aapt | tail -n 1)"
if [[ -n "$aapt_path" && -x "$aapt_path" && "$artifact_type" == "apk" ]]; then
  echo "- aapt: ${aapt_path}"
  set +e
  badging="$("$aapt_path" dump badging "$ARTIFACT" 2>&1)"
  badging_exit=$?
  set -e
  if [[ "$badging_exit" -eq 0 ]]; then
    package_name="$(printf '%s\n' "$badging" | sed -nE "s/^package: name='([^']*)'.*$/\1/p" | head -n 1)"
    version_code="$(printf '%s\n' "$badging" | sed -nE "s/^package: .*versionCode='([^']*)'.*$/\1/p" | head -n 1)"
    version_name="$(printf '%s\n' "$badging" | sed -nE "s/^package: .*versionName='([^']*)'.*$/\1/p" | head -n 1)"
    echo "- aapt package: ${package_name:-unknown}"
    echo "- aapt versionCode: ${version_code:-unknown}"
    echo "- aapt versionName: ${version_name:-unknown}"
    [[ "$package_name" == "$EXPECTED_PACKAGE" ]] || fail "aapt package name mismatch."
    if [[ -n "$EXPECTED_VERSION_CODE" && "$version_code" != "$EXPECTED_VERSION_CODE" ]]; then
      fail "aapt versionCode mismatch."
    fi
    if [[ -n "$EXPECTED_VERSION_NAME" && "$version_name" != "$EXPECTED_VERSION_NAME" ]]; then
      fail "aapt versionName mismatch."
    fi
  else
    warn "aapt could not read APK badging; inspect package metadata manually."
  fi
elif [[ "$artifact_type" == "apk" ]]; then
  warn "aapt not found; package id/version metadata was not verified."
else
  warn "AAB package id/version metadata is not verified by this script; use MQDH/Play tooling or bundletool if needed."
fi

echo
echo "Warnings: ${warnings}"
if [[ "$status" -eq 0 ]]; then
  echo "MQDH package artifact verification: Pass"
else
  echo "MQDH package artifact verification: Fail"
fi

exit "$status"
