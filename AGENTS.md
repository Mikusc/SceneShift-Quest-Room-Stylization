# AGENTS.md

## Read this first
Before doing any work in this repository, read these files in order:
1. `docs/01_PRODUCT_SCOPE_AND_SUCCESS.md`
2. `docs/02_ROOMIFY_TO_META_MAPPING.md`
3. `docs/04_BACKLOG_AND_MILESTONES.md`
4. `docs/05_DATA_CONTRACTS.md` when editing data models
5. `docs/06_CUSTOM_MCP_TOOLS.md` when implementing custom Unity MCP tools

## Project identity
This project builds a **scene-aware mixed-reality discussion room** for Meta Quest.

The canonical setting is **one real library discussion room**. The architecture may generalize later, but every implementation decision must remain testable in one known room.

The product vision has two phases:
- **Phase 1 (current priority):** room stylization only
- **Phase 2 (after Phase 1 is stable):** add a themed NPC learning partner

## Current priority
Deliver a vertical slice that can:
1. read room structure from MRUK,
2. optionally supplement visible-object understanding with Meta AI Building Blocks `Image Segmentation`,
3. infer a simple stylization plan from a preset user intent/theme,
4. apply style-aware replacements or material/effect changes while preserving room readability,
5. allow light manual correction in MR.

Do **not** jump to NPC work until the stylization slice is stable.

## Non-negotiable design principles
All implementation choices must preserve these four principles inspired by Roomify:
1. **Style consistency** — the room should feel like one coherent theme.
2. **Spatial alignment** — virtual content must stay anchored to the real room.
3. **Functional consistency** — if a real object is a table/seat/storage surface, its stylized replacement should preserve that high-level role.
4. **User editability** — the user must be able to inspect and correct wrong placements.

## Required technical bias
Prefer official tools first.

### Approved core stack
- Unity 6 project
- Unity MCP bridge already connected through relay
- Meta XR Core SDK / Meta XR packages already present or to be added when required
- MRUK for room/anchor semantics and scene manipulation
- Meta Building Blocks / AI Building Blocks for official Quest-side perception features
- `Image Segmentation` as the preferred official visible-object proposal layer when available in the installed SDK
- Passthrough / depth / camera access only when necessary for the current slice

### Preferred strategy for Phase 1
Use **Quest-friendly, editable, deterministic stylization**:
- room structure: MRUK
- visible small-object hints: Image Segmentation (or Object Detection fallback)
- style application: preset materials, prefab proxies, lighting, audio, VFX, whiteboard/screen content
- alignment: scene anchors, room labels, bounds fitting, correction handles

### Explicitly avoid in Phase 1
- full SLAM3R / SpatialLM / U-ARE-ME replication
- runtime cloud 3D generation
- dynamic skybox pipelines
- large multi-room generalization work
- multiplayer
- full conversational NPC pipeline

## What “Roomify-like” means in this repository
It does **not** mean copying Roomify’s full research pipeline.
It means implementing a **Meta-first approximation**:
- MRUK gives stable room-scale semantics and anchors.
- Image Segmentation gives object-level proposals or masks for visible items.
- A stylization planner converts semantics + theme intent into a mapping table.
- A scene applier performs proxy replacement/material swaps/effect changes.
- A correction layer lets the user fix errors in MR.

## Canonical deliverable for the current slice
One scene named:
- `Assets/Scenes/MR_RoomStylization.unity`

The scene should demonstrate this flow:
1. detect/load room
2. visualize semantics
3. choose one theme preset
4. generate stylization plan
5. apply stylization
6. inspect/correct
7. reset or switch theme

## Expected folder structure
Use or create these folders when needed:
- `Assets/Scenes/`
- `Assets/Scripts/Core/`
- `Assets/Scripts/MRUK/`
- `Assets/Scripts/Perception/`
- `Assets/Scripts/Stylization/`
- `Assets/Scripts/UI/`
- `Assets/Scripts/Debug/`
- `Assets/Scripts/Editor/`
- `Assets/Data/ThemeProfiles/`
- `Assets/Data/Debug/`
- `Assets/Prefabs/`
- `Assets/Materials/`
- `Assets/Audio/`

## Required high-level runtime modules
When creating systems, prefer these responsibilities:
- `RoomSemanticBootstrap` — initialize room data / semantics
- `ObservedObjectCollector` — collect segmentation or detection results
- `SemanticFusionService` — merge MRUK and perception records
- `ThemeIntentController` — manage theme selection
- `StylizationPlanner` — create stylization mapping entries
- `AnchorThemeApplier` — apply material swaps / spawn proxies
- `RoomMoodController` — lighting/audio/VFX
- `CorrectionModeController` — manual inspect / nudge / confirm / reset
- `StylizationDebugPanel` — show room objects, confidence, mappings, errors

## Rules for code changes
1. **Inspect first.** Before editing, inspect the current package state, project structure, scene list, and console.
2. **Smallest useful step.** Prefer the smallest vertical slice over large speculative refactors.
3. **Compile cleanliness.** After each change set, read Unity console output and fix introduced compile/runtime errors before adding features.
4. **No silent package churn.** Do not add/change/remove packages, Player settings, or XR settings unless the task requires it. Explain those changes clearly.
5. **No hardcoded style logic in scene-only objects.** Put style data in ScriptableObjects or serializable data classes.
6. **Inspector-friendly code.** Expose serialized fields, keep components composable, avoid giant god classes.
7. **Deterministic fallbacks.** If Image Segmentation is unstable/unavailable, use a graceful fallback path and document it.
8. **Preserve user work.** Do not delete or overwrite scenes/prefabs/materials without an explicit reason.
9. **Explain diffs.** Summarize created files, modified files, and remaining risks after each task.

## Rules for domain behavior
- Large collision-relevant objects must keep approximate footprint and walkable clearance.
- Wall/floor/ceiling stylization should prefer materials, decals, lighting, and VFX instead of geometry changes.
- Tables, screens, storage units, seats, and wall displays are the first semantics to support.
- The user must always be able to understand where real furniture still is.
- “Cool visual idea” is not enough; every transformation needs a spatial or functional justification.

## Phase ordering
### Phase 1A: foundation
- package verification
- canonical scene setup
- MRUK room semantic overlay

### Phase 1B: perception fusion
- Image Segmentation or Object Detection integration
- world-space object records
- semantic fusion debug UI

### Phase 1C: stylization
- theme profile assets
- mapping rules
- anchor-aligned proxy/material application
- mood changes

### Phase 1D: editability
- correction mode
- reset/regenerate for a single object category or whole room
- smoke-test flow for demo video

### Phase 2: NPC
Only begin after 1A–1D are stable.

## Definition of done for any task
A task is not done unless all are true:
- project compiles after the change
- no newly introduced console errors remain
- affected inspector fields are sensible and documented inline
- there is a manual verification step
- the diff summary names created/updated files and the next smallest follow-up

## How to respond to the user
- Give concise summaries.
- If the user writes in Chinese, reply in Chinese.
- Keep code, filenames, class names, and comments in English unless asked otherwise.

## If you are unsure
When uncertain, choose the option that is:
1. more testable in one real discussion room,
2. more aligned with official Meta tooling,
3. easier to explain in coursework research/prototype/testing sections,
4. less likely to break spatial alignment.
