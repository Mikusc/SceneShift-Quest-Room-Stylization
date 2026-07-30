# SceneShift Office Room

Scene-aware mixed-reality room stylization prototype for **Meta Quest**.

This repository focuses on **one canonical real office room at UNNC IEB** and builds a Roomify-inspired, Meta-first vertical slice for spatially grounded room stylization. The system uses room-general mechanisms, but the current prototype is evaluated in this controlled physical office setting rather than claiming production-ready arbitrary-room support.

Chinese onboarding guide: [START_HERE_CN.md](START_HERE_CN.md).

## Scope

Current priority is **Phase 1: room stylization**.

The core loop is:

1. Load room structure with `MRUK`.
2. Inspect room semantics and visible candidate objects.
3. Choose a built-in or custom user-facing Style.
4. Generate a `StylizationPlan`.
5. Apply stylized surfaces, openings, furniture proxies, generated candidates, and room mood while preserving room readability.
6. Inspect, hide debug shells, reapply, switch Style, and later correct wrong mappings.

Phase 2 NPC work is intentionally out of scope until the stylization slice is stable.

## Current Project Status

Canonical scene:

- `Assets/Scenes/MR_RoomStylization.unity`

Current prototype status as of `2026-06-05`:

- MRUK room loading, semantic bootstrap, active-room refresh, theme/style selection, stylization planning, room mood, surface overrides, furniture placement, headset HUD, and runtime control panel are in place.
- The internal style scaffold is now the generic room style scaffold. User-facing styles such as `Future Research Lab`, `Arcane Knowledge Chamber`, and custom text styles are runtime Style identities for prompts, cache keys, UI labels, and generated artifacts.
- `RuntimeStyleIntentController` supports built-in and freeform styles with deterministic keyword extraction and an optional `DeepSeekStyleIntentProvider` path. Direct provider API-key environment variable names are configured explicitly in the Editor when that side path is being tested; they are not serialized into the current secure Quest `HttpBackend` package.
- The surface pipeline covers wall, floor, ceiling, door, window frame, and window vista. The latest prompt version is `surface_texture_v3_room_scale_openings`, which asks for room-scale materials, full door/portal panels, open-center window treatments, and 16:9 exterior vistas.
- Runtime surface rendering now uses larger world-scale texture repeats, opaque wall/floor/ceiling materials, wall baseboard/crown/corner trims to hide seams, and a full door panel instead of a thin door frame.
- Generated surface textures are consumed from `Library/SurfaceTextureOutputs/` when ready, with theme-material/procedural fallback if a generated texture is missing.
- Furniture capture is no longer table-only. The generated-object path supports MRUK furniture categories including `TABLE`, `STORAGE`, `SCREEN`, `COUCH` mapped internally to `Seating`, `BED`, `LAMP`, `PLANT`, and `OTHER`, with request-locked placement so old captures are not silently reused for a different target.
- `DevicePassthroughCaptureService` is the Quest Link/headset capture probe. It auto-selects the best visible supported MRUK anchor from gaze, shows status in the headset, and uses keyboard/controller input for capture. Native PCA capture still needs true-device validation on a supported Quest runtime.
- The generated-object side path queues jobs under `Library/GeneratedObjectJobs/`, writes Roomify-style prompt artifacts, and can run through `ApimartImageBackendAdapter -> HostedImageUploadBridge -> Seed3DBackendAdapter -> GeneratedObjectModelImporter`.
- The older direct Editor-side furniture worker chain can still be configured manually as `ApimartImageBackendAdapter -> HostedImageUploadBridge -> Seed3DBackendAdapter`, but the Quest standalone `HttpBackend` package disables these direct adapters and uses only the public backend URL.
- Multiple furniture replacements have been validated in Quest Link / Editor Play, including coexisting generated tables and generated objects with request-specific placement. Generated models themselves remain local artifacts and should not be committed by default.
- The runtime UI is usable through the current stable SceneShift dashboard, with clean-view/object-status controls, `Rotate 90` generated-furniture correction, and a left-hand pure-passthrough safety view. Full Meta UISet prefab adoption remains a future UI polish task because dynamic UISet sample prefabs mis-layout in this runtime panel.
- The standalone Quest runtime generated-object spike now has a local test path: a fixed public GLB URL can be routed through `QuestRuntimeGenerationClient`, downloaded to `Application.persistentDataPath`, loaded by `RuntimeGeneratedModelLoader` without `AssetDatabase`, fitted to a safe `TABLE` target, and reviewed with accept/reject/reset/correction persistence in Editor Play.
- Current Mac pre-device evidence exists under `Library/PreDeviceSmokeReports/`, `Library/PreDeviceVisualEvidence/`, and `Library/PreDeviceBuildReadinessReports/`.
- MQDH/test-channel handoff helpers now exist under `SceneShift/Validation/` and `Tools/`: pre-device build readiness, MQDH evidence template generation, MQDH handoff preflight, terminal Android module checks, terminal secret scan, terminal pre-package suite, Unity MQDH package build runner, package artifact verification, terminal handoff status, handoff bundle/local-gate verification, ADB evidence collection, and headset evidence verification.
- The Unity build readiness report also checks that the preflight/build tools are present and expose the expected Android-support recovery, terminal pre-package suite, package build runner, local gate, package-artifact, self-test, handoff-bundle, and headset-evidence capabilities.
- Android Build Support for Unity `6000.4.3f1` is installed on the current Mac, and the latest verified APK is `Builds/MQDH/SceneShiftQuest_20260526_154220.apk` with `Library/MQDHPackageBuildReports/mqdh_package_build_20260526_154220.md` reporting `BuiltAndVerified`. Keep the package name aligned with the existing Meta app record: `com.mikusc.sceneshiftroom.comp4145`.
- A public Azure Static Web Apps backend is available at `https://www.mikusc.top/api/v1/runtime-generations` for the real runtime generation path. `Tools/check_runtime_backend_azure_smoke.sh` can verify the deployed endpoint reaches the provider boundary without uploading an image or creating a paid Seed3D task; the latest passing no-image smoke is `Library/RuntimeBackendSmokeReports/runtime_backend_azure_smoke_20260527_144851.md`.
- Latest headset ADB evidence is `Library/MQDHHeadsetEvidence/adb_20260526_211143`, which passes `Tools/verify_mqdh_headset_evidence.sh` and contains PCA capture artifacts, runtime backend job records, local `mesh_textured_pbr.glb` files, and review records.
- Missing before demo-final status: one explicitly recorded full MQDH/test-channel standalone headset flow with style input, target capture, backend polling, non-Box GLB download/load, accept/reject/reset/correction, app restart-restore evidence, true-device PCA reliability notes, surface-v3 visual validation in the real office, and final UI polish.

For the rolling implementation tracker, see [docs/08_PROGRESS_STATUS.md](docs/08_PROGRESS_STATUS.md).

## Tech Stack

- Unity 6
- Meta XR Core SDK / Meta XR packages
- MRUK
- OpenXR
- URP
- Unity Input System
- Unity MCP relay workflow for editor inspection and iteration
- Meta Quest Developer Hub (`MQDH`) for true-device deployment, capture, and profiling
- Optional external services: DeepSeek style parsing, APIMart `gpt-image-2`, `www.mikusc.top` upload/runtime-generation backend, and Ark Seed3D 2.0

## Quick Start

1. Open the project in Unity `6000.4.3f1`.
2. Open `Assets/Scenes/MR_RoomStylization.unity`.
3. Use `MetaXRSimulator` for development-time validation, or Quest Link / Editor Play for headset-in-the-loop checks.
4. Enter Play mode and wait for MRUK room initialization.
5. Use the runtime panel and HUD to inspect room readiness, semantic counts, active Style, surface cache, furniture queue state, and best-view target.
6. Use the Theme dropdown to choose a built-in Style, or set a freeform style intent through `RuntimeStyleIntentController` before Play if testing custom style generation.
7. For direct Editor-side surface generation, configure the surface adapter API-key environment variable in the scene and confirm the variable is visible to Unity. Surface jobs are written under `Library/SurfaceTextureJobs/` and generated PNGs are downloaded under `Library/SurfaceTextureOutputs/`.
8. For furniture generation, look at a supported object until the HUD shows a valid target, then capture with the configured keyboard/controller input.
9. For the older direct automated furniture path, explicitly configure the adapter environment variable names in the scene and launch Unity with those variables available. Keep this path separate from the secure Quest `HttpBackend` build.
10. Inspect `Library/GeneratedObjectJobs/`, `Library/GeneratedObjectOutputs/`, `Library/GeneratedObjectModels/`, and `Assets/Generated/ThemeAssets/` for job state, prompts, stylized PNGs, hosted URLs, Seed3D results, and imported prefabs.
11. Use `Clean View` for the pure stylized room, `Object Status` for per-object job cards, `Rotate 90` for one-step yaw correction, and the left-hand pure-passthrough toggle when you need to hide all virtual content.
12. Generated models under `Assets/Generated/ThemeAssets/` are local generated artifacts. Do not commit them unless a specific demo artifact is intentionally being preserved.
13. Before packaging for the headset, run `SceneShift/Validation/Run MQDH Pre-Package Evidence Suite`. It runs build readiness, creates the MQDH evidence template, and runs MQDH handoff preflight in one Unity-side pass.
14. Run `bash Tools/run_mqdh_terminal_prepackage_suite.sh` before packaging or platform switching. It writes/verifies the handoff bundle, runs/verifies the local gate, and summarizes the current MQDH handoff state.
15. Use `SceneShift/Validation/Build MQDH Test Package` to build the Android APK/AAB under `Builds/MQDH/`, write `Library/MQDHPackageBuildReports/mqdh_package_build_*.md`, and run the final package local gate automatically.
16. If building manually, rerun `bash Tools/run_predevice_local_gate.sh --package-artifact <path-to-apk-or-aab>` before MQDH/test-channel upload. This includes the same package checks as `bash Tools/verify_mqdh_package_artifact.sh <path-to-apk-or-aab>`.
17. For the existing Meta dashboard app, keep the Android package name as `com.mikusc.sceneshiftroom.comp4145`; changing it requires creating or selecting a different Meta app record.
18. For a real backend build, set `SCENESHIFT_RUNTIME_BACKEND_URL=https://www.mikusc.top/api/v1/runtime-generations` in the Unity Editor process, then run `SceneShift/Runtime Backend/Configure HttpBackend From Environment` and `SceneShift/Runtime Backend/Report Runtime Backend Configuration`.
19. For the deployed Azure backend, run `bash Tools/check_runtime_backend_azure_smoke.sh` before the paid headset generation attempt. A pass means the endpoint, storage, provider selection, and server-side key setting are reachable; it is not true generation evidence because it intentionally omits the image.
20. When moving to true-device validation, use `MQDH` for deployment, capture/recording, logs, performance traces, and pulling generated files from the headset. Follow [docs/14_MQDH_TEST_CHANNEL_RUNBOOK.md](docs/14_MQDH_TEST_CHANNEL_RUNBOOK.md).
21. If ADB is available, `bash Tools/install_launch_collect_mqdh_headset_evidence.sh --apk <latest Builds/MQDH/*.apk> --template <latest Library/MQDHHeadsetEvidence/*.md>` installs, launches, collects, and verifies the initial headset evidence. After the in-headset generation flow, rerun `bash Tools/collect_mqdh_headset_evidence.sh --package com.mikusc.sceneshiftroom.comp4145 --template <latest Library/MQDHHeadsetEvidence/*.md>` and `bash Tools/verify_mqdh_headset_evidence.sh`. The latest support evidence directory is `Library/MQDHHeadsetEvidence/adb_20260526_211143`; treat it as supporting evidence unless the run notes/video also prove the full review and restart-restore flow.

If `android_build_support_installed` fails on another machine, install Android Build Support, Android SDK & NDK Tools, and OpenJDK for Unity `6000.4.3f1`. The project helper is `bash Tools/install_unity_android_support.sh --run --wait-for-close`; after installation, run `bash Tools/check_android_support_recovery.sh`.

Development-stage validation may use `MetaXRSimulator`, but final validation is still defined against the known real UNNC IEB office room.

## Main Runtime Paths

Surface path:

```text
MRUK surface anchors
-> SurfaceTexturePromptBuilder
-> APIMart surface image jobs
-> SurfaceOverrideApplier
-> wall/floor/ceiling material overlays
-> door panel, window frame, window vista
```

Furniture path:

```text
MRUK furniture anchors + best-view capture
-> GeneratedObjectRequest
-> APIMart gpt-image-2
-> hosted upload
-> Ark Seed3D
-> GeneratedObjectModelImporter
-> request-locked runtime placement
```

Standalone runtime backend path:

```text
MRUK target + style intent + capture
-> QuestRuntimeGenerationClient HttpBackend
-> https://www.mikusc.top/api/v1/runtime-generations
-> server-side Seed3D job
-> headset GLB download to Application.persistentDataPath
-> RuntimeGeneratedModelLoader
-> GeneratedObjectReviewController
```

Style path:

```text
built-in/custom user Style
-> deterministic keyword extraction or DeepSeek
-> style-aware prompt/cache identity
-> surfaces and generated furniture share the same visual intent
```

## Repository Guide

- `AGENTS.md`: Codex working rules, priorities, and implementation constraints.
- `START_HERE_CN.md`: Chinese onboarding guide.
- `docs/01_PRODUCT_SCOPE_AND_SUCCESS.md`: Product scope, research framing, success criteria.
- `docs/02_ROOMIFY_TO_META_MAPPING.md`: Roomify-to-Meta technical translation.
- `docs/03_ARCHITECTURE_AND_SCENE_LAYOUT.md`: Scene structure and module layout guidance.
- `docs/04_BACKLOG_AND_MILESTONES.md`: Milestone breakdown.
- `docs/05_DATA_CONTRACTS.md`: Data model definitions and generated-job contracts.
- `docs/06_CUSTOM_MCP_TOOLS.md`: Proposed higher-level Unity MCP tooling.
- `docs/07_CODEX_WORKFLOW_PROMPTS_CN.md`: Reusable Chinese prompts for working with Codex.
- `docs/08_PROGRESS_STATUS.md`: Current rolling tracker.
- `docs/09_GENERATIVE_OBJECT_PIPELINE.md`: Optional Roomify-like generated-object pipeline.
- `docs/10_MANUAL_EXTERNAL_WORKER_RUNBOOK.md`: Manual and automated worker runbook.
- `docs/11_SMOKE_TEST_AND_DEMO_CHECKLIST.md`: Regression/demo checklist.
- `docs/12_TRUE_DEVICE_VALIDATION_PLAN.md`: Quest validation plan.
- `docs/13_MCP_WORKING_CONFIG.md`: last-known working Unity MCP / Codex configuration notes.
- `docs/14_MQDH_TEST_CHANNEL_RUNBOOK.md`: MQDH/test-channel packaging and headset evidence checklist.
- `Tools/install_unity_android_support.sh`: conservative Unity Hub CLI wrapper for installing Android Build Support modules; dry-runs by default and can wait for you to close Unity Editor and Unity Hub before installing with `--run --wait-for-close`.
- `Tools/check_android_support_recovery.sh`: post-Unity-Hub recovery check that detects Android Support files and stale readiness/template/handoff/terminal-suite/local-gate evidence; when files are missing it prints the exact Unity Hub module command for `android`, `android-sdk-ndk-tools`, and `android-open-jdk`.
- `SceneShift/Validation/Build MQDH Test Package`: Unity Editor menu backed by `Assets/Scripts/Editor/MqdhPackageBuildRunner.cs`; builds the current Android APK/AAB mode into `Builds/MQDH/`, writes `Library/MQDHPackageBuildReports/mqdh_package_build_*.md`, and runs the final `--package-artifact` local gate after a successful build.
- `Tools/verify_mqdh_package_build_report.sh`: verifies the latest MQDH package build report; by default it requires `BuiltAndVerified`, while `--allow-blocked` accepts the current blocked pre-build state for status reporting.
- `Tools/run_mqdh_terminal_prepackage_suite.sh`: terminal-side MQDH pre-package suite that writes/verifies the handoff bundle, runs/verifies the pre-device local gate, and records current handoff status in `Library/MQDHHeadsetEvidence/mqdh_terminal_prepackage_suite_*.md`.
- `Tools/audit_true_device_preflight.sh`: one-command status audit that writes `Library/MQDHHeadsetEvidence/true_device_preflight_audit_*.md` and summarizes readiness, handoff, local gate, package build, package artifact, and headset evidence state.
- `Tools/check_runtime_backend_azure_smoke.sh`: deployed `www.mikusc.top` runtime backend smoke that intentionally omits the image, verifies the Azure Function reaches Seed3D provider configuration before rejecting the request, and writes `Library/RuntimeBackendSmokeReports/runtime_backend_azure_smoke_*.md` without creating a paid Seed3D task.
- `Tools/run_predevice_local_gate.sh`: one-command terminal gate that writes `Library/MQDHHeadsetEvidence/predevice_local_gate_*.md` and aggregates secret scan, bundle verification, Android support, handoff status, and optional APK/AAB package artifact verification through `--package-artifact`.
- `Tools/verify_predevice_local_gate.sh`: verifies that the latest local gate report still references current evidence and records zero secret-scan findings; it rejects `Overall: Fail`, treats `BlockedAndroidSupport` only as a pre-package blocker state, and requires `Overall: Pass` with `--require-package-artifact` after APK/AAB creation.
- `Tools/test_predevice_gate_scripts.sh`: shell self-test for local gate, package-artifact, and `--require-package-artifact` verifier behavior.
- `Tools/verify_mqdh_package_artifact.sh`: verifies an APK/AAB before MQDH/test-channel upload, including ZIP structure, ARM64 Unity libraries, optional `aapt` metadata, and embedded credential strings.
- `Tools/check_unity_android_support.sh`: terminal check for Unity AndroidPlayer, SDK, NDK, OpenJDK, and adb after Unity Hub module installation.
- `Tools/scan_predevice_secrets.sh`: terminal-side scan for likely long-lived credentials in packaged project inputs and generated job JSON records before headset packaging.
- `Tools/show_mqdh_handoff_status.sh`: terminal summary of latest readiness/template/handoff/terminal-suite/local-gate state, package-artifact gate status, and current packaging blocker.
- `Tools/write_mqdh_handoff_bundle.sh`: writes a fixed `Library/MQDHHeadsetEvidence/handoff_bundle_*` manifest plus copies of the latest local evidence files and terminal secret scan result for handoff/archive.
- `Tools/verify_mqdh_handoff_bundle.sh`: verifies the latest handoff bundle still references the latest evidence, includes a zero-finding secret scan, and has matching copied-file hashes.
- `Tools/collect_mqdh_headset_evidence.sh`: ADB/logcat/screenshot/persistent-file evidence collector for the installed headset app.
- `Tools/install_launch_collect_mqdh_headset_evidence.sh`: ADB helper that installs the latest gated APK, launches the app, then delegates to the headset evidence collector and verifier.
- `Tools/verify_mqdh_headset_evidence.sh`: verifies collected headset ADB evidence has a connected device, installed package path, logcat, screenshot, and persistent-file evidence or explicit pull errors.

## Key Runtime Modules

- `RoomSemanticBootstrap`
- `ThemeIntentController`
- `RuntimeStyleIntentController`
- `DeepSeekStyleIntentProvider`
- `StylizationPlanner`
- `SurfaceTexturePromptBuilder`
- `ApimartSurfaceTextureBackendAdapter`
- `SurfaceOverrideApplier`
- `AnchorThemeApplier`
- `RoomMoodController`
- `BestViewCaptureService`
- `DevicePassthroughCaptureService`
- `CapturedFurnitureReuseService`
- `GenerativeObjectCoordinator`
- `ApimartImageBackendAdapter`
- `HostedImageUploadBridge`
- `Seed3DBackendAdapter`
- `GenerationQueueStatusService`
- `SceneShiftUISetDashboard`
- `GenerationJobWorldStatusOverlay`
- `GeneratedObjectRotationCorrectionController`
- `QuestRuntimeGenerationClient`
- `RuntimeGeneratedModelLoader`
- `RuntimeGeneratedModelInstance`
- `GeneratedObjectReviewController`
- `PreDeviceRuntimeLoopValidator`
- `PreDeviceSmokeReportRunner`
- `PreDeviceBuildReadinessReportRunner`
- `MqdhHeadsetEvidenceTemplateWriter`
- `MqdhHandoffPreflightReportRunner`
- `PassthroughOnlyVisibilityToggle`
- `GeneratedObjectAssetCleaner`

Planned or incomplete:

- `ObservedObjectCollector`
- `SemanticFusionService`
- headset-validated real-backend generation loop
- headset-validated `CorrectionModeController` and review/restart behavior
- final UI polish with official Meta interaction patterns

## Design Constraints

Every implementation should preserve:

- style consistency
- spatial alignment
- functional consistency
- user editability

Large collision-relevant objects should keep approximate footprint and clearance. Wall, floor, and ceiling changes should prefer materials, trims, decals, lighting, and effects over heavy geometry changes.

## License / Notes

This repository is currently a prototype workspace for coursework/research-style development. Package licenses and third-party assets remain governed by their original publishers.
