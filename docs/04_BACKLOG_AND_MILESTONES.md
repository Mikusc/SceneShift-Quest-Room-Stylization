# 04 Backlog and Milestones

## How to use this file
Never ask Codex to implement “everything.”
Use this file as the task queue.
Pick one milestone, then one task inside it.

Each task here is deliberately small enough to keep Unity MCP work controllable.

---

# Milestone 0 — Project foundation audit

## Goal
Make sure the project and package baseline are real before any feature work begins.

## Tasks
### M0.1 Inspect the current project state
Deliverables:
- package inventory
- scene inventory
- console status summary
- existing Meta / XR / AI / MRUK related assets summary

Acceptance:
- no code changes required
- Codex returns a clear summary of what exists and what is missing

### M0.2 Create the agreed folder structure
Deliverables:
- `Assets/Scripts/Core`
- `Assets/Scripts/MRUK`
- `Assets/Scripts/Perception`
- `Assets/Scripts/Stylization`
- `Assets/Scripts/UI`
- `Assets/Scripts/Debug`
- `Assets/Scripts/Editor`
- `Assets/Data/ThemeProfiles`
- `Assets/Scenes`

Acceptance:
- folders exist
- no compile errors introduced

### M0.3 Create the canonical scene
Deliverables:
- `Assets/Scenes/MR_RoomStylization.unity`
- root object layout consistent with `docs/03_ARCHITECTURE_AND_SCENE_LAYOUT.md`

Acceptance:
- scene opens
- scene has bootstrap root and UI placeholder roots

---

# Milestone 1 — MRUK semantic debug layer

## Goal
Make the room readable to the system before any stylization work.

## Tasks
### M1.1 Add or verify MRUK setup in the scene
Deliverables:
- scene can initialize MRUK room/scene data
- room-ready event or equivalent bootstrap log

Acceptance:
- room loads on device or a `MetaXRSimulator` simulated room path works
- simulator-based validation is acceptable for Phase 1A development, but it does not replace later validation in the canonical UNNC IEB office room
- no blocking console errors

### M1.2 Build `RoomSemanticBootstrap`
Deliverables:
- `RoomSemanticBootstrap.cs`
- normalized list of room anchors / semantic records

Acceptance:
- debug log shows major room semantics
- records are exposed in inspector or debug panel

### M1.3 Build semantic overlay/debug view
Deliverables:
- anchor labels / bounds visualization
- optional line renderers or gizmos
- simple debug canvas listing counts by category

Acceptance:
- user can visually inspect floor / wall / ceiling / known objects
- overlay can be toggled on/off

### M1.4 Export or inspect room snapshot
Deliverables:
- serializable room snapshot object or debug JSON export

Acceptance:
- snapshot contains room-scale semantics useful for later planner work

---

# Milestone 2 — Visible object perception fusion

## Goal
Supplement MRUK with visible-object understanding using official Meta tools.

## Tasks
### M2.0 Add manual semantic override fallback
Deliverables:
- a small override table keyed by MRUK anchor index/name/id
- override semantic label, function tag, and collision-sensitive flag
- debug display that marks the semantic source as `manual_override`

Acceptance:
- an `OTHER` or mislabelled anchor can be treated as `table` for planning/capture/application
- MRUK labels remain the default source of truth when no override is present
- the override path is clearly presented as user correction, not automatic perception

### M2.1 Verify Image Segmentation path
Deliverables:
- confirm whether installed SDK supports the block in this project
- document exact components/prefabs used
- create a minimal test setup in the scene

Acceptance:
- either Image Segmentation works, or fallback to Object Detection is explicitly chosen and documented

### M2.2 Build `ObservedObjectCollector`
Deliverables:
- `ObservedObjectCollector.cs`
- normalized `RoomObjectRecord`s from segmentation/detection

Acceptance:
- collector can report visible items with category/confidence/source

### M2.3 Add world-space visualization
Deliverables:
- visible proposal boxes, masks, or markers in MR
- optional confidence labels

Acceptance:
- visible proposals appear approximately aligned to the room

### M2.4 Build `SemanticFusionService`
Deliverables:
- merge MRUK anchor semantics and visible-object records
- confidence / source attribution
- deduplication rules

Acceptance:
- fused room snapshot available to later systems
- collision-sensitive records flagged

---

# Milestone 3 — Theme system and stylization planning

## Goal
Create a deterministic mapping from room semantics to themed replacements.

## Tasks
### M3.1 Create `ThemeProfile` scaffold and user `Style` data model
Deliverables:
- `ThemeProfile` ScriptableObject or equivalent for `GenericRoomStyleScaffold`
- starter user Style entries for:
  - `FutureResearchLab`
  - `ArcaneKnowledgeChamber`

Acceptance:
- generic scaffold contains surface/material/proxy/mood fallback fields
- built-in Style entries are the user-facing visual identities

### M3.2 Create `StylizationPlan` data model
Deliverables:
- `StylizationPlan`
- `StylizationPlanEntry`
- serializable mapping objects

Acceptance:
- plan can represent surface overrides and proxy replacements

### M3.3 Build `StylizationPlanner`
Deliverables:
- `StylizationPlanner.cs`
- rule-based mapping from fused semantics to plan entries

Acceptance:
- planner can map at least these categories:
  - wall
  - floor
  - table
  - screen
  - storage
  - seating

### M3.4 Create plan debug panel
Deliverables:
- UI list of plan entries
- warnings for unmapped or low-confidence items

Acceptance:
- user can inspect what the planner decided before application

---

# Milestone 4 — Stylization application

## Goal
Actually transform the room in a spatially grounded way.

## Tasks
### M4.1 Build `AnchorThemeApplier`
Deliverables:
- apply surface materials
- spawn or fit themed proxies
- store applied object metadata

Acceptance:
- one theme can visibly change at least four categories

### M4.2 Build `RoomMoodController`
Deliverables:
- light / ambience / audio changes
- optional whiteboard or screen treatment

Acceptance:
- room feels coherently themed, not just retextured

### M4.3 Add reset / reapply flow
Deliverables:
- reset current stylization
- reapply same theme
- switch between two themes safely

Acceptance:
- repeated application does not duplicate or corrupt content

---

# Milestone 5 — Manual correction mode

## Goal
Make system mistakes correctable in MR.

## Tasks
### M5.1 Select-and-inspect mapped object
Deliverables:
- user can point/select a mapped object
- show original semantic + replacement info

Acceptance:
- inspected object is highlighted

### M5.2 Nudge / rotate / reset controls
Deliverables:
- small correction controls
- yaw rotation only by default
- reset action

Acceptance:
- at least one incorrectly placed object can be corrected without code changes

### M5.3 Save correction deltas
Deliverables:
- correction data stored in memory or serializable form

Acceptance:
- correction persists at least during the current session

---

# Milestone 6 — Demo readiness

## Goal
Make the slice easy to show in a short course demo.

## Tasks
### M6.1 Build minimal user flow UI
Deliverables:
- room ready status
- theme selection buttons
- stylize button
- reset button
- correction mode toggle

Acceptance:
- demo can be run without opening the inspector

### M6.2 Build smoke-test checklist panel or method
Deliverables:
- simple verification flow
- pass/fail logging
- Play Mode pre-device smoke report under `Library/PreDeviceSmokeReports/`
- Editor-only pre-device build readiness report under `Library/PreDeviceBuildReadinessReports/`
- terminal-side secret scan for packaged config/assets and generated job records
- one-command terminal local gate report under `Library/MQDHHeadsetEvidence/predevice_local_gate_*.md`
- one-command terminal MQDH pre-package suite under `Library/MQDHHeadsetEvidence/mqdh_terminal_prepackage_suite_*.md`
- Unity MQDH package build report under `Library/MQDHPackageBuildReports/mqdh_package_build_*.md`
- package build report verifier for the upload gate

Acceptance:
- user can quickly verify whether the scene is stable before recording
- current Mac pre-device reports can pass automated checks while clearly marking Game View/Simulator visual review as manual
- current Mac build readiness detects whether Android Build Support is installed before packaging
- terminal preflight can independently confirm that likely long-lived service credentials are not present in packaged inputs or active generated records
- terminal preflight can summarize the full local gate in one report before platform switching or package upload
- terminal preflight can freeze and verify handoff evidence, local gate state, and handoff status through one suite command
- after Android Build Support is installed and the Editor build target is Android, the package build runner can create the current APK/AAB mode and run the final package local gate
- before MQDH/test-channel upload, the package build report verifier can require a `BuiltAndVerified` report with a present artifact and passing final local-gate checks
- after Android Build Support is installed, current Mac build readiness can report `PassWithWarnings` before packaging, with only the deliberate active build target warning remaining before switching to Android

### M6.3 Capture-friendly debug toggles
Deliverables:
- toggle for semantic overlay
- toggle for plan overlay
- toggle for correction UI

Acceptance:
- system can be shown both in “debug” and “clean demo” modes

---

# Stretch Milestone 7 — NPC preparation (not implementation yet)

## Goal
Prepare extension points for the future themed NPC.

## Tasks
### M7.1 Expose room context API
Deliverables:
- a simple service that returns:
  - current theme
  - important mapped objects
  - whiteboard target
  - mood state

Acceptance:
- API is ready for future LLM/NPC use

### M7.2 Reserve NPC spawn anchor
Deliverables:
- safe default spawn location
- alignment checks

Acceptance:
- future NPC can appear in a semantically sensible place

---

# Stretch Milestone 8 — Generated object enrichment

## Goal
Keep the Roomify-like generated-furniture experiment safe, request-locked, and reversible.

The current demo ambition is now a true-device end-to-end generated-object loop:

```text
user style intent
-> headset capture
-> secure backend generation
-> runtime 3D model load on Quest
-> request-locked replacement at the matching MRUK anchor
-> review / accept / reject / reset / bounded correction
```

This makes M8 the active stretch path for the demo, while preserving the deterministic room-stylization fallback. If generation or runtime loading fails, the room must still stylize through surfaces, openings, mood, and deterministic proxy/material behavior.

## Tasks
### M8.1 Capture generated-furniture reference requests
Deliverables:
- `DevicePassthroughCaptureService`
- `BestViewCaptureService`
- `GeneratedObjectRequest`
- source image path, request JSON, crop metadata, camera pose, object scaffold metadata

Acceptance:
- a headset/Quest Link user can target a supported MRUK furniture anchor from gaze and create one request
- simulator/external-screenshot fallback remains available
- deterministic/proxy room stylization still works if capture fails

Current status:
- implemented for supported categories including `TABLE`, `STORAGE`, `SCREEN`, `COUCH` mapped to `Seating`, `BED`, `LAMP`, `PLANT`, and `OTHER`
- request-locked capture prevents old object captures from silently replacing a different target

### M8.2 Create a file-based backend boundary
Deliverables:
- `GenerativeObjectCoordinator`
- Roomify-inspired `.prompt.txt`
- `.job.json`
- local mock backend mode
- `ExternalFileProtocol` submission/result-template mode

Acceptance:
- a request can move from `CaptureReady` to either a local mock `StylizedImageReady` result or an external/manual-worker submission
- no real cloud service is required for this step

Current status:
- implemented
- automated APIMart `gpt-image-2` image jobs, hosted upload, and Seed3D submission are wired
- manual/external-worker mode remains useful for replay and debugging
- generated-image requests should use the stricter transparent-object prompt version and preserve target object role, footprint, aspect ratio, and yaw

### M8.3 Import and register generated furniture proxies
Deliverables:
- generated-model import path
- generated asset registry/cache
- registration using scaffold size, best-view yaw, bottom-face alignment, and bounded scale

Acceptance:
- generated furniture candidate can be placed in the same scaffold as the deterministic proxy
- failed registration falls back to deterministic proxy

Current status:
- partially implemented
- `GeneratedObjectModelImporter` can import `ModelReady` jobs into generated furniture prefabs
- `AnchorThemeApplier` can prefer imported generated furniture prefabs when they match the active request/object/style
- current fitting uses transformed MRUK `VolumeBounds` corners, exact scaffold scale, and bottom-face alignment
- full Roomify-style OBB/IoU registration search is not implemented yet
- initial standalone runtime loading code now exists through `RuntimeGeneratedModelLoader`; the existing editor import path still remains the stable request-locked placement path until the runtime loader is scene-wired and validated on Quest

### M8.4 Add review and correction for generated furniture
Deliverables:
- preview generated object
- accept/reject generated object
- yaw/position nudge
- reset to deterministic proxy

Acceptance:
- collision-sensitive generated furniture is never silently finalized without an easy revert path

Current status:
- partially implemented
- runtime `Rotate 90` correction exists for selected generated furniture
- `GeneratedObjectReviewController` now provides initial preview/accept/reject/reset/persist decision plumbing for runtime-loaded generated candidates
- `GeneratedObjectReviewController` now restores a persisted review decision when a runtime candidate is loaded again; synthetic Editor Play validation confirmed rejected candidates stay hidden after reload
- `GeneratedObjectReviewController` can restore the latest accepted/corrected candidate from `GeneratedObjectReviews` without re-submitting generation; synthetic Editor Play validation confirmed an accepted candidate reloads from the existing local GLB
- corrected restore is idempotent in Editor Play: repeated selection does not double-apply persisted nudge/yaw corrections
- reset restore is validated in Editor Play: reloading the same runtime-ready job restores `ResetToFallback` and keeps the runtime candidate hidden
- dashboard buttons for `Accept`, `Reject`, and `Reset` have been added, but the full headset UX and MQDH/test-channel restart behavior still need validation
- bounded nudge/yaw correction persistence is now covered by the latest pre-device smoke probe, but this is not yet a complete demo-final headset correction mode

### M8.5 Spike true-device passthrough capture for generated requests
Deliverables:
- `DevicePassthroughCaptureService` validation on the target headset/runtime
- headset RGB frame saved to `Application.persistentDataPath`
- metadata JSON containing camera pose, intrinsics if available, selected MRUK anchor, bounds, and projected crop rect
- Immersive Debugger button or debug UI trigger for capture

Acceptance:
- Quest build can produce a real device capture artifact without relying on Simulator screenshots
- artifacts can be pulled through MQDH/ADB for offline image stylization and Seed3D testing
- deterministic stylization remains usable when device capture fails or permissions are missing

Current status:
- compile/scene-wired and useful in Quest Link / Editor Play workflows
- still needs explicit true-device PCA pass/fail evidence on the target headset/runtime

### M8.6 Add secure headset-to-backend generation service
Deliverables:
- `QuestRuntimeGenerationClient` or equivalent runtime client
- backend endpoint contract for style intent, captured image, anchor metadata, and target physical size
- backend job status polling contract
- generated model URL/result contract with clear failure reasons
- no APIMart, Seed3D, DeepSeek, upload, or signing credentials stored in scene files, job JSON, APK resources, or git

Acceptance:
- Quest build can submit a captured request to a backend without local API keys
- headset UI can show queued/running/failed/ready states
- failed jobs remain inspectable and retryable
- deterministic fallback remains usable during network/backend failure

Current status:
- initial implementation added
- `QuestRuntimeGenerationClient` is scene-wired under `RuntimeState` and exposes a Quest-side backend boundary without embedding APIMart, Seed3D, DeepSeek, upload, or signing credentials
- default `LocalTestModelUrl` mode returns the configured sample GLB URL and advances the latest generated-object job to `RuntimeModelReady`, so headset UI and runtime loader behavior can be validated before a real secure backend exists
- `HttpBackend` mode now uploads multipart metadata/request/prompt/captured image payloads to a configured HTTPS endpoint, polls `RuntimeGenerationBackendResult`, and records backend model URL/hash fields when available
- `Backend/sceneshift_runtime_backend.py` provides the first no-secret backend service with `manual`, `fixed-url`, and real `seed3d` provider modes; provider credentials stay in the backend process environment
- `SceneShift/Runtime Backend/Configure HttpBackend From Environment` can switch the scene client from environment-provided HTTPS URL values without typing keys into the Inspector
- `Tools/check_runtime_backend_seed3d_preflight.sh` verifies the real-backend environment without printing secrets; it must pass before an `HttpBackend` package can count toward true 3D generation closure
- current Unity cloud adapters still use process environment variables and remain Editor/Quest Link development tools, not the standalone APK credential model
- still needs real `seed3d` provider execution through an HTTPS endpoint reachable by Quest and true-device MQDH/ADB validation

### M8.7 Load generated models at runtime on Quest
Deliverables:
- runtime `.glb` download/read path under `Application.persistentDataPath`
- runtime model loader that does not use `AssetDatabase`
- bounds and bottom-pivot normalization equivalent to the editor importer
- collider stripping or collider ignore policy for untrusted generated geometry
- request/object/style-locked handoff to `AnchorThemeApplier`

Acceptance:
- a known test GLB URL can be downloaded, runtime-loaded, and fitted to one selected `TABLE` anchor on Quest
- no Unity Editor reimport step is required
- load errors are visible in the dashboard/status cards
- memory use remains acceptable after loading, replacing, rejecting, and resetting one generated object

Current status:
- initial implementation added
- `RuntimeGeneratedModelLoader` can download or read a GLB into `Application.persistentDataPath`, load it with glTFast without `AssetDatabase`, strip generated colliders, normalize bounds to a centered bottom pivot, and fit to the captured request bounds when a `GeneratedObjectRequest` is available
- `MR_RoomStylization.unity` now scene-wires the runtime loader under `RuntimeState` with a `RuntimeGeneratedModels` root and a fixed Khronos sample GLB URL for the first narrow spike
- `PreDeviceRuntimeLoopValidator` is scene-wired under `RuntimeState` to queue a current MRUK room/current Style request for a safe `TABLE` target before a headset build is published
- `PreDeviceSmokeReportRunner` is scene-wired under `RuntimeState` to write a broader Mac pre-device smoke report after the room is ready
- `PreDeviceBuildReadinessReportRunner` is available from `SceneShift/Validation/Run Pre-Device Build Readiness Report` to verify Android/Quest packaging prerequisites before MQDH/test-channel packaging
- `SceneShiftUISetDashboard` exposes `Submit+Load`, `Load Test GLB`, and `Load Latest Job` so the runtime backend and loader spike can be triggered from the headset panel instead of an Editor-only context menu
- runtime job states and status cards now include `RuntimeBackendSubmitted`, `RuntimeModelReady`, `RuntimeModelDownloaded`, and `RuntimeLoaded`
- synthetic Editor Play validation of `Submit+Load` reached `RuntimeLoaded` with one preview `RuntimeGeneratedModelInstance` fitted to request bounds
- current-room Editor Play pre-device validation reached `RuntimeLoaded` for `TABLE_18` using the fixed test GLB and request bounds
- broader Editor Play smoke report reached `PassWithManualVisualChecks`; the latest run is `Library/PreDeviceSmokeReports/predevice_smoke_20260524231824.md` with `stylization_plan warnings=0`, `runtimeLoaded=7`, runtime-loaded instance metadata for `TABLE_18`, `runtime_request_job_contract=Pass`, `runtime_backend_artifact_contract=Pass`, `runtime_review_editability_persistence=Pass`, `runtime_reset_deterministic_fallback=Pass`, and `runtime_reject_reset_release_policy=Pass`; remaining checks are surface visual quality and dashboard visual layout in Game View/`MetaXRSimulator`, plus MQDH/test-channel headset validation of the configured test GLB and tighter handoff to `AnchorThemeApplier`
- local visual evidence has been recaptured after the latest smoke report as `Library/PreDeviceVisualEvidence/predevice_visual_review_202605242319.md` plus `display2_after_backend_artifact_smoke_202605242319.png`, so build readiness can pair the current smoke with current Game View/Meta XR Simulator screenshots; the readiness gate also requires the review note to reference the latest smoke report and paired screenshot explicitly
- `GeneratedObjectAssetCleaner` can now report/archive stale Mac pre-device runtime artifacts while preserving the latest smoke-linked request/job/prompt/runtime-submission/runtime-result files and matching persistent runtime model folder as active evidence
- `PreDeviceBuildReadinessReportRunner` now directly checks the active pre-device runtime artifact set: exactly one active `predevice_room_loop_*` request, complete runtime job artifacts, a persistent GLB folder, and request-id linkage to the latest smoke report
- `MqdhHeadsetEvidenceTemplateWriter` is available from `SceneShift/Validation/Create MQDH Headset Evidence Template`; it writes `Library/MQDHHeadsetEvidence/*.md` with latest local evidence links plus headset install/restart/log/media fields
- `MqdhHandoffPreflightReportRunner` is available from `SceneShift/Validation/Run MQDH Handoff Preflight`; latest report `Library/MQDHHeadsetEvidence/mqdh_handoff_preflight_20260525095727.md` confirms the evidence template, ADB helper, smoke report, visual evidence, terminal suite fields, true-device preflight audit command, and MQDH package build menu are current
- `Tools/collect_mqdh_headset_evidence.sh` is available for the actual MQDH/test-channel headset pass to collect ADB/logcat/screenshot/persistent-file evidence into `Library/MQDHHeadsetEvidence/adb_*`
- `Tools/verify_mqdh_headset_evidence.sh` is available to validate collected `adb_*` headset evidence directories after the app is installed and opened on the headset
- `Tools/verify_mqdh_package_artifact.sh` is available to validate an APK/AAB before MQDH/test-channel upload, including ZIP structure, ARM64 Unity libraries, optional `aapt` metadata, and embedded credential strings
- `Tools/show_mqdh_handoff_status.sh` is available as a terminal-side summary of latest readiness/template/handoff/terminal-suite/local-gate status, package-artifact gate status, and the current package blocker
- `Tools/scan_predevice_secrets.sh` is available as a terminal-side companion to the Unity readiness secret scans; `Tools/show_mqdh_handoff_status.sh` now reports its pass/fail summary
- `Tools/run_mqdh_terminal_prepackage_suite.sh` is available as the preferred terminal follow-up after the Unity MQDH suite; latest report is `Library/MQDHHeadsetEvidence/mqdh_terminal_prepackage_suite_20260525_095727.md` with overall `Pass`
- `Tools/run_predevice_local_gate.sh` is available as the one-command terminal gate; it can also include package artifact verification through `--package-artifact <apk-or-aab-path>` after a build is produced. Latest final gate report is `Library/MQDHHeadsetEvidence/predevice_local_gate_20260525_095730.md` with overall `Pass`
- `Tools/verify_predevice_local_gate.sh` is available to confirm the latest local gate report still references the latest readiness/smoke/visual/template/handoff/bundle evidence and records zero secret-scan findings; after APK/AAB creation, use `--require-package-artifact` to require a passing package check in the gate report
- `Tools/test_predevice_gate_scripts.sh` is available as a shell regression self-test for local gate, package artifact, and final package-required verifier behavior
- `Tools/check_android_support_recovery.sh` is available for the post-Unity-Hub step: it verifies AndroidPlayer/SDK/NDK/OpenJDK/adb and reports which readiness/template/handoff/terminal-suite/local-gate evidence must be regenerated
- `MqdhPackageBuildRunner` is available from `SceneShift/Validation/Build MQDH Test Package`; it writes `Library/MQDHPackageBuildReports/mqdh_package_build_*.md`, blocks before build if readiness or Android target state is invalid, and runs the final package local gate after a successful package build
- `Tools/verify_mqdh_package_build_report.sh` is available to require a `BuiltAndVerified` package build report before upload; use `--allow-blocked` only to verify the current blocked pre-build state while Android Support is missing
- `Tools/check_unity_android_support.sh` is available to verify Android Build Support files on disk immediately after Unity Hub installation and before reopening Unity
- `Tools/write_mqdh_handoff_bundle.sh` is available to freeze the latest smoke/readiness/visual/template/handoff/generated-object evidence plus terminal secret scan output into a `Library/MQDHHeadsetEvidence/handoff_bundle_*` manifest directory
- `Tools/audit_true_device_preflight.sh` is available as a one-command current-state matrix across readiness, handoff, terminal suite, local gate, package build report, final package gate, ADB availability, and headset ADB evidence; latest report is `Library/MQDHHeadsetEvidence/true_device_preflight_audit_20260525_095959.md` with overall `ReadyForMQDHUpload`
- latest bundle manifest is `Library/MQDHHeadsetEvidence/handoff_bundle_20260525_095727/manifest.md`, documenting the current verified pre-upload evidence set and a zero-finding terminal secret scan
- `Tools/verify_mqdh_handoff_bundle.sh` is available to confirm the latest bundle references the latest evidence files, includes a zero-finding secret scan, and that copied SHA-256 hashes still match
- latest pre-device build readiness is `Library/PreDeviceBuildReadinessReports/predevice_build_readiness_20260525095727.md`; it passes with Android Build Support detected at `/Applications/Unity/Hub/Editor/6000.4.3f1/PlaybackEngines/AndroidPlayer`
- latest MQDH package build report is `Library/MQDHPackageBuildReports/mqdh_package_build_20260525_095717.md` with overall `BuiltAndVerified`; `Tools/verify_mqdh_package_build_report.sh` passes and the artifact is `Builds/MQDH/SceneShiftQuest_20260525_095717.apk`
- next validation step is MQDH/test-channel upload or install, then headset-side evidence collection with `Tools/collect_mqdh_headset_evidence.sh`
- `docs/14_MQDH_TEST_CHANNEL_RUNBOOK.md` now records the operational package/install/headset evidence path after local pre-device gates pass
- after a headset install/run, collect evidence with `Tools/collect_mqdh_headset_evidence.sh` and validate it with `Tools/verify_mqdh_headset_evidence.sh` before treating the run as usable true-device evidence

### M8.8 Complete generated-object review and persistence
Deliverables:
- generated object preview state
- accept generated object
- reject generated object
- reset to deterministic fallback
- bounded nudge / yaw / limited scale correction
- persisted accepted/rejected/corrected record keyed by room/object/style/request

Acceptance:
- collision-sensitive generated furniture is never silently finalized
- accepted objects can be restored for the same room/object/style when the model remains available
- rejected objects do not reappear unless the user retries/regenerates
- reset returns the object to deterministic fallback without corrupting the room

Current status:
- partially implemented
- `GeneratedObjectReviewController` persists accept/reject/reset/corrected decisions under `Application.persistentDataPath/GeneratedObjectReviews/`
- `SceneShiftUISetDashboard` exposes initial `Accept`, `Reject`, and `Reset` actions for runtime generated candidates
- `MR_RoomStylization.unity` now scene-wires `GeneratedObjectReviewController`, `CorrectionModeController`, and `GeneratedObjectRotationCorrectionController`
- accepted-state restore from persisted review plus local GLB is validated in Editor Play
- rejected-state restore after runtime reload is validated in Editor Play
- corrected-state restore and reset-state restore are validated in Editor Play
- accepted/rejected/corrected/reset restore also passed against a current-room pre-device `TABLE_18` request queued by `PreDeviceRuntimeLoopValidator`
- latest `PreDeviceSmokeReportRunner` evidence also passes `runtime_review_editability_persistence`: selected runtime `TABLE_18`, confirmed a bounded 0.025 m forward nudge plus 5 degree yaw, and verified temporary `Accepted`, `Rejected`, `ResetToFallback`, and `Corrected` review-record roundtrips
- latest `PreDeviceSmokeReportRunner` evidence also passes `runtime_request_job_contract`: the loaded `TABLE_18` runtime instance traces back to a matching job record, request record, prompt artifact, room/object/style/semantic identity, request bounds, HTTPS model URL, local runtime GLB file, and `RuntimeLoaded` job state
- latest `PreDeviceSmokeReportRunner` evidence also passes `runtime_backend_artifact_contract`: `LocalTestModelUrl` now writes matching runtime submission/result artifacts and the result records a local-test backend job id, `RuntimeModelReady` state, and HTTPS test GLB URL
- latest `PreDeviceSmokeReportRunner` evidence also passes `runtime_reset_deterministic_fallback`: a reset probe hides the runtime `TABLE_18` candidate and verifies a visible `theme_default` deterministic fallback proxy for the same object id
- latest `PreDeviceSmokeReportRunner` evidence also passes `runtime_reject_reset_release_policy`: hidden reject/reset runtime candidates are configured for release, and the runtime loader can release an inactive runtime model probe
- MQDH/test-channel headset restart behavior and headset-visible deterministic fallback replacement after reset still need validation

---

# Fallback plan if AI perception is unstable

If Image Segmentation or detection blocks progress:
1. keep MRUK as the backbone,
2. stylize only room-scale surfaces and known anchors,
3. manually tag one key table/screen/storage/seating object if needed,
4. continue building planner/applier/correction flow.

That still produces a valid Phase 1 prototype.

---

# Recommended immediate next task
Do not restart from M1 unless the project has been reset.

For the current repository state, use `docs/08_PROGRESS_STATUS.md` as the rolling source of truth.

The smallest safe next task depends on the current demo goal:
- for the true-device generated-object demo, keep using `PreDeviceRuntimeLoopValidator` plus dashboard `Submit+Load` as the local Mac gate, then repeat the same known test model URL and one safe `TABLE` target through MQDH or the configured test release channel on the headset
- run `PreDeviceSmokeReportRunner` before packaging; keep `PassWithManualVisualChecks` framed as local pre-device evidence until the Game View/Simulator and MQDH/test-channel headset runs are complete
- Android Build Support for Unity `6000.4.3f1` is currently detected at the Unity Hub module path; rerun `PreDeviceBuildReadinessReportRunner` if packages, Player Settings, or build target state changes before packaging
- if Android Support has to be reinstalled on another machine, run `bash Tools/check_android_support_recovery.sh` before reopening Unity so stale evidence is explicitly identified
- run `bash Tools/scan_predevice_secrets.sh` before packaging or MQDH handoff so credential leakage is checked even when Unity is not open
- run `bash Tools/run_predevice_local_gate.sh` as the terminal-side gate when pre-package evidence changes; the latest final package gate already passes for `Builds/MQDH/SceneShiftQuest_20260525_095717.apk`
- run `bash Tools/verify_predevice_local_gate.sh` when you need to prove the latest pre-package local gate report is still fresh after new evidence files are generated
- after Android Support is installed and the build target is Android, prefer `SceneShift/Validation/Build MQDH Test Package` to build the package and run the final local gate, then run `bash Tools/verify_mqdh_package_build_report.sh`; if building manually, rerun `bash Tools/run_predevice_local_gate.sh --package-artifact <apk-or-aab-path>` before upload/install so package verification is recorded in the final gate report, then run `bash Tools/verify_predevice_local_gate.sh --require-package-artifact`
- run `bash Tools/test_predevice_gate_scripts.sh` after editing local gate or package-verifier scripts
- after runtime loading works on device, deploy the real secure backend job submission/polling path and harden generated-object accept/reject/reset persistence
- for the core Phase 1 slice, validate the current surface/opening aesthetic in the UNNC IEB office and document remaining visual issues
- before recording or committing a demo build, run the checklist in `docs/11_SMOKE_TEST_AND_DEMO_CHECKLIST.md`

If starting from a fresh clone or a broken scene, then fall back to:
- M1.1
- M1.2
- M1.3
