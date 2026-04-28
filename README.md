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

- `MetaXRSimulator` remains the stable development path for MRUK, planning, surface styling, and deterministic proxy placement.
- MRUK room loading, semantic bootstrap, theme selection, stylization planning, surface overrides, mood changes, and debug HUD are in place.
- `RuntimeStyleIntentController` can add a freeform Roomify-like style layer to generated-object prompts. It has a deterministic fallback and an optional `DeepSeekStyleIntentProvider` path using `DEEPSEEK_API_KEY`.
- `BestViewCaptureService` supports simulator-stage `TABLE` reference capture through `ExternalScreenshot`, with `UnityFramebufferDebug` available only for plumbing/debug checks.
- `DevicePassthroughCaptureService` is the current headset/PCA capture probe. It auto-selects the best visible supported MRUK anchor under the user's gaze, currently `TABLE`, `STORAGE`, or `OTHER`, shows capture status through the headset HUD, and uses keyboard `P` or right-controller primary button for capture.
- The generated-object side path now queues `CaptureReady` jobs under `Library/GeneratedObjectJobs/`, writes Roomify-style prompt artifacts, and can run through `ApimartImageBackendAdapter -> HostedImageUploadBridge -> Seed3DBackendAdapter`.
- `ApimartImageBackendAdapter` is wired for APIMart `gpt-image-2` image stylization and requires `APIMART_API_KEY` in the Unity process environment.
- `HostedImageUploadBridge` uploads local PNG outputs to the configured `www.mikusc.top` endpoint using `SCENESHIFT_UPLOAD_TOKEN` and writes a stable hosted URL back to the job.
- `Seed3DBackendAdapter` can submit hosted stylized image URLs to Ark Seed3D 2.0 using `ARK_API_KEY`, poll tasks, download generated models, and advance jobs toward import.
- Imported generated table prefabs are request-locked by default so an old Simulator table is not silently applied to a different true-device room.
- A manual generated-table path has already succeeded for `TABLE`, but the automated APIMart-to-Seed3D path still needs a live end-to-end validation run before it should be treated as demo-ready.
- Quest Link / Editor Play can validate MRUK, HUD, best-view scoring, job status, and replacement placement. It should not be treated as proof of native passthrough-camera capture support, especially on Quest Pro where the current PCA path is not expected to validate.
- Generated furniture still lacks final accept / reject / reset UX.

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
- Optional external services for the generated-object side path: DeepSeek style parsing, APIMart `gpt-image-2`, `www.mikusc.top` hosted image upload, and Ark Seed3D 2.0

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
6. For simulator-stage Roomify-style capture, keep `BestViewCaptureService.captureSourceMode = ExternalScreenshot`, point `externalScreenshotPath` to a manual screenshot, then enter Play and press `C` after the `TABLE` candidate stabilizes.
7. For headset/PCA capture probing, use `DevicePassthroughCaptureService`: enter Play through a supported headset/runtime, look at the target table/storage/other object until the head-locked HUD shows `Auto -> ...` with a visible best candidate, then press keyboard `P` or the right-controller primary button.
8. For the automated generated-object path, launch Unity with `DEEPSEEK_API_KEY` if using external style parsing, `APIMART_API_KEY` for image stylization, `SCENESHIFT_UPLOAD_TOKEN` for hosted upload, and `ARK_API_KEY` for Seed3D.
9. Inspect `Library/GeneratedObjectJobs/`, `Library/GeneratedObjectOutputs/`, and `Assets/Generated/ThemeAssets/` for `.job.json`, `.prompt.txt`, stylized PNG, hosted URL, Seed3D task/model, and imported prefab artifacts.
10. If the automated image path is unavailable, use the manual/external fallback documented in `docs/10_MANUAL_EXTERNAL_WORKER_RUNBOOK.md`.
11. For a completed Seed3D result, use `SceneShift/Generated Objects/Import Ready Model Jobs` to import `ModelReady` jobs into generated prefabs before checking runtime placement.
12. When moving from simulator checks to headset validation, use `MQDH` to:
   - install the current APK build on-device
   - cast, screenshot, or record the MR result
   - inspect device logs / metrics / traces
   - pull generated capture files from the headset if needed

Development-stage validation may use `MetaXRSimulator`, but final validation is still defined against the known real discussion room.

## Capture And Generated-Object Paths

Current capture and generation paths:

- `ExternalScreenshot`
  Preferred during simulator-stage development through `BestViewCaptureService`. Use a manual screenshot as the image source; the runtime-computed `TABLE` crop rect is retained as metadata because the manual screenshot and Unity camera are not guaranteed to be the same frame.
- `UnityFramebufferDebug`
  Debug-only fallback. It captures Unity's own framebuffer, which is useful for plumbing checks but not for true Roomify-style reference images.
- `DevicePassthroughCaptureService`
  Headset/PCA probe for app-side passthrough camera capture. It is the intended native capture path, but it must be validated on a supported headset/runtime before being considered reliable.
- `ApimartImageBackendAdapter`
  Automated image-generation worker for `CaptureReady -> StylizedImageReady` using APIMart `gpt-image-2`.
- `HostedImageUploadBridge`
  Converts local stylized PNG output into a public hosted image URL for Seed3D.
- `Seed3DBackendAdapter`
  Converts hosted stylized image URLs into Seed3D model-generation jobs and downloads generated models.

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
  Optional advanced plan for Roomify-like best-view image to stylized image to 3D object generation, including the current APIMart/upload/Seed3D path.
- `docs/10_MANUAL_EXTERNAL_WORKER_RUNBOOK.md`
  Step-by-step workflow for both the manual `ExternalFileProtocol` fallback and the automated APIMart image-worker path.
- `docs/11_SMOKE_TEST_AND_DEMO_CHECKLIST.md`
  Compact validation checklist for simulator runs, generated-object artifacts, and demo recording prep.
- `docs/12_TRUE_DEVICE_VALIDATION_PLAN.md`
  Plan for moving from `MetaXRSimulator` to Quest headset validation, including MQDH usage and current PCA capture limitations.

## Key Runtime Modules

Current runtime architecture centers around these components:

- `RoomSemanticBootstrap`
- `ThemeIntentController`
- `StylizationPlanner`
- `AnchorThemeApplier`
- `RoomMoodController`
- `BestViewCaptureService`
- `DevicePassthroughCaptureService`
- `RuntimeStyleIntentController`
- `DeepSeekStyleIntentProvider`
- `GenerativeObjectCoordinator`
- `LocalGeneratedObjectBackendAdapter`
- `ApimartImageBackendAdapter`
- `HostedImageUploadBridge`
- `Seed3DBackendAdapter`
- `StylizationDebugPanel`

Planned next-layer components include:

- `ObservedObjectCollector`
- `SemanticFusionService`
- `CorrectionModeController`
- `GeneratedAssetRegistry`
- accept / reject / reset controls for generated furniture

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
