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

Optional generated-object side branch:

```text
TABLE MRUK anchor
    -> BestViewCaptureService
    -> GeneratedObjectRequest
    -> GenerativeObjectCoordinator
    -> LocalGeneratedObjectBackendAdapter
    -> stylized image / external worker result
    -> HostedImageUploadBridge / public image URL
    -> Seed3DBackendAdapter or manual Seed3D worker
    -> GeneratedObjectModelImporter
    -> AnchorThemeApplier generated proxy registration
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

### 3.7 `SurfaceTexturePromptBuilder`
Responsibility:
- create Roomify-style wall / floor / ceiling prompt artifacts from the active `ThemeProfile`
- write offline prompt and JSON handoff files under `Library/SurfaceTextureJobs/`
- keep deterministic fallback textures available when generated PBR textures are absent

Outputs:
- `SurfaceTexturePromptSet`
- wall / floor / ceiling `.prompt.txt` files
- optional persistent material assets under `Assets/Materials/SurfaceOverrides/`

### 3.8 `SurfaceOverrideApplier`
Responsibility:
- spawn MRUK-scaffold-aligned surface override planes under `StylizedContentRoot/SurfaceOverrides`
- apply ThemeProfile wall/floor/ceiling materials without changing room geometry
- offset wall planes outward by 5cm to avoid occlusion conflicts, matching the Roomify scene-composition rule
- expose `Off`, `Background`, and `DemoStrong` visibility modes so wall/floor/ceiling can remain low-opacity while furniture replacement is still incomplete

Outputs:
- live surface override planes

### 3.9 `RoomMoodController`
Responsibility:
- lighting changes
- ambient audio
- screen/whiteboard treatment
- optional room-wide FX

Outputs:
- environmental mood state

### 3.10 `CorrectionModeController`
Responsibility:
- let the user inspect a mapped object
- show original semantic and replacement info
- support reset / nudge / confirm

Outputs:
- corrected mapping state

### 3.11 `StylizationDebugPanel`
Responsibility:
- show active room ID / anchor count / observed object count
- show current theme
- show stylization plan entries
- show warnings / unmapped semantics / fallback status

### 3.12 `BestViewCaptureService`
Responsibility:
- select a best visible target anchor, currently `TABLE`
- write reference image metadata and `GeneratedObjectRequest` files
- support `ExternalScreenshot`, `UnityFramebufferDebug`, and future device capture modes

### 3.13 `GenerativeObjectCoordinator`
Responsibility:
- turn generated-object requests into local job records
- write prompt artifacts for external image stylization
- expose job state to the debug panel

### 3.14 `LocalGeneratedObjectBackendAdapter`
Responsibility:
- locally simulate a stylized-image backend for development
- support `ExternalFileProtocol` for manual or out-of-process image workers
- consume returned result artifacts and update job state

### 3.15 `HostedImageUploadBridge`
Responsibility:
- optionally turn local stylized PNG outputs into hosted `http(s)` URLs
- write `StylizedImageUrl` back to generated-object jobs
- keep upload credentials out of request/job/result JSON

### 3.16 `Seed3DBackendAdapter`
Responsibility:
- submit hosted stylized images to Ark Seed3D 2.0
- poll submitted tasks and resume interrupted `ModelGenerationSubmitted` jobs
- download model packages for editor-side import
- keep `ARK_API_KEY` in the process environment, not in scene files or job JSON

### 3.17 `GeneratedObjectModelImporter`
Responsibility:
- editor-only import of `ModelReady` generated assets
- normalize generated model wrappers to a centered bottom pivot
- remove untrusted imported colliders
- save generated table proxy prefabs under `Assets/Generated/ThemeAssets/<requestId>/`

### 3.18 Future `DevicePassthroughCaptureService`
Responsibility:
- on Quest builds, capture a real headset-supported RGB frame for the generated-object request path
- save PNG and metadata under `Application.persistentDataPath`
- preserve camera pose, selected MRUK anchor, bounds, and crop metadata for offline/async generation

This future service should feed the existing generated-object request contract rather than creating a second pipeline.

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
│  │  ├─ SegmentationDebugBridge
│  │  ├─ BestViewCaptureService
│  │  ├─ GenerativeObjectCoordinator
│  │  └─ LocalGeneratedObjectBackendAdapter
│  ├─ Stylization
│  │  ├─ StylizationPlanner
│  │  ├─ AnchorThemeApplier
│  │  ├─ SurfaceTexturePromptBuilder
│  │  ├─ SurfaceOverrideApplier
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
- Roomify-style seamless wall/floor texture prompts
- scaffold-aligned surface override planes
- decals
- lighting
- ambient effects

Avoid:
- large geometry changes
- blocking navigation

Wall overrides should be offset outward by about 5cm when using MRUK wall planes so they do not fight with door/window semantics or the underlying debug mesh.

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
