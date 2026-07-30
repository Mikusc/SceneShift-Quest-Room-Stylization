# 12 True Device Validation Plan

## Purpose
This document separates what Editor/Simulator can prove from what must be validated with a Quest headset in the real UNNC IEB office room.

The project has been tested heavily through Unity Editor Play and Quest Link-style workflows, but final validation still needs clear evidence for:
- real MRUK room data
- real passthrough presentation
- headset/controller input behavior
- native passthrough-camera capture when supported
- standalone runtime generated-object loading when the generated-object stretch demo is active
- performance and comfort
- repeatable demo flow in the known office room

## Simulator vs Headset Responsibilities

MetaXRSimulator is useful for:
- editor iteration
- MRUK-like room debugging
- planner/applier logic
- surface material and opening logic
- generated-object job orchestration
- Console hygiene before headset testing

Current Mac validation constraint:
- local iteration should use `MetaXRSimulator` or Unity Editor Play for fast regression
- true-device closed-loop validation should use Meta Quest Developer Hub / release-channel testing: build or upload a test version, update/install it on the headset, then validate inside the standalone app
- do not treat USB `Build And Run` as the default validation path unless the local setup is explicitly changed to support it
- `PreDeviceSmokeReportRunner` is the local automated pre-device gate; it can prove scene wiring and state transitions, but visual quality and dashboard usability still need Game View/Simulator inspection before headset deployment
- `MR_RoomStylization.unity` uses MRUK `DeviceWithPrefabFallback` with the official `Office00` prefab so Mac Editor/Simulator runs can load a room when device scene data is unavailable. This does not replace real room validation on Quest.
- Local visual evidence may be kept under `Library/PreDeviceVisualEvidence/`; use it to support pre-device decisions only, pair each review note with the latest smoke report and screenshot it is closing, and keep any unproven viewpoints or interactions listed as headset validation items.
- `PreDeviceBuildReadinessReportRunner` is the local MQDH/test-channel packaging preflight. It must pass Android Build Support detection before packaging. After that, a `PassWithWarnings` report is acceptable only when the warning is the deliberate active build target still being `StandaloneOSX`; switch to Android before producing the headset package.

Quest headset validation is required for:
- actual room scan/loading behavior
- controller and headset input
- HUD/dashboard readability
- clean view behavior
- real performance
- native passthrough-camera capture support
- runtime GLB download/load and memory behavior on the headset
- user-facing correction usability

Do not infer true-device PCA success from desktop screenshots or MQDH captures.

## Tooling

Use Unity Editor for:
- scene wiring
- console inspection
- import of generated GLBs/prefabs for the current Editor/Quest Link chain
- backend job file inspection

Use Meta Quest Developer Hub / ADB for:
- publishing/installing test builds for headset validation
- updating the headset to the test-channel build before a closed-loop run
- viewing device logs
- recording device video
- pulling persistent app files
- basic performance checks

Use terminal preflight scripts for:
- checking Unity Android Build Support installation and stale local evidence before reopening Unity
- scanning packaged config/assets and generated job JSON for likely long-lived credentials
- running the terminal MQDH pre-package suite after the Unity-side MQDH suite
- summarizing MQDH handoff readiness and frozen evidence bundle state
- verifying that the final APK/AAB has been checked through the same local gate used by the rest of the pre-device evidence

`PreDeviceBuildReadinessReportRunner` should also pass its terminal preflight tool checks before packaging; these checks confirm that the scripts above are present and still expose the expected terminal-suite, local-gate, package-artifact, handoff-bundle, Android-support-recovery, and headset-evidence behaviors.

Use in-headset UI/HUD for:
- target selection feedback
- generation status
- runtime model download/load status
- clean view
- object status cards
- capture trigger confirmation
- style/cache/job state inspection
- generated object accept/reject/reset/correction decisions

## Stage A - Room Load And Base Stylization

Goal:
- prove the canonical UNNC IEB office room can load and receive room stylization.

Validate:
- app launches or Editor Play starts without blocking errors
- MRUK room ready state appears
- active room selection chooses the intended office when multiple rooms exist
- semantic counts are visible
- walls/floor/ceiling stylization appears
- door/window/window-vista overlays appear where appropriate
- clean view can hide MRUK shells and debug/status cards

Pass criteria:
- the user can recognize the real room
- stylization is spatially aligned
- real-world boundaries remain readable
- no blocking Console/device-log errors appear

## Stage B - Furniture Semantics

Goal:
- confirm MRUK furniture labels are good enough for the demo pipeline.

Validate supported semantics:
- `TABLE`
- `STORAGE`
- `SCREEN`
- `COUCH` as MRUK label, shown/handled internally as `Seating`
- `BED`
- `LAMP`
- `PLANT`
- `OTHER`

Pass criteria:
- targetable objects show a stable category/id/score in the HUD or dashboard
- `OTHER` objects can still be captured with the generic prompt path
- missing or wrong labels are documented rather than hidden

If a key office object is mislabeled, use a manual override/correction path for Phase 1 rather than adding heavier perception infrastructure immediately.

## Stage C - Surface / Door / Window / Vista Validation

Goal:
- prove the surface pipeline is visually acceptable in the real office.

Validate:
- dashboard `Generate Room` submits the active `SurfaceTexturePromptSet` through `QuestSurfaceGenerationClient`
- the backend endpoint is HTTPS and public to Quest: `https://www.mikusc.top/api/v1/surface-generations`
- provider credentials for image2/APIMart are present only in backend application settings, not in the APK or Unity scene
- backend status returns ready PNG URLs or clear per-surface failure reasons
- Quest downloads generated PNGs into `Application.persistentDataPath/SurfaceTextureOutputs/`
- wall/floor/ceiling textures use room-scale repeats, not tiny dense wallpaper
- wall seams and wall/floor/ceiling boundaries are acceptable
- trim does not overpower styles like `Arcane Knowledge Chamber`
- doors render as full door/portal panels without cutting a large hole through the wall unless explicitly intended
- valid windows keep an open center and show the vista slightly outside the room
- mistaken small window/frame anchors can be hidden or ignored

Pass criteria:
- at least one headset run proves real backend-created surface PNGs are applied, or explicitly records backend failure while deterministic fallback remains usable
- the room looks coherent from normal user viewpoints
- no obvious gaps expose the passthrough background where a wall/floor boundary should exist
- window scenery does not cover unrelated wall regions or appear as duplicate small panels

## Stage D - Generated Furniture Capture

Goal:
- prove a headset-facing user can capture one or more objects and let the backend chain progress.

Validate:
- Auto Target picks the object the user is looking at
- capture trigger works without watching Unity Inspector
- generated request/job/prompt files are created
- APIMart image generation starts when `APIMART_API_KEY` is visible
- upload bridge writes a public `StylizedImageUrl` when needed
- Seed3D job enters `ModelGenerationSubmitted`
- imported prefabs can be placed only for matching request/object/style
- multiple generated furniture objects can coexist

Pass criteria:
- capturing a new object does not disturb already placed generated furniture
- old captures from another room are not silently reused
- failed/running jobs remain visible in status output

## Stage D2 - Standalone Runtime Generated Replacement

Goal:
- prove the Quest app can complete generated-object replacement without returning to the Unity Editor.

Validate:
- user enters or selects a Style in the headset UI
- capture produces a request containing room id, object id, style id, semantic label, target physical size, crop, and prompt identity
- before publishing a headset build on Mac, `PreDeviceRuntimeLoopValidator` can queue a current-room/current-style `TABLE` request in Editor Play and route it through the local runtime GLB path
- before publishing a headset build on Mac, `PreDeviceSmokeReportRunner` produces a report under `Library/PreDeviceSmokeReports/` with no failed automated checks
- before publishing a headset build on Mac, the smoke report confirms request/job contract traceability for the runtime-loaded object: `.job.json`, `.request.json`, prompt artifact, room/object/style/semantic identity, target bounds/physical dimensions, HTTPS model URL, local runtime GLB file, and `RuntimeLoaded` state
- before publishing a headset build on Mac, the smoke report confirms runtime backend artifact traceability: `.runtime-submission.json` and `.runtime-result.json` preserve request/object/style/semantic identity, source request and prompt paths, target bounds, backend job/result state, and model URL handoff
- for the current narrow spike, `QuestRuntimeGenerationClient` can run `LocalTestModelUrl` mode from dashboard `Submit+Load` without storing cloud service credentials in the APK
- for the narrow runtime-loading spike, dashboard `Load Test GLB` can attach the configured sample GLB URL to the latest captured job/request before the secure backend exists
- for the real-backend test build, `QuestRuntimeGenerationClient` is switched to `HttpBackend` with `backendSubmitUrl=https://.../v1/runtime-generations`
- before building a local Python real-backend package, `bash Tools/check_runtime_backend_seed3d_preflight.sh` passes with `SCENESHIFT_BACKEND_PROVIDER=seed3d`, server-side `ARK_API_KEY`, and HTTPS `SCENESHIFT_PUBLIC_BASE_URL`
- for the intended DeepSeek V4 -> image2 -> Seed3D headset chain, the deployed backend uses `SCENESHIFT_BACKEND_PROVIDER=full_chain` or `deepseek-image2-seed3d`, with `DEEPSEEK_API_KEY`, `APIMART_API_KEY` or `IMAGE2_API_KEY`, and `ARK_API_KEY` stored only in backend application settings
- before using the deployed Azure Static Web Apps backend, `bash Tools/check_runtime_backend_azure_smoke.sh` passes; this is a no-image check that proves deployed backend readiness without creating a paid provider task
- Quest submits multipart metadata/request/prompt/image to a secure backend proxy without local API keys
- backend job states are visible in the headset UI while `RuntimeBackendSubmitted` is polling
- a real generated model URL/hash or clear failure reason returns to the headset
- Quest downloads the model under `Application.persistentDataPath`
- runtime loader instantiates the model without `AssetDatabase`
- model bounds, pivot, and collider policy are applied before placement
- `AnchorThemeApplier` places only the matching request/object/style result
- deterministic fallback remains available if backend, download, or runtime loading fails

Pass criteria:
- one selected `TABLE` or similarly safe target can be generated, runtime-loaded, placed, and reviewed in a standalone Quest build
- `Library/RuntimeBackendSmokeReports/runtime_backend_seed3d_preflight_*.md` records `Overall: Pass` for the local Python backend environment used by that build, or `Library/RuntimeBackendSmokeReports/runtime_backend_azure_smoke_*.md` records `Overall: Pass` for the deployed Azure backend path
- the `RuntimeBackendJobId` and backend job evidence show provider mode `seed3d` for direct generation validation or `full_chain` / `deepseek-image2-seed3d` for the intended full chain, and contain the submitted image/prompt plus provider response; `fixed-url`, `manual`, and `LocalTestModelUrl` evidence are not enough for true closure
- `.runtime-submission.json` records `SourceRequestJson`, `PromptText`, `SourceImageFileName`, `SourceImageMimeType`, `SourceImageSha256`, and `SourceImageByteLength`
- `.runtime-result.json` records `RuntimeModelUrl`, backend job/status URL, and `RuntimeModelHash` when backend caching succeeds
- before true-device closure, the same request-locked runtime load/review behavior has passed in Editor Play or `MetaXRSimulator` using current room/style metadata, and the smoke report confirms the loaded runtime instance has both `RuntimeGeneratedModelInstance` and `StylizedFurnitureInstance` metadata, request/job contract traceability, runtime backend artifact traceability, plus editability/persistence evidence for bounded correction and review-record roundtrip
- before true-device closure, the same local smoke evidence must also show reset-to-deterministic-fallback behavior for the selected object id when a theme fallback exists
- a Mac pre-device report may be `PassWithManualVisualChecks`, but headset closure requires the MQDH/test-channel build to pass the same user-facing flow in the standalone app
- no service credentials are embedded in the APK or written to persistent job records
- the user can continue the room stylization demo after a generation failure
- device logs and HUD/status text identify the exact failure stage when the model does not appear

## Stage E - Correction And Clean View

Goal:
- confirm the user can inspect, accept, reject, reset, and correct generated placements.

Validate:
- object status cards can be shown/hidden
- MRUK shells can be shown/hidden
- clean view leaves only stylized room content and the control panel
- `Rotate 90` changes the selected generated object without breaking bounds fit
- accept finalizes a generated candidate for the current room/object/style
- reject hides or archives the candidate and prevents it from reappearing automatically
- reset restores deterministic fallback for that object
- bounded nudge / yaw / limited scale correction works without breaking anchor alignment
- accepted/rejected/corrected state persists across at least one app restart when the model file remains available
- rejected/reset runtime model GameObjects are released or unloaded cleanly after the decision, while review/job records remain available for restore/retry
- left-hand passthrough-only toggle hides all virtual content and restores it on the next press

Pass criteria:
- the user can switch between debugging view, clean stylized view, and pure passthrough view
- generated object corrections are understandable
- no UI button overlap blocks the critical actions

Still missing before demo-final:
- scene-level headset validation of the new `Accept`, `Reject`, and `Reset` controls
- headset restart-restore validation for accepted/rejected/corrected/reset runtime generated objects
- headset confirmation that rejected runtime generated objects stay hidden after reload or restart
- headset-visible deterministic fallback replacement validation after reset
- headset memory/performance validation that reject/reset releases hidden runtime model instances cleanly
- fine nudge/scale correction UX validation

## Stage F - Performance And Stability

Goal:
- make the demo repeatable and recordable.

Validate:
- frame rate is acceptable in the target office
- no runaway duplicate GLBs or texture allocations occur when switching styles
- no repeated generation starts unless requested
- runtime-loaded models are released or hidden cleanly after reject/reset/style switch
- generated model cleanup/archive tools can remove failed or stale jobs
- Unity/editor crashes do not corrupt the canonical scene
- generated assets are not accidentally committed

Pass criteria:
- one complete demo run can be repeated after restarting Unity
- the project returns to a known state after stopping Play
- logs explain running/failed jobs clearly

## Native PCA Caveat

`DevicePassthroughCaptureService` is the intended Quest Link/headset capture path, but PCA availability depends on:
- headset model
- Horizon OS version
- Meta Horizon Link version
- SDK/package support
- camera permission behavior
- platform policy

Quest 3 / Quest 3S are the expected best-supported targets for current Meta PCA documentation. Quest Pro may behave differently and must be validated empirically. If PCA is unavailable, use simulator/external screenshot fallback for backend debugging and document that true-device capture is not passed.

## Evidence To Collect

For each serious validation run, collect:
- date and hardware
- room id/name shown in UI
- active Style
- semantic counts
- surface cache/job status
- surface backend job/status URL and whether each of `wall`, `floor`, `ceiling`, `door_frame`, `window_frame`, and `window_vista` reached `TextureReady`, `Failed`, or fallback
- furniture job counts
- pre-device smoke report path when running on Mac
- pre-device build readiness report path before packaging
- MQDH pre-package evidence suite report path from `SceneShift/Validation/Run MQDH Pre-Package Evidence Suite`
- MQDH/headset evidence template path from `SceneShift/Validation/Create MQDH Headset Evidence Template`
- MQDH handoff preflight report path from `SceneShift/Validation/Run MQDH Handoff Preflight`
- MQDH package build report path from `SceneShift/Validation/Build MQDH Test Package`, when the package was built through the project runner
- screenshot or visual evidence path when closing manual visual checks locally
- terminal pre-package suite report path from `Tools/run_mqdh_terminal_prepackage_suite.sh`; this should reference the handoff bundle manifest, pre-package local gate report, and current MQDH handoff status
- true-device preflight audit report path from `Tools/audit_true_device_preflight.sh`; this should summarize whether the current state is `BlockedAndroidSupport`, ready for Android switch, ready for package build, or ready for MQDH upload
- MQDH handoff bundle manifest path from `Tools/write_mqdh_handoff_bundle.sh`, when a bundle was created; the bundle should include the terminal secret scan output
- terminal secret scan result from `Tools/scan_predevice_secrets.sh`
- terminal local gate report path from `Tools/run_predevice_local_gate.sh`, plus verification result from `Tools/verify_predevice_local_gate.sh`; after APK/AAB creation this should be a gate report produced with `--package-artifact <apk-or-aab-path>` and verified with `--require-package-artifact`, which requires `Overall: Pass`
- short headset recording
- Console/device-log excerpt for failures
- APK/AAB artifact path and package-check result, preferably from `Tools/run_predevice_local_gate.sh --package-artifact <apk-or-aab-path>` before MQDH/test-channel upload
- `Tools/collect_mqdh_headset_evidence.sh` output directory and `Tools/verify_mqdh_headset_evidence.sh` result when ADB is available
- list of objects captured and whether they placed correctly

## Current Known Risks
- Multiple saved MRUK rooms can cause the wrong room to become active if active-room selection fails.
- Native PCA can fail even if Editor Play works.
- Runtime GLB loading can fail even if Editor import works.
- Backend-generated model URLs can expire or be unreachable from the headset network.
- Generated furniture can drift in scale/orientation/silhouette.
- `OTHER` captures can generate visually plausible but semantically unsafe objects.
- Direct dynamic official UISet sample control instantiation has caused layout problems; the current dashboard prioritizes stable interaction.
- Texture/model memory can spike if too many generated assets remain loaded.

## Smallest Next Validation Task
The current smallest meaningful task is no longer Android setup; the current Mac already has Android Build Support and a verified APK. Use the latest verified package unless code, Player Settings, backend configuration, or build evidence has changed:

- APK: `Builds/MQDH/SceneShiftQuest_20260526_154220.apk`
- package report: `Library/MQDHPackageBuildReports/mqdh_package_build_20260526_154220.md`
- final local gate: `Library/MQDHHeadsetEvidence/predevice_local_gate_20260526_154335.md`
- latest support ADB evidence: `Library/MQDHHeadsetEvidence/adb_20260526_211143`
- latest deployed no-image backend smoke: `Library/RuntimeBackendSmokeReports/runtime_backend_azure_smoke_20260527_144851.md`

Before using or uploading the current package:

1. run `bash Tools/verify_mqdh_package_build_report.sh`,
2. run `bash Tools/verify_predevice_local_gate.sh --require-package-artifact`,
3. run `bash Tools/verify_mqdh_headset_evidence.sh` if you need to confirm the latest support evidence directory is still valid,
4. run `bash Tools/check_runtime_backend_azure_smoke.sh` before a paid full-chain headset attempt.

For the next true-device generated-object closure run:

1. install or update the app on the Quest through MQDH or the configured test channel,
2. open the standalone app and confirm the intended MRUK room identity,
3. enter or select one Style,
4. select one safe `TABLE` target,
5. capture the target in headset,
6. submit through the `HttpBackend` runtime path,
7. confirm backend polling reaches a non-Box `mesh_textured_pbr.glb` URL/hash,
8. confirm the GLB downloads under `Application.persistentDataPath/GeneratedObjectRuntimeModels/`,
9. confirm the model loads, fits the target bounds, and remains spatially aligned,
10. run separate passes for `Accept`, `Reject`, `Reset`, and one bounded correction,
11. restart the app and verify the accepted/corrected/rejected/reset decision is respected,
12. collect fresh ADB/logcat/screenshot/persistent-file evidence after the completed flow with `Tools/collect_mqdh_headset_evidence.sh`,
13. run `bash Tools/verify_mqdh_headset_evidence.sh`,
14. save a short headset recording or MQDH capture that shows the user-facing flow.

If code, package settings, build target, backend URL, or evidence state changes before that run, regenerate the Unity and terminal gates first:

1. run `SceneShift/Validation/Run MQDH Pre-Package Evidence Suite`,
2. run `bash Tools/run_mqdh_terminal_prepackage_suite.sh`,
3. rebuild with `SceneShift/Validation/Build MQDH Test Package`,
4. rerun the package report and final local-gate verifiers.

For the exact packaging and MQDH/test-channel handoff checklist, use `docs/14_MQDH_TEST_CHANNEL_RUNBOOK.md`.
