# 08 Progress Status

## Purpose
This file is the manual progress tracker for the current vertical slice.
Update it after each meaningful implementation step.

## Snapshot
- Last updated: `2026-04-22`
- Current priority: `Phase 1 — room stylization`
- Canonical scene: `Assets/Scenes/MR_RoomStylization.unity`
- Primary development validation path: `MetaXRSimulator`

## Milestone Status
| Milestone | Status | Notes |
| --- | --- | --- |
| M0 — Project foundation audit | Partial | Project audit and canonical scene exist; agreed folder structure is only partially normalized. |
| M1 — MRUK semantic debug layer | Mostly done | Simulator path works, room bootstrap exists, semantic HUD exists, and a thin best-view capture request path now exists. Object-centric crop/export refinement remains open. |
| M2 — Visible object perception fusion | Not started | No `ObservedObjectCollector` or `SemanticFusionService` yet. |
| M3 — Theme system and stylization planning | Mostly done | `ThemeProfile`, `StylizationPlan`, `StylizationPlanner`, and debug HUD are in place. |
| M4 — Stylization application | In progress | Surface stylization and room mood are working. Table proxy replacement is wired but still being refined. |
| M5 — Manual correction mode | Not started | No inspect / nudge / reset correction flow yet. |
| M6 — Demo readiness | Not started | No dedicated demo UI, smoke-test panel, or capture toggles yet. |
| M7 — NPC preparation | Not started | Intentionally deferred until Phase 1 is stable. |

## Working Features
- `RoomSemanticBootstrap` initializes MRUK room semantics for the canonical scene.
- `StylizationDebugPanel` shows room/theme/planner/applier state in-scene.
- Theme selection is available through `ThemeIntentController`.
- `StylizationPlanner` generates deterministic mappings for wall, floor, ceiling, table, screen, storage, and seating.
- `AnchorThemeApplier` visibly stylizes wall / floor / ceiling surfaces.
- `RoomMoodController` provides theme-linked mood changes.
- Initial table proxy spawning is connected from planner to applier.
- `BestViewCaptureService` tracks the best visible `TABLE` anchor in Play mode and writes full-frame screenshot + metadata requests to `Library/BestViewCaptures/`.

## In Progress
- Table proxy alignment and replacement readability in `AnchorThemeApplier`.
- Making the table replacement visually distinct from MRUK debug-like room surfaces.
- Refining `BestViewCaptureService` from a full-frame request capture into a more object-centric crop/export path.
- Cleaning up crash-related transient workspace state before the next commit.

## Known Gaps
- No perception fusion layer yet for `Image Segmentation` or object detection.
- No manual correction workflow yet.
- No reset / reapply / clean theme-switch flow documented as finished.
- No object-centric best-view crop/export contract yet; the current capture path is still whole-frame and MRUK-anchor-driven.
- No complete demo-ready UI path outside inspector/debug HUD usage.

## Latest Stable Verification
Last stable manual/runtime checks before this document update confirmed:
- `MR_RoomStylization` loads through the `MetaXRSimulator` path.
- MRUK room semantics and theme/planner/applier summaries are visible through the debug HUD.
- Wall / floor / ceiling stylization is visible at runtime.
- The first table proxy path resolves a prefab and spawns a proxy root.
- After reloading the scene with `BestViewCaptureService` wired into `Perception`, entering Play introduced no new project errors; only the previously accepted six Meta/OpenXR simulator warnings remained.
- Previously introduced `Locomotor`, `Local Dimming`, `Metal memoryless texture`, and controller-helper warnings were reduced; accepted simulator/runtime noise may still remain.

## Current Local Workspace State
The workspace currently contains uncommitted local state that should be treated as in-progress rather than final:
- `Assets/Scripts/Stylization/AnchorThemeApplier.cs`
  Table proxy fitting/debugging changes are present locally.
- `ProjectSettings/ProjectSettings.asset`
  Unity touched this file during crash/reopen recovery.
- `Assets/_Recovery/0 (9).unity` and `.meta`
  Unity recovery artifacts created after the recent editor crash.

## Biggest Technical Risks
- Unity editor instability when inspecting runtime objects during Play in this project/runtime combination.
- The current table proxy source asset reads more like a tabletop slab than a clear furniture replacement.
- Remaining simulator/runtime warnings can obscure newly introduced regressions if Console hygiene is not maintained carefully.

## Next Smallest Task
Extend the thin best-view capture branch just one step further:
1. keep the current `MRUK anchor -> BestViewCaptureService` selection path,
2. add an object-centric crop rectangle / export record for `TABLE`,
3. verify capture output in `MetaXRSimulator`,
4. then decide whether to feed that record into a thin generated-object request or return to table proxy polish.

## Update Rule
When a task materially changes the state of the prototype, update:
- this file for rolling status,
- `README.md` only when the public-facing project summary changes.
