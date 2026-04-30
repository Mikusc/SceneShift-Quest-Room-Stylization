# 09 Generative Object Pipeline

## Purpose
This document describes the current Roomify-inspired generated furniture pipeline.

The pipeline is no longer a `TABLE`-only experiment. It is still an optional enrichment path on top of deterministic room stylization, but it now supports multiple MRUK furniture categories and request-locked placement.

Current supported generated-furniture categories:
- `TABLE`
- `STORAGE`
- `SCREEN`
- `COUCH` as the MRUK semantic, mapped internally to `Seating`
- `BED`
- `LAMP`
- `PLANT`
- `OTHER`

`OTHER` uses the most generic object-preservation prompt. The image model is allowed to infer the object role from the captured reference, but placement falls back to the same simple bounds-fit behavior instead of category-specific affordances.

## Position In Phase 1
The generated-object branch is not allowed to block the Phase 1 room stylization demo.

The visible room should still work when image generation, upload, Seed3D, or import fails:
- MRUK surfaces still receive deterministic or cached materials.
- Door/window/vista overlays still use cached or fallback assets.
- Furniture can fall back to deterministic proxies or existing generated imports.
- Failed jobs are inspectable and removable without deleting the original capture.

This matches the project rule:
- deterministic, editable stylization first
- generated furniture enrichment second

## Current Runtime Chain

```text
MRUK furniture anchor
-> best visible target selection
-> DevicePassthroughCaptureService or BestViewCaptureService
-> GeneratedObjectRequest
-> GenerativeObjectCoordinator
-> APIMart gpt-image-2 stylized object image
-> optional HostedImageUploadBridge
-> Ark Seed3D 2.0 model generation
-> GeneratedObjectModelImporter
-> request-locked AnchorThemeApplier placement
-> optional Rotate 90 correction
```

The same Style identity is shared by surfaces and furniture:

```text
built-in/custom Style
-> RuntimeStyleIntentController
-> deterministic keywords or DeepSeekStyleIntentProvider
-> prompt text + StyleVariantId cache identity
```

## Current Main Components

### `DevicePassthroughCaptureService`
Used for Quest Link/headset-oriented capture.

Responsibilities:
- score visible MRUK furniture anchors from gaze/head pose
- auto-select the best supported object
- show capture state in the headset HUD/dashboard
- capture a native passthrough-camera frame when supported
- write a `GeneratedObjectRequest`
- write request/job/prompt artifacts compatible with the backend chain

Current input expectation:
- keyboard/controller capture can be used in Editor Play / Quest Link
- the HUD should show target category, anchor id, score, and capture state
- PCA availability still depends on headset/runtime support and permissions

### `BestViewCaptureService`
Kept as a simulator/external-screenshot fallback.

Use it when:
- MetaXRSimulator is being used
- Quest Link PCA is unavailable
- an operator-provided screenshot is the only practical reference source

This path should not be treated as proof of true headset camera capture.

### `GenerativeObjectCoordinator`
Consumes captured requests and creates:
- `Library/GeneratedObjectJobs/*.job.json`
- `Library/GeneratedObjectJobs/*.prompt.txt`

The prompt is Roomify-inspired:
- preserve object role
- preserve footprint, proportions, contact surfaces, and yaw
- produce a single isolated object asset
- avoid full-room edits, walls, floor, background clutter, or extra furniture

### `ApimartImageBackendAdapter`
Automated image stylization backend.

Current behavior:
- reads `APIMART_API_KEY`
- sends the prompt and reference image to APIMart `gpt-image-2`
- prefers direct base64 image input when supported
- polls task state
- downloads the result to `Library/GeneratedObjectOutputs/<requestId>.stylized.png`
- advances the job to `StylizedImageReady`

### `HostedImageUploadBridge`
Optional bridge for backends that require a public image URL.

Current behavior:
- reads `SCENESHIFT_UPLOAD_TOKEN`
- uploads raw PNG bytes to `https://www.mikusc.top/api/scene-shift/upload`
- uses `x-sceneshift-upload-token`
- writes `StylizedImageUrl` back to the job

This is still useful for Seed3D because Seed3D expects an accessible image URL.

### `Seed3DBackendAdapter`
Automated image-to-3D backend.

Current behavior:
- reads `ARK_API_KEY`
- submits `StylizedImageReady` jobs to Ark Seed3D 2.0
- records `ModelGenerationSubmitted`
- resumes polling submitted jobs after Unity restarts or Play mode interruption
- downloads generated model packages
- writes model artifacts under `Assets/Generated/ThemeAssets/<requestId>/`
- advances jobs to `ModelReady`

Generated model quality is configured in the adapter/backend request. Do not hardcode model simplification in docs or assume old high-quality outputs are representative.

### `GeneratedObjectModelImporter`
Editor-side importer.

Current behavior:
- imports `ModelReady` jobs
- removes imported colliders
- normalizes the model under a centered bottom pivot wrapper
- saves a generated prefab under `Assets/Generated/ThemeAssets/<requestId>/`
- advances the job to `Imported`

Generated prefabs and GLBs are local cache/output artifacts. They should not be committed unless a specific demo artifact is intentionally preserved.

### `AnchorThemeApplier`
Runtime placement and fallback.

Current behavior:
- resolves generated prefabs only when they match the active request/object/style constraints
- avoids silently applying an old capture to a new target
- fits imported furniture to MRUK bounds
- aligns bottom face to scaffold bottom
- aligns obvious long-axis mismatches when possible
- falls back to deterministic proxy/material behavior when no valid generated asset exists

## Request Locking
Generated furniture placement must be request-locked.

The applier should only use a generated prefab if it matches one of:
- active `RequestId`
- active source object id / anchor id
- compatible theme/style variant
- imported job state

This prevents a model generated for an old room, old object, or old style from being reused accidentally after:
- switching rooms
- switching style
- capturing another object of the same category
- restarting Play mode

## Surface Pipeline Is Separate
Do not route walls, floors, ceilings, doors, or windows through Seed3D.

Surface/opening flow:

```text
MRUK surface/opening anchors
-> SurfaceTexturePromptBuilder
-> APIMart surface texture / vista jobs
-> SurfaceOverrideApplier
-> wall/floor/ceiling materials
-> door panel
-> window frame + exterior vista
```

Furniture flow:

```text
MRUK furniture anchors
-> capture reference
-> image2 stylized isolated object
-> Seed3D
-> imported prefab
-> bounds-fit placement
```

The two pipelines share Style intent, cache status, job status, and UI reporting. They should not share geometry placement logic.

## Cache And Cleanup Strategy
Generated object cache identity should include:
- room id when available
- style id / style variant id
- semantic category
- source object id / anchor id
- source image hash or request id
- prompt version

The current project includes cleanup tooling for generated object artifacts. Preferred cleanup behavior:
- failed jobs can be removed
- old generated models for the same object/style can be archived
- capture records can be kept so the object can be regenerated without repeating capture
- generated model assets should not be deleted blindly if they are still under review

## Correction Support
Current implemented correction support:
- request-locked placement
- clean view / shell visibility controls
- per-object world status cards
- runtime dashboard status
- `Rotate 90` correction for already placed generated furniture

Still missing before demo-final:
- accept / reject UX
- reset a generated object to deterministic fallback
- persistent correction records
- fine-grained nudge/scale handles

The current `Rotate 90` feature is useful for image-to-3D orientation drift, but it is not a complete correction mode.

## Safety Rules
Hard rules:
- never block room stylization while waiting for generation
- never apply a generated object if the request identity is ambiguous
- never trust generated colliders by default
- keep real-world walkable clearance readable
- preserve approximate footprint and support/contact surfaces
- expose failure state instead of silently hiding it

Collision-sensitive categories:
- `TABLE`
- `STORAGE`
- `SEATING` / MRUK `COUCH`
- `BED`

These should be reviewed visually before using them in a demo recording.

## Known Limitations
- No runtime Quest-side GLB import path; Unity import still depends on Editor-side import.
- Native passthrough-camera capture depends on headset/runtime support and permissions.
- Generated model geometry can drift in scale, silhouette, and orientation.
- `OTHER` is useful for broad coverage but has the weakest semantic safety.
- Cache identity is still simpler than a full production asset registry.
- Correction mode is not yet complete.

## Recommended Demo Use
For a stable demo:
1. Let surfaces and openings load from cache or regenerate first.
2. Capture only objects that are visually important.
3. Wait for generated furniture jobs to reach `Imported`.
4. Re-enter Play or reapply room if needed so imported prefabs are picked up.
5. Use clean view to hide MRUK shells and status cards.
6. Use `Rotate 90` only when an otherwise good model has a yaw mismatch.
7. Do not commit generated GLBs/prefabs unless explicitly preserving a demo asset.
