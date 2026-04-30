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

Current prototype status as of `2026-04-30`:

- MRUK room loading, semantic bootstrap, active-room refresh, theme/style selection, stylization planning, room mood, surface overrides, furniture placement, headset HUD, and runtime control panel are in place.
- The internal style scaffold is now the generic room style scaffold. User-facing styles such as `Future Research Lab`, `Arcane Knowledge Chamber`, and custom text styles are runtime Style identities for prompts, cache keys, UI labels, and generated artifacts.
- `RuntimeStyleIntentController` supports built-in and freeform styles with deterministic keyword extraction and an optional `DeepSeekStyleIntentProvider` path using `DEEPSEEK_API_KEY`.
- The surface pipeline covers wall, floor, ceiling, door, window frame, and window vista. The latest prompt version is `surface_texture_v3_room_scale_openings`, which asks for room-scale materials, full door/portal panels, open-center window treatments, and 16:9 exterior vistas.
- Runtime surface rendering now uses larger world-scale texture repeats, opaque wall/floor/ceiling materials, wall baseboard/crown/corner trims to hide seams, and a full door panel instead of a thin door frame.
- Generated surface textures are consumed from `Library/SurfaceTextureOutputs/` when ready, with theme-material/procedural fallback if a generated texture is missing.
- Furniture capture is no longer table-only. The generated-object path supports MRUK furniture categories including `TABLE`, `STORAGE`, `SCREEN`, `COUCH` mapped internally to `Seating`, `BED`, `LAMP`, `PLANT`, and `OTHER`, with request-locked placement so old captures are not silently reused for a different target.
- `DevicePassthroughCaptureService` is the Quest Link/headset capture probe. It auto-selects the best visible supported MRUK anchor from gaze, shows status in the headset, and uses keyboard/controller input for capture. Native PCA capture still needs true-device validation on a supported Quest runtime.
- The generated-object side path queues jobs under `Library/GeneratedObjectJobs/`, writes Roomify-style prompt artifacts, and can run through `ApimartImageBackendAdapter -> HostedImageUploadBridge -> Seed3DBackendAdapter -> GeneratedObjectModelImporter`.
- `ApimartImageBackendAdapter` uses APIMart `gpt-image-2` and requires `APIMART_API_KEY`. `HostedImageUploadBridge` uses `SCENESHIFT_UPLOAD_TOKEN` and the `www.mikusc.top` upload endpoint. `Seed3DBackendAdapter` uses `ARK_API_KEY`.
- Multiple furniture replacements have been validated in Quest Link / Editor Play, including coexisting generated tables and generated objects with request-specific placement. Generated models themselves remain local artifacts and should not be committed by default.
- The runtime UI is usable through the current stable SceneShift dashboard, with clean-view/object-status controls, `Rotate 90` generated-furniture correction, and a left-hand pure-passthrough safety view. Full Meta UISet prefab adoption remains a future UI polish task because dynamic UISet sample prefabs mis-layout in this runtime panel.
- Missing before demo-final status: polished correction/accept/reject/reset UX, persistent correction records, full true-device PCA capture validation, surface-v3 visual validation in the real office, and final UI polish.

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
- Optional external services: DeepSeek style parsing, APIMart `gpt-image-2`, `www.mikusc.top` hosted image upload, and Ark Seed3D 2.0

## Quick Start

1. Open the project in Unity `6000.4.3f1`.
2. Open `Assets/Scenes/MR_RoomStylization.unity`.
3. Use `MetaXRSimulator` for development-time validation, or Quest Link / Editor Play for headset-in-the-loop checks.
4. Enter Play mode and wait for MRUK room initialization.
5. Use the runtime panel and HUD to inspect room readiness, semantic counts, active Style, surface cache, furniture queue state, and best-view target.
6. Use the Theme dropdown to choose a built-in Style, or set a freeform style intent through `RuntimeStyleIntentController` before Play if testing custom style generation.
7. For surface generation, confirm `APIMART_API_KEY` is visible to Unity. Surface jobs are written under `Library/SurfaceTextureJobs/` and generated PNGs are downloaded under `Library/SurfaceTextureOutputs/`.
8. For furniture generation, look at a supported object until the HUD shows a valid target, then capture with the configured keyboard/controller input.
9. For the automated furniture path, launch Unity with `APIMART_API_KEY`, `SCENESHIFT_UPLOAD_TOKEN`, and `ARK_API_KEY`.
10. Inspect `Library/GeneratedObjectJobs/`, `Library/GeneratedObjectOutputs/`, `Library/GeneratedObjectModels/`, and `Assets/Generated/ThemeAssets/` for job state, prompts, stylized PNGs, hosted URLs, Seed3D results, and imported prefabs.
11. Use `Clean View` for the pure stylized room, `Object Status` for per-object job cards, `Rotate 90` for one-step yaw correction, and the left-hand pure-passthrough toggle when you need to hide all virtual content.
12. Generated models under `Assets/Generated/ThemeAssets/` are local generated artifacts. Do not commit them unless a specific demo artifact is intentionally being preserved.
13. When moving to true-device validation, use `MQDH` for deployment, capture/recording, logs, performance traces, and pulling generated files from the headset.

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
- `PassthroughOnlyVisibilityToggle`
- `GeneratedObjectAssetCleaner`

Planned or incomplete:

- `ObservedObjectCollector`
- `SemanticFusionService`
- polished `CorrectionModeController` integration
- accept / reject / reset controls for generated furniture
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
