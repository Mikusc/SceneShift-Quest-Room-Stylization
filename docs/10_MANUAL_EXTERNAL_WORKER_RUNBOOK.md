# 10 Manual External Worker Runbook

## Purpose
This runbook explains how to use the current `ExternalFileProtocol` flow without a real backend service.

The goal is to manually simulate this generated-object branch:

`GeneratedObjectRequest -> prompt + submission JSON -> external image worker -> stylized image + result JSON -> StylizedImageReady -> Seed3D model -> Imported prefab -> runtime generated table proxy`

This is only for the optional generated-object side branch.
The deterministic room stylization and table proxy path must remain the visible fallback.

Current validated request:
- `table_18_20260424071758`
- source category: `TABLE_18`
- theme: `future_research_lab`
- current stylized image: `Library/GeneratedObjectOutputs/table_18_20260424071758.stylized.png`
- current generated GLB: `Assets/Generated/ThemeAssets/table_18_20260424071758/table_18_20260424071758.seed3d.medium.glb`
- current generated prefab: `Assets/Generated/ThemeAssets/table_18_20260424071758/table_18_20260424071758.generated_table_proxy.prefab`

---

# 1. Preconditions

Before testing, confirm:
- active scene is `Assets/Scenes/MR_RoomStylization.unity`
- `BestViewCaptureService.captureSourceMode` is `ExternalScreenshot`
- `BestViewCaptureService.externalScreenshotPath` points to a manually captured simulator/room screenshot
- `LocalGeneratedObjectBackendAdapter.processingMode` is `ExternalFileProtocol`
- generated table runtime placement is opt-in: the canonical scene may keep `AnchorThemeApplier.applyTableProxies` disabled so the default Play view shows the MRUK shell
- Unity is not compiling
- Console has no new blocking errors

For Codex sessions:
- do not take screenshots after Play starts
- only read Unity Console/logs if runtime validation is needed

---

# 2. Capture one request

In Play mode:
1. wait until MRUK room state and HUD are stable
2. make sure a `TABLE` candidate is visible/available
3. press `C` once
4. wait for the coordinator/backend adapter to process the request

Expected files:
- `Library/BestViewCaptures/*.request.json`
- `Library/GeneratedObjectJobs/*.job.json`
- `Library/GeneratedObjectJobs/*.prompt.txt`
- `Library/GeneratedObjectBackendInbox/*.submission.json`
- `Library/GeneratedObjectBackendInbox/*.result.template.json`

The exact filenames should share the same request id.

---

# 3. Read the submission file

Open the newest:
- `Library/GeneratedObjectBackendInbox/*.submission.json`

Important fields:
- `PromptArtifactPath`
- `SourceInputImagePath`
- `SourceRequestPath`
- `RequestedOutputImagePath`
- `RequestedResultPath`
- `ResultTemplatePath`

Meaning:
- `PromptArtifactPath` is the text prompt to give to the image model.
- `SourceInputImagePath` is the image to upload as the reference image.
- `RequestedOutputImagePath` is where the stylized output image must be saved.
- `RequestedResultPath` is where the completed result JSON must be saved.
- `ResultTemplatePath` is a prefilled result JSON template to copy/edit.
- The prompt treats `SourceInputImagePath` as a reference image, not as the final output canvas.
- The expected stylized output is a single isolated object PNG with alpha, suitable for image-to-3D.

---

# 4. Manual image-worker flow

Use an external image generator manually:
1. open `PromptArtifactPath`
2. upload `SourceInputImagePath`
3. generate a single isolated stylized version of the table/object
4. preserve object role, rough silhouette, support surface, proportions, and yaw/orientation
5. remove all room background, chairs, floor, walls, tabletop clutter, and other source-scene objects
6. save the final transparent PNG exactly to `RequestedOutputImagePath`
7. if the image model cannot produce native alpha, generate on a flat chroma-key background, remove that key locally, then save the alpha PNG to `RequestedOutputImagePath`
8. copy `ResultTemplatePath` to `RequestedResultPath`
9. in the copied result file, confirm:
   - `OutputImagePath` matches `RequestedOutputImagePath`
   - `OutputState` is `StylizedImageReady`
   - `PromptArtifactConsumed` is `true`
   - `BackendAdapterName` identifies the manual/external worker

Do not save the generated image only into `photos/` or Desktop.
The adapter watches the paths listed in the submission JSON.

---

# 5. Expected Unity-side image-worker result

With Play still running, the adapter should detect `RequestedResultPath`.

Expected job state:
- from `BackendSubmitted`
- to `StylizedImageReady`

Expected updated job fields:
- `BackendResultPath`
- `StylizedImagePath`
- `StylizedImageUrl` when the external worker also hosts the image for Seed3D `image_url`
- `BackendAdapterName`
- `StatusNote`
- `UpdatedAtIsoUtc`

After this image-worker stage only, the output is still just a candidate image artifact.
Continue through the Seed3D and Unity import sections below before expecting a generated 3D table in the scene.

---

# 6. Troubleshooting

## No `.submission.json`
Check:
- adapter is in `ExternalFileProtocol`
- coordinator has created a `.job.json`
- request state is `CaptureReady`
- Console has no compile/runtime errors

## `.submission.json` exists but no state change
Check:
- `RequestedResultPath` exists
- result JSON is valid
- `OutputImagePath` exists
- `OutputState` is `StylizedImageReady`
- request id in result matches request id in job

## Generated image looks like a new room instead of one object
The prompt or image model ignored the preservation constraint.
Regenerate with stricter wording:
- keep the same object,
- keep the same role,
- keep the same proportions,
- do not generate a full room,
- produce a clean object-centric stylized reference.

The current prompt builder version for new requests is `roomify_image_asset_v2`.
It explicitly asks for:
- one isolated object asset,
- transparent alpha output,
- no room background,
- no chairs, floor, walls, shelves, board games, tabletop props, or clutter.

If native alpha is unavailable, generate on a flat `#00ff00` chroma-key background, remove that key locally, and only then save the final alpha PNG to `RequestedOutputImagePath`.

## Crop or screenshot framing is wrong
For the current simulator-stage path, use the full external screenshot as the backend input.
Treat `NormalizedCropRect` as metadata, not as a mandatory crop.

---

# 7. Manual image-to-3D worker flow

Use this only after the stylized PNG is an isolated object image.

Current tested backend:
- Volcengine / Ark Seed3D 2.0
- model id: `doubao-seed3d-2-0-260328`
- create task endpoint: `POST https://ark.cn-beijing.volces.com/api/v3/contents/generations/tasks`
- query task endpoint: `GET https://ark.cn-beijing.volces.com/api/v3/contents/generations/tasks/{id}`
- tested text command: `--subdivisionlevel medium --fileformat glb`

Automated adapter option:
- `Assets/Scripts/Perception/HostedImageUploadBridge.cs`
  - optional helper for uploading local stylized PNG files to an operator-configured hosting endpoint
  - writes `StylizedImageUrl` back to the job without changing the job state
  - reads optional upload auth token from a configured environment variable and does not write it to job JSON
- `Assets/Scripts/Perception/Seed3DBackendAdapter.cs`
- reads the API key from environment variable `ARK_API_KEY`
- default endpoint/model/subdivision/fileformat match the tested Seed3D 2.0 values above
- processes only jobs in `StylizedImageReady`
- requires `StylizedImageUrl`, or `StylizedImagePath`, to be a public `http(s)` `image_url`
- does not upload local files; local `Library/.../*.png` paths remain waiting for a hosted URL unless a separate upload bridge writes `StylizedImageUrl`
- writes request/result metadata under `Library/GeneratedObjectModels/<requestId>/`
- downloads the returned model to `Assets/Generated/ThemeAssets/<requestId>/<requestId>.seed3d.generated.glb` when `fileformat=glb`, then sets the job to `ModelReady`

Hard rule:
- do not write API keys, bearer tokens, or signed `file_url` values into repository files or docs
- if a backend response JSON contains a signed URL, keep it under `Library/GeneratedObjectModels/` only and do not commit it

Expected worker steps:
1. use the transparent `*.stylized.png` as the image input
2. request GLB output
3. download and unzip the backend package under `Library/GeneratedObjectModels/<requestId>/`
4. copy only the Unity-ready GLB into `Assets/Generated/ThemeAssets/<requestId>/`
5. set the matching `.job.json` to `ModelReady`
6. set `GeneratedModelPath` to the copied GLB path
7. set `BackendTransformId` to a descriptive value such as `manual_gpt_image_v1+seed3d_2_0_260328_medium_glb`

If using `Seed3DBackendAdapter`, the adapter first records `ModelGenerationSubmitted` with the Ark task id, can resume polling after interruption, and then advances the job to `ModelReady` after the model download completes. The adapter keeps `ARK_API_KEY` out of logs and job JSON.

For the current validated table, the copied GLB path is:
- `Assets/Generated/ThemeAssets/table_18_20260424071758/table_18_20260424071758.seed3d.medium.glb`

---

# 8. Unity import flow

The editor importer is:
- `Assets/Scripts/Editor/GeneratedObjectModelImporter.cs`

It runs on editor load/refresh and is also available from:
- `SceneShift/Generated Objects/Import Ready Model Jobs`

Expected behavior for a `ModelReady` job:
- import the GLB through `AssetDatabase`
- instantiate it under a wrapper
- remove imported colliders
- normalize the wrapper to a centered bottom pivot
- save `Assets/Generated/ThemeAssets/<requestId>/<requestId>.generated_table_proxy.prefab`
- update the job to `Imported`
- write `ImportedPrefabPath` and `ImportedBounds`

For the current validated table, the imported prefab path is:
- `Assets/Generated/ThemeAssets/table_18_20260424071758/table_18_20260424071758.generated_table_proxy.prefab`

---

# 9. Runtime placement check

In Editor/Simulator, `AnchorThemeApplier` can prefer imported generated table prefabs.
If the canonical scene is currently set to the MRUK shell, enable `AnchorThemeApplier.applyTableProxies` for this check and decide separately whether to hide original MRUK volume visuals.

Expected `Table Status` indicators:
- `source=generated_import`
- `prefab=<requestId>.generated_table_proxy`
- `failure=none`
- `fit=target=..., source=..., scale=..., bottomDelta=...`

The latest inspected run for the current table reported:
- `source=generated_import`
- `fit=target=2.02x0.782x0.937, source=2.159x0.761x1.33, scale=0.935x1.027x0.704, bottomDelta=0m`

Interpretation:
- `bottomDelta=0m` means the generated prefab bounds bottom is aligned to the MRUK table scaffold bottom in the applier's fitting space
- this does not replace visual review; the generated table still needs user-facing camera validation and later correction/approval UI

---

# 10. Hard rules
- The deterministic proxy remains the fallback.
- Never block room stylization while waiting for generated assets.
- Do not treat a stylized 2D image as a scene-ready 3D asset.
- Do not silently apply generated collision-sensitive furniture without later review/correction support.
- Keep all request/job/result artifacts for debugging and reproducibility.
- Never commit API keys, bearer tokens, signed backend URLs, or downloaded backend result URLs.
