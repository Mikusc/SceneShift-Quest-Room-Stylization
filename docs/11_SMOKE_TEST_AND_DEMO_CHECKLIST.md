# 11 Smoke Test and Demo Checklist

## Purpose
Use this checklist before recording, committing a demo milestone, or asking Codex to continue runtime work.

It is designed for the current MetaXRSimulator-first development path.
It does not replace later Quest device validation.

---

# 1. Before Play

Check:
- active scene is `Assets/Scenes/MR_RoomStylization.unity`
- Unity is not compiling
- Console has no new blocking errors
- `AppRoot` contains the expected Bootstrap, Perception, Stylization, Interaction, and RuntimeState groups
- `MRUK` is present
- `StylizedContentRoot` is present
- `StylizationDebugPanel` is visible or intentionally hidden
- theme assets exist under `Assets/Data/ThemeProfiles/`

If testing generated-object handoff:
- `BestViewCaptureService.externalScreenshotPath` is set, preferably after entering Play and taking a screenshot from the same camera pose that will be used before pressing `C`
- `BestViewCaptureService.captureSourceMode` is `ExternalScreenshot`
- `LocalGeneratedObjectBackendAdapter.processingMode` is the expected mode
- if testing generated table placement, the expected `.job.json` is `Imported` and has a valid `ImportedPrefabPath`
- if testing the current preferred validated generated table, confirm `Assets/Generated/ThemeAssets/table_18_20260425025836/table_18_20260425025836.generated_table_proxy.prefab` exists
- if testing generated or deterministic table placement, explicitly enable `AnchorThemeApplier.applyTableProxies`; the canonical scene may keep it disabled so the default Play view shows the MRUK shell

---

# 2. Accepted simulator/runtime noise

The following warnings have appeared as known simulator/editor noise in prior sessions and are not automatically blockers:
- Meta/OpenXR simulator function-pointer warnings
- `Local Dimming feature is not supported`
- `Metal: memoryless texture requires 2D texture`
- controller/helper warnings tied to simulator input setup
- runtime warnings from MetaXRSimulator startup that do not stop room loading

Any new red Console error is blocking until inspected.

---

# 3. Core Phase 1 runtime checks

In Play mode, verify:
- MRUK room becomes available
- walls/floor/ceiling debug geometry or surfaces align with the simulated room
- HUD shows room/bootstrap state
- current theme is resolved
- `StylizationPlanner` produces entries for major semantics
- wall/floor/ceiling stylization is visible
- if table proxy placement is enabled, table proxy appears once and is not duplicated repeatedly
- if table proxy placement is enabled, table proxy footprint remains near the real/MRUK table scaffold
- if table proxy placement is disabled, the MRUK/original table shell remains visible and no virtual table replacement is expected
- room remains readable; user can still understand real furniture positions

Fail if:
- scene crashes on entering Play
- room never becomes ready
- material/proxy application repeats every frame
- proxy placement blocks obvious walkable clearance
- generated-object branch prevents deterministic stylization from appearing

---

# 4. Debug and capture toggles

Expected behavior for capture-friendly testing:
- HUD stays visible unless explicitly disabled
- MRUK blue debug boxes can be hidden when taking a clean manual screenshot
- virtual proxy/generated candidates can be hidden when taking an external reference screenshot
- toggles should not destroy scene objects or lose planner/applier state

If a toggle hides the wrong thing, document it as a demo bug rather than editing generated artifacts by hand.

---

# 5. Generated-object checks

## Local mock mode
Expected after pressing `C`:
- `Library/BestViewCaptures/*.request.json`
- `Library/GeneratedObjectJobs/*.job.json`
- `Library/GeneratedObjectJobs/*.prompt.txt`
- `Library/GeneratedObjectOutputs/*.stylized.png`
- `Library/GeneratedObjectOutputs/*.result.json`
- job state reaches `StylizedImageReady`

## External file protocol mode
Expected after pressing `C`:
- `Library/BestViewCaptures/*.request.json`
- `Library/GeneratedObjectJobs/*.job.json`
- `Library/GeneratedObjectJobs/*.prompt.txt`
- `Library/GeneratedObjectBackendInbox/*.submission.json`
- `Library/GeneratedObjectBackendInbox/*.result.template.json`
- job state reaches `BackendSubmitted`

Before pressing `C`, the external screenshot must match the current Play camera pose. If Play has reset the user position, retake the screenshot in that Play session, paste the new absolute path into `BestViewCaptureService.externalScreenshotPath`, and avoid moving before pressing `C`.

After manual worker output is dropped:
- requested output image exists
- requested result JSON exists
- job state reaches `StylizedImageReady`

## Manual image-to-3D/import path
Expected after a manual Seed3D worker run:
- `StylizedImagePath` points to an isolated transparent object PNG
- `GeneratedModelPath` points to a copied GLB under `Assets/Generated/ThemeAssets/<requestId>/`
- job state reaches `ModelReady`
- `GeneratedObjectModelImporter` imports the model
- job state reaches `Imported`
- `ImportedPrefabPath` points to `Assets/Generated/ThemeAssets/<requestId>/<requestId>.generated_table_proxy.prefab`
- if Seed3D returns a zip package, the zip is kept under `Library/GeneratedObjectModels/<requestId>/downloaded_package/` and only the extracted `.glb` is copied into `Assets/Generated/ThemeAssets/<requestId>/`

## Runtime generated table placement
The canonical scene can keep table proxy placement disabled while generation work is in progress. Before running this section, enable `AnchorThemeApplier.applyTableProxies` for the validation run.

Expected in Play mode when the generated prefab is selected:
- `AnchorThemeApplier` `Table Status` includes `source=generated_import`
- `prefab` names the generated table prefab
- `failure=none`
- `fit` includes `target`, `source`, `scale`, and `bottomDelta`
- if the generated table's local long axis differs from the MRUK target long axis, `fit` also includes `axis=rotated90(...)`
- `bottomDelta` is `0m` or close enough to be visually grounded

Fail if:
- the generated table obviously floats
- the generated table is visibly much larger/smaller than the MRUK scaffold
- the deterministic fallback disappears when the generated prefab is unavailable
- generated furniture blocks a path or obscures the real table in a way the user cannot understand

---

# 6. Demo readiness checklist

Before recording:
- clear or understand all Console entries
- choose one theme and keep it fixed
- run the scene once without changing inspector values mid-demo
- verify table proxy alignment from the intended camera angle
- if using the generated table, verify the current table is not floating and that its footprint is acceptable from the intended Simulator/user camera angle
- if staying on the MRUK shell, confirm table proxy placement is disabled and do not treat the missing virtual table as a failure
- decide whether MRUK debug overlay should be visible for explanation or hidden for clean visuals
- keep generated-object branch as an optional artifact demo unless generated-proxy import/registration plus review/reset behavior is complete

Demo should show:
- room semantics,
- theme/planner decision,
- visible surface stylization,
- one grounded furniture proxy,
- clear fallback behavior,
- optional generated-object artifacts if relevant.

---

# 7. Minimal pass criteria

A smoke test passes if:
- no new blocking Console errors appear,
- MRUK room is readable,
- deterministic stylization appears,
- table proxy does not obviously drift or duplicate,
- generated-object file artifacts are created when explicitly tested,
- generated table placement reports a grounded fit if the imported generated prefab is enabled,
- the next failure point is documented.

If the test fails, fix only the smallest blocking issue before adding new features.
