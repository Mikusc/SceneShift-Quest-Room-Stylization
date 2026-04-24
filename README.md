# SceneShift Discussion Room

Scene-aware mixed-reality room stylization prototype for **Meta Quest**.

This repository focuses on **one canonical library discussion room** and builds a Roomify-inspired, Meta-first vertical slice for spatially grounded room stylization.

中文入口见 [START_HERE_CN.md](START_HERE_CN.md)。

## Scope

Current priority is **Phase 1: room stylization**.

The core loop is:

1. load room structure with `MRUK`
2. inspect room semantics
3. choose a theme preset
4. generate a `StylizationPlan`
5. apply stylized surfaces / proxies while preserving room readability
6. inspect and later correct wrong mappings

Phase 2 NPC work is intentionally out of scope until the stylization slice is stable.

## Current Project Status

The canonical scene is:

- `Assets/Scenes/MR_RoomStylization.unity`

Current prototype status:

- `MetaXRSimulator` path is working for Phase 1A development
- MRUK room loading and semantic bootstrap are in place
- theme selection and debug HUD are in place
- wall / floor / ceiling surface stylization is visible
- stylization planning is implemented for wall, floor, ceiling, table, screen, storage, and seating
- a thin `BestViewCaptureService` is in place for `TABLE`-targeted reference capture requests and now supports:
  - `ExternalScreenshot` as the preferred simulator-stage input path,
  - `UnityFramebufferDebug` as a debug-only fallback,
  - `DevicePassthroughReserved` as the future true-device capture path
- the current export path writes a full-frame reference image, metadata, and generated-object request JSON to `Library/BestViewCaptures/`; in `ExternalScreenshot` mode the original screenshot is currently used directly as the backend input while the estimated crop rect is retained as metadata
- a thin `GenerativeObjectCoordinator` shell is now attached to the scene and writes local queued-job records to `Library/GeneratedObjectJobs/` when a new capture request appears
- a Roomify-inspired image prompt is now generated from each `GeneratedObjectRequest` and written as a local `.prompt.txt` artifact alongside queued jobs
- a thin `LocalGeneratedObjectBackendAdapter` is now attached to the scene and can either run local mock stylization or write `ExternalFileProtocol` handoff artifacts for a manual/external image worker
- one manual generated-object path has been completed for `table_18_20260424071758`: isolated RGBA stylized table image, manual Seed3D 2.0 GLB, Unity-imported generated table prefab, and runtime generated-prefab selection in Editor/Simulator
- table replacement now has both deterministic fallback and imported generated-prefab path; the next gap is visual review plus accept/reject/reset controls before treating generated furniture as demo-ready

This means the project is already beyond a blank setup, but it is still in active vertical-slice prototyping rather than demo-final polish.

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

## Quick Start

1. Open the project in Unity `6000.4.3f1`.
2. Open `Assets/Scenes/MR_RoomStylization.unity`.
3. Use `MetaXRSimulator` for development-time validation.
4. Enter Play mode and wait for MRUK room initialization.
5. Use the debug HUD to inspect:
   - room-ready state
   - semantic counts
   - active theme
   - stylization plan state
   - applier state
   - best-view candidate / last capture state
6. For Roomify-style capture simulation, keep `BestViewCaptureService.captureSourceMode = ExternalScreenshot`, point `externalScreenshotPath` to a manual screenshot, then enter Play and press `C` after the `TABLE` candidate stabilizes.
7. If `LocalGeneratedObjectBackendAdapter.processingMode = LocalMockStylization`, inspect `Library/GeneratedObjectJobs/` and `Library/GeneratedObjectOutputs/` for `.job.json`, `.prompt.txt`, `.stylized.png`, and `.result.json` artifacts.
8. If `LocalGeneratedObjectBackendAdapter.processingMode = ExternalFileProtocol`, inspect `Library/GeneratedObjectJobs/` and `Library/GeneratedObjectBackendInbox/` for `.job.json`, `.prompt.txt`, `.submission.json`, and prefilled `.result.template.json` artifacts, then follow `docs/10_MANUAL_EXTERNAL_WORKER_RUNBOOK.md`.
9. For a completed manual Seed3D result, use `SceneShift/Generated Objects/Import Ready Model Jobs` to import `ModelReady` jobs into generated prefabs before checking runtime placement.
10. When moving from simulator checks to headset validation, use `MQDH` to:
   - install the current APK build on-device
   - cast, screenshot, or record the MR result
   - inspect device logs / metrics / traces
   - pull generated capture files from the headset if needed

Development-stage validation may use `MetaXRSimulator`, but final validation is still defined against the known real discussion room.

## Best-View Capture Modes

`BestViewCaptureService` currently supports three capture-source modes:

- `ExternalScreenshot`
  Preferred during simulator-stage development. Use a manual screenshot as the image source; the runtime-computed `TABLE` crop rect is retained as metadata because the manual screenshot and Unity camera are not guaranteed to be the same frame.
- `UnityFramebufferDebug`
  Debug-only fallback. It captures Unity's own framebuffer, which is useful for plumbing checks but not for true Roomify-style reference images.
- `DevicePassthroughReserved`
  Placeholder for the future true-device path. This mode is intentionally reserved so the simulator-stage workflow does not block later integration with real camera/passthrough capture.

## MQDH Role

`MQDH` is not a replacement for `Unity`, `MRUK`, `MetaXRSimulator`, or `Unity MCP`.
In this repository it should be treated as the supporting tool for **true-device iteration**:

- deploy builds to a Quest headset quickly
- mirror, screenshot, and record MR output for debugging or demo capture
- inspect performance through logs, metrics, and traces
- export files produced on-device during capture or testing

For simulator-first work, `MetaXRSimulator` remains the primary validation path.
For passthrough-camera, real-room capture, and performance verification, `MQDH` becomes part of the normal workflow.

## Repository Guide

- `AGENTS.md`
  Codex working rules, priorities, and implementation constraints for this repository.
- `START_HERE_CN.md`
  Chinese onboarding guide for using the documentation and Codex workflow.
- `docs/01_PRODUCT_SCOPE_AND_SUCCESS.md`
  Product scope, research framing, success criteria.
- `docs/02_ROOMIFY_TO_META_MAPPING.md`
  Roomify-to-Meta technical translation.
- `docs/03_ARCHITECTURE_AND_SCENE_LAYOUT.md`
  Scene structure and module layout guidance.
- `docs/04_BACKLOG_AND_MILESTONES.md`
  Main task queue and milestone breakdown.
- `docs/05_DATA_CONTRACTS.md`
  Data model definitions such as `ThemeProfile` and `StylizationPlan`.
- `docs/06_CUSTOM_MCP_TOOLS.md`
  Proposed higher-level Unity MCP tooling.
- `docs/07_CODEX_WORKFLOW_PROMPTS_CN.md`
  Reusable Chinese prompts for working with Codex.
- `docs/08_PROGRESS_STATUS.md`
  Manual rolling tracker for completed work, current risks, and the next smallest task.
- `docs/09_GENERATIVE_OBJECT_PIPELINE.md`
  Optional advanced plan for Roomify-like best-view image to stylized image to 3D object generation.
- `docs/10_MANUAL_EXTERNAL_WORKER_RUNBOOK.md`
  Step-by-step workflow for using `ExternalFileProtocol` with a manual GPT/image generation worker.
- `docs/11_SMOKE_TEST_AND_DEMO_CHECKLIST.md`
  Compact validation checklist for simulator runs, generated-object artifacts, and demo recording prep.
- `docs/12_TRUE_DEVICE_VALIDATION_PLAN.md`
  Plan for moving from `MetaXRSimulator` to Quest headset validation with MQDH and future camera/passthrough capture.

## Key Runtime Modules

Current runtime architecture centers around these components:

- `RoomSemanticBootstrap`
- `ThemeIntentController`
- `StylizationPlanner`
- `AnchorThemeApplier`
- `RoomMoodController`
- `BestViewCaptureService`
- `GenerativeObjectCoordinator`
- `LocalGeneratedObjectBackendAdapter`
- `StylizationDebugPanel`

Planned next-layer components include:

- `ObservedObjectCollector`
- `SemanticFusionService`
- `CorrectionModeController`
- `GeneratedAssetRegistry`
- `GeneratedProxyImporter`

## Design Constraints

Every implementation should preserve these four principles:

- style consistency
- spatial alignment
- functional consistency
- user editability

Large collision-relevant objects should keep approximate footprint and clearance. Wall, floor, and ceiling changes should prefer materials, overlays, lighting, and effects over heavy geometry changes.

## Recommended Working Style

Do not treat this repository as a “build everything” task.

The intended iteration pattern is:

1. inspect current Unity/project state
2. make one small vertical-slice change
3. clear and re-check the Unity Console
4. manually verify in `MR_RoomStylization.unity`
5. move to the next smallest task

## License / Notes

This repository is currently a prototype workspace for coursework/research-style development. Package licenses and third-party assets remain governed by their original publishers.
