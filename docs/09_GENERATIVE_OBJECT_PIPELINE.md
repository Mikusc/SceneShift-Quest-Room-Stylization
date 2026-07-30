# 09 Generative Object Pipeline

## Purpose
This document describes the current Roomify-inspired generated furniture pipeline.

The pipeline is no longer a `TABLE`-only experiment. It supports multiple MRUK furniture categories and request-locked placement.

The current demo ambition is to run the full generated-object loop on a standalone Quest headset: user style intent, passthrough capture, backend generation, runtime 3D model loading, anchor-aligned replacement, and in-headset review/editing.

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

For the current true-device generated-object demo, generated furniture is the active stretch path. It still cannot be the only way the room works.

## Current Editor / Quest Link Chain

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

This chain is useful for development, but it still depends on Editor-side model import before a generated GLB becomes a placeable prefab.

## Target Standalone Quest Chain

```text
user enters style intent in headset UI
-> RuntimeStyleIntentController creates style identity and prompt keywords
-> DevicePassthroughCaptureService captures target object and anchor metadata
-> QuestRuntimeGenerationClient uploads request to secure backend or local test backend
-> backend runs image stylization and image-to-3D generation
-> headset polls backend job status
-> headset downloads generated GLB to Application.persistentDataPath
-> RuntimeGeneratedModelLoader loads and normalizes the GLB without AssetDatabase
-> AnchorThemeApplier fits it to the matching MRUK anchor
-> GeneratedObjectReviewController exposes preview / accept / reject / reset / correction
-> accepted or corrected result is persisted for the same room/object/style
```

The Quest APK must not store APIMart, Seed3D, DeepSeek, upload, or signing credentials. Those calls belong behind a backend proxy.

Before packaging or MQDH handoff, run `bash Tools/scan_predevice_secrets.sh` from the project root. This terminal check mirrors the Unity readiness secret scan for packaged config/assets and generated job JSON records without printing matching line contents.

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
- reads the environment variable named by `apiKeyEnvironmentVariable`; set that field to
  `APIMART_API_KEY` only for an explicitly enabled Editor/Quest Link workflow
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
- reads the environment variable named by `apiKeyEnvironmentVariable`; set that field to
  `ARK_API_KEY` only for an explicitly enabled Editor/Quest Link workflow
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

Standalone Quest requirement:
- `GeneratedObjectModelImporter` is not available in an APK because it uses Unity Editor import APIs.
- A runtime loader must consume a downloaded GLB directly from `Application.persistentDataPath`.
- The runtime loader must reproduce the safety-relevant import steps: bounds normalization, bottom pivot, collider removal/ignore, load failure reporting, and memory cleanup.

### `QuestRuntimeGenerationClient`
Runtime headset client for secure backend orchestration.

Implemented first-slice behavior:
- `LocalTestModelUrl` mode advances the latest generated-object job to `RuntimeModelReady` with a fixed public/sample GLB URL, then can auto-load it through `RuntimeGeneratedModelLoader`.
- `HttpBackend` mode uploads `RuntimeGenerationBackendSubmission` metadata, request JSON, prompt text, and the captured/cropped source image as multipart form data, then consumes/polls `RuntimeGenerationBackendResult` JSON.
- The component is scene-wired under `RuntimeState` in `MR_RoomStylization.unity`.
- Dashboard `Submit+Load` routes the latest captured/generated-object job through this client.

Current backend-facing behavior:
- resolves the most useful source crop from `GeneratedObjectRequest.SourceCroppedImagePath`, `SourceImagePath`, or the job `SourceInputImagePath`
- includes style intent, room id, object id, semantic label, physical target size, fit mode, prompt text, and source request JSON
- writes `.runtime-submission.json` and `.runtime-result.json` artifacts under the job folder
- polls `RuntimeBackendStatusUrl` / `RuntimeBackendJobId` until `RuntimeModelReady`, `Failed`, or timeout
- records backend model URL, mime type, backend hash when available, and the final local runtime hash after Quest download

The backend owns all long-lived service credentials and should return only short-lived model URLs or durable public asset URLs safe for headset use.

Still required:
- deploy or tunnel the runtime backend over an HTTPS URL reachable from Quest
- run `Backend/sceneshift_runtime_backend.py` with `SCENESHIFT_BACKEND_PROVIDER=seed3d` and server-side `ARK_API_KEY`
- switch the scene/client from `LocalTestModelUrl` to `HttpBackend` only for the real-backend test build
- validate real backend mode on Quest with MQDH/ADB evidence proving the returned GLB is not the fixed Khronos `Box.glb`

### `Backend/sceneshift_runtime_backend.py`
Minimal backend used for the real standalone path.

Roles:
- accepts `POST /v1/runtime-generations` from Quest
- stores metadata, request JSON, prompt text, and uploaded image under `Library/RuntimeBackendJobs/`
- reads provider credentials only from the backend process environment
- returns `RuntimeGenerationBackendResult` JSON for submit and poll responses
- serves cached generated model files from `/v1/runtime-generations/<jobId>/files/<fileName>`

Provider modes:
- `manual`: default protocol-test mode. It writes `manual-result.template.json` and waits for an operator-provided `manual-result.json`. This is not true generation evidence.
- `fixed-url`: protocol-test mode using `SCENESHIFT_FIXED_MODEL_URL`. This is not true generation evidence.
- `seed3d`: real provider mode. It reads `ARK_API_KEY`, submits the uploaded image and prompt to Ark Seed3D, polls the task, caches the returned GLB/GLTF/zip model when possible, hashes it, and returns a Quest-downloadable model URL.

Run example:
```bash
cd "/Users/mikusc/Documents/UnityProjects/SceneShift Discussion Room Latest"
export SCENESHIFT_BACKEND_PROVIDER=seed3d
export ARK_API_KEY="..."
export SCENESHIFT_PUBLIC_BASE_URL="https://your-https-tunnel.example"
python3 Backend/sceneshift_runtime_backend.py
```

Protocol smoke:
```bash
bash Tools/run_runtime_backend_protocol_smoke.sh
```

This smoke test starts the backend in `fixed-url` mode and verifies multipart submit plus polling shape. It is useful before a real test build, but it is not true 3D generation evidence.

Real backend preflight:
```bash
export SCENESHIFT_BACKEND_PROVIDER=seed3d
export ARK_API_KEY="..."
export SCENESHIFT_PUBLIC_BASE_URL="https://your-https-tunnel.example"
bash Tools/check_runtime_backend_seed3d_preflight.sh
```

This preflight does not call Seed3D and does not print secrets. It must pass before building an `HttpBackend` Quest package intended to prove true 3D generation.

For Quest validation, `backendSubmitUrl` must point at the public HTTPS `/v1/runtime-generations` endpoint. The APK must still contain no provider API keys.

### `www.mikusc.top` Azure Static Web Apps backend

`/Users/mikusc/Documents/Myblog/api/src/functions/sceneShiftRuntimeGenerations.js` provides an Azure Functions version of the runtime backend for the existing `www.mikusc.top` deployment.

Public endpoint for Unity:
```bash
export SCENESHIFT_RUNTIME_BACKEND_URL="https://www.mikusc.top/api/v1/runtime-generations"
```

Azure Static Web Apps application settings required for true Seed3D runtime generation:
```text
AZURE_STORAGE_CONNECTION_STRING=...
SCENESHIFT_UPLOAD_CONTAINER=scene-shift
SCENESHIFT_BACKEND_PROVIDER=seed3d
ARK_API_KEY=...
SCENESHIFT_PUBLIC_API_BASE_URL=https://www.mikusc.top/api
```

Optional Seed3D overrides:
```text
SEED3D_TASK_ENDPOINT=https://ark.cn-beijing.volces.com/api/v3/contents/generations/tasks
SEED3D_MODEL=doubao-seed3d-2-0-260328
SEED3D_SUBDIVISION_LEVEL=low
SEED3D_FILE_FORMAT=glb
```

The official Seed3D API treats `subdivisionlevel` as the output polygon-density control. For
`doubao-seed3d-2-0-260328`, `low` targets about 100,000 faces, `medium` about 500,000 faces,
and `high` about 1,000,000 faces. SceneShift uses `low` by default for paid true-device
iteration to reduce cost and runtime payload size while capture targeting is still being tuned.
The Seed3D `content.text` payload is kept to official command parameters only, for example
`--subdivisionlevel low --fileformat glb`; SceneShift keeps style, semantic, bounds, and prompt
metadata in its own request/job records rather than relying on Seed3D to parse non-command text.

This serverless path stores job metadata, uploaded capture images, Seed3D responses, and cached GLB/GLTF files in Azure Blob storage. `POST /api/v1/runtime-generations` submits the Seed3D task; `GET /api/v1/runtime-generations/<jobId>` performs lightweight polling and returns `RuntimeModelReady` when the model is cached or downloadable. The APK still receives only the HTTPS backend URL, never the Seed3D API key.

`Tools/check_runtime_backend_seed3d_preflight.sh` validates the local Python backend process, not the deployed Azure Function app. For the `www.mikusc.top` path, validate by confirming the Azure app settings are present, deploying the API, and running `bash Tools/check_runtime_backend_azure_smoke.sh` before building or installing the Quest `HttpBackend` package. The Azure smoke intentionally omits the image, expects the backend to reject the request before creating a paid Seed3D task, and is not real 3D generation evidence.

`/Users/mikusc/Documents/Myblog/api/src/functions/sceneShiftSurfaceGenerations.js` provides the matching Azure Functions surface-generation endpoint for room materials and openings. Unity should call:
```text
https://www.mikusc.top/api/v1/surface-generations
```

Additional Azure Static Web Apps application settings for real surface generation:
```text
APIMART_API_KEY=...
APIMART_IMAGE_MODEL=gpt-image-2
SCENESHIFT_PUBLIC_API_BASE_URL=https://www.mikusc.top/api
```

The surface endpoint accepts the Unity `SurfaceTexturePromptSet`, submits each wall/floor/ceiling/door/window/vista prompt to image2 on the backend, stores the PNGs in Azure Blob storage, and returns Quest-downloadable URLs. It also caches by `RequestId`, so repeated built-in Style requests should reuse existing backend files instead of creating a new paid image task.

Editor configuration helpers:
- `SceneShift/Runtime Backend/Report Runtime Backend Configuration`
- `SceneShift/Runtime Backend/Configure LocalTestModelUrl Mode`
- `SceneShift/Runtime Backend/Configure HttpBackend From Environment`

`Configure HttpBackend From Environment` reads either `SCENESHIFT_RUNTIME_BACKEND_URL=https://.../v1/runtime-generations` or `SCENESHIFT_PUBLIC_BASE_URL=https://...` and refuses non-HTTPS URLs or URL query strings that look like keys/tokens/signatures.

### `PreDeviceRuntimeLoopValidator`
Play Mode helper for Mac-based pre-device regression.

Responsibilities:
- select a safe generated-object target from the current MRUK room, preferring `TABLE`
- write a `GeneratedObjectRequest`, `.job.json`, and prompt artifact using the current room id, object id, Style identity, semantic label, bounds, and prompt version
- submit the job through `QuestRuntimeGenerationClient` local test backend mode
- validate runtime GLB download/load/review behavior without relying on native passthrough-camera capture

This is not proof of true-device PCA capture or standalone Quest performance. It is the local gate before a MQDH/test-channel headset run.

### `RuntimeGeneratedModelLoader`
Runtime model loading and normalization.

Initial implemented behavior:
- download/read `.glb`
- instantiate a renderable object in the Quest app
- compute source bounds
- normalize to centered bottom pivot
- strip or ignore imported/generated colliders
- provide source bounds and local transform metadata through `RuntimeGeneratedModelInstance`
- fit to captured request bounds when a `GeneratedObjectRequest` is available
- attach the configured test GLB URL to the latest generated-object job/request for the first request-bounds placement spike
- update runtime job states to `RuntimeModelDownloaded` and `RuntimeLoaded`
- scene-wired in `MR_RoomStylization.unity` under `RuntimeState`, with a `RuntimeGeneratedModels` root and dashboard `Submit+Load` / `Load Test GLB` / `Load Latest Job` controls

Latest validation:
- synthetic Editor Play `Submit+Load` reached `RuntimeLoaded` with the configured Khronos `Box.glb`, creating one preview `RuntimeGeneratedModelInstance` under `RuntimeGeneratedModels` and fitting it to request bounds without `AssetDatabase`
- current-room Editor Play pre-device validation reached `RuntimeLoaded` through `PreDeviceRuntimeLoopValidator` for the loaded MRUK room's `TABLE_18` target, using real room/style/object identity and request bounds

Still required:
- validate the configured known test GLB URL through a full `MetaXRSimulator` visual/HUD regression and then MQDH/test-channel headset installation on Quest
- continue tightening the shared placement handoff to `AnchorThemeApplier`; reset-to-deterministic-fallback now has pre-device smoke evidence for one `TABLE_18` runtime candidate, but headset behavior is still unproven
- validate reject/reset runtime-model release behavior on headset memory/performance logs; local pre-device smoke now verifies the release policy and runtime loader release path, while preserving local model and review/job records for restore/retry
- do not use non-Play Editor context-menu runs as proof of runtime loading; glTFast runtime instantiation must run in Play Mode or on Quest

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
-> QuestSurfaceGenerationClient
-> HTTPS backend /api/v1/surface-generations
-> backend image2 surface texture / vista jobs
-> SurfaceOverrideApplier
-> wall/floor/ceiling materials
-> door panel
-> window frame + exterior vista
```

The older `ApimartSurfaceTextureBackendAdapter` remains an opt-in Editor/Quest Link development
path, but standalone Quest packages should use `QuestSurfaceGenerationClient` so APIMart/image2
credentials stay in backend application settings. Direct provider adapters default to disabled.

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
- old Mac pre-device runtime evidence with prefix `predevice_room_loop_*` can be archived through `SceneShift/Generated Objects/Archive Pre-Device Runtime Artifacts - Keep Latest`
- before MQDH/test-channel packaging, keep the latest smoke-linked request/job/prompt/runtime-submission/runtime-result files and matching persistent runtime model folder active, and archive older pre-device runtime sets under `Library/GeneratedObjectArchive/PreDeviceRuntimeArtifacts/`

## Correction Support
Current implemented correction support:
- request-locked placement
- clean view / shell visibility controls
- per-object world status cards
- runtime dashboard status
- `Rotate 90` correction for already placed generated furniture
- initial accept / reject / reset persistence for runtime-loaded generated candidates
- automatic persisted-review restore when a runtime candidate is loaded again
- restore of the latest accepted/corrected runtime model from `GeneratedObjectReviews` when the local GLB still exists
- reset-to-deterministic-fallback handoff through `AnchorThemeApplier` for the same object id
- release of hidden reject/reset runtime model GameObjects while preserving the local GLB plus review/job records for retry or persisted-state restore

Still missing before demo-final:
- headset-validated accept / reject UX
- headset-validated reset to deterministic fallback
- headset restart validation for accepted / rejected / corrected / reset decisions
- fine-grained nudge/scale handles

Latest validation:
- synthetic Editor Play rejected-restore test passed: after `Reject`, clearing the runtime instance, and reloading the same runtime-ready job, the newly loaded candidate restored `Rejected` and stayed inactive
- synthetic Editor Play accepted-restore test passed: after `Accept`, clearing the runtime instance, and restoring from `GeneratedObjectReviews`, the candidate reloaded from the existing local GLB, restored `Accepted`, and stayed active
- synthetic Editor Play corrected-restore test passed: a persisted forward nudge and 5 degree yaw correction restored once and did not double-apply after repeated selection
- synthetic Editor Play reset-restore test passed: after `Reset`, reloading the same runtime-ready job restored `ResetToFallback` and kept the runtime candidate inactive
- current-room Editor Play pre-device test passed through `PreDeviceRuntimeLoopValidator`: one `TABLE_18` request in the loaded MRUK room reached `RuntimeLoaded`; accepted/rejected/corrected/reset review restore then passed against that room-context request
- current-room Editor Play smoke test `predevice_smoke_20260524231824` passed `runtime_request_job_contract`: the runtime `TABLE_18` candidate traces back to matching job/request/prompt artifacts, room/object/style/semantic identity, request bounds, HTTPS model URL, local runtime GLB file, and `RuntimeLoaded` job state
- current-room Editor Play smoke test `predevice_smoke_20260524231824` passed `runtime_backend_artifact_contract`: `LocalTestModelUrl` wrote matching runtime submission/result artifacts before loading, with a local-test backend job id, `RuntimeModelReady` result state, and HTTPS test GLB URL
- current-room Editor Play smoke test `predevice_smoke_20260524231824` passed `runtime_reset_deterministic_fallback`: the runtime `TABLE_18` candidate was hidden and a visible `theme_default` deterministic fallback proxy for `TABLE_18` was verified during the probe
- current-room Editor Play smoke test `predevice_smoke_20260524231824` passed `runtime_reject_reset_release_policy`: the review controller is configured to release hidden reject/reset runtime candidates, and the runtime loader successfully released an inactive probe instance
- stale `predevice_room_loop_*` runtime evidence can be archived without deleting it; the latest active readiness check is `Library/PreDeviceBuildReadinessReports/predevice_build_readiness_20260525095727.md`, now passing after Android Build Support was detected at the Unity Hub module path
- build readiness now verifies the active pre-device runtime artifact set directly: exactly one active request set, complete job/request/prompt/runtime-submission/runtime-result files, a persistent runtime GLB folder, and a request id referenced by the latest smoke report

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
- Runtime Quest-side GLB loading now has an initial glTFast-based code path and scene wiring. It has passed synthetic Editor Play and current-room Editor Play pre-device tests, but it is not yet validated on a standalone Quest build.
- The headset-side backend client boundary now exists, but the real secure backend service is not deployed yet; current automated adapters still assume API keys are visible to the Unity process.
- Persisted generated-object review state now has an initial `Application.persistentDataPath` record path, and accepted/rejected/corrected/reset restore behavior is validated in Editor Play. Final headset UX and MQDH/test-channel restart behavior are not validated yet.
- Native passthrough-camera capture depends on headset/runtime support and permissions.
- Generated model geometry can drift in scale, silhouette, and orientation.
- `OTHER` is useful for broad coverage but has the weakest semantic safety.
- Cache identity is still simpler than a full production asset registry.
- Correction mode is not yet complete.

## Recommended Demo Use
For the current Editor / Quest Link chain:
1. Let surfaces and openings load from cache or regenerate first.
2. Capture only objects that are visually important.
3. Wait for generated furniture jobs to reach `Imported`.
4. Re-enter Play or reapply room if needed so imported prefabs are picked up.
5. Use clean view to hide MRUK shells and status cards.
6. Use `Rotate 90` only when an otherwise good model has a yaw mismatch.
7. Do not commit generated GLBs/prefabs unless explicitly preserving a demo asset.

For the target standalone Quest chain, first validate one narrow spike before connecting every service:
1. enter a built-in or custom style intent,
2. select one `TABLE` anchor,
3. press `Submit+Load` to run local test backend mode and receive one known test GLB URL,
4. runtime-load and bounds-fit the GLB on Quest,
5. preview it in review mode,
6. accept, reject, reset, and apply one bounded correction,
7. restart the app and confirm the accepted/corrected state is restored or the rejection is respected.
