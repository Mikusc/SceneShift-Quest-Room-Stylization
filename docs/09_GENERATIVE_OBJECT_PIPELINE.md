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

Current repository note:
- `ExternalScreenshot` is the preferred simulator-stage source mode
- `UnityFramebufferDebug` is retained only for plumbing/debug checks
- `DevicePassthroughReserved` is preserved for the future true-device capture path

#### `GenerativeObjectCoordinator`
Purpose:
- orchestrate the full async pipeline
- request stylized images
- request 3D generation
- track job states
- publish ready/failure status back to the scene

Current repository note:
- a first local shell now exists
- it does not call any real cloud backend automatically yet
- it converts the latest `GeneratedObjectRequest` into a queued local `.job.json` record under `Library/GeneratedObjectJobs/`
- it writes a Roomify-inspired `.prompt.txt` artifact for the backend/manual worker handoff

#### `ImageStylizationBackend`
Purpose:
- produce a stylized reference image from:
  - best-view source image,
  - theme prompt,
  - object semantic/function,
  - geometry-preservation prompt constraints

Current repository note:
- no automated in-repository image stylization API is connected yet
- the current local placeholder is `LocalGeneratedObjectBackendAdapter`
- it simulates a `StylizedImageReady` result by applying a small theme-aware local image transform, writing `Library/GeneratedObjectOutputs/*.stylized.png`, and writing a matching local `*.result.json` artifact that records prompt/input/output handoff
- the same component also supports an `ExternalFileProtocol` mode that writes `Library/GeneratedObjectBackendInbox/*.submission.json` plus a prefilled `*.result.template.json`; that contract has been used once with a manual image worker to produce an isolated RGBA table image

#### `ModelGenerationBackend`
Purpose:
- convert the stylized image into a 3D asset
- return `glb`, `fbx`, or another importable model package

Current repository note:
- the first real model candidate was produced manually through Seed3D 2.0 using the isolated stylized table PNG
- the returned medium-subdivision GLB is stored at `Assets/Generated/ThemeAssets/table_18_20260424071758/table_18_20260424071758.seed3d.medium.glb`
- API keys and signed backend download URLs are operator secrets and must not be committed or copied into documentation

#### `GeneratedAssetRegistry`
Purpose:
- cache completed assets
- deduplicate repeat jobs
- map `(theme, object category, source hash)` to imported Unity assets

Current repository note:
- there is not yet a standalone registry database
- the runtime applier currently scans imported `Library/GeneratedObjectJobs/*.job.json` records and chooses the newest usable imported generated table prefab

#### `GeneratedProxyImporter`
Purpose:
- import returned model files into Unity
- normalize scale/origin
- create prefab variants ready for `AnchorThemeApplier`

Current repository note:
- `Assets/Scripts/Editor/GeneratedObjectModelImporter.cs` now imports `ModelReady` jobs
- it imports the generated GLB, removes imported colliders, normalizes the wrapper to a centered bottom pivot, writes a prefab under `Assets/Generated/ThemeAssets/<requestId>/`, and advances the job to `Imported`

## Roomify Chain Mapped to This Repository

The Roomify paper does not treat the object path as a simple:
- screenshot,
- stylized image,
- 3D model

sequence.

It is a **spatially constrained** chain:
1. semantic scaffold selection,
2. best-view frame selection,
3. stylized image generation with geometry hints,
4. 3D model generation,
5. scaffold-aware registration back into the scene.

In this repository, that chain should map onto runtime modules like this:

| Roomify stage | Paper role | Repository module |
| --- | --- | --- |
| semantic scaffold | object bbox / category / pose / size | `RoomSemanticBootstrap` now, later `RoomObjectRecord` / `RoomSemanticSnapshot` |
| best-view selection | choose one orientation-preserving reference frame | `BestViewCaptureService` |
| style + function prompt assembly | infer what the replica should be and how it should look | `StylizationPlanner` + `GeneratedObjectPromptBuilder` + `GenerativeObjectCoordinator` |
| stylized image generation | preserve object role while applying scene style | `ImageStylizationBackend` |
| image-to-3D | convert stylized image to lightweight model | `ModelGenerationBackend` |
| import/cache | turn backend output into reusable Unity assets | `GeneratedProxyImporter` + `GeneratedAssetRegistry` |
| registration | fit generated asset back to scaffold | `AnchorThemeApplier` consuming generated proxy metadata |
| manual correction | handle hallucination or alignment drift | future `CorrectionModeController` |

### What already exists in this repository
- `RoomSemanticBootstrap` already provides room/anchor semantics.
- `StylizationPlanner` already provides semantic/function mapping decisions.
- `AnchorThemeApplier` already provides deterministic proxy fitting against MRUK anchors.
- `BestViewCaptureService` now provides the first thin entry point for best-view capture requests and exports:
  - a full-frame reference image,
  - a local `GeneratedObjectRequest` JSON record,
  - source/crop metadata,
  all for the `TABLE` path.
- For simulator-stage work, the preferred image source is a manual external screenshot, because Unity's own framebuffer does not faithfully represent the simulator/compositor passthrough-like view.
- In the current `ExternalScreenshot` implementation, the original screenshot is used directly as the backend input image while the estimated crop rect is preserved in metadata for later refinement.
- `GenerativeObjectCoordinator` now consumes captured requests and writes:
  - `Library/GeneratedObjectJobs/*.job.json`,
  - `Library/GeneratedObjectJobs/*.prompt.txt`.
- `GeneratedObjectPromptBuilder` materializes a Roomify-inspired prompt that carries theme, function, geometry, yaw, and preservation constraints.
- `LocalGeneratedObjectBackendAdapter` now provides two workstation-side modes:
  - `LocalMockStylization`, which writes a simulated `.stylized.png` and `.result.json`,
  - `ExternalFileProtocol`, which writes `.submission.json` plus `.result.template.json` for a manual or external worker.
- One manual external-worker path has produced a real isolated table image:
  - `Library/GeneratedObjectOutputs/table_18_20260424071758.stylized.png`
  - the file is RGBA PNG data and is the image-to-3D source for the current generated table candidate.
- One manual Seed3D 2.0 image-to-3D path has produced and imported:
  - `Assets/Generated/ThemeAssets/table_18_20260424071758/table_18_20260424071758.seed3d.medium.glb`
  - `Assets/Generated/ThemeAssets/table_18_20260424071758/table_18_20260424071758.generated_table_proxy.prefab`
- `AnchorThemeApplier` can now prefer that imported generated table prefab in Editor/Simulator and fall back to deterministic theme proxies when no imported generated job is usable.

### What is still missing
- no normalized `RoomObjectRecord` path yet
- no generalized multi-category capture path yet; the current raw/cropped image export only targets `TABLE`
- no automated image stylization API integration; the current image was produced by manual worker flow
- no automated Seed3D worker integration; the current model was produced by a manual API run
- no durable generated asset registry beyond scanning imported `.job.json` records
- no full Roomify-style OBB/IoU generated-proxy registration path yet
- no manual approval/reject/correction UI specifically for generated furniture
- no true-device passthrough/camera-frame capture path; `DevicePassthroughReserved` is only a reserved contract value

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

Repository interpretation:
- right now, the thinnest viable input is a `TABLE`-labeled `MRUKAnchor`
- later, this should become a `RoomObjectRecord` so MRUK/fused perception/manual corrections all share one contract

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

Roomify-specific note:
- the best-view frame is not only a reference image
- it is also the **orientation anchor** used later during registration
- this is why camera pose and best-view yaw must be preserved in the capture record

Repository interpretation:
- `BestViewCaptureService` should evolve from full-frame screenshot export into:
  - best frame id,
  - crop rectangle,
  - camera pose,
  - best-view yaw,
  - scaffold dimensions,
  - visibility/centering score summary
- In the current prototype, simulator-stage best-view input should come from `ExternalScreenshot`.
- `UnityFramebufferDebug` should only be used to validate local export plumbing, not as a true Roomify-style source image.
- `DevicePassthroughReserved` should remain in the contract so true-device passthrough/camera capture can later reuse the same request/export pipeline.

### Stage 3: stylized image generation
Send a prompt that preserves:
- overall silhouette,
- dominant orientation,
- aspect ratio,
- object function,
- footprint trust.

Prompt emphasis should be:
- “create one isolated stylized object asset from this reference”
- not “invent any object you want”
- not “edit the original room photo as the final canvas”

For collision-sensitive objects, the prompt should explicitly preserve:
- support surface,
- sit-able area,
- storage volume,
- major front/back orientation.

Roomify-specific note:
- the paper’s prompt explicitly injects:
  - object function,
  - target replica function,
  - global style,
  - object size,
  - object yaw,
  - the requirement to keep the same angle, dimensions, and proportions
- it also asks for a blank/transparent background so the resulting image can cleanly drive the 3D generation stage

Repository interpretation:
- `StylizationPlanner` should remain responsible for:
  - semantic label,
  - function tag,
  - theme style keywords,
  - safety classification
- the future `ImageStylizationBackend` should receive those values rather than re-infer them from scratch
- in the current repository, `GeneratedObjectPromptBuilder` now materializes this handoff as a Roomify-inspired `.prompt.txt` artifact written by `GenerativeObjectCoordinator`
- current prompt version is `roomify_image_asset_v2`
- the generated image worker must return a single object asset with transparent alpha, no room background, no chairs/floor/walls, and no source-scene clutter
- when GPT image 2 or another worker cannot output native alpha, the worker should generate on a flat chroma-key background and remove that key before writing the final `*.stylized.png`

### Stage 4: image-to-3D generation
Send the stylized image to the selected 3D generation backend.

Output should include:
- model file
- thumbnail preview
- optional texture set
- backend metadata

Do not assume the raw output is scene-ready.
The next import/registration stages are mandatory.

Current repository interpretation:
- Seed3D 2.0 is currently a manual/offline backend step, not a committed runtime worker
- the tested model command requested medium subdivision and GLB output
- the downloaded backend package is unpacked outside `Assets/`, then only the Unity-ready GLB is copied into `Assets/Generated/ThemeAssets/<requestId>/`
- generated backend metadata may be kept under `Library/GeneratedObjectModels/`, but API keys and signed URLs must stay out of tracked files and docs

Roomify-specific note:
- the paper uses this stage because image models currently preserve style and coarse geometry better than direct text-to-3D
- but it also explicitly acknowledges hallucination risk in:
  - shape,
  - aspect ratio,
  - orientation

### Stage 5: Unity import and normalization
After model import:
- place the asset under a generated-assets folder
- compute mesh bounds
- normalize local origin and scale metadata
- save as a prefab for later reuse

Recommended local folder:
- `Assets/Generated/ThemeAssets/`

This folder should be treated as cache/output, not hand-authored content.

Repository interpretation:
- imported generated assets should not replace hand-authored theme assets
- they should be treated as per-theme cache entries that can be invalidated and regenerated
- the current importer normalizes generated model wrappers to a centered bottom pivot so runtime bottom-face alignment can be deterministic
- imported colliders are removed because generated furniture collision should not be trusted until review/correction is available

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

Current repository implementation:
- `AnchorThemeApplier` keeps `ReplacementMode.ProxyPrefab` and only changes which prefab source is resolved
- in Editor/Simulator, imported generated table jobs can win over the theme/default proxy
- the runtime status records `source=generated_import` when that path is selected
- fitting now computes the target bounds by transforming all eight corners of MRUK `VolumeBounds` from anchor-local space into proxy-root local space
- the current fitting uses exact per-axis scale against the scaffold bounds with default `1.0` footprint/height padding, then aligns generated model bottom to scaffold bottom
- the runtime status logs `target`, `source`, `scale`, and `bottomDelta`; the latest inspected run reported `bottomDelta=0m`
- this fixes the immediate size/floating issue but is still simpler than the full Roomify OBB/IoU refinement stage

### Registration logic borrowed directly from Roomify
The paper’s registration stage is the most important part of the pipeline to preserve.

For this repository, the generated-object path should follow these rules:

1. **Isotropic scale first**
Scale the generated model so its longest OBB edge matches the scaffold’s longest edge.

2. **Use best-view yaw as the orientation prior**
Do not search arbitrary full rotations first.
Search around the best-view yaw because that pose links:
- the captured reference image,
- the generated stylized image,
- the final placed object.

3. **Use bounded orientation search**
For general furniture, search yaw in a small neighborhood around best-view yaw.
The Roomify paper uses:
- `best-view yaw ± 45°`
- `5°` step

4. **Handle flat objects separately**
If the object is very thin, yaw-only search may be insufficient.
The paper tests `90°` axis flips and keeps the best IoU result.

5. **Clamp refined per-axis scale**
After coarse isotropic fit, refine per-axis scale but do not allow exaggerated growth.
The paper uses an empirical guard of `<= 1.3x scaffold extents`.

6. **Bottom-face alignment is mandatory**
Place the generated object so its bottom face matches the scaffold’s bottom face.
This is the minimum requirement for believable grounding.

Repository interpretation:
- the existing deterministic table fitting already performs part of this logic in simplified form
- the future generated-proxy path should extend `AnchorThemeApplier` rather than inventing a second unrelated placer
- if generated registration fails, the system must fall back to the deterministic proxy already planned by `StylizationPlanner`

### Stage 7: correction and approval
For collision-sensitive categories, do not silently finalize generated assets.
Require:
- visual review,
- small correction if needed,
- easy revert to deterministic proxy.

## Integration with current data contracts
The current repository contracts in `docs/05_DATA_CONTRACTS.md` are still valid.
The generated-object branch has now added explicit contracts rather than overloading unrelated classes.

The implementation source of truth is:
- `Assets/Scripts/Perception/GeneratedObjectContracts.cs`

The documentation source of truth is:
- `docs/05_DATA_CONTRACTS.md`

### Current additional data contracts
- `GeneratedObjectRequest`
- `GeneratedAssetRecord`
- `GeneratedImageBackendSubmission`
- `GeneratedImageBackendResult`

### Current generated-object states
```csharp
public enum GeneratedObjectJobState
{
    Pending,
    CaptureReady,
    StylizedImageReady,
    ModelReady,
    Imported,
    Failed,
    BackendSubmitted,
}
```

### Current capture source modes
```csharp
public enum BestViewCaptureSourceMode
{
    ExternalScreenshot,
    UnityFramebufferDebug,
    DevicePassthroughReserved,
}
```

Notes:
- `ExternalScreenshot` is the current simulator-stage practical path.
- `UnityFramebufferDebug` is only for local export plumbing checks.
- `DevicePassthroughReserved` keeps the future headset capture path visible without pretending it works in the simulator.

## Thinnest Repository-First Implementation Order
To keep this repository aligned with Phase 1 priorities, keep this chain small:

1. `BestViewCaptureService`
Capture source image, crop metadata, scaffold dimensions, camera pose, and `GeneratedObjectRequest` for `TABLE`.

Status:
- first version exists

2. `GenerativeObjectCoordinator`
Convert captured requests into `.job.json` and `.prompt.txt` artifacts.

Status:
- first version exists

3. `LocalGeneratedObjectBackendAdapter`
Consume jobs through:
- local mock stylization,
- or `ExternalFileProtocol` for a manual/external worker.

Status:
- first version exists

4. Manual external-worker verification
Use one `TABLE` request, the generated prompt, and an external image generator to produce a real stylized image.
Drop the image and result JSON back into the paths requested by `.submission.json`.

Status:
- first real `TABLE_18` image exists as an RGBA stylized PNG
- future requests should use `roomify_image_asset_v2`, which explicitly requires a transparent isolated object asset

5. `AnchorThemeApplier` generated-proxy registration
Add a generated-proxy registration path that can consume a locally imported prefab using:
- longest-edge scale,
- best-view yaw prior,
- bottom-face alignment,
- deterministic fallback.

Status:
- partially implemented through imported generated table prefab selection, MRUK-corner target bounds, exact scaffold fitting, and bottom-face alignment
- not yet implemented as a full Roomify OBB/IoU registration search

6. Backend hookup
Only after the file protocol and generated-proxy registration are stable, connect:
- image stylization backend,
- model generation backend,
- imported asset registry.

Status:
- manual GPT-image and manual Seed3D steps have been validated for one table candidate
- automated workers and persistent registry are not implemented

This preserves the repository’s core principle:
- deterministic, editable stylization first
- generative asset enrichment second

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
Keep the current `ExternalFileProtocol` flow as the repeatable manual image-worker boundary.

Current status:
- completed once for `table_18_20260424071758`
- new requests should keep using this boundary until an automated image API worker is committed

### Step 2
Keep deterministic proxy as the fallback object while the generated image/model remains a candidate artifact under review.

### Step 3
Use offline/manual image-to-3D generation and Unity import for one `TABLE`.

Current status:
- completed once for `table_18_20260424071758`
- importer produced a generated table prefab under `Assets/Generated/ThemeAssets/`

### Step 4
Let `AnchorThemeApplier` choose between:
- deterministic table proxy,
- generated table prefab,
based on asset readiness.

Current status:
- implemented for Editor/Simulator by reading imported generated table job records

### Step 5
Add correction/approval UI for generated furniture.

Current status:
- not implemented

### Step 6
Only after `TABLE` is stable, extend to:
- `STORAGE`
- `SEATING`

## Repository recommendation
Use this path only as a **Phase 1 extension or Phase 2-quality upgrade**, not as the first system to stabilize.

For the current repository state, the recommended order is:
1. keep deterministic proxy/material stylization as the visible demo path,
2. visually review the current generated table placement from the intended Simulator/user camera,
3. add accept/reject/reset-to-deterministic controls for the generated table,
4. only then tighten registration toward the fuller Roomify yaw/IoU refinement behavior.
