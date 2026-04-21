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
- a thin `BestViewCaptureService` is in place for `TABLE`-targeted reference capture requests and writes screenshot + metadata files to `Library/BestViewCaptures/`
- first table proxy path has been wired into the planner/applier, but proxy alignment and replacement visibility are still being refined

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

Development-stage validation may use `MetaXRSimulator`, but final validation is still defined against the known real discussion room.

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

## Key Runtime Modules

Current runtime architecture centers around these components:

- `RoomSemanticBootstrap`
- `ThemeIntentController`
- `StylizationPlanner`
- `AnchorThemeApplier`
- `RoomMoodController`
- `BestViewCaptureService`
- `StylizationDebugPanel`

Planned next-layer components include:

- `ObservedObjectCollector`
- `SemanticFusionService`
- `CorrectionModeController`

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
