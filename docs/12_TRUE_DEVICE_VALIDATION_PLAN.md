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
- future true-device camera/passthrough capture.

---

# 2. Tooling

Use Meta Quest Developer Hub or equivalent device tooling for:
- installing builds,
- launching the app,
- viewing logs,
- capturing device video,
- checking basic performance,
- exporting files when needed.

Use Unity editor tooling for:
- asset/scene wiring,
- non-Play Console inspection,
- build settings,
- package configuration.

Do not infer true-device camera access from simulator screenshots.
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

Artifacts to collect:
- request JSON,
- job JSON,
- prompt text,
- backend submission/result files if used.
- generated table status log when an imported prefab is present.

Pass criteria:
- generated-object branch remains optional, debuggable, and reversible.

---

# 6. Stage D — future true passthrough/camera capture

Goal:
- replace simulator-stage manual screenshots with a device-supported image source if project requirements and platform permissions allow it.

Do not start here until Stages A-C pass.

Validate before implementation:
- current Meta documentation for camera/passthrough frame access,
- required packages and permissions,
- device support,
- privacy/platform restrictions,
- whether the captured image has the same frame/camera relationship expected by registration.

Implementation target:
- keep the existing `BestViewCaptureSourceMode.DevicePassthroughReserved` contract,
- write to the same `GeneratedObjectRequest` shape,
- preserve `BestViewCameraPose`, crop metadata, object bounds, and best-view yaw.

Pass criteria:
- captured image is a real headset-supported source,
- request artifacts stay compatible with the existing backend protocol,
- deterministic fallback still works.

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
