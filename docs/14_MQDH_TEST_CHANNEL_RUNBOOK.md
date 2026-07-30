# 14 MQDH Test Channel Runbook

## Purpose

This runbook is the handoff from Mac-side pre-device validation to the first standalone Quest test build.

It does not replace `docs/12_TRUE_DEVICE_VALIDATION_PLAN.md`. It is the operational checklist for the current Mac route:

```text
Editor / MetaXRSimulator pre-device proof
-> Android Build Support installed
-> Android build target
-> APK/AAB build + final package local gate
-> MQDH or test release-channel install/update
-> standalone headset validation
```

## Current Status

As of the latest checked pre-device and package reports:

- latest build readiness: `Library/PreDeviceBuildReadinessReports/predevice_build_readiness_20260526154331.md`
- readiness overall: `Pass`
- Android Build Support path: `/Applications/Unity/Hub/Editor/6000.4.3f1/PlaybackEngines/AndroidPlayer`
- latest MQDH package build report: `Library/MQDHPackageBuildReports/mqdh_package_build_20260526_154220.md` with `BuiltAndVerified`
- latest APK artifact: `Builds/MQDH/SceneShiftQuest_20260526_154220.apk`
- latest terminal pre-package suite: `Library/MQDHHeadsetEvidence/mqdh_terminal_prepackage_suite_20260526_154332.md` with `Pass`
- latest final local gate: `Library/MQDHHeadsetEvidence/predevice_local_gate_20260526_154335.md` with `Pass`
- latest true-device preflight audit: `Library/MQDHHeadsetEvidence/true_device_preflight_audit_20260526_152920.md` with `ReadyForMQDHUpload`, but it predates the latest `154220` package build
- latest headset ADB evidence: `Library/MQDHHeadsetEvidence/adb_20260526_211143`; `Tools/verify_mqdh_headset_evidence.sh` reports `Pass`
- latest deployed runtime backend smoke: `Library/RuntimeBackendSmokeReports/runtime_backend_azure_smoke_20260527_144851.md` with `Pass`

The Android module blocker is resolved on this machine. Packaging and evidence-collection gates pass for the latest APK. The remaining required step is a deliberately recorded full standalone headset flow that ties style input, target capture, backend polling, non-Box GLB download/load, review controls, and restart restore together.

Resolve the current latest report from terminal when needed:

```bash
ls -t Library/MQDHPackageBuildReports/mqdh_package_build_*.md | head -1
ls -t Library/MQDHHeadsetEvidence/true_device_preflight_audit_*.md | head -1
ls -td Library/MQDHHeadsetEvidence/adb_* | head -1
```

Before upload/install or after a run, the current quick verification commands are:

```bash
bash Tools/verify_mqdh_package_build_report.sh
bash Tools/verify_predevice_local_gate.sh --require-package-artifact
bash Tools/verify_mqdh_headset_evidence.sh
```

If Android Build Support is ever missing on another machine, install these modules for the exact Unity Editor version before packaging:

- Android Build Support
- Android SDK & NDK Tools
- OpenJDK

Safer project helper:

```bash
bash Tools/install_unity_android_support.sh --run --wait-for-close
```

The helper prints the same Unity Hub command, writes an install log under `Library/AndroidSupportInstallLogs/`, and waits for you to manually close Unity Editor and Unity Hub before installing. This avoids the Hub profile-lock state seen when the CLI is invoked while Hub is open.

Raw Unity Hub CLI route:

```bash
"/Applications/Unity Hub.app/Contents/MacOS/Unity Hub" -- --headless install-modules --version 6000.4.3f1 -m android android-sdk-ndk-tools android-open-jdk
```

After installation, reopen the project and rerun:

```text
SceneShift/Validation/Run Pre-Device Build Readiness Report
```

## Pre-Package Gate

Before switching to Android or building, the local evidence set should include:

- latest `Library/PreDeviceSmokeReports/predevice_smoke_*.md`
- latest `Library/PreDeviceVisualEvidence/predevice_visual_review_*.md`
- latest `Library/PreDeviceBuildReadinessReports/predevice_build_readiness_*.md`
- latest `Library/MQDHHeadsetEvidence/mqdh_headset_evidence_*.md`
- latest active `predevice_room_loop_*` request/job/prompt/runtime-submission/runtime-result files under `Library/GeneratedObjectJobs/`
- latest matching runtime model folder under `Application.persistentDataPath/GeneratedObjectRuntimeModels/`
- terminal secret scan result from `bash Tools/scan_predevice_secrets.sh`

Required report state before packaging:

- no `Fail` checks
- no warning except `active_build_target=StandaloneOSX`
- Android manifest or Player Settings provide internet access for runtime GLB download/backend submission
- custom Android manifest is enabled and includes MRUK scene/anchor permissions, headset camera permission, passthrough feature metadata, Quest 3/3S support, HorizonOS SDK metadata, permission dialog, and VR launch metadata
- local test GLB URL and any configured runtime backend endpoint use HTTPS
- runtime model loading path is verified as `Application.persistentDataPath` + `UnityWebRequest` + glTF runtime loading, not `AssetDatabase`
- latest smoke report status is `Pass` or `PassWithManualVisualChecks`
- latest smoke report includes a safe `TABLE` target, `stylization_plan warnings=0`, `runtimeLoaded > 0` queue evidence, runtime-loaded instance metadata, request/job contract traceability evidence, runtime backend artifact traceability evidence, editability/persistence evidence, reset-to-deterministic-fallback evidence, reject/reset release-policy evidence, and dashboard runtime/review controls
- visual review evidence and its screenshot exist, are newer than or equal to the latest smoke report, and the review note explicitly references both that smoke report and screenshot
- packaged config/assets and generated job JSON pass the build-readiness secret scan
- terminal-side `Tools/scan_predevice_secrets.sh` reports zero findings
- active pre-device runtime artifact checks pass: exactly one `predevice_room_loop_*` evidence set remains active, its job/request/prompt/runtime-submission/runtime-result files are present, its persistent runtime GLB folder exists, and the latest smoke report references that request id
- preflight/build tool checks pass in the readiness report: Android Support recovery, terminal pre-package suite, Unity MQDH package build runner, package build report verifier, local gate, package artifact verification, package-required gate verification, gate self-test, handoff bundle verification, and headset ADB evidence collection/verification scripts must be present

That remaining warning is expected only before the intentional platform switch.

## Package Preparation

1. Install Android Build Support if `android_build_support_installed` fails.
2. Before reopening Unity, run `bash Tools/check_android_support_recovery.sh` to confirm `AndroidPlayer`, SDK, NDK, OpenJDK, and `adb` exist on disk and to identify stale Unity/terminal evidence that must be regenerated.
3. Reopen Unity and wait for import/compile to finish.
4. If old Mac pre-device runtime evidence has accumulated, run `SceneShift/Generated Objects/Archive Pre-Device Runtime Artifacts - Keep Latest` so only the latest smoke-linked generated-object evidence remains active.
5. Run `SceneShift/Validation/Run MQDH Pre-Package Evidence Suite`. It runs build readiness, creates the MQDH evidence template, runs MQDH handoff preflight, and writes a suite summary under `Library/MQDHHeadsetEvidence/`.
6. Keep the generated `Library/MQDHHeadsetEvidence/mqdh_headset_evidence_*.md` file open during the headset run.
7. Confirm the generated handoff preflight report passes after the build readiness blocker is resolved and the latest evidence template is generated. It also verifies the template contains latest terminal-suite/handoff/local-gate fields, final APK/AAB gate commands, and the ADB evidence collection command.
8. Run `bash Tools/run_mqdh_terminal_prepackage_suite.sh` from the project root. It writes and verifies the handoff bundle, writes and verifies the pre-package local gate, and records the current handoff status before platform switching or packaging.
9. Run `bash Tools/audit_true_device_preflight.sh` when you need a single status report before Android switching/package work; it writes `Library/MQDHHeadsetEvidence/true_device_preflight_audit_*.md`.
10. Use `bash Tools/scan_predevice_secrets.sh`, `bash Tools/write_mqdh_handoff_bundle.sh`, `bash Tools/verify_mqdh_handoff_bundle.sh`, `bash Tools/run_predevice_local_gate.sh`, `bash Tools/verify_predevice_local_gate.sh`, and `bash Tools/show_mqdh_handoff_status.sh` directly only when debugging a failed step from the terminal suite or audit.
11. If local gate, package verifier, audit, or terminal suite scripts were edited in this iteration, run `bash Tools/test_predevice_gate_scripts.sh` before trusting the updated gate behavior.
12. If only the `StandaloneOSX` warning remains, switch Build Target to Android.
13. Rerun the build readiness report after the switch.
14. Regenerate the MQDH evidence template and rerun the handoff preflight if the readiness report path changed.
15. Build the test APK/AAB, preferably with `SceneShift/Validation/Build MQDH Test Package` so `Library/MQDHPackageBuildReports/mqdh_package_build_*.md` and the final package local gate are produced together.
16. Before MQDH/test-channel upload, run `bash Tools/verify_mqdh_package_build_report.sh`; it should pass without `--allow-blocked` and therefore prove the build report is `BuiltAndVerified`.
17. If building manually, rerun `bash Tools/run_predevice_local_gate.sh --package-artifact <apk-or-aab-path>` and then `bash Tools/verify_predevice_local_gate.sh --require-package-artifact` before MQDH/test-channel upload so APK/AAB verification is captured and enforced in the final gate report. The final gate must be `Overall: Pass`; `BlockedAndroidSupport` remains a pre-package blocker state, not an upload state. Use `bash Tools/verify_mqdh_package_artifact.sh <apk-or-aab-path>` directly only when debugging package-specific failures.
18. Upload or install through the chosen MQDH/test-channel path.
19. After installing/updating the app on the headset, use `bash Tools/collect_mqdh_headset_evidence.sh --package com.mikusc.sceneshiftroom.comp4145 --template <latest Library/MQDHHeadsetEvidence/*.md>` to collect ADB/logcat/screenshot/persistent-file evidence where available.
20. Run `bash Tools/verify_mqdh_headset_evidence.sh` to validate the latest collected `Library/MQDHHeadsetEvidence/adb_*` directory.

## Editor Reload Handling

Use this rule while iterating with Codex and Unity:

- C# script edits usually trigger Unity compile/domain reload without a scene reload popup. After script-only edits, use `Assets/Refresh`, wait for compile/import to finish, and check Console errors.
- Scene, prefab, `ProjectSettings`, package manifest/lockfile, or opened asset state changes can trigger a reload/unsaved-scene prompt. After those changes, click the Unity Editor window, handle the reload prompt, then rerun the relevant validation menu.
- Shell scripts and docs do not require Unity reload. Validate them from terminal unless they are consumed by an Editor validation menu.

Do not add API keys or service credentials to:

- scene files
- ProjectSettings
- APK resources
- generated job JSON
- git

The first headset spike can keep `QuestRuntimeGenerationClient` in `LocalTestModelUrl` mode and use the fixed Khronos Box GLB URL. That only proves runtime loading/review plumbing.

For the real 3D generation closure, build a separate test-channel build with:

- `QuestRuntimeGenerationClient.clientMode = HttpBackend`
- `QuestRuntimeGenerationClient.backendSubmitUrl = https://<public-backend>/v1/runtime-generations`
- no provider/upload/signing credentials serialized into the scene, ProjectSettings, job JSON, or APK
- a server-side backend running either `SCENESHIFT_BACKEND_PROVIDER=seed3d` for direct capture-to-Seed3D validation, or `SCENESHIFT_BACKEND_PROVIDER=full_chain` / `deepseek-image2-seed3d` for the intended DeepSeek V4 -> image2 -> Seed3D chain

Recommended configuration flow:

1. Run `bash Tools/run_runtime_backend_protocol_smoke.sh` to verify the backend protocol shape locally.
2. Start the real backend with server-side provider credentials. Direct Seed3D mode needs `ARK_API_KEY`; full-chain mode needs `DEEPSEEK_API_KEY`, `APIMART_API_KEY` or `IMAGE2_API_KEY`, and `ARK_API_KEY`.
3. Expose it through HTTPS and set either `SCENESHIFT_RUNTIME_BACKEND_URL=https://.../v1/runtime-generations` or `SCENESHIFT_PUBLIC_BASE_URL=https://...` in the Unity Editor process environment.
4. For a local Python Seed3D backend, run `bash Tools/check_runtime_backend_seed3d_preflight.sh`; it must pass before that run can count as true generation validation.
5. Run `SceneShift/Runtime Backend/Configure HttpBackend From Environment`.
6. Run `SceneShift/Runtime Backend/Report Runtime Backend Configuration` and `SceneShift/Validation/Run Pre-Device Build Readiness Report`.
7. Build/upload the test package only after the backend preflight, readiness report, and secret scans remain clean.

For the `www.mikusc.top` Azure Static Web Apps backend, the Unity endpoint is:

```bash
launchctl setenv SCENESHIFT_RUNTIME_BACKEND_URL "https://www.mikusc.top/api/v1/runtime-generations"
```

The Azure app settings must include `AZURE_STORAGE_CONNECTION_STRING`, `SCENESHIFT_PUBLIC_API_BASE_URL=https://www.mikusc.top/api`, and `SCENESHIFT_BACKEND_PROVIDER`. Use `seed3d` with `ARK_API_KEY` for direct generation, or `full_chain` / `deepseek-image2-seed3d` with `DEEPSEEK_API_KEY`, `APIMART_API_KEY` or `IMAGE2_API_KEY`, and `ARK_API_KEY` for the intended headset chain. This serverless backend uses `POST /api/v1/runtime-generations` for submission and `GET /api/v1/runtime-generations/<jobId>` for polling/cached model handoff.

For this Azure path, the local Python `Tools/check_runtime_backend_seed3d_preflight.sh` does not validate the deployed app settings. Treat the Azure application settings plus a deployed endpoint smoke test as the backend readiness check before building the `HttpBackend` Quest package.

Use this no-image deployed smoke before the paid headset run:

```bash
bash Tools/check_runtime_backend_azure_smoke.sh
```

A passing `runtime_backend_azure_smoke_*.md` means the deployed function, storage connection, provider selection, and server-side provider key settings are reachable. It intentionally omits the captured image, so the expected result is a clean `Failed` response saying the upload did not include a readable image file. This does not create a paid provider task and is not true 3D generation evidence. The latest passing smoke currently recorded is `Library/RuntimeBackendSmokeReports/runtime_backend_azure_smoke_20260527_144851.md`.

## Headset Validation Flow

On the headset-installed standalone app:

1. Open the updated test build.
2. Confirm MRUK room readiness and the intended room identity.
3. Select or enter the intended Style.
4. Use one safe `TABLE` target.
5. Trigger the runtime generated-object flow from `Submit+Load`.
6. For local-test validation, confirm the fixed GLB downloads/loads under the app runtime path.
7. For real-backend validation, confirm the dashboard enters backend polling, returns a non-Box generated model URL/hash, and downloads/loads that GLB under the app runtime path.
8. Confirm the object fits the target bounds.
9. Press `Accept`, `Reject`, `Reset`, and apply one bounded correction during separate test passes.
10. Confirm reject/reset releases or hides the runtime model instance without growing loaded-object count or causing visible memory/performance degradation.
11. Restart the app and confirm persisted review state is respected.
12. Capture MQDH/ADB logs and a short headset recording.

ADB helper when the Quest is connected and USB debugging is authorized:

```bash
bash Tools/install_launch_collect_mqdh_headset_evidence.sh \
  --apk Builds/MQDH/SceneShiftQuest_20260526_154220.apk \
  --template Library/MQDHHeadsetEvidence/mqdh_headset_evidence_20260526154332.md
```

That command installs and launches the app, then collects baseline ADB evidence. After the user completes the in-headset style intent, `TABLE` capture, backend polling, GLB load, review controls, and restart restore, rerun `Tools/collect_mqdh_headset_evidence.sh` and `Tools/verify_mqdh_headset_evidence.sh` so the saved files reflect the completed flow rather than launch-only state.

The latest ADB evidence directory, `Library/MQDHHeadsetEvidence/adb_20260526_211143`, already contains package/device/log/screenshot/persistent-file evidence and passes the evidence verifier. Its persistent app files include PCA capture artifacts, runtime backend job records, `GeneratedObjectRuntimeModels/*/mesh_textured_pbr.glb`, and review records. Treat that as support evidence, not final demo closure, unless the accompanying run notes/video explicitly prove the full flow and restart-restore result.

## Evidence To Save

For each headset test run, record:

- generated `Library/MQDHHeadsetEvidence/*.md` path
- generated `Library/MQDHHeadsetEvidence/mqdh_handoff_preflight_*.md` path
- generated `Library/MQDHHeadsetEvidence/mqdh_terminal_prepackage_suite_*.md` path
- generated `Library/MQDHPackageBuildReports/mqdh_package_build_*.md` path, when using the Unity package build runner
- generated `Library/MQDHHeadsetEvidence/handoff_bundle_*/manifest.md` path, when a bundle was created
- build version and Android bundle version code
- APK/AAB path and package verification result from `Tools/run_predevice_local_gate.sh --package-artifact <apk-or-aab-path>` or the direct `Tools/verify_mqdh_package_artifact.sh` debug command
- MQDH/test-channel install method
- headset model and OS version
- `Tools/run_predevice_local_gate.sh` report path before platform switch and the final report path generated with `--package-artifact` plus `Tools/verify_predevice_local_gate.sh --require-package-artifact` result before package/upload
- bundled terminal secret scan status when using a handoff bundle
- room id/name shown in the dashboard
- active Style
- target object id and semantic label
- runtime model URL or failure reason
- runtime backend mode, backend job id, and backend provider mode
- backend job directory under `Library/RuntimeBackendJobs/` for real-backend runs
- `Library/RuntimeBackendSmokeReports/runtime_backend_seed3d_preflight_*.md` for the backend environment used by the test build
- `Library/RuntimeBackendSmokeReports/runtime_backend_azure_smoke_*.md` for the deployed `www.mikusc.top` backend path when Azure Static Web Apps is used
- `.runtime-submission.json` fields proving request JSON, prompt text, image file name/mime/hash/byte length were submitted
- `.runtime-result.json` fields proving backend polling returned model URL/hash or a clear failure
- accept/reject/reset/correction outcome
- reject/reset release or cleanup outcome
- restart-restore outcome
- MQDH/ADB log excerpt for failures
- headset video or screenshots
- output directory from `Tools/collect_mqdh_headset_evidence.sh`
- verification result from `Tools/verify_mqdh_headset_evidence.sh`

## Stop Conditions

Stop and fix locally before another headset package if:

- build readiness has any `Fail`
- Android Build Support is missing
- Unity Console has compile errors
- `PreDeviceSmokeReportRunner` fails an automated check
- runtime GLB loading works only through `AssetDatabase`
- service credentials appear in packaged files or generated records
- `Tools/scan_predevice_secrets.sh` reports any finding
- APK/AAB package artifact fails `Tools/verify_mqdh_package_artifact.sh`
- collected headset evidence fails `Tools/verify_mqdh_headset_evidence.sh`
- rejected/reset generated objects reappear after reload
