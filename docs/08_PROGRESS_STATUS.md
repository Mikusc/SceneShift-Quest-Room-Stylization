# 08 Progress Status

## Purpose
This file is the manual progress tracker for the current vertical slice.
Update it after each meaningful implementation step.

## Snapshot
- Last updated: `2026-04-21`
- Current priority: `Phase 1 — room stylization`
- Canonical scene: `Assets/Scenes/MR_RoomStylization.unity`
- Primary development validation path: `MetaXRSimulator`

## Milestone Status
| Milestone | Status | Notes |
| --- | --- | --- |
| M0 — Project foundation audit | Partial | Project audit and canonical scene exist; agreed folder structure is only partially normalized. |
| M1 — MRUK semantic debug layer | Mostly done | Simulator path works, room bootstrap exists, semantic HUD exists. Dedicated snapshot export remains open. |
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

## In Progress
- Table proxy alignment and replacement readability in `AnchorThemeApplier`.
- Making the table replacement visually distinct from MRUK debug-like room surfaces.
- Cleaning up crash-related transient workspace state before the next commit.

## Known Gaps
- No perception fusion layer yet for `Image Segmentation` or object detection.
- No manual correction workflow yet.
- No reset / reapply / clean theme-switch flow documented as finished.
- No dedicated room snapshot export.
- No complete demo-ready UI path outside inspector/debug HUD usage.

## Latest Stable Verification
Last stable manual/runtime checks before this document update confirmed:
- `MR_RoomStylization` loads through the `MetaXRSimulator` path.
- MRUK room semantics and theme/planner/applier summaries are visible through the debug HUD.
- Wall / floor / ceiling stylization is visible at runtime.
- The first table proxy path resolves a prefab and spawns a proxy root.
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
Finish the first `TABLE` replacement so it is clearly readable in-scene:
1. keep the current planner -> applier -> proxy path,
2. improve the chosen table proxy asset or its material treatment,
3. verify the result in `MetaXRSimulator`,
4. then update this file and `README.md` if the visible status changes.

## Update Rule
When a task materially changes the state of the prototype, update:
- this file for rolling status,
- `README.md` only when the public-facing project summary changes.
