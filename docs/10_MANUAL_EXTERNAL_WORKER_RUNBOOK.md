# 10 Manual And External Worker Runbook

## Purpose
This runbook explains how to operate the current generated furniture worker flow.

The default automated chain is:

```text
GeneratedObjectRequest
-> prompt/job JSON
-> APIMart gpt-image-2
-> optional www.mikusc.top upload
-> Ark Seed3D
-> Unity generated model importer
-> request-locked runtime placement
```

This is the current Editor / Quest Link development chain. It is not yet the final standalone Quest chain because Unity import still depends on the Editor.

Target standalone Quest chain:

```text
style intent in headset UI
-> native passthrough capture
-> secure backend proxy
-> backend image + 3D generation
-> Quest runtime GLB download/load
-> request-locked placement
-> in-headset review / accept / reject / reset / correction
```

Manual/external-worker steps are still useful when:
- APIMart is unavailable
- Seed3D is unavailable
- a specific generated image needs manual cleanup
- a failed job needs to be replayed without repeating capture

Generated furniture is optional. The room must remain usable with deterministic or cached fallback styling.

## Preconditions
Before testing:
- active scene is `Assets/Scenes/MR_RoomStylization.unity`
- Unity has no blocking compile errors
- MRUK room state is ready in Play
- runtime dashboard/HUD is visible enough to confirm target and job state
- generated model artifacts under `Assets/Generated/ThemeAssets/` are treated as local cache unless intentionally preserved

For the automated backend chain, launch Unity from an environment that exposes:
- `APIMART_API_KEY`
- `SCENESHIFT_UPLOAD_TOKEN`
- `ARK_API_KEY`

Optional style extraction:
- `DEEPSEEK_API_KEY`

Never commit API keys, signed backend URLs, or generated backend result metadata containing secrets.

For standalone Quest builds, do not put these API keys in the APK, scene fields, resources, or persistent job files. A backend service must own the credentials and expose a narrow SceneShift job API to the headset.
Before packaging or MQDH handoff, run `bash Tools/scan_predevice_secrets.sh` to check packaged config/assets and generated job JSON records for likely long-lived credentials.

## Capture Paths

### Quest Link / headset-oriented path
Use `DevicePassthroughCaptureService`.

Expected operator flow:
1. Enter Play.
2. Wait for MRUK room ready.
3. Look at the target object.
4. Confirm the HUD/dashboard shows a valid target category, id, and score.
5. Trigger capture from the configured keyboard/controller input or dashboard button.
6. Do not assume completion until the job status moves beyond `CaptureReady`.

Supported target categories:
- `TABLE`
- `STORAGE`
- `SCREEN`
- `COUCH` mapped internally to `Seating`
- `BED`
- `LAMP`
- `PLANT`
- `OTHER`

### Simulator / external screenshot fallback
Use `BestViewCaptureService` only when native headset capture is not available.

Expected operator flow:
1. Enter Play.
2. Move to the intended view.
3. Take an external screenshot from that same Play session.
4. Set `BestViewCaptureService.externalScreenshotPath`.
5. Trigger capture before moving the camera.

This path is useful for backend debugging but does not prove true-device passthrough-camera capture.

## Expected Artifacts
After capture and coordinator processing, check:

```text
Library/BestViewCaptures/
Library/GeneratedObjectJobs/
Library/GeneratedObjectOutputs/
Library/GeneratedObjectBackendInbox/
Library/GeneratedObjectModels/
Assets/Generated/ThemeAssets/
```

Important files:
- `*.request.json`
- `*.job.json`
- `*.prompt.txt`
- `*.stylized.png`
- backend submission/result JSON when manual mode is used
- downloaded Seed3D package metadata
- imported generated prefab

## Automated Image Worker
`ApimartImageBackendAdapter` consumes `CaptureReady` jobs.

Expected behavior:
1. read `PromptArtifactPath`
2. attach the reference image, preferably as base64 input when supported
3. submit APIMart `gpt-image-2`
4. poll the returned progress/task id
5. download the generated PNG
6. set job state to `StylizedImageReady`

The generated image should be:
- one isolated stylized object
- no room background
- no walls/floor/ceiling
- no extra furniture
- no text
- object role and proportions preserved
- transparent alpha if possible

If the image model cannot produce alpha, use a clean chroma-key background and remove it before continuing.

## Upload Bridge
`HostedImageUploadBridge` is used when the next backend needs a public URL.

Current endpoint:

```text
POST https://www.mikusc.top/api/scene-shift/upload
Header: x-sceneshift-upload-token: <SCENESHIFT_UPLOAD_TOKEN>
Body: PNG bytes
Content-Type: image/png
```

Success response contains a public URL. The bridge writes that URL to `StylizedImageUrl` on the job.

## Automated Seed3D Worker
`Seed3DBackendAdapter` consumes jobs in `StylizedImageReady` that have a usable `StylizedImageUrl`.

Expected behavior:
1. submit the stylized image URL to Ark Seed3D 2.0
2. record `ModelGenerationSubmitted`
3. poll until completion
4. download the model package
5. unpack if needed
6. copy the Unity-ready GLB to `Assets/Generated/ThemeAssets/<requestId>/`
7. set job state to `ModelReady`

Seed3D can return a zip package even when GLB is requested. Do not place a zip payload into `Assets/` with a `.glb` extension. Extract the package outside `Assets/`, then copy only the real `.glb`.

## Unity Import
The editor importer is:

```text
Assets/Scripts/Editor/GeneratedObjectModelImporter.cs
```

It can be triggered through:

```text
SceneShift/Generated Objects/Import Ready Model Jobs
```

Expected behavior for `ModelReady` jobs:
- import GLB
- remove generated colliders
- normalize under a centered bottom pivot wrapper
- save generated prefab under `Assets/Generated/ThemeAssets/<requestId>/`
- set job state to `Imported`

Runtime placement can only use the generated prefab after import.

## Target Quest Runtime Model Loading
This section describes the required future path for the standalone headset demo.

Expected behavior:
1. backend returns a generated model URL, hash, and status metadata,
2. Quest downloads the model to `Application.persistentDataPath`,
3. a runtime GLB loader instantiates the model without `AssetDatabase`,
4. the loader computes render bounds and normalizes the object to a centered bottom pivot,
5. generated colliders are stripped or ignored,
6. `AnchorThemeApplier` fits the runtime-loaded object to the matching MRUK anchor,
7. the review UI exposes preview, accept, reject, reset, and bounded correction.

The smallest useful validation is one known test GLB URL placed on one selected `TABLE` anchor in a Quest build. Do not wait for the full backend before proving runtime loading and placement.

The runtime loader should write enough status for MQDH/ADB log review:
- request id,
- room id,
- object id / anchor id,
- active Style id,
- downloaded model path,
- model hash if available,
- source bounds,
- fitted target bounds,
- load or placement failure reason.

## Runtime Placement Check
In Play, the dashboard/object status should show:
- active target category
- active object id / anchor id
- cache state
- total queued/running/ready jobs
- whether generated furniture is imported or still running

For a placed generated object, expected status includes:
- `source=generated_import` or equivalent generated source marker
- matching request/object identity
- no active failure
- reasonable visual fit to MRUK shell

If a good object is rotated incorrectly, use the dashboard `Rotate 90` control for the selected generated object.

If the object is too rough, wrong scale, or wrong category:
1. keep the capture
2. archive or remove the old generated model/job
3. rerun from the saved capture or use Reuse Captures
4. do not delete unrelated generated assets

## Manual Image Worker Fallback
Use this when APIMart is not producing an acceptable image.

Steps:
1. open the latest `Library/GeneratedObjectBackendInbox/*.submission.json`
2. read `PromptArtifactPath`
3. upload `SourceInputImagePath` to the external image model
4. generate one isolated stylized object
5. preserve object role, silhouette, proportions, major contact surface, and yaw
6. save the PNG exactly to `RequestedOutputImagePath`
7. copy `ResultTemplatePath` to `RequestedResultPath`
8. set `OutputState` to `StylizedImageReady`
9. verify the request id matches the job

Do not save the result only to Desktop or a photos folder. The watcher reads the paths listed in the submission JSON.

## Manual Seed3D Fallback
Use this when the automated Seed3D adapter is not available.

Steps:
1. upload the stylized PNG or hosted URL to Seed3D
2. request GLB output
3. download the backend package
4. extract it under `Library/GeneratedObjectModels/<requestId>/`
5. copy only the real GLB into `Assets/Generated/ThemeAssets/<requestId>/`
6. update the matching job to `ModelReady`
7. run the Unity importer

Do not put backend zip files, signed URLs, or API secrets into tracked folders.

## Troubleshooting

### No target in HUD
Check:
- MRUK room is loaded
- target object has a supported MRUK semantic
- Auto Target is enabled
- the object is in gaze/frustum
- clean view has not hidden the feedback you need

### Capture created but no image generation
Check:
- latest job is in `CaptureReady`
- `APIMART_API_KEY` was visible before Unity launched
- prompt path and source image path exist
- backend adapter is enabled
- Console has no API/auth/network errors

### Image generated but Seed3D does not start
Check:
- `StylizedImageUrl` exists and is public `https`
- upload bridge has `SCENESHIFT_UPLOAD_TOKEN`
- `ARK_API_KEY` was visible before Unity launched
- job state is `StylizedImageReady`

### Imported model does not appear
Check:
- job state is `Imported`
- `ImportedPrefabPath` exists
- active Play target matches the generated request/object id
- current Style matches the generated style variant
- `AnchorThemeApplier` has reapplied after import

### Runtime model does not appear on Quest
Check:
- backend job reached a model-ready state
- model URL is reachable from the headset network
- downloaded GLB exists under `Application.persistentDataPath`
- runtime loader reported valid render bounds
- generated object identity matches room/object/style/request
- `AnchorThemeApplier` received the runtime-loaded object, not an Editor prefab path
- dashboard shows the failure reason instead of silently hiding it

### Model quality is poor
Options:
- rerun image generation with a stricter prompt
- rerun Seed3D using the same stylized image
- archive old generated artifacts but keep the capture
- use deterministic fallback for the demo if the object is not visually stable

## Hard Rules
- Do not commit generated GLBs/prefabs by default.
- Do not commit `Library/` artifacts.
- Do not commit API keys or signed URLs.
- Do not embed backend API keys in a Quest APK.
- Do not silently reuse an old object model for a different object.
- Do not block the room stylization demo on generated-object success.
