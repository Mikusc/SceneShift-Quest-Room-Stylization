# 08 Progress Status

## Purpose
This file is the manual progress tracker for the current vertical slice.
Update it after each meaningful implementation step.

## Snapshot
- Last updated: `2026-04-25`
- Current priority: `Phase 1 — room stylization`
- Canonical scene: `Assets/Scenes/MR_RoomStylization.unity`
- Primary development validation path: `MetaXRSimulator` for MRUK/planner/applier logic; Quest build validation is required for passthrough camera capture and final spatial confidence.

## Milestone Status
| Milestone | Status | Notes |
| --- | --- | --- |
| M0 — Project foundation audit | Partial | Project audit and canonical scene exist; agreed folder structure is only partially normalized. |
| M1 — MRUK semantic debug layer | Mostly done | Simulator path works, room bootstrap exists, semantic HUD exists, and a thin best-view capture plus generated-object request export path now exists with a `TABLE` object-level crop rect and external-screenshot simulation input. Further capture refinement remains open. |
| M2 — Visible object perception fusion | Not started | No `ObservedObjectCollector` or `SemanticFusionService` yet. A smaller manual semantic override fallback is now the recommended next step if true-room MRUK labels miss the table. |
| M3 — Theme system and stylization planning | Mostly done | `ThemeProfile`, `StylizationPlan`, `StylizationPlanner`, and debug HUD are in place. |
| M4 — Stylization application | In progress | Surface stylization and room mood are working. Table proxy replacement is wired but still being refined. |
| M5 — Manual correction mode | Started | A standalone `CorrectionModeController` code skeleton now exists, but it is not yet scene-integrated. |
| M6 — Demo readiness | Not started | No dedicated demo UI, smoke-test panel, or capture toggles yet. |
| M7 — NPC preparation | Not started | Intentionally deferred until Phase 1 is stable. |
| M8 — Generated object enrichment | In progress | Multiple `TABLE` side-branch jobs reached transparent image, Seed3D GLB, imported prefab, and runtime generated-prefab placement in Simulator. Current preferred candidate is `table_18_20260425025836`. |

## Working Features
- `RoomSemanticBootstrap` initializes MRUK room semantics for the canonical scene.
- `StylizationDebugPanel` shows room/theme/planner/applier state in-scene.
- Theme selection is available through `ThemeIntentController`.
- `RuntimeStyleIntentController` is attached under `AppRoot/Perception` and provides a Roomify-like optional user style layer. It converts freeform text such as `cyberpunk` into deterministic style/material/color/motif keywords, can write an LLM handoff prompt under `Library/StyleIntentJobs/`, and feeds those keywords into generated-object requests.
- `DeepSeekStyleIntentProvider` is attached under `AppRoot/Perception` as an optional external style parser. In Play mode it can call DeepSeek V4 (`deepseek-v4-flash` by default) using `DEEPSEEK_API_KEY`, then replace the deterministic fallback with JSON keyword fields if the response is valid.
- `StylizationPlanner` generates deterministic mappings for wall, floor, ceiling, table, screen, storage, and seating.
- `AnchorThemeApplier` visibly stylizes wall / floor / ceiling surfaces.
- When a `ThemeProfile` has no explicit wall / floor / ceiling material assets, `AnchorThemeApplier` now falls back to deterministic runtime procedural surface textures instead of plain color-only materials.
- `SurfaceTexturePromptBuilder` writes Roomify-style wall / floor / ceiling prompt artifacts under `Library/SurfaceTextureJobs/` for future offline seamless PBR texture generation.
- `Assets/Materials/SurfaceOverrides/` now contains GPT-image-2 generated albedo PNGs plus Unity materials for both current themes, and the `ThemeProfile` surface material fields are populated for wall / floor / ceiling instead of being visually empty in the Inspector.
- `SurfaceOverrideApplier` now spawns MRUK-scaffold-aligned surface override planes under `StylizedContentRoot/SurfaceOverrides`, including a 5cm wall outward offset aligned with the Roomify scene-composition rule.
- `SurfaceOverrideApplier` now exposes `Off`, `Background`, and `DemoStrong` surface visibility modes. The canonical scene defaults to `Background` so wall / floor / ceiling stay as low-opacity style atmosphere while furniture replacement is still being brought up.
- `RoomMoodController` provides theme-linked mood changes.
- Initial table proxy spawning is connected from planner to applier.
- `BestViewCaptureService` tracks the best visible `TABLE` anchor in Play mode and writes a full-frame reference image + metadata + `GeneratedObjectRequest` JSON export to `Library/BestViewCaptures/`; in `ExternalScreenshot` mode the original screenshot is currently used directly as the backend input while the estimated crop rect remains in metadata. Requests now carry target physical length / width / height, target aspect ratio, safety footprint scale, and vertical fit mode for generated asset workers.
- `BestViewCaptureService` now distinguishes between `ExternalScreenshot`, `UnityFramebufferDebug`, and `DevicePassthroughReserved` source modes; `ExternalScreenshot` is the preferred simulation path.
- `DevicePassthroughCaptureService` is now attached under `Perception` as the first Quest Link / headset PCA probe. In Play mode it tracks the best visible `TABLE` MRUK anchor, uses `Meta.XR.PassthroughCameraAccess` for native camera texture, pose, intrinsics, and projection, writes full-frame/cropped PNG plus metadata/request JSON, and can queue a `CaptureReady` generated-object job directly under `Library/GeneratedObjectJobs/`.
- `DevicePassthroughCaptureHud` is attached under `Perception` and creates a head-locked world-space PCA status panel at runtime. The panel shows PCA readiness, target category, best anchor, best-view score, distance, viewport/crop values, input hint, and latest capture/job id so Quest Link validation does not require watching the Unity Game View.
- `DevicePassthroughCaptureService` now supports keyboard `P` and right-controller primary button capture input for headset-side validation.
- `GenerativeObjectCoordinator` is now attached under `Perception` and writes a local `.job.json` shell to `Library/GeneratedObjectJobs/` when a new captured request appears.
- `GeneratedObjectPromptBuilder` now converts each request into a Roomify-inspired prompt artifact and `GenerativeObjectCoordinator` writes it as `Library/GeneratedObjectJobs/*.prompt.txt`.
- `GeneratedObjectPromptBuilder` now emits prompt version `roomify_image_asset_v3_style_keywords`, including `STYLE_INTENT` fields when runtime user style text is provided. The user style layer affects visual language only; object function, footprint, dimensions, yaw, and safety constraints remain controlled by MRUK/planner data.
- `LocalGeneratedObjectBackendAdapter` is now attached under `Perception` and can locally consume `CaptureReady` jobs into simulated `StylizedImageReady` outputs by applying a theme-aware mock stylization transform, writing `Library/GeneratedObjectOutputs/*.stylized.png`, and writing a matching `*.result.json` backend artifact.
- `LocalGeneratedObjectBackendAdapter` now also exposes an `ExternalFileProtocol` mode that writes `Library/GeneratedObjectBackendInbox/*.submission.json` plus a prefilled `*.result.template.json`, and can later consume a dropped external `*.result.json` without changing the Unity-side job/prompt contract.
- `LocalGeneratedObjectBackendAdapter` can now consume either a local stylized PNG path or an external hosted stylized image URL from `GeneratedImageBackendResult`, enabling the Seed3D API path without adding an upload worker.
- `ApimartImageBackendAdapter` is attached under `Perception` as the first automated image-generation worker. It reads `APIMART_API_KEY`, submits `CaptureReady` jobs to APIMart `gpt-image-2`, downloads the returned stylized PNG into `Library/GeneratedObjectOutputs/`, and advances the job to `StylizedImageReady`.
- `HostedImageUploadBridge` uploads local stylized PNG outputs to the configured `www.mikusc.top` Azure Static Web Apps endpoint using `SCENESHIFT_UPLOAD_TOKEN`, raw `image/png` body, and `x-sceneshift-upload-token`, then writes `StylizedImageUrl` back to the job for Seed3D `image_url` input.
- `Seed3DBackendAdapter` can process `StylizedImageReady` jobs when a public `http(s)` stylized image URL is available, read `ARK_API_KEY` from the Unity process environment, create Ark Seed3D 2.0 tasks, record `ModelGenerationSubmitted`, resume polling by task id after interruption, download the generated model under `Assets/Generated/ThemeAssets/<requestId>/`, and advance the job to `ModelReady`.
- Current Seed3D downloads must be checked for zip packaging. The validated manual flow extracts backend zip packages under `Library/GeneratedObjectModels/<requestId>/downloaded_package/` and copies only the real `.glb` into `Assets/Generated/ThemeAssets/<requestId>/` before import.
- Earlier manual GPT-image/Seed3D outputs exist for `TABLE_18`, including `table_18_20260424071758`, `table_18_20260424173938`, and `table_18_20260425025836`.
- The current preferred generated table candidate is:
  - stylized image: `Library/GeneratedObjectOutputs/table_18_20260425025836.stylized.png`
  - hosted image: `https://www.mikusc.top/scene-shift/seed3d/table_18_20260425025836.stylized.png`
  - Seed3D task: `cgt-20260425030546-n8b7x`
  - GLB: `Assets/Generated/ThemeAssets/table_18_20260425025836/table_18_20260425025836.seed3d.pbr.glb`
  - prefab: `Assets/Generated/ThemeAssets/table_18_20260425025836/table_18_20260425025836.generated_table_proxy.prefab`
  - imported bounds: `0.884 x 0.700 x 2.000`
- `GeneratedObjectModelImporter` imports `ModelReady` jobs, normalizes generated model bounds to a centered bottom pivot, removes imported colliders, saves a prefab, and advances the job to `Imported`.
- The current imported generated table prefab is `Assets/Generated/ThemeAssets/table_18_20260425025836/table_18_20260425025836.generated_table_proxy.prefab`.
- `AnchorThemeApplier` can use imported generated table prefabs, but generated-table lookup is now locked to the active capture request by default. It checks the latest `DevicePassthroughCaptureService` request first, then the simulator `BestViewCaptureService` request, and only uses imported jobs with matching `RequestId`, `ObjectId`, or source request path. If no active capture exists, it falls back to deterministic theme/default proxies instead of silently using an old Simulator-generated table.
- `AnchorThemeApplier` now auto-aligns imported generated table long axes before bounds fitting. If the generated model's local long axis and MRUK target long axis differ, runtime status reports `axis=rotated90(...)` and the visual child is rotated before final per-axis fit.
- The canonical scene currently keeps table replacement disabled (`applyTableProxies=false`) and original MRUK volume visuals visible (`hideOriginalVolumeVisuals=false`) so the default Play view returns to the MRUK shell. Generated/deterministic table proxy placement is an opt-in validation step, not the default scene state.
- Table generated-prefab fitting now transforms the MRUK `VolumeBounds` corners through the anchor transform into proxy-root local space before sizing, so rotated MRUK bounds do not treat a local axis as world-up by mistake.
- Table proxy footprint and height padding are now saved as `1.0` in the canonical scene for size consistency, and runtime status logs target/source/scale/bottomDelta fit diagnostics. The latest local code adds full-height generated-table fitting from MRUK floor to MRUK table top, horizontal safety footprint scaling, per-axis local footprint correction, and a final vertical offset field for one-room calibration.
- `CorrectionModeController` now exists as a standalone component code skeleton for selecting one applied object, inspecting metadata, nudging position, rotating yaw, resetting, and confirming correction deltas; it is not yet wired into the canonical scene or applier registry.
- Project documentation now includes a manual external-worker runbook, smoke-test/demo checklist, and true-device validation plan so the generated-object side branch can be tested without relying on ad hoc chat instructions.

## In Progress
- Table replacement readability and accept/reject UX after the generated Seed3D prefab is placed.
- Verifying the generated table visually from the user's intended Simulator camera angle, not just from fit metrics.
- Verifying that the generated table candidate for the active capture request is selected after import/reapply, rather than the newest unrelated generated table.
- Deciding whether exact per-axis generated-prefab fitting is acceptable for the current demo or whether to add a later Roomify-style IoU/orientation refinement step.
- Keeping old generated jobs compatible while new prompt artifacts use `roomify_image_asset_v3_style_keywords`; the already imported `table_18_20260424071758` job was produced by an earlier prompt artifact.
- Keeping documentation synchronized with the current code/scene state after each generated-object workflow change.
- Cleaning up crash-related transient workspace state before the next commit.

## Known Gaps
- No perception fusion layer yet for `Image Segmentation` or object detection.
- No manual semantic override table yet. If true-device MRUK labels the real table as `OTHER` instead of `TABLE`, this should be implemented before heavier perception work.
- No manual correction workflow yet.
- No reset / reapply / clean theme-switch flow documented as finished.
- Runtime user style extraction has a deterministic fallback plus an optional DeepSeek API path for Editor/Quest Link testing. For standalone headset builds, use a backend/proxy instead of embedding API keys in the APK.
- The committed surface texture assets are currently GPT-image-2 albedo maps only; matching normal / roughness / metallic maps are still a future refinement.
- The current object-level crop path is `TABLE`-only and still MRUK-anchor-driven; it is not yet generalized to fused `RoomObjectRecord` inputs.
- The current `ExternalScreenshot` path assumes the manual screenshot matches the active gameplay view and excludes window chrome. Because MetaXRSimulator resets the user pose on Play entry, new generated-table runs should take the screenshot after entering Play, paste that screenshot path into `BestViewCaptureService.externalScreenshotPath` in the same Play session, then press `C` without moving.
- The estimated crop rect can still drift from the screenshot composition because the image source and runtime camera are not the same frame.
- Image stylization is now wired through APIMart `gpt-image-2`, but it still needs live-key validation against a real `CaptureReady` job. Seed3D model generation has been exercised through Ark for recent table candidates, and hosted upload is configured through `www.mikusc.top`.
- Generated asset lookup still scans imported `.job.json` records; there is no dedicated generated asset registry database yet. The scan is now request-locked by default to avoid applying an unrelated generated table in a different real room.
- Generated furniture still lacks explicit user approval/reject/correction UI.
- A first true-device/Link passthrough-camera capture path exists through `DevicePassthroughCaptureService`, but it has not yet been validated on a Quest 3 / Quest 3S headset. Treat it as a probe until a real Link/device run produces PNG, metadata, request, and job artifacts.
- MQDH screenshots/casts are useful for human review, but they are not the app-consumable capture source. The app-side capture source should be the headset-supported PCA path, and MQDH should only be used to document what the user saw or to pull saved artifacts.
- No complete demo-ready UI path outside inspector/debug HUD usage.
- The Ark API key is read only from `ARK_API_KEY` in the Unity process environment; Unity must be launched with that environment variable for `Seed3DBackendAdapter` to see it.
- MetaXRSimulator / Editor Play still resets the user start pose between Play sessions. A first editor-only pose-bookmark experiment was reverted because it did not reliably override the simulator start pose; use simulator controls or a future official-rig-compatible solution instead.

## Latest Stable Verification
Last stable manual/runtime checks before this document update confirmed:
- `MR_RoomStylization` loads through the `MetaXRSimulator` path.
- MRUK room semantics and theme/planner/applier summaries are visible through the debug HUD.
- Wall / floor / ceiling stylization is visible at runtime.
- The first table proxy path resolves a prefab and spawns a proxy root.
- After reloading the scene with `BestViewCaptureService` wired into `Perception`, entering Play introduced no new project errors; only the previously accepted six Meta/OpenXR simulator warnings remained.
- After adding `GeneratedObjectRequest` export, Unity reimported the new script cleanly; no new compile errors were introduced by the request-contract change.
- After adding object-level crop rect calculation for `TABLE`, Unity reimported the updated script cleanly; no new compile errors or Console warnings were introduced by the crop-rect change.
- After switching best-view export to explicit capture-source modes, Unity reimported the updated scripts cleanly; no new compile errors or Console warnings were introduced by the contract/source-mode change.
- After setting the simulator-stage path to `ExternalScreenshot`, `Assets/Refresh` completed cleanly and Console remained at `0` entries in non-Play state.
- After adding `GenerativeObjectCoordinator`, Unity reimported the updated scripts cleanly, the component was attached to `AppRoot/Perception`, and the canonical scene saved successfully with no new Console entries.
- Manual validation confirmed that entering Play and pressing `C` now produces both a new `GeneratedObjectRequest` and a matching `Library/GeneratedObjectJobs/*.job.json` record, and the HUD shows a `GenerativeObjectCoordinator` status block.
- After adding `LocalGeneratedObjectBackendAdapter`, Unity reimported the updated scripts cleanly, the component was attached to `AppRoot/Perception`, and the canonical scene saved successfully with no new Console entries.
- After adding `GeneratedObjectPromptBuilder` and prompt-artifact writing in `GenerativeObjectCoordinator`, Unity reimported the updated scripts cleanly with Console remaining at `0` entries in non-Play state.
- After extending `LocalGeneratedObjectBackendAdapter` to apply a theme-aware local image transform and write `Library/GeneratedObjectOutputs/*.result.json`, Unity reimported the updated scripts cleanly with Console remaining at `0` entries in non-Play state.
- After extending `LocalGeneratedObjectBackendAdapter` with `ExternalFileProtocol` and a prefilled `*.result.template.json` artifact, Unity reimported the updated scripts cleanly with Console remaining at `0` entries in non-Play state.
- After switching the scene-side `LocalGeneratedObjectBackendAdapter` processing mode to `ExternalFileProtocol`, `Assets/Refresh` completed cleanly and Console remained at `0` entries in non-Play state.
- Documentation was brought in line with the current generated-object workflow, including data contracts, file protocol, manual-worker steps, smoke testing, and true-device validation planning.
- Previously introduced `Locomotor`, `Local Dimming`, `Metal memoryless texture`, and controller-helper warnings were reduced; accepted simulator/runtime noise may still remain.
- After adding procedural surface texture fallback, Unity reimported the new script cleanly with Console at `0` entries before Play. In Play mode, `AnchorThemeApplier` reported `Surface Anchors: 12`, `Renderers: 12`, and `Coverage: floor=1, wall=10, ceiling=1`; a multi-angle Scene View capture showed the runtime wall/floor/ceiling patterned materials on the MRUK room surfaces. Only the accepted Meta/OpenXR simulator warnings appeared during Play.
- After adding `SurfaceTexturePromptBuilder` and `SurfaceOverrideApplier`, Unity reimported the new scripts cleanly with Console at `0` entries before Play. In Play mode, surface prompt artifacts were written for `future_research_lab` wall/floor/ceiling, and `SurfaceOverrideApplier` reported `Override Planes: 12`, `Coverage: floor=1, wall=10, ceiling=1`, `Skipped: 0`, and `Wall Offset: 0.050m`; a multi-angle Scene View capture confirmed the override planes under `SurfaceOverrides`.
- After creating persistent surface material assets, both theme profiles now reference explicit wall/floor/ceiling materials. A follow-up Play smoke test reported `Override Planes: 12`, `Coverage: floor=1, wall=10, ceiling=1`, `Skipped: 0`, and `Wall Offset: 0.050m`; Unity returned to edit mode with `IsCompiling=false` and Console at `0` entries after cleanup.
- After replacing the procedural placeholder PNGs with GPT-image-2 generated wall/floor/ceiling albedo textures, Unity refreshed with no project errors. A Play smoke test again reported `Override Planes: 12`, `Coverage: floor=1, wall=10, ceiling=1`, `Skipped: 0`, and `Wall Offset: 0.050m`; a multi-angle Scene View capture showed the new texture detail on the surface override planes, and Console was cleared back to `0` entries after exiting Play.
- After adding surface visibility modes, Unity refreshed cleanly with Console at `0` entries before Play. The canonical scene is saved in `Background` mode. In Play mode, `SurfaceOverrideApplier` reported `Visibility Mode: Background`, `Override Planes: 12`, `Coverage: floor=1, wall=10, ceiling=1`, and `Background Alpha: wall=0.30, floor=0.24, ceiling=0.20`; Unity exited Play with no Error entries.
- After the manual GPT-image worker step, `Library/GeneratedObjectOutputs/table_18_20260424071758.stylized.png` exists as `1323 x 1189` RGBA PNG data.
- After the manual Seed3D 2.0 step, the generated model was copied into `Assets/Generated/ThemeAssets/table_18_20260424071758/table_18_20260424071758.seed3d.medium.glb`.
- `GeneratedObjectModelImporter` imported that GLB into `Assets/Generated/ThemeAssets/table_18_20260424071758/table_18_20260424071758.generated_table_proxy.prefab` and updated the matching job to `Imported`.
- A later Simulator Play run selected the generated prefab rather than the deterministic fallback. `AnchorThemeApplier` logged `source=generated_import`, `fit=target=2.02x0.782x0.937, source=2.159x0.761x1.33, scale=0.935x1.027x0.704, bottomDelta=0m`, and `failure=none`.
- After the latest table fitting change, no `error CS`, `Compiler errors`, or `Compilation failed` entries were found in the inspected Unity Editor log tail.
- After adding generated physical-size request fields, hosted stylized image URL propagation, `Seed3DBackendAdapter`, floor-to-tabletop table fitting, and the standalone correction controller skeleton, `Assets/Refresh` completed and Unity Console reported `0` Error entries and `0` Warning entries.
- After adding resumable Seed3D `ModelGenerationSubmitted` state, generated model quality review fields, and importer quality gating to `NeedsReview`, static diff checks passed before the final Unity refresh.
- After the editor play-pose bookmark experiment was reverted, the canonical scene no longer contains `PlayModeViewPoseBookmark`; the current scene state intentionally remains on the MRUK shell until table proxy placement is explicitly re-enabled for validation.
- After the generated-table long-axis correction, `AnchorThemeApplier` reported `axis=rotated90(source=Z, target=X)`, `bottomDelta=0m`, and `failure=none` for generated table placement using an imported generated prefab.
- After regenerating the table with a stronger long-rectangle prompt, Seed3D task `cgt-20260425030546-n8b7x` succeeded, the returned zip package was extracted outside `Assets/`, `table_18_20260425025836.seed3d.pbr.glb` imported cleanly, `GeneratedObjectModelImporter` advanced the job to `Imported`, and Unity Console reported `0` Error entries after refresh/import.
- After adding `DevicePassthroughCaptureService`, enabling `horizonos.permission.HEADSET_CAMERA`, and attaching `Meta.XR.PassthroughCameraAccess` plus the service under `AppRoot/Perception`, `Assets/Refresh` completed with Unity Console at `0` entries in non-Play state. This validates compile/scene wiring only; Link/headset camera access is still unverified.
- After adding `DevicePassthroughCaptureHud` and XR controller capture input, `Assets/Refresh` completed with Unity Console at `0` entries. The HUD scene wiring is saved, but headset readability and controller input still need real Link/device validation.
- After locking generated-table lookup to the active capture request, `Assets/Refresh` completed with Unity Console at `0` entries. This prevents old Simulator generated-table jobs from being used on a new true-device room unless they match the active request or the fallback lock is explicitly disabled.

## Current Local Workspace State
The workspace currently contains uncommitted local state that should be treated as in-progress rather than final:
- `Assets/Scenes/MR_RoomStylization.unity`
  Current local scene state includes `ExternalScreenshot` capture input, `DevicePassthroughCaptureService`, `DevicePassthroughCaptureHud`, `HostedImageUploadBridge`, `Seed3DBackendAdapter`, request-locked generated-table selection, and table proxy placement disabled so the default view shows the MRUK shell.
- `Assets/Scripts/Stylization/AnchorThemeApplier.cs`
  Generated table prefab selection, MRUK-volume-corner target bounds, 1:1 proxy padding defaults, bottom-face alignment, and fit diagnostics are present locally.
- `Assets/Scripts/Editor/GeneratedObjectModelImporter.cs`
  Generated GLB-to-prefab import is present locally.
- `Assets/Generated/ThemeAssets/table_18_20260425025836/`
  Contains the current preferred Seed3D GLB and generated table prefab.
- `Assets/Scripts/Perception/GeneratedObjectPromptBuilder.cs`
  Future generated-object prompts now require an isolated transparent object asset; old jobs may still carry older prompt text.
- `Assets/Scripts/Perception/DevicePassthroughCaptureService.cs`
  First native passthrough camera capture probe exists for Quest Link/headset runs and writes the same generated-object request/job shape as the existing simulator screenshot path. It exposes score/status properties for headset HUD display and supports keyboard plus right-controller primary-button capture input.
- `Assets/Scripts/UI/DevicePassthroughCaptureHud.cs`
  Runtime head-locked PCA capture HUD exists locally and displays readiness, score, distance, viewport/crop, and latest capture/job status in the headset.
- `Assets/Scripts/Perception/ApimartImageBackendAdapter.cs`
  APIMart `gpt-image-2` image-generation adapter is present locally and requires `APIMART_API_KEY` in the Unity process environment.
- `Assets/Scripts/Perception/Seed3DBackendAdapter.cs`
  Ark Seed3D 2.0 task creation/poll/download adapter is present locally and requires hosted image URLs plus `ARK_API_KEY` in the Unity process environment.
- `Assets/Scripts/Perception/HostedImageUploadBridge.cs`
  Local-PNG-to-hosted-URL bridge is configured for the `www.mikusc.top` upload endpoint and requires `SCENESHIFT_UPLOAD_TOKEN` in the Unity process environment.
- `Assets/Scripts/Stylization/CorrectionModeController.cs`
  Standalone correction-mode code skeleton exists locally but is not yet integrated into the scene.
- `ProjectSettings/URPProjectSettings.asset`
  Unity touched this file during prior graphics/rendering setting recovery.
- `Assets/_Recovery/0 (11).unity` and `.meta`
  Unity recovery artifacts created after the recent editor crash.

## Biggest Technical Risks
- Unity editor instability when inspecting runtime objects during Play in this project/runtime combination.
- The current table proxy source asset reads more like a tabletop slab than a clear furniture replacement, though new prompts/contracts now carry target physical proportions to reduce this risk in future generations.
- APIMart image generation is wired but not yet validated from Unity because `APIMART_API_KEY` is not currently visible to the Unity process.
- Remaining simulator/runtime warnings can obscure newly introduced regressions if Console hygiene is not maintained carefully.

## Next Smallest Task
For the generated-table branch:
1. set `APIMART_API_KEY` before launching Unity and run one `CaptureReady -> StylizedImageReady` APIMart image job,
2. confirm `HostedImageUploadBridge` writes a stable `www.mikusc.top` `StylizedImageUrl`,
3. confirm `Seed3DBackendAdapter` advances the same request through `ModelGenerationSubmitted -> ModelReady`,
4. import the matching generated table asset and confirm the table status reports `generated=locked(...), match=<requestId>`,
5. inspect the generated table from the intended Simulator/user camera angle with `AnchorThemeApplier.applyTableProxies` explicitly enabled,
6. add a minimal accept/reject or reset-to-deterministic control before treating generated furniture as demo-ready.

For the Quest Link / true-device capture probe:
1. run `MR_RoomStylization` through Quest Link on Quest 3 / Quest 3S with compatible Meta Horizon Link and headset OS versions,
2. grant `horizonos.permission.HEADSET_CAMERA`,
3. confirm the head-locked PCA HUD is readable in the headset and shows `PCA playing=true`, a non-empty best anchor, and a visible best-view score,
4. press keyboard `P` or the right-controller primary button in Play mode and confirm PCA full-frame PNG, crop PNG, metadata JSON, request JSON, prompt text, and `.job.json` are written,
5. confirm `LocalGeneratedObjectBackendAdapter.ExternalFileProtocol` can consume the resulting job into the existing manual/external worker flow,
6. after the matching prefab is imported, enable table proxies/reapply and confirm `AnchorThemeApplier` reports a generated match for that same request before accepting the replacement.

For the core Phase 1 room-stylization branch:
1. keep `SurfaceOverrideApplier` in `Background` mode while furniture replacement is incomplete,
2. add a simple reset/reapply control to the existing debug or correction UI,
3. begin the first `CorrectionModeController` path for inspect / nudge / confirm on one selected stylized object.

## Update Rule
When a task materially changes the state of the prototype, update:
- this file for rolling status,
- `README.md` only when the public-facing project summary changes.
- `docs/05_DATA_CONTRACTS.md` when serialized request/job/result fields change.
- `docs/09_GENERATIVE_OBJECT_PIPELINE.md` when the generated-object workflow state changes.
