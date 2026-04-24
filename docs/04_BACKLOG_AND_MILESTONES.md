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
- simulator-based validation is acceptable for Phase 1A development, but it does not replace later validation in the canonical real room
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
### M3.1 Create `ThemeProfile` data model
Deliverables:
- `ThemeProfile` ScriptableObject or equivalent
- starter assets for:
  - `FutureResearchLab`
  - `ArcaneKnowledgeChamber`

Acceptance:
- theme asset contains surface/material/proxy/mood fields

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

Acceptance:
- user can quickly verify whether the scene is stable before recording

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

# Optional Milestone 8 — Generated object enrichment

## Goal
Keep the Roomify-like object-generation experiment as a side branch behind the deterministic stylization path.

This milestone must not block the Phase 1 room-stylization demo.
It exists to enrich one collision-sensitive object after the stable proxy/material flow already works.

## Tasks
### M8.1 Capture a `TABLE` reference request
Deliverables:
- `BestViewCaptureService`
- `GeneratedObjectRequest`
- source image path, request JSON, crop metadata, camera pose, object scaffold metadata

Acceptance:
- pressing `C` in Play mode can create a request for one `TABLE`
- deterministic table proxy still works if capture fails

Current status:
- implemented for a `TABLE`-first path using `ExternalScreenshot` as the practical Simulator source mode

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
- one manual GPT-image worker output exists for `table_18_20260424071758`
- future generated-image requests should use the stricter transparent-object prompt version

### M8.3 Import and register a generated table proxy
Deliverables:
- generated-model import path
- generated asset registry/cache
- registration using scaffold size, best-view yaw, bottom-face alignment, and bounded scale

Acceptance:
- generated table candidate can be placed in the same scaffold as the deterministic proxy
- failed registration falls back to deterministic proxy

Current status:
- partially implemented
- one Seed3D 2.0 GLB has been copied into `Assets/Generated/ThemeAssets/`
- `GeneratedObjectModelImporter` can import `ModelReady` jobs into generated table prefabs
- `AnchorThemeApplier` can prefer imported generated table prefabs in Editor/Simulator
- current fitting uses transformed MRUK `VolumeBounds` corners, exact scaffold scale, and bottom-face alignment
- full Roomify-style OBB/IoU registration search is not implemented yet

### M8.4 Add review and correction for generated furniture
Deliverables:
- preview generated object
- accept/reject generated object
- yaw/position nudge
- reset to deterministic proxy

Acceptance:
- collision-sensitive generated furniture is never silently finalized without an easy revert path

Current status:
- not implemented
- this is the next blocker before treating generated furniture as demo-ready rather than an optional artifact preview

---

# Fallback plan if AI perception is unstable

If Image Segmentation or detection blocks progress:
1. keep MRUK as the backbone,
2. stylize only room-scale surfaces and known anchors,
3. manually tag one table and one screen if needed,
4. continue building planner/applier/correction flow.

That still produces a valid Phase 1 prototype.

---

# Recommended immediate next task
Do not restart from M1 unless the project has been reset.

For the current repository state, use `docs/08_PROGRESS_STATUS.md` as the rolling source of truth.

The smallest safe next task depends on the current demo goal:
- for the generated-object branch, visually review the current imported Seed3D table from the intended Simulator/user camera and add a reset-to-deterministic control
- for the core Phase 1 slice, continue table proxy alignment/readability and then move into M5 correction mode
- before recording or committing a demo build, run the checklist in `docs/11_SMOKE_TEST_AND_DEMO_CHECKLIST.md`

If starting from a fresh clone or a broken scene, then fall back to:
- M1.1
- M1.2
- M1.3
