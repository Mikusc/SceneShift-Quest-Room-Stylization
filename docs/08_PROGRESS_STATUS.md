# 08 Progress Status

## Purpose

This file is the rolling implementation tracker for the current vertical slice. Update it after meaningful implementation work, especially when runtime behavior, generated-job contracts, scene wiring, or demo validation changes.

## Snapshot

- Last updated: `2026-04-30`
- Current priority: `Phase 1 - room stylization`
- Canonical setting: `one real UNNC IEB office room`
- Canonical scene: `Assets/Scenes/MR_RoomStylization.unity`
- Current development path: `Quest Link / Unity Editor Play` plus `MetaXRSimulator` for safer iteration
- Current demo target: a coherent stylized office room with surfaces, openings, furniture replacement, clean view, runtime status, and style switching

## Milestone Status

| Milestone | Status | Notes |
| --- | --- | --- |
| M0 - Project foundation audit | Mostly done | Canonical scene, folder structure, docs, and Git workflow exist. Recovery/UISet sample clutter still needs cleanup before a final milestone commit. |
| M1 - MRUK semantic debug layer | Mostly done | MRUK room bootstrap, semantic counts, debug shell visibility, active-room refresh, and headset-visible status are in place. |
| M2 - Visible object perception fusion | Partial fallback | Full Image Segmentation fusion is not implemented. Current path uses MRUK furniture labels plus gaze/best-view capture and supports `OTHER` fallback. |
| M3 - Theme system and stylization planning | Mostly done | Generic scaffold plus built-in/custom Style identity is implemented. Style-aware prompt/cache IDs are in use. |
| M4 - Stylization application | In progress | Surfaces, openings, window vista, generated furniture placement, and mood changes are implemented. Surface v3 aesthetic validation is next. |
| M5 - Manual correction mode | Partial | Runtime `Rotate 90` correction exists for generated furniture, but polished accept/reject/reset/nudge persistence is not demo-final. |
| M6 - Demo readiness | In progress | Main runtime panel, clean view, object status cards, and queue summaries exist. UI polish and final validation remain. |
| M7 - NPC preparation | Deferred | Still out of scope until Phase 1 is stable. |
| M8 - Generated object enrichment | In progress | APIMart image2, hosted upload, Seed3D, import, and request-locked placement are wired. Multiple generated furniture replacements have been validated in Editor Play, but accept/reject UX remains open. |

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
- `ApimartSurfaceTextureBackendAdapter` can submit active-theme/active-style surface jobs to APIMart `gpt-image-2`.
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
- `AnchorThemeApplier` can place imported generated furniture by matching active request IDs so old generated models are not silently applied to unrelated room objects.
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
- Generated furniture still needs a clear accept / reject / reset flow.
- True-device PCA capture is still a probe; Quest Link / Editor Play validates much of the pipeline, but not native camera support on every headset/runtime.
- Surface v3 prompts/code are implemented, but the resulting aesthetics need a new Play validation in the actual office room.
- Official UISet sample prefabs are available, but direct dynamic instantiation caused layout problems. Current dashboard prioritizes stable interaction over perfect official visual fidelity.
- Official ray visibility still needs headset validation after removing the custom SceneShift fallback ray. If the official ray is visually hidden by the world-space backplate, treat it as a depth/rendering-order issue rather than reintroducing the custom ray.
- Generated model artifacts under `Assets/Generated/ThemeAssets/` should generally remain local and uncommitted unless a specific demo asset is intentionally preserved.
- Recovery scenes under `Assets/_Recovery/` and imported UISet sample scenes should be reviewed before final commits.
- Some existing compiler warnings are non-blocking but should be cleaned before a polished milestone.

## Latest Verification

Latest verified by Codex on `2026-04-30`:

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
- `PassthroughOnlyVisibilityToggle.cs` now owns the left-controller `Y` pure passthrough toggle; `DevicePassthroughCaptureService` no longer uses controller `Y` for target cycling in the canonical scene.
- Theme profile assets include updated room-scale surface and opening prompt hints.
- `MR_RoomStylization.unity` contains many runtime systems and local scene wiring changes.
- `Assets/Scenes/UISet.unity` and `Assets/Scenes/UISetPatterns.unity` exist locally as sample/reference scenes.
- `Assets/_Recovery/` contains crash-recovery scene artifacts.

## Environment Variables

For automatic generation, Unity must be launched with the relevant variables visible to the Unity process:

- `DEEPSEEK_API_KEY`
- `APIMART_API_KEY`
- `SCENESHIFT_UPLOAD_TOKEN`
- `ARK_API_KEY`

Do not commit API keys.

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

For demo readiness:

1. Add minimal accept/reject/reset for generated furniture.
2. Persist confirmed generated-furniture rotation corrections per room/style/object.
3. Clean or intentionally ignore Recovery/UISet sample artifacts before commit.
4. Run `docs/11_SMOKE_TEST_AND_DEMO_CHECKLIST.md`.

For true-device validation:

1. Validate PCA capture on a supported Quest runtime.
2. Confirm capture writes PNG, metadata, request JSON, prompt text, and job JSON.
3. Confirm the generated result can be imported and placed against the same request.

## Update Rule

When a task materially changes the prototype, update:

- `docs/08_PROGRESS_STATUS.md` for rolling status.
- `README.md` when the public project summary changes.
- `START_HERE_CN.md` when the user-facing workflow changes.
- `docs/05_DATA_CONTRACTS.md` when serialized request/job/result contracts change.
- `docs/09_GENERATIVE_OBJECT_PIPELINE.md` when generated-object workflow changes.
- `docs/11_SMOKE_TEST_AND_DEMO_CHECKLIST.md` when validation steps change.
