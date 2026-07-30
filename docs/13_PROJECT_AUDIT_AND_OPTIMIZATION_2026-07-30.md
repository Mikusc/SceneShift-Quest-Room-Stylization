# 13 Project Audit And Optimization - 2026-07-30

## Executive Verdict

The current checkout contains a viable Phase 1 room-stylization vertical slice and a substantial
optional cloud-generation branch. The main technical risk was not a missing feature. It was that
prototype conveniences could bypass request, room, review, credential, and timeout boundaries.

This audit hardened those boundaries without deleting scenes, packages, generated assets, or the
user's existing uncommitted work.

Current status after this pass:

- deterministic room stylization remains the fallback and primary safety path;
- direct paid-provider adapters are opt-in instead of auto-running on newly added components;
- generated furniture selection now fails closed across request and room boundaries;
- rejected, reset, `NeedsReview`, or unknown-room records no longer count as ready restore state;
- serialized credential overrides are removed and a build guard is present;
- all project-owned `UnityWebRequest` call sites have a finite request timeout;
- the runtime GLB dependency and Unity MCP Git revision are reproducible in the package manifest;
- static checks and Unity Roslyn compilation pass;
- full Unity batchmode startup and the serialized credential guard now pass after the Editor
  launches its version-specific Licensing Client fallback;
- no Play Mode, Meta XR Simulator, APK, or headset claim was added by this audit.

## Audit Scope

Reviewed:

- project and product documentation;
- Git status and tracked/untracked asset policy;
- canonical scene and Build Settings;
- packages and lockfile;
- runtime generation, review, cache, and placement code;
- direct provider and secure backend HTTP paths;
- serialized credential surfaces;
- generated fallbacks and imported/generated asset policy;
- existing validation scripts and available compile evidence.

The audit started from an already dirty worktree with roughly 39 tracked modified files and many
untracked implementation, sample, recovery, backend, tool, and documentation files. Those changes
were treated as user work and preserved.

## Positive Findings

- Only `Assets/Scenes/MR_RoomStylization.unity` is enabled for the current player build.
- Android is configured for ARM64.
- The canonical scene contains deterministic fallback prefabs for table, seating, storage, and
  screen semantics.
- Direct APIMart, upload, Seed3D, and DeepSeek scene adapters are disabled in the canonical scene.
- Quest runtime generation uses HTTPS backend endpoints instead of provider keys in the APK.
- Generated-object review supports accept, reject, reset, and bounded correction persistence.
- The pre-device secret scan found zero likely long-lived credentials.
- Git LFS integrity is currently clean.

## Findings And Actions

| Priority | Finding | Action in this pass | Status |
| --- | --- | --- | --- |
| P0 | `DeepSeekStyleIntentProvider` exposed a serialized `apiKeyOverride` field. | Removed the override; only an explicitly named process environment variable can supply the direct Editor provider. Added a pre-build credential scanner. | Fixed |
| P0 | Newly added direct APIMart/upload/Seed3D/DeepSeek components auto-enabled external calls. | Changed all direct provider defaults to opt-in. Existing scene values remain disabled. | Fixed |
| P0 | A generated record could match an active capture by only `ObjectId` or source path. | Active placement and backend submission now require exact `RequestId` plus `ObjectId`; arbitrary "latest job" submission is no longer used. | Fixed |
| P0 | Imported generated furniture could cross room boundaries when object identifiers were reused. | Imported records now require a readable source request whose `RoomId` matches the current room. | Fixed |
| P0 | Theme rules could turn off baseline footprint, yaw, or collision safety. | Rule values now strengthen baseline safety with boolean OR instead of replacing it. | Fixed |
| P1 | `NeedsReview`, rejected, reset, or unknown-room records could appear ready in cache/restore flows. | Excluded unsafe review states and made room/theme restore checks fail closed. | Fixed |
| P1 | Several HTTP submit, poll, upload, model, and texture download requests had no per-request timeout. | Added finite timeouts to every project-owned `UnityWebRequest` call site. | Fixed |
| P1 | Runtime code directly imports glTFast while the package was only a transitive Assistant dependency. | Added direct `com.unity.cloud.gltfast` dependency at the already resolved version. | Fixed |
| P1 | Unity MCP used an unpinned Git URL. | Pinned the manifest and lock entry to the already resolved commit. | Fixed |
| P1 | Core deterministic Phase 1 and optional cloud-generation R&D share one large scene/build surface. | Documented below; no destructive scene/package split was attempted in a dirty worktree. | Open |
| P1 | No project-owned `.asmdef` or automated EditMode/PlayMode tests were found. | Documented below; adding assembly boundaries safely requires a dedicated checkpoint and migration pass. | Open |
| P1 | Several classes are too large for low-risk maintenance. | Documented refactor targets; no broad rewrite was performed. | Open |
| P2 | Recovery scenes, imported Meta sample content, developer scenes, generated solutions, and OS files clutter status. | Added ignore rules for future `.DS_Store`, `.slnx`, and `Assets/_Recovery/` artifacts. Existing tracked files were not removed. | Partial |
| P2 | Generated GLBs/prefabs are both treated as cache and present in tracked history; `*.glb` has no explicit LFS policy. | Requires an explicit product decision: reproducible cache, curated demo fixture, or LFS artifact. | Open |
| P2 | Meta package versions are not fully aligned (`audio` 85 versus core/interaction 201), and prototype packages may be unused. | Package removal/version churn deferred until dependency and headset regression evidence exists. | Open |

## Implemented Hardening

### Credential boundary

- Added `SceneShiftCredentialBuildGuard`.
- Builds are blocked when common credential fields contain non-empty serialized values in Unity
  scenes, prefabs, assets, or JSON under `Assets/` or `ProjectSettings/`.
- The guard reports only field name, path, and line number; it does not print the credential value.
- The direct DeepSeek provider no longer supports an Inspector key override.
- Standalone Quest remains responsible only for the public HTTPS backend URL.

### Generated-object identity and restore boundary

- `QuestRuntimeGenerationClient` uses the capture service's exact queued job, with exact
  `RequestId` and `ObjectId` recovery only when the path moved.
- `AnchorThemeApplier` requires exact request and object identity for the active capture.
- Imported generated records also require source-request room identity when a current room is known.
- `allowLatestGeneratedTableWhenNoActiveCapture` is disabled in code and scene.
- `allowNeedsReviewGeneratedTablesForValidation` is disabled in code and scene.
- Persisted review restore requires current room and theme identity instead of accepting missing
  identity as a wildcard.
- `RoomStyleCacheService` does not advertise rejected, reset, or `NeedsReview` furniture as ready.

### Spatial safety

`StylizationPlanner` now preserves any safety constraint already inferred from MRUK semantics.
A theme rule can add `PreserveFootprint`, `PreserveYawOrientation`, or `CollisionSensitive`, but
cannot remove a baseline constraint for collision-relevant furniture or structural surfaces.

### Network reliability

Finite request timeouts now cover:

- APIMart object-image submit, poll, and download;
- APIMart surface-image submit, poll, and download;
- hosted image upload;
- Seed3D submit, foreground/background poll, and model download;
- secure Quest object backend submit and poll;
- secure Quest surface backend submit, poll, and texture download;
- runtime GLB download;
- DeepSeek style intent request.

The job-level polling window remains separate from each HTTP request timeout.

### Reproducibility and repository hygiene

- `com.meta.xr.unity-mcp.extension` is pinned to
  `22a736de7ce7d51ef39db0d84f8697bf1fc21aad`.
- `com.unity.cloud.gltfast` `6.14.1` is a direct dependency because project runtime code imports it.
- Future `.DS_Store`, `.slnx`, and `Assets/_Recovery/` artifacts are ignored.
- Existing tracked files and generated assets were not untracked, deleted, or rewritten.

## Structural Optimization Backlog

### 1. Separate stable demo and cloud R&D surfaces

Recommended after a checkpoint:

- keep `MR_RoomStylization.unity` as the deterministic, device-safe product scene;
- define an explicit build/profile switch for secure cloud-generation functionality;
- keep direct provider adapters in an Editor-only development surface;
- ensure the Quest package contains secure backend clients but not direct provider behaviors;
- run both profiles through the same room, planner, applier, correction, and reset contracts.

This resolves the current tension between the Phase 1 deterministic priority and the optional
generated-object stretch branch without deleting the stretch work.

### 2. Introduce assembly and test boundaries

Suggested assemblies:

- `SceneShift.Core`
- `SceneShift.Stylization`
- `SceneShift.Generation`
- `SceneShift.UI`
- `SceneShift.Editor`
- `SceneShift.Tests.EditMode`
- `SceneShift.Tests.PlayMode`

First tests should cover:

- planner safety invariants;
- request/object/room matching;
- rejected/reset/needs-review cache behavior;
- review restore filtering;
- backend result parsing and terminal-state transitions;
- deterministic fallback selection.

Do this only after current untracked scripts and prefabs are checkpointed, because introducing
assembly definitions can expose hidden dependencies across the current monolithic assembly.

### 3. Split oversized classes by responsibility

Largest refactor candidates observed:

- `SceneShiftUISetDashboard` - about 2,800 lines;
- `AnchorThemeApplier` - about 2,700 lines;
- `SurfaceOverrideApplier` - about 2,000 lines;
- `PreDeviceBuildReadinessReportRunner` - about 1,800 lines;
- `DevicePassthroughCaptureService` - about 1,750 lines.

Prefer extraction around stable contracts, not line-count-only rewrites:

- dashboard view composition versus command binding versus status formatting;
- generated candidate selection versus deterministic proxy application versus placement fitting;
- surface geometry versus material resolution versus generated texture cache;
- readiness data collection versus report rendering;
- capture targeting versus image acquisition versus request writing.

### 4. Decide generated asset ownership

Choose one policy per asset:

- reproducible cache: ignored and regenerated;
- curated demo fixture: committed with source/provider/license/provenance metadata;
- large durable artifact: Git LFS with an explicit `*.glb` rule.

Do not leave the same folder simultaneously described as ignored cache and relied-on tracked demo
content.

### 5. Clean package and sample residue with evidence

After a stable checkpoint, audit references before changing:

- `com.meta.xr.sdk.audio` version alignment;
- `com.unity.collab-proxy`;
- `com.unity.multiplayer.center`;
- `com.unity.timeline`;
- `com.unity.visualscripting`;
- imported `Assets/Samples/` content;
- UISet developer scenes.

Remove or align only after Editor, simulator, Android build, and headset smoke checks.

## Validation Evidence

Passed on `2026-07-30`:

- `bash Tools/scan_predevice_secrets.sh`
  - packaged files: 121
  - generated records: 34
  - findings: 0
- package manifest and lock JSON parsing;
- manifest/lock consistency for pinned Unity MCP and direct glTFast;
- scene assertions for strict generated-object fallback/review settings;
- `git diff --check`;
- `git lfs fsck`;
- Unity-bundled Roslyn compile of current `Assembly-CSharp` sources using the latest available
  Unity response file;
- Unity-bundled Roslyn compile of current `Assembly-CSharp-Editor` sources, including the new
  credential guard.
- full Unity 6.4 batchmode import/compile/execute-method validation;
- `SceneShiftCredentialBuildGuard.ValidateFromCommandLine`;
- successful batchmode shutdown with exit code `0`.

Licensing note:

- the first generic Hub Licensing Client handshake still reports unsupported protocol `1.18.1`;
- Unity then launches and connects to its `6000.4.3` version-specific Licensing Client;
- the Unity Personal entitlement resolves successfully, so this is no longer a validation blocker.

Not run:

- Unity Play Mode;
- Meta XR Simulator interaction and visual regression;
- Android APK build;
- MQDH/test-channel install;
- standalone Quest room/capture/generation/review/restart flow.

## Required Next Gate

1. Open the project in Unity 6.4 and accept the external scene reload prompt if the canonical
   scene is already open.
2. Confirm Console has no new compile errors.
3. Run the deterministic room smoke before enabling any secure backend generation.
4. Run the pre-package evidence suite and true-device checklist before creating a new APK claim.
5. Checkpoint this audited worktree before assembly, package cleanup, or scene-profile work.
