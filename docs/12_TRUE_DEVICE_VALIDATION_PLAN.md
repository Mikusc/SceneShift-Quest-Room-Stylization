# 12 True Device Validation Plan

## Purpose
This document separates what the simulator can prove from what must be validated on a Quest headset.

The current project uses MetaXRSimulator heavily, but simulator success does not prove:
- real MRUK room capture behavior,
- true passthrough/camera-frame access,
- device performance,
- headset permission behavior,
- user comfort and correction usability.

---

# 1. Simulator vs headset responsibilities

## MetaXRSimulator is useful for
- editor iteration,
- MRUK-like room debugging,
- planner/applier logic,
- proxy fitting sanity checks,
- generated-object file protocol tests,
- Console hygiene before a build.

## Quest device validation is required for
- real room setup/loading,
- real passthrough presentation,
- headset input/controller behavior,
- performance and thermal behavior,
- build/install stability,
- true-device camera/passthrough capture.

---

# 2. Tooling

## Meta Quest Developer Hub / ADB
Use Meta Quest Developer Hub or equivalent device tooling for:
- installing builds,
- launching the app,
- viewing device logs / logcat output,
- capturing device video or screenshots for human review,
- checking basic performance,
- exporting files when needed,
- verifying that saved capture artifacts exist under the app's persistent data area.

MQDH screenshots and videos are debugging evidence. They are not the application-side image source for the generated-object pipeline, because the running Unity app cannot consume an MQDH desktop capture as its own input.

## Immersive Debugger
Use Meta XR Immersive Debugger for in-headset inspection when Link/Editor Play debugging is unavailable:
- view Console messages while wearing the headset,
- inspect selected runtime components,
- expose watch values such as current room state, selected anchor id, latest capture path, latest upload URL, and last failure reason,
- expose tweakable values such as generated table safety scale, selected anchor index, and correction deltas,
- invoke no-argument debug actions such as capture frame, save last capture, upload last capture, reapply table proxy, or reset to deterministic proxy.

Recommended debug members for the device capture/generated-table path:
```text
CapturePassthroughFrame()
SaveLastCapture()
UploadLastCapture()
ReapplyTableProxy()
ResetToDeterministicTable()
selectedAnchorIndex
lastCapturePath
lastCaptureMetadataPath
lastUploadUrl
lastError
tableFootprintSafetyScale
```

Use Unity editor tooling for:
- asset/scene wiring,
- non-Play Console inspection,
- build settings,
- package configuration.

Do not infer true-device camera access from simulator screenshots or MQDH screenshots.
Before implementing real passthrough/camera capture, verify the current Meta SDK, Quest OS, permission, and platform-policy requirements for the target device.

---

# 3. Stage A — deterministic room stylization on device

Goal:
- prove the core Phase 1 path runs on headset without the generated-object branch.

Validate:
- app launches,
- MRUK room data is available or can be loaded,
- walls/floor/ceiling stylization appears,
- table proxy appears once,
- fallback path works if generated-object services are disabled,
- no blocking device log errors.

Artifacts to collect:
- device log excerpt,
- short capture video,
- notes on room alignment and proxy drift.

Pass criteria:
- user can recognize the room,
- stylization is visible,
- real furniture readability is preserved.

---

# 4. Stage B — canonical room semantics

Goal:
- confirm the known library discussion room gives stable enough semantics for the prototype.

Validate:
- floor, walls, ceiling are detected/readable,
- table semantics or equivalent anchor path is available,
- screen/storage/seating labels are present or have documented fallback,
- bounds are stable enough for planner/applier use.

Artifacts to collect:
- semantic HUD summary,
- room snapshot JSON if available,
- notes on missing/incorrect labels.

Pass criteria:
- the deterministic planner can produce a useful plan without manual scene edits.

If MRUK does not label the real table as `TABLE`, use a manual semantic override before adding heavier perception:
- override by anchor index/name/id,
- set semantic label to `table`,
- set function tag to `support_surface`,
- mark the object collision-sensitive,
- show `source=manual_override` in debug UI.

This is acceptable for Phase 1 because it is a user-editability/correction mechanism, not a claim that automatic perception succeeded.

---

# 5. Stage C — generated-object file artifacts and imported prefab on device

Goal:
- verify the file protocol can still produce/debug artifacts in a device-oriented workflow, and that an already imported generated prefab remains an optional fallback-safe placement path.

Validate:
- pressing the capture trigger or mapped debug input creates request/job/prompt artifacts,
- logs clearly report artifact paths,
- artifacts can be pulled/exported if needed,
- an imported generated table prefab can be present without blocking deterministic stylization,
- generated table placement can be disabled, rejected, or ignored if visual fit is not acceptable,
- generated-object failure does not block deterministic stylization.
- old Simulator/generated-table jobs are not selected for a new device room unless they match the active capture request.

Artifacts to collect:
- request JSON,
- job JSON,
- prompt text,
- backend submission/result files if used.
- generated table status log when an imported prefab is present.

Pass criteria:
- generated-object branch remains optional, debuggable, and reversible.

---

# 6. Stage D - Quest Link / true passthrough-camera capture probe

Goal:
- replace simulator-stage manual screenshots with a device-supported image source if project requirements and platform permissions allow it.

Current implementation status:
- `DevicePassthroughCaptureService` is attached under `AppRoot/Perception` in the canonical scene.
- The service uses `Meta.XR.PassthroughCameraAccess`, `horizonos.permission.HEADSET_CAMERA`, and the existing `BestViewCaptureSourceMode.DevicePassthroughReserved` contract.
- It writes full-frame/cropped PNG, capture metadata, `GeneratedObjectRequest`, prompt text, and `.job.json` artifacts in the same generated-object pipeline shape.
- `DevicePassthroughCaptureHud` creates a head-locked status panel in Play mode that shows PCA readiness, best anchor, best-view score, distance, viewport/crop, input hint, and last capture/job status.
- Capture can be triggered with keyboard `P` or the right-controller primary button.
- This is compile/scene-wiring validated only. It is not yet a passed device result.

Validate before treating this as working:
- compatible Quest 3 / Quest 3S headset,
- compatible Meta Horizon Link / headset OS versions for Editor Link PCA,
- camera permission prompt and grant behavior,
- whether `PassthroughCameraAccess.IsPlaying` becomes true,
- whether the head-locked HUD is readable and stable enough while the user searches for the best view,
- whether the displayed best-view score responds usefully to viewpoint changes,
- whether right-controller primary-button capture works without watching Unity Game View,
- whether the captured image has the same frame/camera relationship expected by registration.
- whether generated replacement only loads an imported prefab matching the active PCA request id/object id and otherwise falls back to deterministic proxy.

Implementation target:
- keep the existing `BestViewCaptureSourceMode.DevicePassthroughReserved` contract,
- use `DevicePassthroughCaptureService` as the device/Link capture component,
- use a headset-supported passthrough/camera API path rather than Unity `ScreenCapture` or MQDH screenshots,
- save capture artifacts under `Application.persistentDataPath`,
- write to the same `GeneratedObjectRequest` shape,
- preserve `BestViewCameraPose`, crop metadata, object bounds, and best-view yaw.

Expected saved artifacts:
- `*.pca.png`
- `*.pca.crop.png`
- `*.pca.json`
- `*.request.json`
- `GeneratedObjectJobs/*.job.json`
- `GeneratedObjectJobs/*.prompt.txt`

Expected metadata:
- timestamp,
- camera pose,
- camera intrinsics if available,
- image resolution,
- selected MRUK anchor id/name/index,
- anchor semantic source (`mruk`, `manual_override`, or later `fusion`),
- table/world bounds,
- projected crop rect,
- any permission or API availability status.

Pass criteria:
- captured image is a real headset-supported source,
- request artifacts stay compatible with the existing backend protocol,
- deterministic fallback still works.

Debugging sequence for MacBook-only development without Link passthrough:
1. build and install the APK with MQDH or ADB,
2. launch the app on Quest,
3. use Immersive Debugger, in-app debug UI, or the mapped capture key path to trigger `CapturePassthroughFrame`,
4. use MQDH cast/screenshot only to document what the user saw,
5. pull `capture.png` and `capture.metadata.json` from device storage,
6. run the current workstation-side image stylization and Seed3D flow,
7. import the resulting prefab in Unity,
8. build again with the generated prefab or test runtime loading later.

---

# 7. Stage E — performance and demo recording

Goal:
- make the demo recordable and repeatable.

Validate:
- stable frame rate for the demo path,
- no repeated allocations or proxy duplication,
- no runaway file writes,
- HUD/debug overlays can be shown or hidden appropriately,
- controller/hand input is understandable to the viewer.

Artifacts to collect:
- short device recording,
- device log,
- notes on FPS/performance if available,
- list of accepted warnings.

Pass criteria:
- one complete demo run can be repeated without editor intervention.

---

# 8. Main risks
- MRUK simulator labels may differ from true room labels.
- Real room scan/update state may be stale or incomplete.
- Passthrough/camera access may be permission-limited or unavailable for the desired capture path.
- Generated assets may drift in scale, orientation, or silhouette.
- Device performance may expose issues hidden by editor testing.
- Unity/editor crash recovery artifacts can obscure whether a failure is project code or tooling instability.

---

# 9. Smallest next true-device task

Build and run the deterministic stylization path on Quest with generated-object backend disabled or left optional.

Only after that passes, test whether one `TABLE` request can produce the same request/job/prompt artifacts on a device-oriented run.
