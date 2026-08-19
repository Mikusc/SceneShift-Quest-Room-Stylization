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
MRUK furniture anchor
    -> DevicePassthroughCaptureService or BestViewCaptureService
    -> GeneratedObjectRequest
    -> GenerativeObjectCoordinator
    -> ApimartImageBackendAdapter or LocalGeneratedObjectBackendAdapter
    -> stylized image / external worker result
    -> HostedImageUploadBridge / public image URL
    -> Seed3DBackendAdapter or manual Seed3D worker
    -> GeneratedObjectModelImporter
    -> AnchorThemeApplier generated proxy registration
```

Target true-device generated-object loop:

```text
RuntimeStyleIntentController
    -> DevicePassthroughCaptureService
    -> QuestRuntimeGenerationClient
    -> secure backend proxy
    -> stylized image + image-to-3D backend jobs
    -> RuntimeGeneratedModelLoader
    -> AnchorThemeApplier runtime generated proxy registration
    -> GeneratedObjectReviewController
    -> persisted accepted/rejected/corrected record
```

This target loop is the active demo ambition for generated furniture. It must not depend on Unity Editor `AssetDatabase`, local Mac environment variables, or manually importing GLBs into `Assets/` while the headset user is in the demo.

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
- create Roomify-style wall / floor / ceiling / door-frame / window-frame / window-vista prompt artifacts from the active `ThemeProfile`
- include the active `RuntimeStyleIntent` in prompt text and in a stable `StyleVariantId` cache key, allowing arbitrary user style requests without reusing stale preset textures
- set image aspect per output role, using square images for materials/trim and wide `16:9` images for window vistas
- write prompt, JSON handoff, and `.surface.job.json` files under `Library/SurfaceTextureJobs/`
- keep deterministic fallback textures available when generated PBR textures are absent

Outputs:
- `SurfaceTexturePromptSet`
- wall / floor / ceiling / door-frame / window-frame / window-vista `.prompt.txt` files
- `SurfaceTextureJobRecord` files for APIMart image generation
- optional persistent material assets under `Assets/Materials/SurfaceOverrides/`

### 3.7b `ApimartSurfaceTextureBackendAdapter`
Responsibility:
- consume `PromptReady` surface texture jobs
- default to processing only jobs whose `ThemeId` and `StyleVariantId` match the current active theme/style, so Play mode does not submit inactive-theme or stale-style jobs unnecessarily
- process surface image jobs with bounded parallelism (`maxConcurrentSurfaceImageJobs`, default 2) so surface generation does not block furniture generation
- call APIMart `gpt-image-2` for image-only surface/trim/vista textures
- download generated PNGs under `Library/SurfaceTextureOutputs/`
- advance jobs to `TextureReady` without entering the Seed3D furniture path

### 3.7c `GenerationQueueStatusService`
Responsibility:
- scan `Library/GeneratedObjectJobs/` and `Library/SurfaceTextureJobs/`
- summarize object-generation and surface-generation queue counts for the headset/debug HUD
- keep progressive feedback visible while jobs run in parallel

### 3.7d `GenerationJobWorldStatusOverlay`
Responsibility:
- scan `Library/GeneratedObjectJobs/` during Play
- resolve each generated-object job back to its `GeneratedObjectRequest`
- place a small world-space status label above the captured furniture bounds
- keep per-object progress understandable when several furniture jobs are running or already placed

### 3.8 `SurfaceOverrideApplier`
Responsibility:
- spawn MRUK-scaffold-aligned surface override planes under `StylizedContentRoot/SurfaceOverrides`
- apply ThemeProfile wall/floor/ceiling materials without changing room geometry
- keep wall/floor/ceiling overrides opaque for clean stylized-room presentation
- apply full door/portal panels on continuous wall material
- apply open-center window frame overlays and optional exterior vista planes slightly outside the room
- use wall overlap and trim strips to reduce MRUK plane seams without relying on bright debug borders

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

### 3.11b `SceneShiftUISetDashboard`
Responsibility:
- provide the headset-facing main control panel through the current stable SceneShift dashboard
- keep the dashboard content instantiated under `SceneShiftDashboardContent` in the canonical scene so its layout is inspectable and editable in the Editor
- preserve official UISet button/dropdown prefab instances and Interaction SDK ray/poke interaction; rebuild the hierarchy explicitly through the dashboard Inspector or `SceneShift/UI` menu, with runtime creation only as a missing-content fallback
- expose demo-critical controls: capture current target, auto gaze target, style selection, reapply room surfaces, clean view, object status cards, and generated-furniture `Rotate 90`
- keep the old debug panel available for dense developer diagnostics while this panel serves as the cleaner user-facing entry point

### 3.11c `RoomStyleCacheService`
Responsibility:
- summarize reusable generated content by active Style/theme and room context when available
- count active-theme surface texture cache readiness and generated-furniture readiness
- report `cached / partial / generating / missing` status for each theme
- give the UISet dashboard a stable cache status line before users switch styles

### 3.12 `BestViewCaptureService`
Responsibility:
- provide simulator/external-screenshot capture fallback for generated-object requests
- write reference image metadata and `GeneratedObjectRequest` files
- support `ExternalScreenshot`, `UnityFramebufferDebug`, and the shared `DevicePassthroughReserved` request contract

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
- save generated furniture prefabs under `Assets/Generated/ThemeAssets/<requestId>/`

### 3.18 `DevicePassthroughCaptureService`
Responsibility:
- on Quest Link/headset runs, capture a headset-supported RGB frame for the generated-object request path when PCA is available
- save PNG and metadata under `Application.persistentDataPath`
- preserve camera pose, selected MRUK anchor, bounds, and crop metadata for offline/async generation

This service feeds the existing generated-object request contract rather than creating a second pipeline.

### 3.19 `QuestRuntimeGenerationClient`
Planned responsibility:
- submit `GeneratedObjectRequest` plus active `RuntimeStyleIntent` to a secure backend service
- poll backend job status from the headset
- download the final runtime-loadable model URL or failure reason
- write local job/status records under `Application.persistentDataPath`
- keep APIMart, Seed3D, DeepSeek, upload, and signing credentials out of the Quest APK

This component replaces the current direct API-key-in-Unity pattern for standalone-device demos.

### 3.20 `RuntimeGeneratedModelLoader`
Planned responsibility:
- download or read a generated `.glb` from `Application.persistentDataPath`
- load it at runtime on Quest without `AssetDatabase`
- normalize bounds, bottom pivot, and scale metadata into the same placement contract used by `AnchorThemeApplier`
- strip or ignore untrusted generated colliders
- expose load failure states to the dashboard rather than silently falling back

The first spike should load one known test GLB URL and place it on the selected `TABLE` anchor before connecting the full backend.

### 3.21 `GeneratedObjectReviewController`
Planned responsibility:
- bind a generated candidate to one request/object/style identity
- support preview, accept, reject, reset to deterministic fallback, and bounded correction
- persist the accepted/rejected/corrected decision under `Application.persistentDataPath`
- restore accepted generated assets for the same room/object/style when available
- require a clear revert path for collision-sensitive furniture

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

### 8.1b Openings: door / window frame / window vista
Use:
- full door or portal panel overlays on the room-facing side
- open-center window frame overlays
- exterior vista planes slightly outside valid windows
- edge glow or decal-like generated textures when they match the Style

Avoid:
- cutting large holes for doors unless a later interaction explicitly needs pass-through
- opaque window center fills
- anything that blocks passage, view, or real-world affordances

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
- accept generated candidate
- reject generated candidate
- reset generated candidate to deterministic fallback
- persist correction only after explicit confirmation

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
