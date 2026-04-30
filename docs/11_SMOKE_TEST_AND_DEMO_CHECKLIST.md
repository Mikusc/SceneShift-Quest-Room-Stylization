# 11 Smoke Test and Demo Checklist

## Purpose

Use this checklist before recording, committing a demo milestone, or asking Codex to continue runtime work.

This checklist reflects the current `2026-04-30` project state: generic scaffold, built-in/custom Styles, surface-v3 room materials, continuous-wall door overlays, window cutouts/window-vista overlays, generated furniture, runtime dashboard, and Quest Link / Editor Play validation.

---

# 1. Before Play

Check:

- Active scene is `Assets/Scenes/MR_RoomStylization.unity`.
- Unity is not compiling.
- Console has no new blocking red errors.
- `AppRoot` contains Bootstrap, Perception, Stylization, Interaction, and RuntimeState groups.
- `MRUK` exists.
- `StylizedContentRoot` and `SurfaceOverrides` exist.
- `SceneShiftUISetDashboard` or the current stable runtime dashboard exists under `UI`.
- `GenerationQueueStatusService` exists.
- `GenerationJobWorldStatusOverlay` exists if checking furniture status cards.
- Theme/Profile data exists under `Assets/Data/ThemeProfiles/`.
- If testing custom style intent, set it before Play and confirm runtime summary shows extracted keywords.
- If testing DeepSeek style parsing, `DEEPSEEK_API_KEY` is visible to Unity or a local non-committed override is set.
- If testing APIMart image generation, `APIMART_API_KEY` is visible to Unity.
- If testing hosted upload, `SCENESHIFT_UPLOAD_TOKEN` is visible to Unity and upload uses `x-sceneshift-upload-token`.
- If testing Seed3D, `ARK_API_KEY` is visible to Unity.

Generated artifact rule:

- Do not commit generated models by default.
- Check `Assets/Generated/ThemeAssets/` only for local validation unless a specific demo artifact is intentionally being preserved.

---

# 2. Accepted Editor Noise

Known non-blocking noise can include:

- Meta/OpenXR simulator startup warnings.
- Meta XR optional project setup notice.
- Unity AI Account API warning.
- Existing obsolete API warnings for `FindObjectsSortMode`.
- Existing `CharacterController` name-collision log.

Any new compile error, exception loop, missing component error, or repeated runtime null reference is blocking.

---

# 3. Core Runtime Checks

In Play mode, verify:

- MRUK room becomes available.
- Active room is the expected office room if multiple Quest room scans exist.
- Runtime dashboard is visible and readable.
- Theme dropdown / Style label shows the intended Style.
- `StylizationPlanner` produces entries for major semantics.
- `GenerationQueueStatusService` reports surface/furniture queue state.
- Clean View toggles debug shells and status overlays as expected.
- Runtime dashboard `Rotate 90` can select the current generated furniture target and rotate only that placed proxy around world Y.
- Left-controller `Y` / keyboard `Y` toggles pure passthrough: all virtual surfaces, furniture, UI, rays, shells, and status cards disappear; pressing it again restores the previous virtual view.
- Room remains readable; user can still understand real furniture positions.

Fail if:

- Scene crashes on entering Play.
- Room never becomes ready.
- Generated/object/surface application repeats every frame.
- Clean View hides the stylized room itself.
- Runtime UI cannot be hidden or blocks the entire scene.
- Pure passthrough mode cannot restore virtual content with a second `Y` press.

---

# 4. Surface Aesthetic Checks

Surface path currently uses `surface_texture_v3_room_scale_openings`.

Check wall/floor/ceiling:

- Wall texture reads as broad room-scale material, not tiny wallpaper.
- Floor texture reads as walkable surface, not dense small tiles.
- Ceiling texture is subtle and does not make the room feel visually noisy.
- Wall/floor and wall/ceiling transitions look intentional.
- Wall corner gaps are reduced or hidden by trim strips.
- Opaque surfaces do not appear washed out or semi-transparent unless intentionally configured.

Check door:

- Door appears as a complete flat door or portal panel.
- Door is not just a thin rectangular frame.
- Door does not cut a hole in the wall override mesh.
- The door-host wall uses the same material, tiling, opacity, and seam logic as other walls.
- Door does not protrude into walkable space.
- Door style matches the active Style.

Check window:

- Window frame keeps an open center.
- Window frame does not block the view.
- Valid window openings can still be cut from the wall override so vista/frame content is not hidden behind the wall material.
- Window vista appears outside/behind the window, not pasted across the room wall.
- Window vista is opaque enough to read clearly.
- No duplicate small window/vista appears from a false-positive `WINDOW_FRAME` anchor.

If surface aesthetics fail, tune:

- `wallTextureTileSizeMeters`
- `floorTextureTileSizeMeters`
- `ceilingTextureTileSizeMeters`
- `openingTextureTileSizeMeters`
- `baseboardHeightMeters`
- `crownTrimHeightMeters`
- `cornerTrimWidthMeters`
- `doorPanelArchDepthRatio`
- generated surface prompt hints in theme profile assets

---

# 5. Furniture Capture Checks

Supported generated-object categories currently include:

- `TABLE`
- `STORAGE`
- `SCREEN`
- `COUCH` as MRUK label, mapped internally to `Seating`
- `BED`
- `LAMP`
- `PLANT`
- `OTHER`

Before capture:

- Look at the target object until HUD shows a stable candidate.
- Confirm target label, anchor id, score, and distance are plausible.
- Confirm the target is not accidentally another object behind it.
- If using capture reuse, confirm reuse is intended for this physical object and current Style.

After capture:

- A request JSON exists.
- A job JSON exists under `Library/GeneratedObjectJobs/`.
- Prompt artifact exists.
- Job status appears in the runtime panel or object status card.
- Existing placed generated furniture remains stable and is not overwritten by the new capture.

Fail if:

- Capture targets the wrong anchor.
- New capture changes an already accepted/working generated object.
- Multiple objects receive the same generated prefab without matching request identity.

---

# 6. Automated Generation Checks

APIMart image2:

- `CaptureReady` advances to image generation running/submitted.
- Stylized PNG appears under `Library/GeneratedObjectOutputs/`.
- Job advances to `StylizedImageReady`.

Hosted upload:

- Local PNG uploads to `https://www.mikusc.top/api/scene-shift/upload`.
- Request uses raw PNG body or supported multipart format.
- Header is `x-sceneshift-upload-token`.
- Job receives a valid hosted image URL.

Seed3D:

- Job advances to `ModelGenerationSubmitted`.
- Polling does not permanently stall.
- Job advances to `ModelReady`.
- Downloaded model is stored under the expected local model/generated-asset path.
- If Seed3D returns a zip package, only the real `.glb` is copied into `Assets/Generated/ThemeAssets/<requestId>/`.

Import:

- `GeneratedObjectModelImporter` imports the matching `ModelReady` job.
- Job advances to `Imported` or `NeedsReview`.
- `ImportedPrefabPath` points to a valid prefab.
- Import does not locally resize embedded GLB textures; use upstream low-quality/low-texture generation settings to control memory and asset size.

Placement:

- Runtime placement reports request-locked match.
- Placed generated furniture receives a `StylizedFurnitureInstance` marker so correction controls can target it by `ObjectId`.
- Aim at a generated furniture object until the panel `Rotate` row shows its object id, then press `Rotate 90`.
- The selected generated furniture rotates 90 degrees around world Y without moving room surfaces, other furniture, or MRUK shells.
- Rotation is a runtime correction for the current Play session; do not treat it as persisted room calibration until persistence is explicitly added.
- Generated object fits the MRUK scaffold within acceptable visual bounds.
- Bottom/contact surface is grounded.
- Rotation is plausible.
- It does not block walkable clearance.

---

# 7. UI / Interaction Checks

Runtime panel:

- Text is readable in headset and Game View.
- Buttons are selectable by the configured interaction mode.
- Bottom-row buttons are not outside the hit area.
- Theme dropdown or selector can be used without layout breaking.
- Clean View state is clearly visible.
- Object Status toggles world-space cards.
- Rotate 90 is selectable and does not overlap the other room-control buttons.
- Main panel hide/show input works if enabled.
- Pure passthrough input works independently of Clean View and does not reuse the furniture target-cycle input.
- Official Interaction SDK ray/poke interaction remains enabled.
- No duplicate `SceneShiftDashboardPointerRay` / custom fallback line ray appears.
- If the official ray is hidden by the UI backplate, treat it as an official ray material/depth/render-order issue rather than re-enabling the custom fallback ray.

Object status cards:

- Cards are near the relevant furniture request bounds.
- Cards are not enormous.
- Text stays inside the card.
- Cards can be hidden for clean demo capture.

Known issue:

- Direct dynamic instantiation of complex official UISet sample controls has caused layout problems. The stable fallback dashboard is preferred until a dedicated hand-authored UI scene/prefab is built.
- The dashboard may still contain hidden legacy/debug HUD suppression logic. Do not delete those components until the UISet panel, status cards, and clean-view flow have passed headset validation.

---

# 8. Demo Readiness Checklist

Before recording:

- Clear or understand all Console entries.
- Choose one room and one Style.
- Do not change inspector values mid-demo unless explaining a debug workflow.
- Confirm surface aesthetics from the intended user viewpoint.
- Confirm generated furniture is stable and request-matched.
- Decide whether MRUK shells are visible for explanation or hidden for clean view.
- Decide whether object status cards are visible or hidden.
- Keep generated-object branch framed as an optional enrichment unless accept/reject/reset is complete.

Demo should show:

- Room semantics.
- Style selection or custom style intent.
- Surface transformation across wall/floor/ceiling/openings.
- Window/vista treatment if a valid window exists.
- At least one grounded furniture replacement.
- Clean View.
- Queue/status visibility.
- Fallback behavior when a generated artifact is missing.

---

# 9. Minimal Pass Criteria

A smoke test passes if:

- No new blocking Console errors appear.
- MRUK room is readable.
- Runtime panel is usable.
- Surface stylization appears and is not visually noisy.
- Door/window behavior does not break spatial readability.
- Existing generated furniture remains stable.
- New capture does not overwrite unrelated generated furniture.
- Generated-object file artifacts are created when explicitly tested.
- The next failure point is documented.

If the test fails, fix only the smallest blocking issue before adding new features.
