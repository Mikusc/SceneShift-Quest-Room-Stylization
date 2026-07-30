# 01 Product Scope and Success

## 1. Project name
**SceneShift Office Room**

Working technical subtitle:
**Scene-aware room stylization for a UNNC IEB office room on Meta Quest**

## 2. Canonical problem statement
Transform one real office room at UNNC IEB into a coherent themed mixed-reality work/study space while preserving room readability, major furniture roles, and user confidence about where real objects still are.

## 3. Current development scope
### Phase 1 — Room stylization only
The current implementation must support:
- one canonical real office room at UNNC IEB
- one internal `GenericRoomStyleScaffold` for deterministic mappings and safe fallbacks
- built-in or user-defined `Style` entries as the user-facing visual identity
- spatially grounded room stylization
- a visible and inspectable stylization plan
- basic manual correction in MR

During Phase 1A development, `MetaXRSimulator` may be used as a controlled validation environment for MRUK room loading, semantic visualization, and debug tooling. It accelerates iteration, but it does **not** replace final verification in the canonical UNNC IEB office room.

### Current stretch target — true-device generated-object loop
The current demo ambition is to run one complete generated-object loop on a standalone Quest headset:
1. the user enters a freeform style intent in the headset UI,
2. the app captures a target furniture/object reference from passthrough camera data when supported,
3. a secure backend generates the stylized image and 3D asset without exposing API keys in the APK,
4. the headset downloads and runtime-loads the generated 3D asset,
5. the asset is fitted back to the matching MRUK anchor using request/object/style identity,
6. the user reviews the result in MR and can accept, reject, reset, or apply bounded transform corrections.

This stretch target raises the generated-object branch from "optional enrichment" to the active demo goal, but it does not remove the deterministic fallback rule. The room must remain usable if capture, network generation, runtime model loading, or review fails.

### Phase 2 — NPC learning partner
Only after Phase 1 is stable, add:
- themed NPC presence
- user question input
- room-reactive work/study support
- whiteboard/screen keyword feedback

## 4. Research framing
This project is **Roomify-inspired**, but not a reproduction of the full paper pipeline.

The repository should implement a **Meta-first approximation** of spatially grounded style transformation using official Unity/Meta Quest tools.

## 5. Experience goal
The user should feel:
- “This is still my real room.”
- “The room now belongs to a coherent theme.”
- “I still know where real furniture is.”
- “The system helps me reinterpret the space instead of hiding it.”

## 6. Primary user journey for Phase 1
1. User enters the canonical UNNC IEB office room.
2. System loads room semantics via MRUK.
3. System optionally enriches visible object understanding with Image Segmentation.
4. User selects a built-in Style or enters a custom style intent. The internal `GenericRoomStyleScaffold` handles functional mappings and fallbacks, not the visible theme identity.
5. System creates a stylization plan.
6. System applies material swaps, proxy objects, lighting, audio, and surface effects.
7. User inspects highlighted objects and adjusts incorrect placements.
8. User resets or switches theme.

## 7. Design principles
### A. Style consistency
The room must read as one theme instead of many disconnected effects.

### B. Spatial alignment
Every stylized element must remain grounded in the real room, especially room-scale surfaces and large furniture.

### C. Functional consistency
The replacement does not need to preserve every detailed affordance, but it should preserve the high-level role:
- table -> still read as a table/work surface
- seat -> still read as sit-able seating
- storage -> still read as storage / support furniture
- screen -> still read as a display / board

### D. User editability
If the system is wrong, the user must be able to correct it without leaving the MR experience.

## 8. Minimum supported semantics in Phase 1
Support these first:
- floor
- wall
- ceiling
- table / desk
- screen / board / display surface
- storage cabinet / shelf
- seating

If other categories appear, classify them as optional or debug-only.

## 9. What success looks like
### Functional success
- The project compiles and runs on the target Meta Quest setup.
- The system can load room semantics in a known room.
- The system can apply one coherent preset or user-defined style.
- At least four semantic categories visibly change in a spatially grounded way.
- The user can inspect and correct at least one incorrect mapping.

Development-stage validation may use `MetaXRSimulator`, but milestone success is still defined against the known real UNNC IEB office target.

### Demo success
The system can support a short demo flow:
- enter room
- scan/load room
- select style
- stylize room
- inspect/correct
- reset or switch style

For the generated-object true-device demo, success additionally means:
- a headset user can complete `style intent -> capture -> backend generation -> runtime model load -> request-locked placement -> review/edit` without returning to the Unity Editor,
- cloud API credentials are handled by a backend service, not embedded in the Quest app,
- generated furniture can be accepted, rejected, reset to deterministic fallback, or corrected within bounded MR controls.

### Coursework success
The implementation stays explainable as a proof-of-concept prototype for one setting and one core mixed-reality interaction loop.

## 10. Explicit non-goals for Phase 1
Do not treat these as required:
- arbitrary-room production-grade generalization
- runtime generation of every object into fresh 3D assets
- full SLAM3R + SpatialLM reproduction
- dynamic skybox or cinematic world generation
- multiplayer / colocation
- full AI conversation system

For the core Phase 1 acceptance path, generated furniture is not a dependency; deterministic stylization remains the fallback milestone. For the current stretch demo, however, one request-locked generated-object loop on Quest is an explicit target.
Window vistas are treated as lightweight opening overlays, not as a full dynamic skybox pipeline.
The room must still stylize correctly through deterministic materials/proxies if generated images or generated 3D assets are unavailable.

## 11. Style strategy
Use one neutral `GenericRoomStyleScaffold` internally, then expose built-in and custom `Style` entries to the user.
Built-in starter styles:
- **Future Research Lab**
- **Arcane Knowledge Chamber**

The scaffold should define:
- surface material family
- proxy replacements for major furniture semantics
- lighting preset
- VFX / ambient audio palette
- whiteboard / screen treatment

Every built-in or custom runtime Style should be treated as a first-class generated-artifact identity in cache status, prompt records, generated furniture records, and UI labels, for example `Future Research Lab`, `Arcane Knowledge Chamber`, or `Custom: Underwater Research Lounge`. The underlying scaffold remains responsible for deterministic mappings, proxy availability, and safe fallbacks.

## 12. User-editability strategy
Correction must stay lightweight.
Support only:
- select mapped object
- show original semantic + replacement type
- nudge position / rotation / scale within safe bounds
- confirm or reset

## 13. Acceptance checklist for the first real milestone
A first milestone is accepted only if:
- a canonical stylization scene exists,
- MRUK room semantics are visible in debug form,
- at least one theme preset asset exists,
- stylization can be triggered from a simple UI or debug button,
- the result is inspectable rather than hidden behind opaque automation.

## 14. Naming conventions
- Keep code identifiers in English.
- Keep theme names user-friendly.
- Keep data assets descriptive and category-based.

Examples:
- `ThemeProfile_FutureResearchLab.asset`
- `ThemeProfile_ArcaneKnowledgeChamber.asset`
- `MR_RoomStylization.unity`
- `RoomSemanticBootstrap.cs`
- `StylizationPlanner.cs`

## 15. Recommended next implementation target
Build the smallest vertical slice that proves this chain:

**MRUK room semantics -> GenericRoomStyleScaffold + user Style -> stylization plan -> material/proxy application -> correction overlay**
