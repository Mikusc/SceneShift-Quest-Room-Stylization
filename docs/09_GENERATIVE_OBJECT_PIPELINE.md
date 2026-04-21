# 09 Generative Object Pipeline

## Purpose
This document describes an **optional advanced pipeline** for furniture stylization that more closely resembles the object workflow shown in the Roomify paper:

1. capture a best-view reference image for one room object,
2. generate a stylized 2D reference image,
3. convert that stylized image into a 3D asset,
4. register the result back onto the room scaffold.

This is **not** the default Phase 1 path.
It is a future-facing extension that should plug into the existing deterministic stylization system.

## Why this is optional
Roomify demonstrates that this route can improve stylistic richness, but it also introduces:
- generation latency,
- cloud dependency,
- 2D-to-3D shape hallucination,
- scale/orientation drift,
- more correction burden,
- more asset-import and caching complexity.

For this repository, the deterministic path remains the primary implementation:
- MRUK / fused semantics provide the scaffold,
- `ThemeProfile` + `StylizationPlanner` decide the mapping,
- `AnchorThemeApplier` applies stable materials or proxy replacements.

Only add the generative object route after the deterministic proxy path is stable for the core categories.

## Roomify-inspired interpretation
The Roomify paper uses a reference-guided object workflow:
- a best-view frame is selected per object,
- a stylized image is generated from that frame,
- that stylized image is converted into a 3D model,
- the result is registered back to the original object scaffold.

The paper also treats registration as a separate step with orientation and scale refinement.
That idea should be preserved here even if the actual models/services differ.

## Repository-level decision
If this path is implemented in this repository, it should follow these rules:

1. It is a **backend option**, not the only stylization method.
2. It runs **asynchronously** and may finish later than the base room stylization.
3. It must always support **fallback to deterministic proxies**.
4. It must preserve:
   - semantic role,
   - approximate footprint,
   - major contact surfaces,
   - collision awareness,
   - user editability.

## Recommended scope
Start with only one category:
- `TABLE`

Do not begin with:
- all furniture at once,
- wall/floor/ceiling generation,
- dynamic skybox generation,
- direct runtime Quest-side 3D generation.

`TABLE` is the best first target because:
- it is semantically important,
- it is collision-sensitive,
- it exposes scale/orientation problems quickly,
- it already exists in the current planner/applier path.

## Proposed architecture

### Existing systems that remain unchanged
- `RoomSemanticBootstrap`
- `ThemeIntentController`
- `StylizationPlanner`
- `AnchorThemeApplier`
- `StylizationDebugPanel`

These remain the backbone.
The generative route should attach to them rather than replace them.

### Proposed new services

#### `BestViewCaptureService`
Purpose:
- capture one or more object-centric reference images for a selected `RoomObjectRecord`
- store camera pose, crop metadata, and object scaffold metadata

#### `GenerativeObjectCoordinator`
Purpose:
- orchestrate the full async pipeline
- request stylized images
- request 3D generation
- track job states
- publish ready/failure status back to the scene

#### `ImageStylizationBackend`
Purpose:
- produce a stylized reference image from:
  - best-view source image,
  - theme prompt,
  - object semantic/function,
  - geometry-preservation prompt constraints

#### `ModelGenerationBackend`
Purpose:
- convert the stylized image into a 3D asset
- return `glb`, `fbx`, or another importable model package

#### `GeneratedAssetRegistry`
Purpose:
- cache completed assets
- deduplicate repeat jobs
- map `(theme, object category, source hash)` to imported Unity assets

#### `GeneratedProxyImporter`
Purpose:
- import returned model files into Unity
- normalize scale/origin
- create prefab variants ready for `AnchorThemeApplier`

## Proposed runtime flow

### Stage 1: scaffold selection
Input comes from the existing room/object understanding layer:
- MRUK anchor,
- or later a fused `RoomObjectRecord`

For each eligible object, collect:
- `ObjectId`
- semantic label
- function tag
- world pose
- dimensions
- world bounds
- collision-sensitive flag
- theme id

### Stage 2: best-view capture
Capture one object-centric image and record:
- image path or asset id
- crop rectangle
- camera pose
- approximate object yaw
- scaffold dimensions

Important rule:
- best-view capture is a **helper stage**
- if it fails, the deterministic proxy path must still proceed

### Stage 3: stylized image generation
Send a prompt that preserves:
- overall silhouette,
- dominant orientation,
- aspect ratio,
- object function,
- footprint trust.

Prompt emphasis should be:
- “stylize this object”
- not “invent any object you want”

For collision-sensitive objects, the prompt should explicitly preserve:
- support surface,
- sit-able area,
- storage volume,
- major front/back orientation.

### Stage 4: image-to-3D generation
Send the stylized image to the selected 3D generation backend.

Output should include:
- model file
- thumbnail preview
- optional texture set
- backend metadata

Do not assume the raw output is scene-ready.
The next import/registration stages are mandatory.

### Stage 5: Unity import and normalization
After model import:
- place the asset under a generated-assets folder
- compute mesh bounds
- normalize local origin and scale metadata
- save as a prefab for later reuse

Recommended local folder:
- `Assets/Generated/ThemeAssets/`

This folder should be treated as cache/output, not hand-authored content.

### Stage 6: scene registration
Use the existing scaffold from MRUK/fused semantics to fit the generated object:
- preserve approximate footprint,
- preserve bottom-face alignment,
- preserve major yaw orientation,
- clamp exaggerated extents,
- expose correction handles if the result still drifts.

The planner should still own the mapping decision.
The applier should only swap:
- deterministic proxy,
- or generated proxy,
based on asset readiness.

### Stage 7: correction and approval
For collision-sensitive categories, do not silently finalize generated assets.
Require:
- visual review,
- small correction if needed,
- easy revert to deterministic proxy.

## Integration with current data contracts
The current repository contracts in `docs/05_DATA_CONTRACTS.md` are still valid.
If this pipeline is implemented later, add these contracts rather than overloading unrelated classes.

### Suggested additional data contracts

#### `GeneratedObjectRequest`
```csharp
string RequestId;
string ObjectId;
string ThemeId;
string SemanticLabel;
string FunctionTag;
Pose WorldPose;
Bounds WorldBounds;
Vector3 Dimensions;
string SourceImagePath;
Pose BestViewCameraPose;
bool CollisionSensitive;
```

#### `GeneratedObjectJobState`
```csharp
public enum GeneratedObjectJobState
{
    Idle,
    CapturingReference,
    WaitingForStylizedImage,
    WaitingFor3DModel,
    Importing,
    Ready,
    Failed
}
```

#### `GeneratedAssetRecord`
```csharp
string RequestId;
string ObjectId;
string ThemeId;
GeneratedObjectJobState State;
string StylizedImagePath;
string GeneratedModelPath;
string ImportedPrefabPath;
Vector3 NormalizedBoundsSize;
float SourceYawDegrees;
string FailureReason;
DateTime UpdatedAtUtc;
```

## Planner and applier responsibilities

### `StylizationPlanner`
Should still decide:
- whether a category is eligible for generated treatment,
- whether deterministic proxy remains the default,
- whether the object is too sensitive to generate automatically.

Suggested planner output:
- `ReplacementMode = ProxyPrefab` while waiting,
- plus metadata such as:
  - `generation_mode = deterministic | generated_candidate | generated_ready`

### `AnchorThemeApplier`
Should still:
- own final placement,
- own footprint fitting,
- own collision-sensitive safeguards,
- own fallback behavior.

It should not own:
- cloud orchestration,
- image generation API calls,
- long-running async job management.

## Safety and fallback rules

### Hard fallback rule
If any generation stage fails, revert to:
- deterministic proxy,
- or material/overlay path.

Never leave a collision-sensitive object in an indeterminate state.

### Collision-sensitive approval rule
For `TABLE`, `SEATING`, and `STORAGE`:
- keep the generated object within bounded scale deviation,
- require correction affordances,
- preserve the object’s gross location and navigational clearance.

### Orientation rule
Generated objects must support:
- yaw correction,
- optional 90-degree axis correction,
- fast reset.

This is especially important because image-to-3D pipelines often drift in front/back inference.

## Recommended execution model
For this project, use:
- **editor/workstation-side async generation**
- not on-device runtime generation

Reasons:
- lower Quest-side complexity,
- easier debugging,
- easier asset caching,
- easier coursework explanation,
- fewer UX stalls during the core demo loop.

This means the user flow should be:
1. stylize room immediately with deterministic assets,
2. optionally request a higher-fidelity generated object,
3. preview it when ready,
4. accept or reject it.

## Caching strategy
Cache generated assets by a stable key such as:
- theme id,
- semantic label,
- source object id,
- source image hash,
- prompt version.

This prevents unnecessary regeneration when:
- the room is reloaded,
- the same theme is re-applied,
- the same object is reviewed again.

## What not to do
Do not implement this path as:
- a blocking step before any stylization can appear,
- a Quest-only runtime generation loop,
- a replacement for MRUK semantics,
- a replacement for correction mode,
- a reason to skip deterministic proxy support.

## Recommended adoption order

### Step 1
Add best-view image capture metadata only.
No 3D generation yet.

### Step 2
Add stylized reference image generation for one `TABLE`.
Still keep deterministic proxy as the applied object.

### Step 3
Add offline/async image-to-3D generation and Unity import for one `TABLE`.

### Step 4
Let `AnchorThemeApplier` choose between:
- deterministic table proxy,
- generated table prefab,
based on asset readiness.

### Step 5
Add correction/approval UI for generated furniture.

### Step 6
Only after `TABLE` is stable, extend to:
- `STORAGE`
- `SEATING`

## Repository recommendation
Use this path only as a **Phase 1 extension or Phase 2-quality upgrade**, not as the first system to stabilize.

For the current repository state, the recommended order remains:
1. stabilize deterministic proxy/material stylization,
2. finish correction mode,
3. then attach this generative-object backend behind the planner/applier pipeline.
