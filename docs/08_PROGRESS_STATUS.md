# 08 Progress Status

## Purpose

This file is the rolling implementation tracker for the current vertical slice. Update it after meaningful implementation work, especially when runtime behavior, generated-job contracts, scene wiring, or demo validation changes.

## Snapshot

- Last updated: `2026-07-30`
- Current priority: `Phase 1 - room stylization`
- Canonical setting: `one real UNNC IEB office room`
- Canonical scene: `Assets/Scenes/MR_RoomStylization.unity`
- Current development path: Unity Editor Play plus `MetaXRSimulator` for fast local iteration and pre-device regression, then Meta Quest Developer Hub / test release-channel installation for true-device closure on the current Mac setup
- Current Mac validation constraint: Editor/`MetaXRSimulator` evidence is only a pre-device gate; standalone headset closure must come from a MQDH/test-channel build that is installed or updated on the headset.
- Current demo target: a true-device generated-object vertical slice on Quest, while preserving the coherent deterministic room-stylization fallback
- Target generated-object flow: `user style intent -> headset capture -> secure backend generation -> runtime 3D model load -> request-locked MRUK replacement -> accept/reject/reset/correction`

## 2026-07-30 Audit And Hardening

- Current audit: `docs/13_PROJECT_AUDIT_AND_OPTIMIZATION_2026-07-30.md`.
- Serialized DeepSeek key overrides were removed, and `SceneShiftCredentialBuildGuard` now blocks
  builds that contain non-empty serialized credential fields.
- Direct APIMart, upload, Seed3D, and DeepSeek adapters now default to opt-in; the canonical scene
  keeps them disabled for the secure Quest backend configuration.
- Runtime submission and imported generated-furniture selection now require current
  request/object/room identity instead of accepting an arbitrary latest job or same-object record.
- `NeedsReview`, rejected, reset, unknown-room, and mismatched-theme records no longer count as
  ready restore state.
- Theme rules can strengthen but cannot disable baseline footprint, yaw, or collision constraints.
- All project-owned HTTP call sites now have finite request timeouts.
- Unity MCP is pinned to its resolved Git commit, and glTFast is now an explicit runtime dependency.
- Static checks, secret scan, Git LFS validation, and Unity-bundled Roslyn runtime/editor
  compilation pass.
- Full Unity batchmode validation is currently blocked by a local Licensing Client protocol
  mismatch (`1.18.1`); no new Play Mode, simulator, APK, or headset pass is claimed.

## Milestone Status

| Milestone | Status | Notes |
| --- | --- | --- |
| M0 - Project foundation audit | Mostly done | The 2026-07-30 code/security/reproducibility audit is complete. Recovery/UISet sample clutter, generated-asset ownership, tests, and a clean checkpoint remain. |
| M1 - MRUK semantic debug layer | Mostly done | MRUK room bootstrap, semantic counts, debug shell visibility, active-room refresh, and headset-visible status are in place. |
| M2 - Visible object perception fusion | Partial fallback | Full Image Segmentation fusion is not implemented. Current path uses MRUK furniture labels plus gaze/best-view capture and supports `OTHER` fallback. |
| M3 - Theme system and stylization planning | Mostly done | Generic scaffold plus built-in/custom Style identity is implemented. Style-aware prompt/cache IDs are in use. |
| M4 - Stylization application | In progress | Surfaces, openings, window vista, generated furniture placement, and mood changes are implemented. Surface v3 and runtime-object generation can route through secure HTTPS backend endpoints; deployed endpoint smoke now passes, while real-office/headset visual validation remains. |
| M5 - Manual correction mode | Partial / headset evidence collected | Runtime `Rotate 90`, bounded nudge, accept/reject/reset, and review-record persistence are wired for runtime-generated furniture. Editor Play and pre-device smoke pass the editability/persistence probes, and headset persistent files now include review records; a documented restart-restore pass is still required before demo-final status. |
| M6 - Demo readiness | In progress | Main runtime panel, clean view, object status cards, and queue summaries exist. UI polish and final validation remain. |
| M7 - NPC preparation | Deferred | Still out of scope until Phase 1 is stable. |
| M8 - Generated object enrichment | In progress / elevated stretch target | APIMart image2, hosted upload, Seed3D, editor import, request-locked placement, secure backend proxy, and runtime GLB loading are wired. Latest headset ADB evidence contains real backend model URLs and local `mesh_textured_pbr.glb` files, but final closure still needs a recorded full headset flow and restart-restore validation. |

## Working Features

- `RoomSemanticBootstrap` initializes MRUK room data and exposes semantic counts.
- `RoomSemanticBootstrap` can refresh the active/current room during Play, which helps when Quest contains multiple scanned rooms.
- `ThemeIntentController` uses a generic room scaffold internally.
- `RuntimeStyleIntentController` treats built-in and custom user-facing Styles as first-class runtime identities.
- Built-in Styles include `Future Research Lab` and `Arcane Knowledge Chamber`.
- Custom style text can produce deterministic style/material/color/motif keywords.
- `DeepSeekStyleIntentProvider` can optionally replace deterministic keyword extraction when `DEEPSEEK_API_KEY` is available.
- `StylizationPlanner` maps MRUK room and furniture semantics into a `StylizationPlan`.
- `SurfaceTexturePromptBuilder` writes style-aware surface prompt/job records under `Library/SurfaceTextureJobs/`.
- Surface prompt version is now `surface_texture_v3_room_scale_openings`.
- Surface jobs cover `wall`, `floor`, `ceiling`, `door_frame`, `window_frame`, and `window_vista`.
- `ApimartSurfaceTextureBackendAdapter` can submit active-theme/active-style surface jobs to APIMart `gpt-image-2` for Editor/Quest Link iteration.
- `QuestSurfaceGenerationClient` can submit the active `SurfaceTexturePromptSet` to `https://www.mikusc.top/api/v1/surface-generations`, poll the backend, download wall/floor/ceiling/door/window/vista PNGs, and reapply room surface overrides without storing provider API keys in the APK.
- `SurfaceOverrideApplier` can consume generated surface PNGs from `Library/SurfaceTextureOutputs/`.
- `SurfaceOverrideApplier` falls back to theme materials or procedural textures when generated textures are missing.
- Wall/floor/ceiling overlays are now opaque and use larger world-scale texture repeats to avoid dense wallpaper-like visuals.
- Wall overlays now include baseboard, crown, and corner trim strips to reduce visible MRUK seams and improve wall/floor/ceiling transitions.
- Door anchors now use a flat full-door/portal panel mesh on the room-facing side of the wall instead of cutting a hole in the wall surface.
- Door-host walls use the same wall material, tiling, opacity, and seam logic as other walls.
- Window anchors can still cut a valid window opening out of the wall override so the open-center frame and 16:9 exterior vista remain visible.
- `RoomMoodController` applies theme-linked lighting/ambient mood.
- `SceneShiftUISetDashboard` provides the runtime control panel with theme selection, capture, auto target, reapply, clean view, and object status controls.
- The dashboard currently uses a stable UISet-inspired fallback implementation because dynamic official UISet sample controls mis-layout when instantiated into the runtime panel.
- `GeneratedObjectRotationCorrectionController` adds a dashboard `Rotate 90` action for the currently selected/generated furniture target, using `ObjectId` first and gaze/viewport fallback second.
- `PassthroughOnlyVisibilityToggle` provides a left-controller `Y` / keyboard `Y` safety view that hides all virtual renderers, canvases, rays, shells, generated assets, and surface overlays, then restores them on the next press.
- `GenerationQueueStatusService` summarizes object and surface queue counts for the HUD/panel.
- `GenerationJobWorldStatusOverlay` shows per-furniture job status cards near captured objects.
- `DevicePassthroughCaptureService` tracks supported MRUK furniture anchors from gaze and can create generated-object requests.
- Supported generated-furniture categories now include `TABLE`, `STORAGE`, `SCREEN`, `COUCH` mapped internally to `Seating`, `BED`, `LAMP`, `PLANT`, and `OTHER`.
- `CapturedFurnitureReuseService` supports reusing previous capture data across Styles when the physical object image is still valid.
- `BestViewCaptureService` remains useful for simulator/external-screenshot tests.
- `GenerativeObjectCoordinator` writes generated-object `.job.json` and prompt artifacts.
- `ApimartImageBackendAdapter` can process `CaptureReady -> StylizedImageReady` with APIMart `gpt-image-2`.
- `HostedImageUploadBridge` uploads local PNGs to `www.mikusc.top` with `x-sceneshift-upload-token`.
- `Seed3DBackendAdapter` can submit hosted stylized images to Ark Seed3D 2.0, poll tasks, download models, and resume polling.
- `GeneratedObjectModelImporter` imports ready generated models into Unity prefabs.
- `GeneratedObjectModelImporter` no longer rewrites generated GLB embedded textures on import; model texture size is controlled by the upstream generation quality/settings.
- `GeneratedObjectAssetCleaner` can report or archive duplicate generated models for the same object/style while keeping generated assets local by default.
- `GeneratedObjectAssetCleaner` can report or archive stale `predevice_room_loop_*` runtime artifacts while keeping the latest pre-device evidence set active.
- `AnchorThemeApplier` can place imported generated furniture only when request, object, theme/style, and known room identity match, so stale models are not silently applied to unrelated room objects.
- `AnchorThemeApplier` marks each placed furniture proxy with `StylizedFurnitureInstance` metadata for runtime correction controls.
- Multiple generated furniture placements have been validated in Quest Link / Editor Play, including two tables coexisting correctly.
- Parallelism is bounded for APIMart image jobs, uploads, Seed3D jobs, and surface image jobs.
- Official Interaction SDK ray/poke components are preserved for the dashboard. The custom SceneShift fallback `LineRenderer` pointer ray has been removed so only the official ray visual should appear.

## Current Surface Aesthetic Direction

The latest surface work moved the project away from debug-style planes and toward interior-design readability:

- Wall/floor/ceiling textures should read at room scale, not as tiny repeated wallpaper.
- Wall seams should be softened by trims rather than pretending MRUK planes are perfectly watertight.
- Floor/wall and wall/ceiling junctions should be visually intentional.
- Doors should read as complete doors or portals placed on a continuous wall, not as holes cut out of the wall.
- Windows should keep a readable opening and may use non-square visual language through frame/trim texture and silhouette cues.
- Window vista should appear outside the room and should not include a duplicate window frame or room interior.

## Known Gaps

- Full Meta Image Segmentation / object-detection fusion is not implemented.
- Manual correction UX is not yet polished enough for final demo use.
- Generated furniture now has accept / reject / reset plumbing for runtime-loaded candidates. Accepted, rejected, corrected, and reset restore behavior has passed synthetic Editor Play tests, and the latest current-room pre-device smoke passes a bounded correction plus temporary review-record roundtrip probe. Headset-side persistent evidence now includes runtime model files and review records, but the completed user-facing review/restart sequence still needs to be recorded as one explicit MQDH/test-channel pass.
- Standalone Quest runtime GLB loading now has a code path through `RuntimeGeneratedModelLoader`, and `MR_RoomStylization.unity` has scene-level loader/review wiring. The runtime GLB path has passed synthetic Editor Play, current-room Editor Play pre-device tests, MQDH package gates, and ADB persistent-file evidence collection. It still needs final operator/video/log evidence for a complete in-headset flow before it replaces the Editor-imported prefab path in demo claims.
- `MR_RoomStylization.unity` now uses MRUK `DeviceWithPrefabFallback` with the official `Office00` room prefab so Mac Editor/Simulator regression can load room data when no headset scene data is available. On headset, device room data remains the first source.
- Secure headset-to-backend generation clients now exist for runtime GLB generation and room-surface PNG generation. The deployed `www.mikusc.top` runtime endpoint has a passing no-image Azure smoke, and headset persistent files contain backend runtime results for table captures. The no-image smoke is not paid generation evidence, and the headset run still needs a clean narrative tying user input, capture, backend polling, GLB load, review actions, and restart restore together.
- Generated-object review decisions can now be written to `Application.persistentDataPath/GeneratedObjectReviews/`. The review controller now auto-restores persisted review state when the runtime loader creates a new candidate, so rejected/reset candidates do not reappear just because a GLB was reloaded.
- True-device PCA capture is still a probe; Quest Link / Editor Play validates much of the pipeline, but not native camera support on every headset/runtime.
- Surface v3 prompts/code are implemented, but the resulting aesthetics still need a current visual validation in the actual office room and headset viewpoint.
- Official UISet sample prefabs are available, but direct dynamic instantiation caused layout problems. Current dashboard prioritizes stable interaction over perfect official visual fidelity.
- Official ray visibility still needs headset validation after removing the custom SceneShift fallback ray. If the official ray is visually hidden by the world-space backplate, treat it as a depth/rendering-order issue rather than reintroducing the custom ray.
- Generated model artifacts under `Assets/Generated/ThemeAssets/` should generally remain local and uncommitted unless a specific demo asset is intentionally preserved.
- Recovery scenes under `Assets/_Recovery/` and imported UISet sample scenes should be reviewed before final commits.
- Some existing compiler warnings are non-blocking but should be cleaned before a polished milestone.

## Latest Verification

Latest evidence audit by Codex on `2026-06-05` from existing reports and terminal verifiers:

- Latest package build report is `Library/MQDHPackageBuildReports/mqdh_package_build_20260526_154220.md` with overall `BuiltAndVerified`; it produced `/Users/mikusc/Documents/UnityProjects/SceneShift Discussion Room Latest/Builds/MQDH/SceneShiftQuest_20260526_154220.apk`.
- `bash Tools/verify_mqdh_package_build_report.sh` passes for the latest package report.
- Latest final local gate is `Library/MQDHHeadsetEvidence/predevice_local_gate_20260526_154335.md` with overall `Pass`, tied to `Builds/MQDH/SceneShiftQuest_20260526_154220.apk`.
- `bash Tools/verify_predevice_local_gate.sh --require-package-artifact` passes for that latest final local gate.
- Latest terminal pre-package suite is `Library/MQDHHeadsetEvidence/mqdh_terminal_prepackage_suite_20260526_154332.md` with overall `Pass`.
- Android Build Support is present at `/Applications/Unity/Hub/Editor/6000.4.3f1/PlaybackEngines/AndroidPlayer`; SDK, NDK, OpenJDK, and `adb` are present according to the latest gate output.
- Latest headset ADB evidence directory is `Library/MQDHHeadsetEvidence/adb_20260526_211143`; `bash Tools/verify_mqdh_headset_evidence.sh` reports `Pass`.
- Latest ADB package dump shows package `com.mikusc.sceneshiftroom.comp4145`, `versionName=0.1.2`, `versionCode=11`, `targetSdk=34`, `primaryCpuAbi=arm64-v8a`, and `lastUpdateTime=2026-05-27 04:50:48`.
- Latest ADB persistent files include PCA captures under `BestViewCaptures/DevicePassthrough/`, runtime backend submissions/results under `GeneratedObjectJobs/`, real backend model URLs pointing to `https://www.mikusc.top/api/v1/runtime-generations/.../mesh_textured_pbr.glb`, local runtime GLB files under `GeneratedObjectRuntimeModels/`, and review records under `GeneratedObjectReviews/`.
- Latest deployed runtime backend smoke is `Library/RuntimeBackendSmokeReports/runtime_backend_azure_smoke_20260527_144851.md` with overall `Pass`; it intentionally omits the image and therefore does not create a paid provider task or prove full 3D generation closure.
- Current interpretation: packaging and evidence collection gates are in good shape, and there is headset-side evidence of runtime model files. The remaining demo-final gap is a deliberately recorded full headset flow with Style input, capture, backend polling, GLB load, placement, accept/reject/reset/correction, and restart restore.

Latest verified by Codex on `2026-05-27`:

- Added `QuestSurfaceGenerationClient.cs` as the no-secret Quest-side room-surface backend client.
- `MR_RoomStylization.unity` now wires `QuestSurfaceGenerationClient` under `Stylization` with `SurfaceTexturePromptBuilder`, `SurfaceOverrideApplier`, and `GenerationQueueStatusService`.
- `SceneShiftUISetDashboard.cs` now routes the advanced `Generate Room` action through `QuestSurfaceGenerationClient` when present, then falls back to local `SurfaceOverrideApplier` reapply behavior if the backend client is missing.
- Added `/Users/mikusc/Documents/Myblog/api/src/functions/sceneShiftSurfaceGenerations.js`, exposing `POST /api/v1/surface-generations`, `GET /api/v1/surface-generations/{jobId}`, and `GET /api/v1/surface-generations/{jobId}/files/{fileName}` for backend image2 surface generation and cached PNG delivery.
- Unity MCP dynamic compile/execution succeeded after reloading `Assets/Scenes/MR_RoomStylization.unity`; the scene contains `QuestSurfaceGenerationClient`, `SceneShiftUISetDashboard`, `SurfaceTexturePromptBuilder`, `SurfaceOverrideApplier`, and `GenerationQueueStatusService`.
- Unity Console reported `0` errors after the scene wiring check. The remaining warning is the known Unity AI account/network accessibility warning.
- Backend JavaScript syntax passed `node --check`.
- This is implementation/readiness evidence only. It is not yet proof that Azure has the function deployed, that app settings are present, or that Quest has downloaded/applied real newly generated surface PNGs.

Latest verified by Codex on `2026-05-25`:

- Added and scene-wired `PreDeviceRuntimeLoopValidator` under `RuntimeState` as a repeatable Play Mode pre-device validation entry.
- Editor Play loaded the current MRUK room `Room - 1c40b878-eb4f-4000-8ba5-65d9ac98d5ab` with 27 anchors, including one `TABLE`, one `STORAGE`, and one `COUCH`.
- `PreDeviceRuntimeLoopValidator` queued a current-room/current-style request for `TABLE_18`; `QuestRuntimeGenerationClient` local test mode returned the fixed Khronos `Box.glb` URL; `RuntimeGeneratedModelLoader` downloaded it under `Application.persistentDataPath`, loaded it without `AssetDatabase`, and fitted it to the table request bounds.
- Room-context review restore passed in Editor Play: `Accepted` reloaded active, `Rejected` reloaded hidden, `ResetToFallback` reloaded hidden, and `Corrected` restored a 0.03 m forward nudge plus 5 degree yaw without double-applying after repeated selection.
- Older disposable pre-device artifacts with prefix `predevice_room_loop_*` were archived under `Library/GeneratedObjectArchive/PreDeviceRuntimeArtifacts/`; the latest smoke-linked request/job/prompt/runtime-submission/runtime-result files and matching persistent runtime model folder are intentionally kept active as local pre-device evidence.
- Unity Console reported `0` errors after the room-context pre-device regression. Remaining warnings were the known Meta/OpenXR unsupported desktop function-pointer warnings during Editor XR initialization.
- `git diff --check` passes after the scene/script/doc updates.
- This is still pre-device evidence only. A full `MetaXRSimulator` visual/HUD pass and MQDH/test-channel standalone Quest validation remain required before claiming true-device closure.
- Added and scene-wired `PreDeviceSmokeReportRunner` under `RuntimeState`.
- MRUK `DeviceWithPrefabFallback` loaded the current/office fallback room in Editor Play with 27 anchors, including one `TABLE`, one `STORAGE`, and one `COUCH`.
- Latest `PreDeviceSmokeReportRunner` evidence is `Library/PreDeviceSmokeReports/predevice_smoke_20260524231824.json` / `.md` with overall status `PassWithManualVisualChecks`.
- Automated checks passed for Play Mode, room readiness, safe table target, style identity, `stylization_plan` with `entries=26, warnings=0`, surface overrides, runtime client/loader/review/correction wiring, `runtimeLoaded=7` queue status, runtime request/job contract traceability, runtime backend submission/result artifact traceability, runtime-loaded instance metadata for `TABLE_18`, runtime review editability/persistence, reset-to-deterministic-fallback evidence, reject/reset release-policy evidence, dashboard controls, absence of custom `SceneShiftDashboardPointerRay`, Clean View toggle, and passthrough-only toggle.
- The new `runtime_request_job_contract` smoke gate verified that the runtime-loaded `TABLE_18` instance can be traced back to a matching `.job.json`, `.request.json`, prompt artifact, room id, object id, style id/variant, semantic label, target bounds/physical dimensions, HTTPS model URL, local runtime GLB file, and `RuntimeLoaded` state.
- The new `runtime_backend_artifact_contract` smoke gate verified that the local-test runtime backend path writes matching `.runtime-submission.json` and `.runtime-result.json` artifacts with room/object/style/semantic identity, source request and prompt paths, target bounds, `RuntimeModelReady` result state, local-test backend job id, and HTTPS model URL.
- The new `runtime_review_editability_persistence` smoke gate selected the runtime `TABLE_18` candidate, applied a bounded 0.025 m forward nudge plus 5 degree yaw, confirmed the correction, wrote/read/deleted temporary `Accepted`, `Rejected`, `ResetToFallback`, and `Corrected` review records, and passed.
- The new `runtime_reset_deterministic_fallback` smoke gate hid the runtime `TABLE_18` candidate, forced deterministic fallback for the same object id, and verified a visible `theme_default` fallback proxy before restoring the probe state.
- The new `runtime_reject_reset_release_policy` smoke gate verified that hidden reject/reset runtime candidates are configured for release and that the runtime loader can release an inactive probe instance without deleting the model or review/job records.
- The two remaining smoke checks are intentionally manual: surface visual quality and dashboard visual layout must still be inspected in Game View/`MetaXRSimulator` and then repeated in the MQDH/test-channel headset build.
- Local visual evidence was recaptured after that latest smoke under `Library/PreDeviceVisualEvidence/predevice_visual_review_202605242319.md` with `display2_after_backend_artifact_smoke_202605242319.png`. The Game view and Meta XR Simulator preview show the room, dashboard, runtime/review controls, and visible fixed test GLB at the table target; this remains pre-device evidence, not headset closure.
- Added `PreDeviceBuildReadinessReportRunner` as an Editor-only MQDH/test-channel packaging preflight.
- `PreDeviceBuildReadinessReportRunner` now also checks that the preflight/build toolchain is present and exposes the expected Android support recovery, terminal pre-package suite, Unity MQDH package build runner, package build report verifier, local gate, package artifact verification, gate self-test, handoff bundle, and headset evidence collection/verification capabilities.
- At that time, `PreDeviceBuildReadinessReportRunner` evidence was `Library/PreDeviceBuildReadinessReports/predevice_build_readiness_20260525095727.json` / `.md` with overall status `Pass`.
- Build readiness passed for canonical build scene, Android application id/version, Min SDK 32, Target SDK 34, ARM64, IL2CPP, Meta XR/MRUK/Interaction/OpenXR packages, glTFast runtime dependency, Android OpenXR loader, MetaXR OpenXR feature, runtime loader/review/correction/fallback scene wiring, runtime passthrough-only bootstrap, local fixed GLB mode, empty backend/API-key overrides, latest smoke status, safe `TABLE` target, zero planner warnings, `runtimeLoaded=7`, runtime-loaded instance metadata, runtime request/job contract evidence, runtime backend artifact evidence, runtime review editability/persistence, reset-to-deterministic-fallback evidence, reject/reset release-policy evidence, dashboard runtime/review controls, visual evidence paired with the latest smoke report, visual screenshot freshness, explicit visual-review references to both the latest smoke report and paired screenshot, and active pre-device runtime artifact consistency.
- That build readiness run passed the packaged config/assets secret scan and generated job JSON secret scan after the old pre-device runtime artifacts were archived; the generated-record scan focused on the active local evidence set.
- That build readiness run also verified that exactly one active `predevice_room_loop_*` runtime evidence set remained, its job/request/prompt/runtime-submission/runtime-result files and persistent runtime GLB folder were present, and that request id was referenced by the latest smoke report.
- Added `MqdhHeadsetEvidenceTemplateWriter` under `SceneShift/Validation/Create MQDH Headset Evidence Template`; the then-current generated template was `Library/MQDHHeadsetEvidence/mqdh_headset_evidence_20260525095727.md`, which captured the smoke/readiness/visual evidence, active runtime request, Android module state, Android Support install helper, terminal-suite/audit/package-build fields, and headset run checklist.
- Added `Tools/collect_mqdh_headset_evidence.sh` so the headset run can collect `adb devices`, device properties, package dump, Unity/logcat output, screenshot, and best-effort persistent app files into `Library/MQDHHeadsetEvidence/adb_*`.
- Added `Tools/verify_mqdh_headset_evidence.sh` so collected `Library/MQDHHeadsetEvidence/adb_*` headset evidence can be checked for connected-device state, installed package path, package dump, Unity logcat, screenshot, and persistent-file evidence or explicit pull errors.
- Added `MqdhHandoffPreflightReportRunner` under `SceneShift/Validation/Run MQDH Handoff Preflight`; it verifies the latest template references the latest readiness report, includes the ADB collection command, includes the final APK/AAB local-gate commands, and keeps the package-only verifier as a debug command. The then-current report was `Library/MQDHHeadsetEvidence/mqdh_handoff_preflight_20260525095727.md` with overall status `Pass`.
- `MqdhHeadsetEvidenceTemplateWriter` now records existing terminal-suite/handoff/local-gate context at template creation plus fillable terminal suite, pre-package local gate, final package local gate, and final package gate verification fields so the MQDH/test-channel run has a single evidence sheet for terminal and headset evidence without implying future gate files already exist.
- Added `MqdhPrePackageEvidenceSuiteRunner` under `SceneShift/Validation/Run MQDH Pre-Package Evidence Suite`; it runs readiness, writes the MQDH headset evidence template, runs handoff preflight, and writes `Library/MQDHHeadsetEvidence/mqdh_prepackage_evidence_suite_*.md` with the next terminal commands.
- Added `Tools/show_mqdh_handoff_status.sh` for terminal-side MQDH handoff status summaries across latest readiness, evidence template, handoff preflight, terminal suite, smoke evidence, visual evidence, and the ADB collection helper.
- Added `Tools/check_unity_android_support.sh` so Android Build Support installation can be verified from the terminal before reopening Unity and rerunning readiness.
- Added `Tools/check_android_support_recovery.sh` as the post-Unity-Hub recovery check: it verifies AndroidPlayer/SDK/NDK/OpenJDK/adb and reports whether readiness/template/handoff/terminal-suite/local-gate evidence is stale and must be regenerated. When modules are missing, it prints the exact Unity Hub CLI command for `android`, `android-sdk-ndk-tools`, and `android-open-jdk`.
- Added `Tools/install_unity_android_support.sh` as a conservative Unity Hub CLI wrapper. It dry-runs by default, resolves the exact Unity version from readiness evidence, prints the install command, writes `Library/AndroidSupportInstallLogs/android_support_install_*.log` in `--run` mode, and supports `--wait-for-close` so it can wait for the user to manually close Unity Editor and Unity Hub before installing.
- Added `Tools/scan_predevice_secrets.sh` as a terminal-side companion to the Unity readiness secret scans; it checks packaged config/assets and generated job JSON records without printing secret line contents.
- Added `Tools/verify_mqdh_package_artifact.sh` to validate an APK/AAB before MQDH/test-channel upload. It checks ZIP structure, ARM64 Unity libraries, optional `aapt` package/version metadata, and compressed artifact entries for likely credential strings.
- `Tools/show_mqdh_handoff_status.sh` now reports the terminal pre-device secret scan result alongside readiness/template/handoff/terminal-suite/bundle state.
- Added `Tools/run_predevice_local_gate.sh` as a one-command terminal gate that aggregates secret scan, handoff bundle verification, Unity Android Support filesystem check, MQDH handoff status, and optional package artifact verification through `--package-artifact <apk-or-aab-path>` into `Library/MQDHHeadsetEvidence/predevice_local_gate_*.md`.
- Added `Tools/run_mqdh_terminal_prepackage_suite.sh` as the preferred one-command terminal follow-up after the Unity MQDH pre-package suite. It writes/verifies the handoff bundle, runs/verifies the pre-package local gate, records `Tools/show_mqdh_handoff_status.sh`, and writes `Library/MQDHHeadsetEvidence/mqdh_terminal_prepackage_suite_*.md`.
- Added `Tools/audit_true_device_preflight.sh` as a one-command true-device preflight status audit. It writes `Library/MQDHHeadsetEvidence/true_device_preflight_audit_*.md` and summarizes readiness, handoff, terminal suite, local gate, package build report, final package gate, ADB availability, and headset ADB evidence state without changing Unity scenes or settings. The then-current aligned audit was `Library/MQDHHeadsetEvidence/true_device_preflight_audit_20260525_095959.md` with overall `ReadyForMQDHUpload`.
- `MqdhHandoffPreflightReportRunner` now checks that the MQDH headset evidence template includes the terminal suite command and terminal suite evidence fields.
- Added `MqdhPackageBuildRunner` under `SceneShift/Validation/Build MQDH Test Package`; it blocks when readiness fails or the active build target is not Android, writes `Library/MQDHPackageBuildReports/mqdh_package_build_*.md`, builds the current Android APK/AAB mode into `Builds/MQDH/` when allowed, and runs the final `--package-artifact` local gate after a successful build.
- Added `Tools/verify_mqdh_package_build_report.sh`; by default it requires a `BuiltAndVerified` package build report with a present artifact and passing final local-gate checks, while `--allow-blocked` is available only for current-state status summaries before Android Support is installed.
- At that time, package build report `Library/MQDHPackageBuildReports/mqdh_package_build_20260525_095717.md` had overall `BuiltAndVerified`; it produced `Builds/MQDH/SceneShiftQuest_20260525_095717.apk`, verified Android Build Support at `/Applications/Unity/Hub/Editor/6000.4.3f1/PlaybackEngines/AndroidPlayer`, and passed the final package local gate.
- At that time, terminal pre-package suite report `Library/MQDHHeadsetEvidence/mqdh_terminal_prepackage_suite_20260525_095727.md` had overall `Pass`: bundle write/verify, local-gate verification, and handoff status all passed.
- At that time, final local gate report `Library/MQDHHeadsetEvidence/predevice_local_gate_20260525_095730.md` had overall `Pass`: secret scan, bundle verification, Android support filesystem check, handoff status, and package artifact verification all passed.
- Added `Tools/verify_predevice_local_gate.sh` to verify that the latest local gate report still references the latest readiness, smoke, visual review, MQDH template, handoff preflight, and handoff bundle evidence, and records a zero-finding secret scan. It rejects any `Overall: Fail` report, can accept `BlockedAndroidSupport` only as a pre-package blocker state, and supports `--require-package-artifact` for the final post-build/pre-upload gate where `Overall: Pass` is required.
- Added `Tools/test_predevice_gate_scripts.sh` as a shell self-test covering normal pre-package gate verification, package-required rejection of pre-package gates, clean APK fixture acceptance, and credential-bearing APK fixture rejection.
- `Tools/show_mqdh_handoff_status.sh` now reports the latest terminal suite, local gate verification status, and whether the latest local gate includes a passing package artifact check when not called from inside `Tools/run_predevice_local_gate.sh`.
- Added `Tools/write_mqdh_handoff_bundle.sh` to create a fixed `Library/MQDHHeadsetEvidence/handoff_bundle_*` manifest plus copies of the latest smoke/readiness/visual/template/handoff/generated-object evidence and terminal secret scan output.
- At that time, generated MQDH handoff bundle `Library/MQDHHeadsetEvidence/handoff_bundle_20260525_095727/manifest.md` copied the readiness, smoke, visual review/image, MQDH template, handoff preflight, active `predevice_room_loop_table_18_20260524231822` job artifacts, runtime `Box.glb`, and `files/secret_scan/secret_scan.md`, with SHA-256 hashes.
- The bundled secret scan reports `Packaged files scanned: 84`, `Generated records scanned: 13`, and `Findings: 0`.
- `Tools/verify_mqdh_handoff_bundle.sh` verifies that the latest handoff bundle references the latest evidence files, includes a zero-finding secret scan, and that copied file hashes match.
- Build readiness now passes after resolving the Unity Hub module path: Android Build Support is installed at `/Applications/Unity/Hub/Editor/6000.4.3f1/PlaybackEngines/AndroidPlayer`, with SDK, NDK, OpenJDK, and `adb` available.
- Unity iteration rule added to the MQDH runbook: script-only C# edits normally require compile/Console checks but not a scene reload prompt; scene, prefab, `ProjectSettings`, package, or opened asset-state changes require clicking Unity and handling any reload/unsaved-scene prompt before validation.
- Unity-side readiness now also verifies `Tools/install_unity_android_support.sh`, and the MQDH evidence template / pre-package suite includes the safer `--wait-for-close` Android Support install command as the preferred terminal route.
- The active Editor build target is now `Android`, and the latest APK package has passed the final local gate. The remaining validation gap is MQDH/test-channel upload or install plus headset-side ADB/log/video/persistent-file evidence.
- Added `docs/14_MQDH_TEST_CHANNEL_RUNBOOK.md` as the operational handoff from Mac pre-device proof to the first MQDH/test-channel headset package.

Latest verified by Codex on `2026-05-24`:

- Unity MCP connection is working again on Assistant `2.6.0-pre.1`.
- `MR_RoomStylization.unity` now contains `RuntimeGeneratedModelLoader`, `GeneratedObjectReviewController`, `CorrectionModeController`, `GeneratedObjectRotationCorrectionController`, and a `RuntimeGeneratedModels` root under `RuntimeState`.
- `SceneShiftUISetDashboard.cs` now exposes `Load Test GLB` and `Load Latest Job` controls plus a runtime loader status row.
- `RuntimeGeneratedModelLoader.cs` can attach the configured test GLB URL to the latest generated-object job/request so request bounds can be used for the first runtime placement spike.
- `QuestRuntimeGenerationClient.cs` adds the first headset-side runtime backend boundary. Its default `LocalTestModelUrl` mode advances the latest captured job to `RuntimeModelReady` without embedding cloud service API keys, and `HttpBackend` mode now uploads multipart request/prompt/image payloads to a secure backend endpoint and polls for a runtime model result.
- A synthetic Editor non-Play test confirmed the local runtime backend can write `RuntimeModelUrl` / `RuntimeModelLocalPath`, but glTFast runtime instantiation must be validated in Play Mode or a Quest build. `RuntimeGeneratedModelLoader` now blocks non-Play runtime loads instead of marking those jobs as failed.
- A synthetic Editor Play test of dashboard-equivalent `Submit+Load` reached `RuntimeLoaded` for request `runtime_play_20260524_154238`: the fixed Khronos `Box.glb` downloaded under `Application.persistentDataPath`, instantiated under `RuntimeGeneratedModels`, produced one `RuntimeGeneratedModelInstance` in `Previewing` review state, and fitted to the synthetic `1 x 1 x 1` request bounds at `(0, 0.5, 1.5)`.
- A synthetic Editor Play review-restore test reached the expected rejected reload behavior for request `runtime_review_20260524_154803`: after `Reject`, clearing the runtime instance, and loading the same job again, the new `RuntimeGeneratedModelInstance` restored `Rejected` and remained inactive.
- A synthetic Editor Play accepted-restore test reached the expected restore-without-regeneration behavior for request `runtime_accept_20260524_155659`: after `Accept`, clearing the runtime instance, and restoring from `GeneratedObjectReviews`, the model reloaded from the existing local GLB, restored `Accepted`, and remained active.
- A synthetic Editor Play corrected-restore test reached the expected bounded-correction behavior for request `runtime_correct_20260524_160229`: after a forward nudge and 5 degree yaw correction, clearing the runtime instance, and restoring from `GeneratedObjectReviews`, the model restored `Corrected`; repeated selection did not double-apply the correction.
- A synthetic Editor Play reset-restore test reached the expected deterministic-fallback behavior for request `runtime_reset_20260524_160353`: after `Reset`, clearing the runtime instance, and loading the same job again, the new `RuntimeGeneratedModelInstance` restored `ResetToFallback` and remained inactive.
- Current Mac true-device route is now documented as `MetaXRSimulator` / Editor Play for fast local regression, followed by MQDH or configured test release-channel installation/update on the headset for standalone closed-loop evidence.
- `git diff --check` passes after the scene wiring update.
- Unity Console reports `0` errors after the review-restore Play test. The latest warnings are Meta/OpenXR unsupported-function warnings from desktop/Editor XR initialization, not project compile errors.
- `MetaXRSimulator` and MQDH/test-channel headset validation are still required before treating this as demo-clean.

Earlier verified by Codex on `2026-04-30`:

- `dotnet build Assembly-CSharp.csproj` succeeded with `0` errors after the runtime generated-furniture `Rotate 90` correction change.
- Remaining compiler warnings are known non-blocking warnings: existing `FindObjectsSortMode` deprecation warnings and serialized JSON DTO field warnings in `DeepSeekStyleIntentProvider`.
- Unity-side Play validation has not yet been rerun after these latest documentation/code updates.

Latest verified by user during prior Play runs:

- Two generated tables can coexist and align acceptably.
- Generated furniture replacement can be positioned correctly after request-specific capture/import.
- Runtime panel is visible and more stable after avoiding direct dynamic UISet sample control instantiation.
- Some surface/window aesthetics still needed improvement, which is what the current surface-v3 update targets.

## Current Local Workspace State

The workspace contains active uncommitted work. Treat it as in-progress:

- `README.md` and `START_HERE_CN.md` have been refreshed to reflect the current project state.
- `docs/08_PROGRESS_STATUS.md` has been rewritten as the current rolling tracker.
- `docs/05_DATA_CONTRACTS.md` now documents full-door/portal surface behavior.
- `SurfaceOverrideApplier.cs` includes room-scale tiling, trims, opaque room surfaces, full door panels, and window-vista behavior.
- `SurfaceOverrideApplier.cs` now keeps door-host walls continuous and only uses wall cutouts for valid window openings.
- `SurfaceTexturePromptBuilder.cs` uses `surface_texture_v3_room_scale_openings`.
- `GeneratedObjectModelImporter.cs` imports generated GLBs without applying a local embedded-texture resize cap.
- `SceneShiftUISetDashboard.cs` preserves official Interaction SDK interaction and no longer creates a custom fallback ray visual.
- `SceneShiftUISetDashboard.cs` now exposes a `Rotate 90` button and status row for selected generated furniture.
- `GeneratedObjectRotationCorrectionController.cs` and `StylizedFurnitureInstance.cs` provide runtime-only generated furniture yaw correction.
- `RuntimeGeneratedModelLoader.cs` adds the first runtime GLB download/read/load/normalize path for generated objects.
- `RuntimeGeneratedModelInstance.cs` stores runtime-loaded generated-object identity, bounds, placement, and review metadata.
- `GeneratedObjectReviewController.cs` adds initial accept/reject/reset/correction persistence for runtime generated candidates, subscribes to `RuntimeGeneratedModelLoader` so a newly loaded candidate immediately restores a saved review decision, and can restore the latest accepted/corrected local runtime model without submitting a new generation job.
- `QuestRuntimeGenerationClient.cs` adds a no-secret runtime backend boundary with local fixed-GLB test mode and multipart/polling HTTP backend mode.
- `Backend/sceneshift_runtime_backend.py` adds the first secure-backend service skeleton. It can run protocol-only `manual`/`fixed-url` modes and real `seed3d` mode using server-side `ARK_API_KEY`.
- `RuntimeBackendConfigurationRunner.cs` adds Editor menu items for reporting runtime backend configuration and switching between LocalTest and environment-provided HTTPS HttpBackend mode without serializing secrets.
- `Tools/check_runtime_backend_seed3d_preflight.sh` adds a no-secret backend environment gate. Current local environment fails this gate because `ARK_API_KEY` and `SCENESHIFT_PUBLIC_BASE_URL` are not set, so true backend closure is not yet proven.
- `PreDeviceRuntimeLoopValidator.cs` adds a Play Mode pre-device regression entry that queues a current MRUK room/current Style request for a safe generated-object target and submits it through the local runtime GLB path.
- `PreDeviceSmokeReportRunner.cs` adds a broader Play Mode smoke report for Mac pre-device validation, covering room readiness, stylization plan, surface overrides, runtime generated-object wiring, dashboard controls, Clean View, and passthrough-only state.
- `PreDeviceBuildReadinessReportRunner.cs` adds an Editor-only packaging preflight under `SceneShift/Validation/Run Pre-Device Build Readiness Report`.
- `MqdhHeadsetEvidenceTemplateWriter.cs` adds a one-click MQDH/test-channel headset evidence template under `SceneShift/Validation/Create MQDH Headset Evidence Template`.
- `MqdhPrePackageEvidenceSuiteRunner.cs` adds a Unity-side suite under `SceneShift/Validation/Run MQDH Pre-Package Evidence Suite` for the normal readiness/template/handoff sequence.
- `MqdhHandoffPreflightReportRunner.cs` validates that the latest MQDH/headset template references the latest readiness report, has the ADB collection command, and that the ADB helper plus local smoke/visual evidence are present.
- `Tools/collect_mqdh_headset_evidence.sh` adds a repeatable ADB evidence collection helper for MQDH/test-channel headset runs.
- `Tools/verify_mqdh_headset_evidence.sh` verifies collected ADB evidence directories after headset install/open; current machine has no `adb_*` evidence directory yet, so this is expected to fail until a headset run is collected.
- `Tools/show_mqdh_handoff_status.sh` prints the current terminal-side package/handoff blocker or ready state, including the latest terminal suite result and latest package build report verification.
- `Tools/check_unity_android_support.sh` checks the Unity `AndroidPlayer` folder, SDK, NDK, OpenJDK, and `adb` presence from the terminal.
- `Tools/check_android_support_recovery.sh` wraps the Android support filesystem check and the current evidence freshness checks for the moment immediately after Unity Hub module installation; if Android Support files appear but evidence is stale, it points to the Unity MQDH pre-package suite followed by `Tools/run_mqdh_terminal_prepackage_suite.sh`.
- `Tools/run_mqdh_terminal_prepackage_suite.sh` is the preferred terminal follow-up after `SceneShift/Validation/Run MQDH Pre-Package Evidence Suite`; it creates/verifies the handoff bundle, creates/verifies the pre-package local gate, and captures the handoff status report.
- `MqdhPackageBuildRunner.cs` adds the project-side package build runner for the step after Android support installation and Android build-target switch; current machine cannot run the build path yet because Android Support is missing.
- `Tools/verify_mqdh_package_build_report.sh` verifies package build reports, so the MQDH/test-channel upload gate can require `BuiltAndVerified` rather than relying only on the APK/AAB file path.
- `Tools/verify_mqdh_package_artifact.sh` verifies the APK/AAB artifact after Unity build and before MQDH/test-channel upload; package id/version metadata is checked when `aapt` is available from Android build tools. The preferred final upload gate is now `bash Tools/run_predevice_local_gate.sh --package-artifact <apk-or-aab-path>` followed by `bash Tools/verify_predevice_local_gate.sh --require-package-artifact`, so this package check is captured with the other local evidence checks and the final local gate must have `Overall: Pass`.
- `Tools/write_mqdh_handoff_bundle.sh` writes an archiveable handoff manifest and evidence file copy set under `Library/MQDHHeadsetEvidence/`.
- `Tools/verify_mqdh_handoff_bundle.sh` validates handoff bundle freshness and copied-file SHA-256 hashes.
- `GeneratedObjectAssetCleaner.cs` adds Editor menu entries to report or archive stale pre-device runtime artifacts while preserving the latest smoke-linked evidence set.
- `SceneShiftUISetDashboard.cs` now exposes initial `Submit+Load`, `Load Test GLB`, `Load Latest Job`, `Accept`, `Reject`, and `Reset` controls for runtime generated-object review.
- `PassthroughOnlyVisibilityToggle.cs` now owns the left-controller `Y` pure passthrough toggle; `DevicePassthroughCaptureService` no longer uses controller `Y` for target cycling in the canonical scene.
- Theme profile assets include updated room-scale surface and opening prompt hints.
- `MR_RoomStylization.unity` contains many runtime systems and local scene wiring changes.
- `Assets/Scenes/UISet.unity` and `Assets/Scenes/UISetPatterns.unity` exist locally as sample/reference scenes.
- `Assets/_Recovery/` contains crash-recovery scene artifacts.

## Newly Recorded Requirement - True-Device Generated Object Loop

Recorded on `2026-05-24`:

- The desired demo is a full standalone Quest flow: the user enters a style intent, captures a target object, waits for a backend to generate a stylized 3D asset, sees that model replace the matching MRUK anchor, and edits the result in MR.
- This requires a `QuestRuntimeGenerationClient` or equivalent backend-facing runtime client. The Quest APK must not contain APIMart, Seed3D, DeepSeek, upload, or signing credentials.
- Initial `RuntimeGeneratedModelLoader` code now exists because `GeneratedObjectModelImporter` is Editor-only and cannot run in an APK.
- Initial generated-object review persistence now exists through `GeneratedObjectReviewController`, with accepted, rejected, corrected, and reset restore behavior verified in Editor Play. The full headset-facing state machine and MQDH/test-channel restart behavior still need validation.
- `MR_RoomStylization.unity` now wires the runtime backend client, loader, review, and correction components into `RuntimeState`, with a fixed Khronos sample GLB URL configured for the first submit/download/load spike.
- `PreDeviceRuntimeLoopValidator` is now scene-wired under `RuntimeState` so Play Mode can queue a current-room/current-style `TABLE` request without relying on native camera capture.
- The first implementation spike should load one known test GLB URL on Quest and fit it to one selected `TABLE` anchor before connecting the full backend pipeline.
- Deterministic surfaces/proxies remain the fallback when capture, backend generation, runtime model loading, or review fails.

## Environment Variables

For the current Editor / Quest Link automatic generation chain, Unity must be launched with the relevant variables visible to the Unity process:

- `DEEPSEEK_API_KEY`
- `APIMART_API_KEY`
- `SCENESHIFT_UPLOAD_TOKEN`
- `ARK_API_KEY`

Do not commit API keys. For standalone Quest builds, these credentials must move behind a backend service and must not be embedded in the APK.

## Next Smallest Tasks

For surface aesthetics:

1. Enter Play in the real office / Quest Link setup.
2. Confirm wall/floor/ceiling no longer look like dense repeated wallpaper.
3. Confirm trim strips reduce visible wall seams and wall/floor/ceiling edge issues.
4. Confirm door appears as a complete door or portal panel.
5. Confirm the wall behind/around the door is not cut out and uses the same wall material as other walls.
6. Confirm window frame keeps an open center and the vista appears only outside the window area.
7. Confirm the dashboard uses only the official Interaction SDK ray visual, with no duplicate SceneShift line ray.
8. If needed, tune `wallTextureTileSizeMeters`, `floorTextureTileSizeMeters`, `ceilingTextureTileSizeMeters`, trim sizes, and door arch depth in `SurfaceOverrideApplier`.

For pre-device generated-object demo readiness on the current Mac setup:

1. Use `PreDeviceRuntimeLoopValidator` in Play Mode to queue a current MRUK room/current Style request for one safe `TABLE`-class generated-object target.
2. Confirm the validator-created `.job.json` has `SourceRequestPath`, room id, object id, style id, semantic label, request bounds, and prompt identity.
3. Route the job through `QuestRuntimeGenerationClient` local test backend mode, receive the configured sample GLB URL, download it, runtime-load it, and fit it to the request bounds.
4. Validate review persistence in the local pre-device loop: accepted stays visible, rejected/reset stays hidden, reset can reveal deterministic fallback for the same object id, and corrected restores once without accumulating repeated offset/yaw.
5. Run the broader `MetaXRSimulator` / Editor Play visual checklist for room surfaces, clean view, passthrough-only toggle, and dashboard layout.
6. Run `PreDeviceSmokeReportRunner` after the room is ready; treat `PassWithManualVisualChecks` as the expected Mac pre-device automated gate, not as headset closure.
7. Capture or review visual evidence under `Library/PreDeviceVisualEvidence/`; close only the checks that the screenshot viewpoints actually prove.
8. Android Build Support for Unity `6000.4.3f1` is currently installed and detected at `/Applications/Unity/Hub/Editor/6000.4.3f1/PlaybackEngines/AndroidPlayer`; reinstall it only if a future `android_build_support_installed` check fails.
9. If Android Support is reinstalled or Unity Hub modules change, run `bash Tools/check_android_support_recovery.sh`; if it reports `NeedsUnityEvidenceRefresh`, reopen Unity and regenerate readiness/template/handoff evidence through the Unity suite, then regenerate terminal-suite/bundle/local-gate evidence through `bash Tools/run_mqdh_terminal_prepackage_suite.sh`.
10. Run `SceneShift/Generated Objects/Archive Pre-Device Runtime Artifacts - Keep Latest` before final packaging checks if multiple old `predevice_room_loop_*` evidence sets have accumulated.
11. Run `SceneShift/Validation/Run MQDH Pre-Package Evidence Suite` after any meaningful package, Player Settings, build target, or evidence change; the latest suite generated `Library/MQDHHeadsetEvidence/mqdh_prepackage_evidence_suite_20260526154331.md` with `Pass`.
12. If a single artifact goes stale later, rerun the individual readiness/template/handoff preflight menu as needed.
13. Run `bash Tools/run_mqdh_terminal_prepackage_suite.sh` before starting the MQDH/test-channel run; the latest suite is `Library/MQDHHeadsetEvidence/mqdh_terminal_prepackage_suite_20260526_154332.md` with `Pass`.
14. The latest Android package was built through `SceneShift/Validation/Build MQDH Test Package` as `Builds/MQDH/SceneShiftQuest_20260526_154220.apk`; rerun `bash Tools/verify_mqdh_package_build_report.sh` before MQDH/test-channel upload or install.
15. Verify the final package local gate with `bash Tools/verify_predevice_local_gate.sh --require-package-artifact`; the latest package-tied gate is `Library/MQDHHeadsetEvidence/predevice_local_gate_20260526_154335.md` with `Pass`.
16. If building APK/AAB manually later, run `bash Tools/run_predevice_local_gate.sh --package-artifact <apk-or-aab-path>` and then `bash Tools/verify_predevice_local_gate.sh --require-package-artifact`.
17. Run `bash Tools/check_runtime_backend_azure_smoke.sh` before any paid full-chain headset attempt; the latest no-image Azure smoke is `Library/RuntimeBackendSmokeReports/runtime_backend_azure_smoke_20260527_144851.md` with `Pass`.
18. Use latest headset ADB evidence only as support evidence, not final demo closure, unless the evidence run explicitly records the full user flow and restart-restore result.
19. Clean or intentionally ignore Recovery/UISet sample artifacts before commit.
20. Use `docs/14_MQDH_TEST_CHANNEL_RUNBOOK.md` for the current verified APK, MQDH/test-channel install, and headset evidence flow.
21. Run `docs/11_SMOKE_TEST_AND_DEMO_CHECKLIST.md` and `docs/12_TRUE_DEVICE_VALIDATION_PLAN.md`.

For true-device closure after the pre-device gates pass:

1. Install or update the latest verified APK on Quest, then repeat the full flow as standalone-app evidence.
2. Validate the result on Quest: style input/selection, capture, backend polling, model download/load, placement, preview visibility, accept, reject, reset, and bounded correction persistence from the headset dashboard.
3. Restart the app on Quest and confirm the accepted/corrected/rejected/reset decisions are respected.
4. Collect a fresh `adb_*` evidence directory after the completed flow, not just after app launch, and run `bash Tools/verify_mqdh_headset_evidence.sh`.

For true-device validation:

1. Validate PCA capture on a supported Quest runtime.
2. Confirm capture writes PNG, metadata, request JSON, prompt text, and job JSON.
3. Confirm a runtime-loaded model can be placed without Unity Editor import.
4. Confirm backend/network failures leave deterministic room stylization usable.

## Update Rule

When a task materially changes the prototype, update:

- `docs/08_PROGRESS_STATUS.md` for rolling status.
- `README.md` when the public project summary changes.
- `START_HERE_CN.md` when the user-facing workflow changes.
- `docs/05_DATA_CONTRACTS.md` when serialized request/job/result contracts change.
- `docs/09_GENERATIVE_OBJECT_PIPELINE.md` when generated-object workflow changes.
- `docs/11_SMOKE_TEST_AND_DEMO_CHECKLIST.md` when validation steps change.
