# 08 Progress Status

## Purpose
This file is the manual progress tracker for the current vertical slice.
Update it after each meaningful implementation step.

## Snapshot
- Last updated: `2026-04-25`
- Current priority: `Phase 1 — room stylization`
- Canonical scene: `Assets/Scenes/MR_RoomStylization.unity`
- Primary development validation path: `MetaXRSimulator`

## Milestone Status
| Milestone | Status | Notes |
| --- | --- | --- |
| M0 — Project foundation audit | Partial | Project audit and canonical scene exist; agreed folder structure is only partially normalized. |
| M1 — MRUK semantic debug layer | Mostly done | Simulator path works, room bootstrap exists, semantic HUD exists, and a thin best-view capture plus generated-object request export path now exists with a `TABLE` object-level crop rect and external-screenshot simulation input. Further capture refinement remains open. |
| M2 — Visible object perception fusion | Not started | No `ObservedObjectCollector` or `SemanticFusionService` yet. |
| M3 — Theme system and stylization planning | Mostly done | `ThemeProfile`, `StylizationPlan`, `StylizationPlanner`, and debug HUD are in place. |
| M4 — Stylization application | In progress | Surface stylization and room mood are working. Table proxy replacement is wired but still being refined. |
| M5 — Manual correction mode | Started | A standalone `CorrectionModeController` code skeleton now exists, but it is not yet scene-integrated. |
| M6 — Demo readiness | Not started | No dedicated demo UI, smoke-test panel, or capture toggles yet. |
| M7 — NPC preparation | Not started | Intentionally deferred until Phase 1 is stable. |
| M8 — Generated object enrichment | In progress | One `TABLE` side-branch job reached transparent image, Seed3D GLB, imported prefab, and runtime generated-prefab placement in Simulator. |

## Working Features
- `RoomSemanticBootstrap` initializes MRUK room semantics for the canonical scene.
- `StylizationDebugPanel` shows room/theme/planner/applier state in-scene.
- Theme selection is available through `ThemeIntentController`.
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
- `GenerativeObjectCoordinator` is now attached under `Perception` and writes a local `.job.json` shell to `Library/GeneratedObjectJobs/` when a new captured request appears.
- `GeneratedObjectPromptBuilder` now converts each request into a Roomify-inspired prompt artifact and `GenerativeObjectCoordinator` writes it as `Library/GeneratedObjectJobs/*.prompt.txt`.
- `LocalGeneratedObjectBackendAdapter` is now attached under `Perception` and can locally consume `CaptureReady` jobs into simulated `StylizedImageReady` outputs by applying a theme-aware mock stylization transform, writing `Library/GeneratedObjectOutputs/*.stylized.png`, and writing a matching `*.result.json` backend artifact.
- `LocalGeneratedObjectBackendAdapter` now also exposes an `ExternalFileProtocol` mode that writes `Library/GeneratedObjectBackendInbox/*.submission.json` plus a prefilled `*.result.template.json`, and can later consume a dropped external `*.result.json` without changing the Unity-side job/prompt contract.
- `LocalGeneratedObjectBackendAdapter` can now consume either a local stylized PNG path or an external hosted stylized image URL from `GeneratedImageBackendResult`, enabling the Seed3D API path without adding an upload worker.
- `HostedImageUploadBridge` can optionally upload local stylized PNG outputs to an operator-configured hosting endpoint and write `StylizedImageUrl` back to the job for Seed3D `image_url` input.
- `Seed3DBackendAdapter` can process `StylizedImageReady` jobs when a public `http(s)` stylized image URL is available, read `ARK_API_KEY` from the Unity process environment, create Ark Seed3D 2.0 tasks, record `ModelGenerationSubmitted`, resume polling by task id after interruption, download the generated model under `Assets/Generated/ThemeAssets/<requestId>/`, and advance the job to `ModelReady`.
- One manual GPT-image worker output now exists for `TABLE_18`: `Library/GeneratedObjectOutputs/table_18_20260424071758.stylized.png`. The file is RGBA PNG data and is intended to be an isolated transparent table reference, not a room-photo edit.
- One manual Seed3D 2.0 image-to-3D run produced `Assets/Generated/ThemeAssets/table_18_20260424071758/table_18_20260424071758.seed3d.medium.glb`; backend metadata remains under `Library/GeneratedObjectModels/`.
- `GeneratedObjectModelImporter` imports `ModelReady` jobs, normalizes generated model bounds to a centered bottom pivot, removes imported colliders, saves a prefab, and advances the job to `Imported`.
- The current imported generated table prefab is `Assets/Generated/ThemeAssets/table_18_20260424071758/table_18_20260424071758.generated_table_proxy.prefab`.
- `AnchorThemeApplier` can prefer the latest imported generated table prefab in Editor/Simulator and fall back to the theme/default deterministic proxy when no usable generated prefab exists.
- The canonical scene currently keeps table replacement disabled (`applyTableProxies=false`) and original MRUK volume visuals visible (`hideOriginalVolumeVisuals=false`) so the default Play view returns to the MRUK shell. Generated/deterministic table proxy placement is an opt-in validation step, not the default scene state.
- Table generated-prefab fitting now transforms the MRUK `VolumeBounds` corners through the anchor transform into proxy-root local space before sizing, so rotated MRUK bounds do not treat a local axis as world-up by mistake.
- Table proxy footprint and height padding are now saved as `1.0` in the canonical scene for size consistency, and runtime status logs target/source/scale/bottomDelta fit diagnostics. The latest local code adds full-height generated-table fitting from MRUK floor to MRUK table top, horizontal safety footprint scaling, per-axis local footprint correction, and a final vertical offset field for one-room calibration.
- `CorrectionModeController` now exists as a standalone component code skeleton for selecting one applied object, inspecting metadata, nudging position, rotating yaw, resetting, and confirming correction deltas; it is not yet wired into the canonical scene or applier registry.
- Project documentation now includes a manual external-worker runbook, smoke-test/demo checklist, and true-device validation plan so the generated-object side branch can be tested without relying on ad hoc chat instructions.

## In Progress
- Table replacement readability and accept/reject UX after the generated Seed3D prefab is placed.
- Verifying the generated table visually from the user's intended Simulator camera angle, not just from fit metrics.
- Deciding whether exact per-axis generated-prefab fitting is acceptable for the current demo or whether to add a later Roomify-style IoU/orientation refinement step.
- Keeping old generated jobs compatible while new prompt artifacts use `roomify_image_asset_v2`; the already imported `table_18_20260424071758` job was produced by an earlier prompt artifact.
- Keeping documentation synchronized with the current code/scene state after each generated-object workflow change.
- Cleaning up crash-related transient workspace state before the next commit.

## Known Gaps
- No perception fusion layer yet for `Image Segmentation` or object detection.
- No manual correction workflow yet.
- No reset / reapply / clean theme-switch flow documented as finished.
- The committed surface texture assets are currently GPT-image-2 albedo maps only; matching normal / roughness / metallic maps are still a future refinement.
- The current object-level crop path is `TABLE`-only and still MRUK-anchor-driven; it is not yet generalized to fused `RoomObjectRecord` inputs.
- The current `ExternalScreenshot` path assumes the manual screenshot roughly matches the active gameplay view and excludes window chrome.
- The estimated crop rect can still drift from the screenshot composition because the image source and runtime camera are not the same frame.
- Image stylization is still manual/offline unless using the local mock adapter. Seed3D model generation now has an automated Ark adapter and optional upload bridge, but both need operator-provided endpoint/key configuration and have not been exercised against real services in this repo session.
- Generated asset lookup currently scans imported `.job.json` records; there is no dedicated generated asset registry database yet.
- Generated furniture still lacks explicit user approval/reject/correction UI.
- No true-device passthrough/camera-frame capture path is implemented yet; it is only reserved in the contract.
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

## Current Local Workspace State
The workspace currently contains uncommitted local state that should be treated as in-progress rather than final:
- `Assets/Scenes/MR_RoomStylization.unity`
  Current local scene state includes `ExternalScreenshot` capture input, `HostedImageUploadBridge`, `Seed3DBackendAdapter`, and table proxy placement disabled so the default view shows the MRUK shell.
- `Assets/Scripts/Stylization/AnchorThemeApplier.cs`
  Generated table prefab selection, MRUK-volume-corner target bounds, 1:1 proxy padding defaults, bottom-face alignment, and fit diagnostics are present locally.
- `Assets/Scripts/Editor/GeneratedObjectModelImporter.cs`
  Generated GLB-to-prefab import is present locally.
- `Assets/Generated/ThemeAssets/table_18_20260424071758/`
  Contains the current Seed3D GLB and generated table prefab.
- `Assets/Scripts/Perception/GeneratedObjectPromptBuilder.cs`
  Future generated-object prompts now require an isolated transparent object asset; old jobs may still carry older prompt text.
- `Assets/Scripts/Perception/Seed3DBackendAdapter.cs`
  Ark Seed3D 2.0 task creation/poll/download adapter is present locally and requires hosted image URLs plus `ARK_API_KEY` in the Unity process environment.
- `Assets/Scripts/Perception/HostedImageUploadBridge.cs`
  Optional local-PNG-to-hosted-URL bridge is present locally and requires a configured upload endpoint.
- `Assets/Scripts/Stylization/CorrectionModeController.cs`
  Standalone correction-mode code skeleton exists locally but is not yet integrated into the scene.
- `ProjectSettings/URPProjectSettings.asset`
  Unity touched this file during prior graphics/rendering setting recovery.
- `Assets/_Recovery/0 (11).unity` and `.meta`
  Unity recovery artifacts created after the recent editor crash.

## Biggest Technical Risks
- Unity editor instability when inspecting runtime objects during Play in this project/runtime combination.
- The current table proxy source asset reads more like a tabletop slab than a clear furniture replacement, though new prompts/contracts now carry target physical proportions to reduce this risk in future generations.
- The upload bridge is endpoint-agnostic and untested against a real hosting provider; a concrete hosting endpoint must be configured before it can complete the local PNG -> Ark `image_url` handoff.
- Remaining simulator/runtime warnings can obscure newly introduced regressions if Console hygiene is not maintained carefully.

## Next Smallest Task
For the generated-table branch:
1. provide a hosted `StylizedImageUrl` for a `StylizedImageReady` job and run one real `Seed3DBackendAdapter` request with `ARK_API_KEY` available to the Unity process,
2. import the resulting `ModelReady` GLB,
3. explicitly re-enable `AnchorThemeApplier.applyTableProxies` only for generated-table placement validation and inspect from the intended Simulator/user camera angle,
4. decide whether the new floor-to-tabletop fit plus safety footprint scale is acceptable or whether yaw/aspect correction is still needed,
5. add a minimal accept/reject or reset-to-deterministic control before treating generated furniture as demo-ready.

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
