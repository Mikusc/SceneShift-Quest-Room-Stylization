# 11 Smoke Test and Demo Checklist

## Purpose

Use this checklist before recording, committing a demo milestone, or asking Codex to continue runtime work.

This checklist reflects the current `2026-04-30` project state: generic scaffold, built-in/custom Styles, surface-v3 room materials, continuous-wall door overlays, window cutouts/window-vista overlays, generated furniture, runtime dashboard, and Quest Link / Editor Play validation.

---

# 0. Validation Route

Current Mac workflow:

- Use `MetaXRSimulator` or Unity Editor Play for fast local regression.
- Use Meta Quest Developer Hub / test release-channel deployment for true-device closed-loop validation: build or upload the test version, update/install it on the headset, then validate inside the standalone app.
- Do not count USB `Build And Run` or Editor-only checks as the default true-device path unless that local setup is deliberately changed.
- Treat `PreDeviceSmokeReportRunner` output as a Mac pre-device gate only. `PassWithManualVisualChecks` means automated scene/wiring checks passed and Game View/Simulator visual checks still need human confirmation.
- The canonical scene uses MRUK `DeviceWithPrefabFallback` with the official `Office00` prefab so Mac regression can load room semantics without headset scene data. A standalone Quest run must still validate the real device room.

---

# 1. Before Play

Check:

- Active scene is `Assets/Scenes/MR_RoomStylization.unity`.
- Unity is not compiling.
- Console has no new blocking red errors.
- `AppRoot` contains Bootstrap, Perception, Stylization, Interaction, and RuntimeState groups.
- `MRUK` exists.
- `StylizedContentRoot` and `SurfaceOverrides` exist.
- `SceneShiftUISetDashboard` or the current stable runtime dashboard exists under `UI`.
- `GenerationQueueStatusService` exists.
- `GenerationJobWorldStatusOverlay` exists if checking furniture status cards.
- Theme/Profile data exists under `Assets/Data/ThemeProfiles/`.
- If testing custom style intent, set it before Play and confirm runtime summary shows extracted keywords.
- If testing DeepSeek style parsing, `DEEPSEEK_API_KEY` is visible to Unity or a local non-committed override is set.
- If testing APIMart image generation, `APIMART_API_KEY` is visible to Unity.
- If testing hosted upload, `SCENESHIFT_UPLOAD_TOKEN` is visible to Unity and upload uses `x-sceneshift-upload-token`.
- If testing Seed3D, `ARK_API_KEY` is visible to Unity.
- If testing standalone Quest surface generation, do not expose image2/APIMart keys to Unity. Confirm the deployed backend has `APIMART_API_KEY` or `IMAGE2_API_KEY` in Azure application settings and the Unity scene uses `QuestSurfaceGenerationClient.backendSubmitUrl=https://www.mikusc.top/api/v1/surface-generations`.

Generated artifact rule:

- Do not commit generated models by default.
- Check `Assets/Generated/ThemeAssets/` only for local validation unless a specific demo artifact is intentionally being preserved.

---

# 2. Accepted Editor Noise

Known non-blocking noise can include:

- Meta/OpenXR simulator startup warnings.
- Meta XR optional project setup notice.
- Unity AI Account API warning.
- Existing obsolete API warnings for `FindObjectsSortMode`.
- Existing `CharacterController` name-collision log.

Any new compile error, exception loop, missing component error, or repeated runtime null reference is blocking.

---

# 3. Core Runtime Checks

In Play mode, verify:

- MRUK room becomes available.
- Active room is the expected office room if multiple Quest room scans exist.
- Runtime dashboard is visible and readable.
- Theme dropdown / Style label shows the intended Style.
- `StylizationPlanner` produces entries for major semantics.
- `GenerationQueueStatusService` reports surface/furniture queue state.
- Clean View toggles debug shells and status overlays as expected.
- Runtime dashboard `Rotate 90` can select the current generated furniture target and rotate only that placed proxy around world Y.
- Left-controller `Y` / keyboard `Y` toggles pure passthrough: all virtual surfaces, furniture, UI, rays, shells, and status cards disappear; pressing it again restores the previous virtual view.
- Room remains readable; user can still understand real furniture positions.

Fail if:

- Scene crashes on entering Play.
- Room never becomes ready.
- Generated/object/surface application repeats every frame.
- Clean View hides the stylized room itself.
- Runtime UI cannot be hidden or blocks the entire scene.
- Pure passthrough mode cannot restore virtual content with a second `Y` press.

---

# 4. Surface Aesthetic Checks

Surface path currently uses `surface_texture_v3_room_scale_openings`. On standalone Quest, the expected real-generation path is `QuestSurfaceGenerationClient -> /api/v1/surface-generations -> backend image2 -> SurfaceOverrideApplier`.

Before judging generated surface aesthetics:

- Press dashboard `Generate Room`.
- Confirm the panel shows `Room Surfaces: submitting`, `polling`, or `ready` rather than only local fallback state.
- Confirm `GenerationQueueStatusService` reports surface jobs moving from prompt/submitted to ready, or records clear backend failure reasons.
- Confirm generated PNG files are downloaded under `Application.persistentDataPath/SurfaceTextureOutputs/` on Quest or under `Library/SurfaceTextureOutputs/` in Editor.

Check wall/floor/ceiling:

- Wall texture reads as broad room-scale material, not tiny wallpaper.
- Floor texture reads as walkable surface, not dense small tiles.
- Ceiling texture is subtle and does not make the room feel visually noisy.
- Wall/floor and wall/ceiling transitions look intentional.
- Wall corner gaps are reduced or hidden by trim strips.
- Opaque surfaces do not appear washed out or semi-transparent unless intentionally configured.

Check door:

- Door appears as a complete flat door or portal panel.
- Door is not just a thin rectangular frame.
- Door does not cut a hole in the wall override mesh.
- The door-host wall uses the same material, tiling, opacity, and seam logic as other walls.
- Door does not protrude into walkable space.
- Door style matches the active Style.

Check window:

- Window frame keeps an open center.
- Window frame does not block the view.
- Valid window openings can still be cut from the wall override so vista/frame content is not hidden behind the wall material.
- Window vista appears outside/behind the window, not pasted across the room wall.
- Window vista is opaque enough to read clearly.
- No duplicate small window/vista appears from a false-positive `WINDOW_FRAME` anchor.

If surface aesthetics fail, tune:

- `wallTextureTileSizeMeters`
- `floorTextureTileSizeMeters`
- `ceilingTextureTileSizeMeters`
- `openingTextureTileSizeMeters`
- `baseboardHeightMeters`
- `crownTrimHeightMeters`
- `cornerTrimWidthMeters`
- `doorPanelArchDepthRatio`
- generated surface prompt hints in theme profile assets

---

# 5. Furniture Capture Checks

Supported generated-object categories currently include:

- `TABLE`
- `STORAGE`
- `SCREEN`
- `COUCH` as MRUK label, mapped internally to `Seating`
- `BED`
- `LAMP`
- `PLANT`
- `OTHER`

Before capture:

- Look at the target object until HUD shows a stable candidate.
- Confirm target label, anchor id, score, and distance are plausible.
- Confirm the target is not accidentally another object behind it.
- If using capture reuse, confirm reuse is intended for this physical object and current Style.

After capture:

- A request JSON exists.
- A job JSON exists under `Library/GeneratedObjectJobs/`.
- Prompt artifact exists.
- Job status appears in the runtime panel or object status card.
- Existing placed generated furniture remains stable and is not overwritten by the new capture.

Fail if:

- Capture targets the wrong anchor.
- New capture changes an already accepted/working generated object.
- Multiple objects receive the same generated prefab without matching request identity.

---

# 6. Automated Generation Checks

APIMart image2:

- `CaptureReady` advances to image generation running/submitted.
- Stylized PNG appears under `Library/GeneratedObjectOutputs/`.
- Job advances to `StylizedImageReady`.

Hosted upload:

- Local PNG uploads to `https://www.mikusc.top/api/scene-shift/upload`.
- Request uses raw PNG body or supported multipart format.
- Header is `x-sceneshift-upload-token`.
- Job receives a valid hosted image URL.

Seed3D:

- Job advances to `ModelGenerationSubmitted`.
- Polling does not permanently stall.
- Job advances to `ModelReady`.
- Downloaded model is stored under the expected local model/generated-asset path.
- If Seed3D returns a zip package, only the real `.glb` is copied into `Assets/Generated/ThemeAssets/<requestId>/`.

Import:

- `GeneratedObjectModelImporter` imports the matching `ModelReady` job.
- Job advances to `Imported` or `NeedsReview`.
- `ImportedPrefabPath` points to a valid prefab.
- Import does not locally resize embedded GLB textures; use upstream low-quality/low-texture generation settings to control memory and asset size.

Placement:

- Runtime placement reports request-locked match.
- Placed generated furniture receives a `StylizedFurnitureInstance` marker so correction controls can target it by `ObjectId`.
- Aim at a generated furniture object until the panel `Rotate` row shows its object id, then press `Rotate 90`.
- The selected generated furniture rotates 90 degrees around world Y without moving room surfaces, other furniture, or MRUK shells.
- Rotation is a runtime correction for the current Play session; do not treat it as persisted room calibration until persistence is explicitly added.
- Generated object fits the MRUK scaffold within acceptable visual bounds.
- Bottom/contact surface is grounded.
- Rotation is plausible.
- It does not block walkable clearance.

Runtime GLB spike:

- `RuntimeState` contains `RuntimeGeneratedModelLoader`, `GeneratedObjectReviewController`, `CorrectionModeController`, and `GeneratedObjectRotationCorrectionController`.
- `RuntimeState` contains `QuestRuntimeGenerationClient` in `LocalTestModelUrl` mode unless a real secure backend endpoint is being tested.
- `RuntimeState` contains `PreDeviceRuntimeLoopValidator` for Play Mode pre-device validation on Mac.
- `RuntimeState` contains `PreDeviceSmokeReportRunner` for the broader Play Mode smoke report.
- In Play Mode, `PreDeviceRuntimeLoopValidator` can queue one current-room/current-style `TABLE` request, write request/job/prompt artifacts, and submit it through `QuestRuntimeGenerationClient` local test mode.
- After the room is ready, `PreDeviceSmokeReportRunner.RunSmokeReport()` writes JSON/Markdown reports under `Library/PreDeviceSmokeReports/`.
- A valid current Mac pre-device smoke result can be `PassWithManualVisualChecks` when only surface visual quality and dashboard visual layout remain manual.
- Store supporting screenshots or local review notes under `Library/PreDeviceVisualEvidence/` when closing any manual visual item from Mac evidence.
- Dashboard `Submit+Load` advances the latest generated-object job through the runtime backend boundary and immediately loads the returned model URL when local test mode is active.
- Dashboard `Load Test GLB` attaches the configured sample GLB URL to the latest generated-object job/request and attempts runtime loading without `AssetDatabase`.
- Dashboard `Load Latest Job` can load a job that already has `RuntimeModelUrl`, `RuntimeModelLocalPath`, or `GeneratedModelPath`.
- Runtime row reports the loader state, including download/load failures.
- A runtime-loaded object is parented under `RuntimeGeneratedModels` and receives `RuntimeGeneratedModelInstance` plus `StylizedFurnitureInstance` markers.
- The smoke report must validate request/job contract traceability: the runtime-loaded object should map to a matching `.job.json`, `.request.json`, prompt artifact, room id, object id, style id/variant, semantic label, target bounds/physical dimensions, HTTPS model URL, local runtime GLB file, and `RuntimeLoaded` state.
- The smoke report must validate runtime backend artifact traceability: `LocalTestModelUrl` and later `HttpBackend` runs should write matching `.runtime-submission.json` and `.runtime-result.json` artifacts that preserve request/object/style/semantic identity, source request and prompt paths, target bounds, backend job/result state, and model URL handoff.
- For true-device closure on the current Mac setup, repeat the successful local spike through a MQDH/test-channel headset install rather than treating Editor Play as final evidence.
- If a candidate was previously rejected or reset, loading the same runtime-ready job should immediately restore that review state and keep the candidate hidden before the user presses any review button.
- If a candidate was previously accepted or corrected and its local GLB still exists, the review controller should be able to restore it from `GeneratedObjectReviews` without submitting a new backend generation job.
- Persisted corrections should be idempotent: selecting or restoring the same corrected candidate repeatedly must not increase its offset, yaw, or scale each time.
- Reset-to-fallback should hide the runtime generated candidate and reveal a deterministic fallback proxy for the same object id when a theme fallback exists.
- Reject/reset should release hidden runtime generated model GameObjects after writing review/job state, while keeping local GLB and review/job records available for retry or persisted-state restore.
- After a pre-device regression, run `SceneShift/Generated Objects/Archive Pre-Device Runtime Artifacts - Keep Latest` when old `predevice_room_loop_*` jobs, runtime model folders, or review records should be removed from the active evidence set but preserved for audit.

Build readiness preflight:

- Run `SceneShift/Validation/Check Serialized Credentials`. Any finding is a build blocker; the
  report identifies only the field and source location, not the secret value.
- Run `SceneShift/Validation/Run MQDH Pre-Package Evidence Suite` before creating a MQDH/test-channel package. It runs build readiness, creates the MQDH evidence template, and runs MQDH handoff preflight.
- The report is written under `Library/PreDeviceBuildReadinessReports/`.
- The report must pass `android_build_support_installed`; if it fails, install Android Build Support for the exact Unity Editor version before packaging.
- The report must pass `android_internet_permission`; the runtime GLB loader and backend client use headset-side `UnityWebRequest`.
- The report must pass the custom Android manifest checks for MRUK scene/anchor permissions, headset camera/PCA permission, passthrough support, supported Quest devices, HorizonOS SDK metadata, permissions dialog behavior, and VR launch metadata.
- The report must pass `runtime_local_test_model_https` and `runtime_backend_submit_url_https`; use HTTPS for the local test GLB and any configured backend endpoint.
- The report must pass `runtime_loader_assetdatabase_free` and `runtime_backend_client_runtime_path`; the Quest runtime path must use `Application.persistentDataPath`, `UnityWebRequest`, and glTF runtime loading rather than Editor-only `AssetDatabase` import.
- The report must read the latest smoke report status as `Pass` or `PassWithManualVisualChecks`.
- The report must parse the latest smoke report and find a safe `TABLE` target, `stylization_plan warnings=0`, `runtimeLoaded > 0`, runtime-loaded instance metadata with `RuntimeGeneratedModelInstance` plus `StylizedFurnitureInstance`, request/job contract traceability evidence, runtime backend artifact traceability evidence, editability/persistence evidence for bounded correction and temporary review-record roundtrip, reset-to-deterministic-fallback evidence, reject/reset release-policy evidence, and dashboard controls for `Submit+Load`, `Load Test GLB`, `Load Latest Job`, `Accept`, `Reject`, `Reset`, and `Rotate 90`.
- The report must find local visual review evidence and a screenshot that are newer than or equal to the latest smoke report when manual visual checks remain, and the review note must explicitly reference both the latest smoke report and the paired screenshot.
- The report must pass the packaged config/assets and generated job JSON secret scans.
- The report must pass active pre-device runtime artifact checks: one active `predevice_room_loop_*` set, complete job/request/prompt/runtime-submission/runtime-result files, a matching persistent runtime GLB folder, and latest-smoke request-id reference.
- The report must pass preflight/build tool checks for Android Support recovery, terminal pre-package suite, Unity MQDH package build runner, package build report verifier, local gate, package artifact verification, package-required gate verification, gate self-test, handoff bundle verification, and headset ADB evidence collection/verification scripts.
- After Android Build Support is installed, the acceptable pre-switch state is `PassWithWarnings` only when the remaining warning is `active_build_target=StandaloneOSX`; switch the Editor to Android before packaging.
- Treat any other warning or any failure as a packaging blocker unless it is explicitly documented for the current run.
- After installing Android Build Support from Unity Hub, run `bash Tools/check_android_support_recovery.sh` before reopening Unity to confirm the module files are present and identify stale readiness/template/handoff/terminal-suite/local-gate evidence that must be regenerated. If using Unity Hub CLI, install the required modules with `"/Applications/Unity Hub.app/Contents/MacOS/Unity Hub" -- --headless install-modules --version 6000.4.3f1 -m android android-sdk-ndk-tools android-open-jdk`.
- Use the generated `Library/MQDHHeadsetEvidence/mqdh_headset_evidence_*.md` file during the headset install/validation pass.
- The suite should generate `Library/MQDHHeadsetEvidence/mqdh_prepackage_evidence_suite_*.md`; follow its terminal command by running `bash Tools/run_mqdh_terminal_prepackage_suite.sh`.
- `SceneShift/Validation/Run MQDH Handoff Preflight` should confirm the template, latest terminal-suite/handoff/local-gate evidence fields, final APK/AAB gate commands, ADB helper, smoke evidence, and visual evidence are current; any `Fail` is a package/headset-run blocker.
- Run `bash Tools/scan_predevice_secrets.sh` before packaging to independently scan packaged config/assets and generated job JSON records for likely long-lived credentials.
- Run `bash Tools/run_mqdh_terminal_prepackage_suite.sh` before platform switching or packaging to write and verify the handoff bundle, write and verify the pre-package local gate, and record current handoff status in one report.
- Run `bash Tools/audit_true_device_preflight.sh` after the terminal suite when you need a single current matrix of readiness, handoff, local gate, package build, final package gate, and headset evidence state.
- Use `bash Tools/run_predevice_local_gate.sh`, `bash Tools/verify_predevice_local_gate.sh`, `bash Tools/show_mqdh_handoff_status.sh`, `bash Tools/write_mqdh_handoff_bundle.sh`, and `bash Tools/verify_mqdh_handoff_bundle.sh` directly only when debugging the individual steps reported by the terminal suite.
- After Android Support is installed and the Editor build target is Android, prefer `SceneShift/Validation/Build MQDH Test Package`; it builds the current Android APK/AAB mode, writes `Library/MQDHPackageBuildReports/mqdh_package_build_*.md`, and runs the final `--package-artifact` local gate automatically.
- Before MQDH/test-channel upload, `bash Tools/verify_mqdh_package_build_report.sh` should pass without `--allow-blocked`; the `--allow-blocked` option is only for documenting the current pre-build blocker state.
- If you create an APK/AAB manually, rerun `bash Tools/run_predevice_local_gate.sh --package-artifact <apk-or-aab-path>` and then `bash Tools/verify_predevice_local_gate.sh --require-package-artifact` before MQDH/test-channel upload so package artifact verification is recorded and enforced in the final local gate report. The final verifier must see `Overall: Pass`; `Fail` and `BlockedAndroidSupport` are not acceptable for upload. Use `bash Tools/verify_mqdh_package_artifact.sh <apk-or-aab-path>` directly only when debugging the package check.
- If any local gate, package artifact, or package-required verifier script changed, run `bash Tools/test_predevice_gate_scripts.sh` once before trusting the updated gate behavior.
- When the headset app is installed and open, run `bash Tools/collect_mqdh_headset_evidence.sh --package com.mikusc.sceneshiftroom.comp4145 --template <latest Library/MQDHHeadsetEvidence/*.md>` if ADB is available, then run `bash Tools/verify_mqdh_headset_evidence.sh` to validate the collected `adb_*` evidence directory.
- Use `docs/14_MQDH_TEST_CHANNEL_RUNBOOK.md` for the MQDH/test-channel handoff steps and headset evidence list.

---

# 7. UI / Interaction Checks

Runtime panel:

- Text is readable in headset and Game View.
- Buttons are selectable by the configured interaction mode.
- Bottom-row buttons are not outside the hit area.
- Theme dropdown or selector can be used without layout breaking.
- Clean View state is clearly visible.
- Object Status toggles world-space cards.
- `Submit+Load`, `Load Test GLB`, and `Load Latest Job` are selectable and do not crowd the review buttons.
- Reloading the same runtime-ready job respects persisted review state; rejected/reset candidates must not visibly reappear after reload or app restart.
- Rotate 90 is selectable and does not overlap the other room-control buttons.
- Main panel hide/show input works if enabled.
- Pure passthrough input works independently of Clean View and does not reuse the furniture target-cycle input.
- Official Interaction SDK ray/poke interaction remains enabled.
- No duplicate `SceneShiftDashboardPointerRay` / custom fallback line ray appears.
- If the official ray is hidden by the UI backplate, treat it as an official ray material/depth/render-order issue rather than re-enabling the custom fallback ray.

Object status cards:

- Cards are near the relevant furniture request bounds.
- Cards are not enormous.
- Text stays inside the card.
- Cards can be hidden for clean demo capture.

Known issue:

- Direct dynamic instantiation of complex official UISet sample controls previously caused layout problems. The canonical scene now uses an explicitly baked `SceneShiftDashboardContent` hierarchy; runtime creation is only the recovery path when that hierarchy is missing.
- After rebuilding the Editor hierarchy, save the scene and confirm the baked controls remain present after a scene reload before entering Play Mode.
- The dashboard may still contain hidden legacy/debug HUD suppression logic. Do not delete those components until the UISet panel, status cards, and clean-view flow have passed headset validation.

---

# 8. Demo Readiness Checklist

Before recording:

- Clear or understand all Console entries.
- Choose one room and one Style.
- Do not change inspector values mid-demo unless explaining a debug workflow.
- Confirm surface aesthetics from the intended user viewpoint.
- Confirm generated furniture is stable and request-matched.
- Decide whether MRUK shells are visible for explanation or hidden for clean view.
- Decide whether object status cards are visible or hidden.
- Keep generated-object branch framed as an optional enrichment unless accept/reject/reset is complete.

Demo should show:

- Room semantics.
- Style selection or custom style intent.
- Surface transformation across wall/floor/ceiling/openings.
- Window/vista treatment if a valid window exists.
- At least one grounded furniture replacement.
- Clean View.
- Queue/status visibility.
- Fallback behavior when a generated artifact is missing.

---

# 9. Minimal Pass Criteria

A smoke test passes if:

- No new blocking Console errors appear.
- MRUK room is readable.
- Runtime panel is usable.
- Surface stylization appears and is not visually noisy.
- Door/window behavior does not break spatial readability.
- Existing generated furniture remains stable.
- New capture does not overwrite unrelated generated furniture.
- Generated-object file artifacts are created when explicitly tested.
- The next failure point is documented.

For the current Mac route, do not mark a run as true-device closed unless the same flow has been installed or updated through Meta Quest Developer Hub / the configured test release channel and verified inside the standalone headset app.
If only local screenshots exist, call the result pre-device `PassWithCaveats` unless surface quality, dashboard readability, and interaction reachability are each visible from the captured viewpoints.

If the test fails, fix only the smallest blocking issue before adding new features.
