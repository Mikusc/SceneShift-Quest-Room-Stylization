# 12 True Device Validation Plan

## Purpose
This document separates what Editor/Simulator can prove from what must be validated with a Quest headset in the real UNNC IEB office room.

The project has been tested heavily through Unity Editor Play and Quest Link-style workflows, but final validation still needs clear evidence for:
- real MRUK room data
- real passthrough presentation
- headset/controller input behavior
- native passthrough-camera capture when supported
- performance and comfort
- repeatable demo flow in the known office room

## Simulator vs Headset Responsibilities

MetaXRSimulator is useful for:
- editor iteration
- MRUK-like room debugging
- planner/applier logic
- surface material and opening logic
- generated-object job orchestration
- Console hygiene before headset testing

Quest Link / headset validation is required for:
- actual room scan/loading behavior
- controller and headset input
- HUD/dashboard readability
- clean view behavior
- real performance
- native passthrough-camera capture support
- user-facing correction usability

Do not infer true-device PCA success from desktop screenshots or MQDH captures.

## Tooling

Use Unity Editor for:
- scene wiring
- console inspection
- import of generated GLBs/prefabs
- backend job file inspection

Use Meta Quest Developer Hub / ADB for:
- installing builds when not using Link
- viewing device logs
- recording device video
- pulling persistent app files
- basic performance checks

Use in-headset UI/HUD for:
- target selection feedback
- generation status
- clean view
- object status cards
- capture trigger confirmation
- style/cache/job state inspection

## Stage A - Room Load And Base Stylization

Goal:
- prove the canonical UNNC IEB office room can load and receive room stylization.

Validate:
- app launches or Editor Play starts without blocking errors
- MRUK room ready state appears
- active room selection chooses the intended office when multiple rooms exist
- semantic counts are visible
- walls/floor/ceiling stylization appears
- door/window/window-vista overlays appear where appropriate
- clean view can hide MRUK shells and debug/status cards

Pass criteria:
- the user can recognize the real room
- stylization is spatially aligned
- real-world boundaries remain readable
- no blocking Console/device-log errors appear

## Stage B - Furniture Semantics

Goal:
- confirm MRUK furniture labels are good enough for the demo pipeline.

Validate supported semantics:
- `TABLE`
- `STORAGE`
- `SCREEN`
- `COUCH` as MRUK label, shown/handled internally as `Seating`
- `BED`
- `LAMP`
- `PLANT`
- `OTHER`

Pass criteria:
- targetable objects show a stable category/id/score in the HUD or dashboard
- `OTHER` objects can still be captured with the generic prompt path
- missing or wrong labels are documented rather than hidden

If a key office object is mislabeled, use a manual override/correction path for Phase 1 rather than adding heavier perception infrastructure immediately.

## Stage C - Surface / Door / Window / Vista Validation

Goal:
- prove the surface pipeline is visually acceptable in the real office.

Validate:
- wall/floor/ceiling textures use room-scale repeats, not tiny dense wallpaper
- wall seams and wall/floor/ceiling boundaries are acceptable
- trim does not overpower styles like `Arcane Knowledge Chamber`
- doors render as full door/portal panels without cutting a large hole through the wall unless explicitly intended
- valid windows keep an open center and show the vista slightly outside the room
- mistaken small window/frame anchors can be hidden or ignored

Pass criteria:
- the room looks coherent from normal user viewpoints
- no obvious gaps expose the passthrough background where a wall/floor boundary should exist
- window scenery does not cover unrelated wall regions or appear as duplicate small panels

## Stage D - Generated Furniture Capture

Goal:
- prove a headset-facing user can capture one or more objects and let the backend chain progress.

Validate:
- Auto Target picks the object the user is looking at
- capture trigger works without watching Unity Inspector
- generated request/job/prompt files are created
- APIMart image generation starts when `APIMART_API_KEY` is visible
- upload bridge writes a public `StylizedImageUrl` when needed
- Seed3D job enters `ModelGenerationSubmitted`
- imported prefabs can be placed only for matching request/object/style
- multiple generated furniture objects can coexist

Pass criteria:
- capturing a new object does not disturb already placed generated furniture
- old captures from another room are not silently reused
- failed/running jobs remain visible in status output

## Stage E - Correction And Clean View

Goal:
- confirm the user can inspect and temporarily correct generated placements.

Validate:
- object status cards can be shown/hidden
- MRUK shells can be shown/hidden
- clean view leaves only stylized room content and the control panel
- `Rotate 90` changes the selected generated object without breaking bounds fit
- left-hand passthrough-only toggle hides all virtual content and restores it on the next press

Pass criteria:
- the user can switch between debugging view, clean stylized view, and pure passthrough view
- generated object corrections are understandable
- no UI button overlap blocks the critical actions

Still missing before demo-final:
- accept generated object
- reject generated object
- reset to deterministic fallback
- persistent correction record
- fine nudge/scale correction

## Stage F - Performance And Stability

Goal:
- make the demo repeatable and recordable.

Validate:
- frame rate is acceptable in the target office
- no runaway duplicate GLBs or texture allocations occur when switching styles
- no repeated generation starts unless requested
- generated model cleanup/archive tools can remove failed or stale jobs
- Unity/editor crashes do not corrupt the canonical scene
- generated assets are not accidentally committed

Pass criteria:
- one complete demo run can be repeated after restarting Unity
- the project returns to a known state after stopping Play
- logs explain running/failed jobs clearly

## Native PCA Caveat

`DevicePassthroughCaptureService` is the intended Quest Link/headset capture path, but PCA availability depends on:
- headset model
- Horizon OS version
- Meta Horizon Link version
- SDK/package support
- camera permission behavior
- platform policy

Quest 3 / Quest 3S are the expected best-supported targets for current Meta PCA documentation. Quest Pro may behave differently and must be validated empirically. If PCA is unavailable, use simulator/external screenshot fallback for backend debugging and document that true-device capture is not passed.

## Evidence To Collect

For each serious validation run, collect:
- date and hardware
- room id/name shown in UI
- active Style
- semantic counts
- surface cache/job status
- furniture job counts
- short headset recording
- Console/device-log excerpt for failures
- list of objects captured and whether they placed correctly

## Current Known Risks
- Multiple saved MRUK rooms can cause the wrong room to become active if active-room selection fails.
- Native PCA can fail even if Editor Play works.
- Generated furniture can drift in scale/orientation/silhouette.
- `OTHER` captures can generate visually plausible but semantically unsafe objects.
- Direct dynamic official UISet sample control instantiation has caused layout problems; the current dashboard prioritizes stable interaction.
- Texture/model memory can spike if too many generated assets remain loaded.

## Smallest Next Validation Task
Run one complete Quest Link / Editor Play pass in the UNNC IEB office:
1. select or confirm the intended room
2. apply one Style
3. verify walls/floor/ceiling/door/window/vista
4. capture one supported object
5. confirm the job progresses to imported placement
6. use clean view
7. use passthrough-only toggle
8. record remaining visual/correction issues
