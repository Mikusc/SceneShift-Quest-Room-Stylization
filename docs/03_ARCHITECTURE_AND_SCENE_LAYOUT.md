# 03 Architecture and Scene Layout

## 1. Architectural goal
Build a room stylization pipeline that is:
- spatially grounded,
- deterministic enough to debug,
- modular enough to extend into NPC interaction later.

## 2. High-level runtime flow
```text
MRUK room data
    +
Image Segmentation / Object Detection proposals
    -> SemanticFusionService
    -> ThemeIntentController
    -> StylizationPlanner
    -> AnchorThemeApplier
    -> RoomMoodController
    -> CorrectionModeController
    -> Stylized MR room
```

## 3. Core modules

### 3.1 `RoomSemanticBootstrap`
Responsibility:
- initialize MRUK
- load the active room / scene data
- expose major room semantics
- publish events when room data is ready

Inputs:
- MRUK room and anchors

Outputs:
- normalized room semantic records
- room-ready event

### 3.2 `ObservedObjectCollector`
Responsibility:
- read visible object proposals from Image Segmentation or Object Detection
- convert them into normalized `RoomObjectRecord`s
- provide confidence values and source attribution

Inputs:
- segmentation or detection output
- depth/world transform support if available

Outputs:
- observed object list

### 3.3 `SemanticFusionService`
Responsibility:
- merge MRUK anchor semantics and observed visible objects
- deduplicate overlapping objects
- attach function tags
- mark confidence / collision sensitivity

Outputs:
- fused `RoomSemanticSnapshot`

### 3.4 `ThemeIntentController`
Responsibility:
- hold active theme selection
- expose current `ThemeProfile`
- switch themes safely

Outputs:
- active theme changed event

### 3.5 `StylizationPlanner`
Responsibility:
- transform `RoomSemanticSnapshot + ThemeProfile` into a `StylizationPlan`
- decide which semantics are transformed, skipped, or only decorated
- preserve functional consistency rules

Outputs:
- `StylizationPlan`

### 3.6 `AnchorThemeApplier`
Responsibility:
- instantiate themed proxies
- fit prefabs to bounds
- apply material overrides to surfaces
- attach replacement metadata for correction mode

Outputs:
- live stylized scene objects

### 3.7 `RoomMoodController`
Responsibility:
- lighting changes
- ambient audio
- screen/whiteboard treatment
- optional room-wide FX

Outputs:
- environmental mood state

### 3.8 `CorrectionModeController`
Responsibility:
- let the user inspect a mapped object
- show original semantic and replacement info
- support reset / nudge / confirm

Outputs:
- corrected mapping state

### 3.9 `StylizationDebugPanel`
Responsibility:
- show active room ID / anchor count / observed object count
- show current theme
- show stylization plan entries
- show warnings / unmapped semantics / fallback status

## 4. Recommended C# data flow objects
Use these serializable types or ScriptableObjects:
- `RoomObjectRecord`
- `RoomSemanticSnapshot`
- `ThemeProfile`
- `StylizationPlan`
- `StylizationPlanEntry`
- `AppliedStylizationRecord`

See `docs/05_DATA_CONTRACTS.md` for details.

## 5. Recommended scene hierarchy
For `Assets/Scenes/MR_RoomStylization.unity`:

```text
MR_RoomStylization
├─ AppRoot
│  ├─ Bootstrap
│  │  ├─ RoomSemanticBootstrap
│  │  ├─ SemanticFusionService
│  │  └─ ThemeIntentController
│  ├─ Perception
│  │  ├─ ObservedObjectCollector
│  │  └─ SegmentationDebugBridge
│  ├─ Stylization
│  │  ├─ StylizationPlanner
│  │  ├─ AnchorThemeApplier
│  │  └─ RoomMoodController
│  ├─ Interaction
│  │  ├─ CorrectionModeController
│  │  └─ StylizationDebugPanel
│  └─ RuntimeState
├─ XR
│  ├─ CameraRig / InteractionRig
│  └─ EventSystem
├─ MRUK
│  ├─ Room/Scene objects created by MRUK
│  └─ Debug helpers
├─ StylizedContentRoot
│  ├─ SurfaceOverrides
│  ├─ ProxyObjects
│  └─ FX
└─ UI
   ├─ ThemeSelectionCanvas
   ├─ DebugCanvas
   └─ CorrectionCanvas
```

## 6. Folder layout recommendation
```text
Assets/
├─ Scenes/
│  └─ MR_RoomStylization.unity
├─ Scripts/
│  ├─ Core/
│  ├─ MRUK/
│  ├─ Perception/
│  ├─ Stylization/
│  ├─ UI/
│  ├─ Debug/
│  └─ Editor/
├─ Data/
│  ├─ ThemeProfiles/
│  └─ Debug/
├─ Prefabs/
│  ├─ UI/
│  ├─ Stylization/
│  └─ Debug/
├─ Materials/
├─ Audio/
└─ VFX/
```

## 7. Source-of-truth hierarchy
When multiple systems disagree, use this priority order:
1. manual user correction
2. MRUK room/anchor semantics for room-scale structure
3. segmentation/detection proposals for visible object hints
4. fallback hardcoded defaults

This avoids making camera-visible noise override stable room geometry.

## 8. Replacement strategy by semantic type
### 8.1 Surfaces: wall / floor / ceiling
Use:
- material overrides
- decals
- lighting
- ambient effects

Avoid:
- large geometry changes
- blocking navigation

### 8.2 Table / desk
Use:
- themed proxy shell or tabletop overlay
- holographic or magical tabletop props

Preserve:
- footprint
- orientation when obvious
- usable height impression

### 8.3 Screen / board / display
Use:
- emissive plane treatment
- holographic panel
- animated themed board content

### 8.4 Storage / shelf / cabinet
Use:
- themed skins or bounded proxy replacements

Preserve:
- gross location
- obstacle presence

### 8.5 Seating
Use:
- shell-style proxy or bounded replacement

Preserve:
- sit-able interpretation
- approximate size / position

## 9. Debugging architecture
Always include debug views during development.
Recommended overlays:
- MRUK anchor labels and bounds
- observed visible object boxes/masks
- fused semantic records
- current stylization plan entries
- collision-sensitive items highlighted
- fallback usage warnings

## 10. Correction mode design
Correction mode should be simple and bounded.

Recommended controls:
- select mapped object
- toggle original semantic overlay
- nudge position
- rotate around yaw only by default
- limited scale adjustments
- reset object to planner output

Avoid full arbitrary editing at first.

## 11. Phase-2 NPC extension points
Design Phase 1 so these future hooks are easy to add:
- `ThemeIntentController` also provides theme context to NPC
- `RoomMoodController` exposes room reaction methods
- `StylizationDebugPanel` can later become the whiteboard/keyword board
- `AppliedStylizationRecord` can later be referenced by the NPC for spatially grounded speech

## 12. Performance guidance
Keep the first version cheap:
- avoid expensive per-frame semantic recomputation
- cache room snapshot when possible
- apply stylization only on demand
- use simple prefab proxies before complex mesh generation
- keep debug overlays easy to disable

## 13. Serialization guidance
Persist or export when useful:
- room semantic snapshot
- selected theme ID
- stylization plan
- correction deltas

This helps debugging and demo reproducibility.

## 14. Recommended “first architecture complete” checkpoint
Architecture is considered established when all of these exist:
- one canonical scene
- one runtime bootstrap path
- one room semantic snapshot path
- one theme profile asset
- one stylization plan object
- one applier path
- one correction mode controller
- one debug canvas
