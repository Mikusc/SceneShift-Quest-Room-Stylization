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

# Fallback plan if AI perception is unstable

If Image Segmentation or detection blocks progress:
1. keep MRUK as the backbone,
2. stylize only room-scale surfaces and known anchors,
3. manually tag one table and one screen if needed,
4. continue building planner/applier/correction flow.

That still produces a valid Phase 1 prototype.

---

# Recommended immediate next task
Start with:

**M1.1 + M1.2 + M1.3**

Reason:
The project should first prove that room semantics are stable and inspectable. Without that, later stylization work will be blind.
