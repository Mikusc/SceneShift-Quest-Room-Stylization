# 02 Roomify-to-Meta Mapping

## Purpose of this document
This file translates the Roomify research pipeline into a practical implementation strategy for a Unity + Meta Quest project that prioritizes official Meta tools.

The goal is **not** to reproduce Roomify exactly.
The goal is to preserve its most important ideas:
- style coherence
- spatial grounding
- functional consistency
- user editability

## 1. The original Roomify logic
Roomify is organized as four major stages:
1. Scene Understanding
2. Style Extraction and Mapping
3. Content Generation
4. Scene Composition

The paper also emphasizes a cross-reality authoring workflow where MR is used for grounded editing and VR is used for immersive preview.

## 2. Our Meta-first approximation
We replace each research-heavy component with the most practical official-tool alternative.

| Roomify stage | Paper intent | Meta-first implementation | Status in this repository |
|---|---|---|---|
| Scene Understanding | understand room geometry + semantics | MRUK room / anchors / labels / scene visuals, optionally enriched with Image Segmentation or Object Detection | Required |
| Style Extraction and Mapping | turn user intent into coherent object-level style rules | one generic `ThemeProfile` scaffold plus built-in/custom user-facing `Style` entries expanded by LLM/local extraction | Required |
| Content Generation | generate stylized objects/textures/skybox | pre-authored proxy prefabs, materials, decals, VFX, lighting, ambient audio | Required |
| Scene Composition | register content back into the room | anchor-based placement, bounds fitting, semantics-aware replacement, correction UI | Required |
| Cross-reality authoring | let user inspect and correct | MR debug overlay + correction mode | Required |
| Immersive preview | VR themed experience | optional later; not required for the first stylization slice | Optional |

## 3. Scene Understanding: what replaces SpatialLM
### Roomify version
Roomify uses a full research pipeline for scene understanding:
- SLAM reconstruction
- axis alignment
- semantic parsing
- structured scene JSON with oriented bounding boxes

### Repository version
For this project, the closest official-tool approximation is:

#### A. MRUK as the structural backbone
Use MRUK for:
- room-level scene data
- semantic anchors / room interpretation
- content placement
- scene visual manipulation
- debugging overlays

MRUK is the **main source of truth** for room-scale structure.
During development, this can be validated first through `MetaXRSimulator`, MRUK prefab rooms, or JSON fallback data. These are iteration scaffolds, not substitutes for final validation in the canonical UNNC IEB office room.

#### B. Image Segmentation as a supplemental visible-object layer
Use Meta AI Building Blocks `Image Segmentation` when available in the installed SDK to:
- isolate visible object regions in passthrough
- produce finer object proposals or masks
- help infer world-space object occupancy for visible items

Important: this does **not** replace a full structured indoor language model.
It supplements MRUK with **camera-visible detail**.
In practice, simulator-first development should prioritize MRUK room semantics first; passthrough-dependent visible-object layers may require device-side validation later.

#### C. Fallback if Image Segmentation is unstable or unavailable
Fallback order:
1. Object Detection Building Block
2. manual tagging / debug-only semantic overrides
3. theme application only on room surfaces and major MRUK anchors

For the current one-office-room prototype, a manual semantic override is an acceptable Phase 1 correction mechanism.
If MRUK labels the real office table as `OTHER` or misses the expected `TABLE` semantic, prefer a small user-visible override keyed by anchor index/name/id before adding heavier perception infrastructure.
The debug UI should show the semantic source as `manual_override` so this is not mistaken for automatic recognition.

## 4. Why Image Segmentation matters here
Image Segmentation is useful because it can help bridge the gap between:
- room-scale semantics from MRUK,
- and the smaller visible object cues that make stylization feel believable.

Examples:
- identify a visible chair region within a larger seating area
- isolate a table surface for a themed overlay
- detect a display surface or wall-adjacent object for replacement planning

But this repository should treat it as a **proposal layer**, not as a fully trusted room graph.

## 5. Style Extraction and Mapping
### Roomify version
Roomify takes text/image intent and derives:
- style keywords
- object-level replacement mappings
- texture prompts
- skybox prompts
- collision-risk judgments

### Repository version
For Phase 1, use a deterministic stylization pipeline:

#### Input
- user-selected built-in Style or freeform runtime style intent
- one internal `GenericRoomStyleScaffold` for functional mappings and deterministic fallbacks
- fused room/object semantics

#### Output
A `StylizationPlan` made of entries such as:
- `wall -> material override + decal set`
- `table -> future_lab_table_proxy`
- `screen -> holographic board treatment`
- `storage -> archive cabinet shell`
- `seat -> themed seating proxy`

Generated artifacts, cache keys, and UI labels should use the user-facing Style as the visible identity, such as `Future Research Lab`, `Arcane Knowledge Chamber`, or `Custom: Underwater Research Lounge`. The selected `ThemeProfile` is now the internal `GenericRoomStyleScaffold` for safe mappings, available proxies, and deterministic fallbacks.

#### Design rule
Every mapping must answer three questions:
1. What real semantic category is this?
2. What themed replacement keeps its high-level function?
3. How much geometry may change before spatial trust is harmed?

## 6. Content Generation
### Roomify version
The paper uses generated images, image-to-3D, and generated skyboxes.

### Repository version
Use deterministic assets first.

#### Preferred assets
- proxy prefabs for major categories
- material libraries
- decals
- emissive panels
- particle/VFX accents
- themed ambient audio loops

#### Why
This keeps the system:
- faster
- easier to debug
- easier to align to real furniture
- easier to explain in coursework

#### Hard rule
Do not block the stylization slice on runtime 3D generation.

Optional note:
The repository now contains a generated-furniture side branch for MRUK furniture anchors. That branch may use headset/external captures, Roomify-inspired prompt artifacts, APIMart image generation, hosted upload, and Seed3D model generation, but it remains secondary to the deterministic material/proxy path.

## 7. Scene Composition
### Roomify version
The paper registers generated assets to semantic scaffolds.

### Repository version
Use these alignment strategies:
- anchor category -> replacement category mapping
- bounds-fit scaling
- preserve footprint when collision-sensitive
- preserve front-facing orientation when the real object has an obvious direction
- apply surface materials without changing room passability

### Collision-sensitive objects
Treat these as high-sensitivity:
- tables / desks
- seats
- shelves / cabinets
- large display stands
- obstacles near walk paths

For these, preserve footprint and gross location as much as possible.

## 8. Cross-reality authoring and editability
Roomify’s key practical strength is not only generation; it is **correction inside MR**.

That should directly influence this repository.

### Required capabilities
- highlight an object and show its semantic label
- show current mapped replacement
- allow small transform correction
- allow reset to original mapping

### Not required yet
- freeform scene editing
- object-by-object semantic rewriting through voice
- full VR preview pipeline

## 9. What counts as “Roomify-like enough” for this project
A Roomify-like milestone is acceptable if all of the following are true:
- the room remains legible as the real room,
- the theme reads coherently across multiple semantics,
- major furniture still makes sense spatially,
- the user can see and correct system mistakes,
- the implementation is driven by spatial semantics rather than a simple global post-processing effect.

## 10. What not to imitate from the paper right now
Do not spend current effort on:
- making the paper-style prompt pipeline the required Phase 1 path,
- cloud orchestration for multiple model calls as a blocking dependency,
- best-view frame selection research code,
- generated 3D asset registration algorithms,
- dynamic skybox generation. Window vistas are acceptable as lightweight opening overlays when they preserve room readability.

These belong to optional extension work. The current repository may keep a thin file-based generated-object experiment as long as deterministic room stylization still runs first.

## 11. Repository-level interpretation of the four design requirements
### Style diversity and consistency
Implementation target:
- one generic scaffold plus built-in style entries
- category-specific mappings that share the same palette / motif / material language

### Spatial alignment
Implementation target:
- anchor-based placement
- debug overlays for room bounds and mapped replacements
- correction mode

### Functional consistency
Implementation target:
- replacement/fallback mapping tables for each semantic category
- preserve approximate size / footprint for collision-sensitive furniture

### User editability
Implementation target:
- object selection
- inspect current mapping
- nudge / rotate / reset

## 12. Recommended first Roomify-inspired vertical slice
Implement only this chain:

1. load MRUK room
2. list room semantics and visible proposals
3. choose a built-in or custom Style
4. produce a plan for walls, floor, ceiling, openings, and core furniture
5. apply materials / proxies
6. allow one object correction

If that works, the repository is on the right track.
