# 05 Data Contracts

## Purpose
These data contracts keep the room stylization system deterministic, debuggable, and easy for Codex to extend.

Do not hardcode theme rules directly into random scene components. Put them into data contracts and assets.

---

# 1. ThemeProfile

## Purpose
Defines one coherent room theme.

## Recommended representation
ScriptableObject:
- `ThemeProfile`

## Suggested fields
```csharp
string ThemeId;
string DisplayName;
string ShortDescription;
ThemeCategory Category;
Color AccentColor;
MaterialSet SurfaceMaterials;
string WallTexturePromptHint;
string FloorTexturePromptHint;
string CeilingTreatmentPromptHint;
LightingPreset LightingPreset;
AudioClip AmbientLoop;
List<SemanticReplacementRule> ReplacementRules;
GameObject DefaultTableProxy;
GameObject DefaultSeatProxy;
GameObject DefaultStorageProxy;
GameObject DefaultScreenTreatmentPrefab;
bool PreserveCollisionSensitiveFootprints = true;
```

## Notes
- The theme should not know about a specific room instance.
- It only defines the styling language and preferred replacements.

---

# 2. RoomObjectRecord

## Purpose
Normalized record for any room-relevant entity.

This is the central unit used by the planner.

## Suggested fields
```csharp
string ObjectId;
RoomObjectSource Source; // MRUKAnchor, Segmentation, Detection, Manual
string SemanticLabel;    // wall, floor, table, seat, storage, screen, etc.
string FunctionTag;      // support_surface, seating, storage, display_surface, boundary
Pose WorldPose;
Vector3 Dimensions;
Bounds WorldBounds;
float Confidence;
bool CollisionSensitive;
bool UserCorrected;
string ParentAnchorId;
Dictionary<string, string> Metadata;
```

## Notes
- `SemanticLabel` is the semantic category.
- `FunctionTag` is the higher-level functional role.
- `Source` is important for debugging and trust hierarchy.

---

# 3. RoomSemanticSnapshot

## Purpose
A single merged semantic view of the room at one point in time.

## Suggested fields
```csharp
string SnapshotId;
string RoomId;
DateTime CreatedAtUtc;
List<RoomObjectRecord> Objects;
Vector3 RoomOriginPosition;
Quaternion RoomOriginRotation;
Dictionary<string, int> CategoryCounts;
```

## Notes
- This is the main planner input.
- It should be exportable to JSON for debugging.

---

# 4. SemanticReplacementRule

## Purpose
A theme-defined rule that says how one semantic category should be stylized.

## Suggested fields
```csharp
string SemanticLabel;
string FunctionTag;
ReplacementMode Mode; // MaterialOverride, ProxyPrefab, Overlay, Skip, FXOnly
GameObject ProxyPrefab;
Material PrimaryMaterial;
Material SecondaryMaterial;
bool PreserveFootprint;
bool PreserveYawOrientation;
bool CollisionSensitive;
string Notes;
```

## Example
- table -> ProxyPrefab + PreserveFootprint + PreserveYawOrientation
- wall -> MaterialOverride + decals
- screen -> Overlay / emissive board treatment

---

# 5. StylizationPlan

## Purpose
A full room plan generated from a theme plus room snapshot.

## Suggested fields
```csharp
string PlanId;
string ThemeId;
string RoomId;
List<StylizationPlanEntry> Entries;
List<string> Warnings;
DateTime CreatedAtUtc;
```

## Notes
- The plan should exist before objects are applied.
- The user should be able to inspect the plan in a debug UI.

---

# 6. StylizationPlanEntry

## Purpose
A single mapping decision for one room object.

## Suggested fields
```csharp
string EntryId;
string ObjectId;
string OriginalSemanticLabel;
string OriginalFunctionTag;
ReplacementMode ReplacementMode;
string ReplacementId;
string ReplacementDisplayName;
bool PreserveFootprint;
bool PreserveYawOrientation;
bool CollisionSensitive;
float PlannerConfidence;
string Rationale;
Dictionary<string, string> Parameters;
```

## Example rationale strings
- "Table transformed into future research desk shell while preserving walk-around clearance."
- "Wall receives material override only to avoid navigation changes."

---

# 7. AppliedStylizationRecord

## Purpose
Tracks what the applier actually placed in the scene.

## Suggested fields
```csharp
string ObjectId;
string PlanEntryId;
GameObject AppliedInstance;
Pose AppliedPose;
Vector3 AppliedScale;
bool IsSurfaceOnly;
bool IsCorrected;
CorrectionDelta Correction;
```

## Notes
- This is necessary for correction mode and reset.

---

# 8. CorrectionDelta

## Purpose
Stores user changes after planner output.

## Suggested fields
```csharp
Vector3 PositionOffset;
Vector3 EulerOffset;
Vector3 ScaleMultiplier;
bool Confirmed;
```

## Notes
- Keep correction deltas small and bounded.
- Prefer yaw-only rotation unless a clear need arises.

---

# 9. Recommended enums

## `RoomObjectSource`
```csharp
public enum RoomObjectSource
{
    MRUKAnchor,
    Segmentation,
    Detection,
    Manual,
    Derived
}
```

## `ReplacementMode`
```csharp
public enum ReplacementMode
{
    Skip,
    MaterialOverride,
    ProxyPrefab,
    Overlay,
    FXOnly
}
```

## `ThemeCategory`
```csharp
public enum ThemeCategory
{
    Future,
    Arcane,
    Minimal,
    Experimental
}
```

---

# 10. SurfaceTexturePromptSet

## Purpose
Tracks the Roomify-style prompt handoff for room boundary surfaces, opening-frame overlays, and window-vista backdrops.

The script source of truth is:
- `Assets/Scripts/Stylization/SurfaceTextureContracts.cs`

## Current fields
```csharp
string ThemeId;
string ThemeDisplayName;
string ThemeDescription;
string StyleVariantId;
string UserStyleIntent;
string StyleIntentSource;
string CreatedAtIsoUtc;
string JobFolder;
List<SurfaceTexturePromptEntry> Entries;
```

## Notes
- Written by `SurfaceTexturePromptBuilder` under `Library/SurfaceTextureJobs/`.
- `StyleVariantId` is `preset` when no runtime style text is active. Arbitrary user style text creates a stable style-specific variant id, so cyberpunk / solarpunk / underwater requests do not reuse each other's cached textures.
- This is now the first automated surface-generation boundary. APIMart can consume matching `.surface.job.json` files, while runtime procedural textures remain the fallback.
- Runtime procedural textures remain the deterministic fallback.

# 11. SurfaceTexturePromptEntry

## Purpose
Describes one wall, floor, ceiling, door-frame, window-frame, or window-vista prompt request.

## Current fields
```csharp
string RequestId;
string StyleVariantId;
string UserStyleIntent;
string StyleIntentSource;
string SemanticLabel;
ThemeSurfaceKind SurfaceKind;
string OutputRole;
string PromptVersion;
string Prompt;
string NegativePrompt;
string ImageSize;
string PromptPath;
string JobPath;
string OutputImagePath;
bool SeamlessTileable;
bool PbrMaterial;
bool RuntimeFallbackAvailable;
```

## Notes
- Wall and floor entries request seamless tileable PBR-style materials.
- Ceiling is treated as a lightweight ceiling treatment / optional skybox concept, not a hard Phase 1 dynamic skybox dependency.
- Door and window entries request trim/frame/decal treatments with open centers. They must not block passage, view, or real-world affordances.
- Window-vista entries request wide exterior backdrops with `ImageSize = "16:9"`. They are placed behind `WINDOW_FRAME` anchors and must not include window frames, room interiors, people, or text.

# 12. SurfaceTextureJobRecord

## Purpose
Tracks the async state for one generated surface texture, frame/trim overlay texture, or window-vista backdrop.

The script source of truth is:
- `Assets/Scripts/Stylization/SurfaceTextureContracts.cs`

## Current fields
```csharp
string RequestId;
string ThemeId;
string ThemeDisplayName;
string StyleVariantId;
string UserStyleIntent;
string StyleIntentSource;
string SemanticLabel;
ThemeSurfaceKind SurfaceKind;
string OutputRole;
SurfaceTextureJobState State;
string PromptVersion;
string ImageSize;
string PromptArtifactPath;
string JobPath;
string BackendAdapterName;
string BackendRequestPath;
string BackendResultPath;
string BackendTransformId;
string OutputImagePath;
string OutputImageUrl;
string StatusNote;
string FailureReason;
string UpdatedAtIsoUtc;
```

## Current states
```csharp
public enum SurfaceTextureJobState
{
    PromptReady,
    BackendSubmitted,
    TextureReady,
    MaterialReady,
    Failed,
}
```

## Notes
- `PromptReady` jobs are written by `SurfaceTexturePromptBuilder` under `Library/SurfaceTextureJobs/`.
- `ApimartSurfaceTextureBackendAdapter` submits `PromptReady` jobs to APIMart `gpt-image-2`, polls task status, downloads a PNG under `Library/SurfaceTextureOutputs/`, and advances the job to `TextureReady`.
- `ImageSize` lets material jobs stay square while `window_vista` jobs request a wide image.
- `SurfaceOverrideApplier` can consume `TextureReady` PNGs directly at runtime. If no generated texture exists, it falls back to theme materials or procedural textures.

---

# Generated-object side branch contracts
The following contracts support the optional Roomify-like `TABLE` generated-object branch.
They do not replace the deterministic Phase 1 data model.

The script source of truth is:
- `Assets/Scripts/Perception/GeneratedObjectContracts.cs`

Use these contracts only for:
- best-view capture artifacts,
- prompt/image backend handoff,
- local/mock backend results,
- future generated proxy import and registration.

---

# 13. GeneratedObjectRequest

## Purpose
Captures the complete request needed to turn one room object into a stylized generated asset candidate.

This is currently produced by `BestViewCaptureService` for the `TABLE` path.

## Current fields
```csharp
string RequestId;
string ObjectId;
string RoomId;
string ThemeId;
string ThemeDisplayName;
string ThemeShortDescription;
string UserStyleIntent;
string StyleIntentSource;
string GlobalStyleSummary;
List<string> StyleKeywords;
List<string> MaterialKeywords;
List<string> ColorKeywords;
List<string> MotifKeywords;
List<string> NegativeStyleKeywords;
string ObjectStyleDirective;
string SemanticLabel;
string FunctionTag;
string SourceAnchorName;
int SourceAnchorIndex;
SerializablePose WorldPose;
SerializableBounds WorldBounds;
Vector3 Dimensions;
float TargetLengthMeters;
float TargetWidthMeters;
float TargetHeightMeters;
float TargetAspectRatio;
float SafetyFootprintScale;
GeneratedObjectVerticalFitMode VerticalFitMode;
bool CollisionSensitive;
ReplacementMode PlannedReplacementMode;
string PlannedReplacementId;
string PlannedReplacementDisplayName;
bool PreserveFootprint;
bool PreserveYawOrientation;
BestViewCaptureSourceMode CaptureSourceMode;
string SourceOriginalInputPath;
string SourceImagePath;
string SourceFullFrameImagePath;
string SourceCroppedImagePath;
string SourceMetadataPath;
string SourceRequestPath;
SerializableRect NormalizedCropRect;
SerializablePose BestViewCameraPose;
float BestViewYawDegrees;
Vector3 ScaffoldLongestAxis;
float VisibilityScore;
string PromptVersion;
string AppearancePrompt;
string ImageStylizationPrompt;
string CreatedAtIsoUtc;
```

## Notes
- `SourceImagePath` is the image the backend should consume.
- In `ExternalScreenshot` mode, `SourceImagePath` may point to the original manual screenshot, while `NormalizedCropRect` remains metadata.
- `BestViewYawDegrees`, `Dimensions`, and `WorldBounds` are required later for registration.
- `PromptVersion` must change when prompt format changes.
- Current image prompt version for new requests is `roomify_image_asset_v3_style_keywords`.
- Older artifacts can still show earlier prompt versions such as `roomify_image_v1`; keep those artifacts for reproducibility instead of rewriting them in place.
- Runtime style fields are optional. When `UserStyleIntent` is empty, the generated-object prompt uses only the active `ThemeProfile`; when present, `StyleKeywords`, material/color/motif keywords, and negative style keywords act as a Roomify-style visual layer over the preset functional mapping.
- `ImageStylizationPrompt` must ask for a single isolated object asset suitable for image-to-3D generation. It must not ask the model to edit the original room photo as the final canvas.
- The final worker output should be a PNG with alpha. If the selected image model cannot produce native transparency, the worker should use the prompt's chroma-key note, remove the key locally, and only then save to `RequestedOutputImagePath`.

---

# 14. GeneratedAssetRecord

## Purpose
Tracks the state of one generated-object job from capture through future model import.

This is currently written as `.job.json` under:
- `Library/GeneratedObjectJobs/`

## Current fields
```csharp
string RequestId;
string ObjectId;
string ThemeId;
BestViewCaptureSourceMode CaptureSourceMode;
GeneratedObjectJobState State;
string SourceInputImagePath;
string SourceRequestPath;
string CoordinatorJobPath;
string StatusNote;
string BackendAdapterName;
string BackendRequestPath;
string PromptVersion;
string PromptArtifactPath;
string BackendResultPath;
string BackendResultTemplatePath;
string BackendTransformId;
string ModelGenerationTaskId;
string ModelGenerationRequestPath;
string ModelGenerationResultPath;
string StylizedImagePath;
string StylizedImageUrl;
string GeneratedModelPath;
string ImportedPrefabPath;
string PreviewImagePath;
SerializableBounds ImportedBounds;
float SourceYawDegrees;
float TargetLengthMeters;
float TargetWidthMeters;
float TargetHeightMeters;
float TargetAspectRatio;
float SafetyFootprintScale;
GeneratedObjectVerticalFitMode VerticalFitMode;
Vector3 RegisteredScale;
Vector3 RegisteredEulerDegrees;
float RegistrationIoUScore;
bool QualityReviewPassed;
float QualityScore;
string QualityReviewStatus;
string QualityReviewWarnings;
string FailureReason;
string UpdatedAtIsoUtc;
```

## Notes
- The target size fields are physical scaffold constraints for generated furniture. For the current MRUK table path, `BestViewCaptureService` derives length/width/height from the anchor `Dimensions`, using the larger horizontal axis as length and the smaller horizontal axis as width.
- `TargetAspectRatio` is `length / width`.
- `SafetyFootprintScale` tells image/model workers how tightly to preserve the collision-sensitive footprint.
- `VerticalFitMode` tells later workers/importers whether to preserve scaffold height, fit inside height, or only bottom-align the generated asset.

## `GeneratedObjectVerticalFitMode`
```csharp
public enum GeneratedObjectVerticalFitMode
{
    PreserveScaffoldHeight,
    FitInsideHeight,
    BottomAlignOnly,
}
```

## Notes
- `State` is the main orchestration flag.
- `StylizedImagePath` is the local generated PNG path when the image worker writes a file.
- `StylizedImageUrl` is an optional hosted `http(s)` URL for the same stylized image. `Seed3DBackendAdapter` needs this field, or an `http(s)` `StylizedImagePath`, because the Ark task API consumes `image_url`.
- `GeneratedModelPath`, `ImportedPrefabPath`, and `ImportedBounds` are now actively used by the manual Seed3D/import path.
- `RegisteredScale`, `RegisteredEulerDegrees`, and `RegistrationIoUScore` remain reserved for a later fuller registration/refinement step.
- `QualityReviewPassed`, `QualityScore`, `QualityReviewStatus`, and `QualityReviewWarnings` are written by `GeneratedObjectModelImporter` to keep suspicious generated models out of automatic placement.
- A current successful manual path is:
  - `StylizedImageReady`
  - `ModelGenerationSubmitted` when using the automated Seed3D adapter
  - `ModelReady`
  - `Imported`
- `ModelReady` means a Unity-importable model file exists at `GeneratedModelPath`.
- `Imported` means `GeneratedObjectModelImporter` saved a generated prefab and wrote `ImportedPrefabPath`.
- `NeedsReview` means a prefab was saved, but the automatic quality gate found suspicious proportions or bounds, so runtime systems should keep using deterministic fallback or a previously accepted generated asset.
- The current validated imported generated table candidate is `table_18_20260425025836`.
- Earlier generated table candidates such as `table_18_20260424071758` and `table_18_20260424173938` remain useful for comparison, but new runtime validation should prefer the newest `Imported` job that passes quality review.

---

# 15. GeneratedImageBackendSubmission

## Purpose
Defines the file-based request consumed by an external/manual image worker.

This is written by `LocalGeneratedObjectBackendAdapter` in `ExternalFileProtocol` mode under:
- `Library/GeneratedObjectBackendInbox/`

## Current fields
```csharp
string RequestId;
string ObjectId;
string ThemeId;
string PromptVersion;
string PromptArtifactPath;
string SourceInputImagePath;
string SourceRequestPath;
string RequestedOutputImagePath;
string RequestedResultPath;
string ResultTemplatePath;
string BackendAdapterName;
string SubmissionNote;
string CreatedAtIsoUtc;
```

## Notes
- `PromptArtifactPath` points to the `.prompt.txt` that should be sent to the image model.
- `SourceInputImagePath` points to the source screenshot/reference image.
- `RequestedOutputImagePath` is where the external worker should save the generated stylized image.
- `RequestedResultPath` is where the external worker should save the final result JSON.

---

# 16. GeneratedImageBackendResult

## Purpose
Defines the result file dropped back by the local mock backend or external/manual worker.

## Current fields
```csharp
string RequestId;
string ObjectId;
string ThemeId;
string PromptVersion;
string PromptArtifactPath;
string SourceInputImagePath;
string SourceRequestPath;
string OutputImagePath;
string OutputImageUrl;
string BackendAdapterName;
string AppliedTransformId;
bool PromptArtifactConsumed;
GeneratedObjectJobState OutputState;
string StatusNote;
string CreatedAtIsoUtc;
```

## Notes
- For the current image-only step, `OutputState` should normally be `StylizedImageReady`.
- `OutputImagePath` should match the generated image that exists on disk when a local file is available.
- `OutputImageUrl` may be set by an external worker when the generated image is also hosted at a public `http(s)` URL for Seed3D.
- The adapter can then advance the `.job.json` without a real cloud service.

---

# 17. Generated-object enums

## `GeneratedObjectJobState`
```csharp
public enum GeneratedObjectJobState
{
    Pending,
    CaptureReady,
    StylizedImageReady,
    ModelReady,
    Imported,
    Failed,
    BackendSubmitted,
    ModelGenerationSubmitted,
    NeedsReview,
}
```

## `BestViewCaptureSourceMode`
```csharp
public enum BestViewCaptureSourceMode
{
    ExternalScreenshot,
    UnityFramebufferDebug,
    DevicePassthroughReserved,
}
```

## Notes
- `ExternalScreenshot` is the preferred simulator-stage source mode.
- `UnityFramebufferDebug` is only for plumbing/debug export checks.
- `DevicePassthroughReserved` preserves the future true-device camera/passthrough path without pretending it works in the simulator today.

---

# 18. JSON export shape recommendation

For exported debug snapshots, prefer readable JSON like:

```json
{
  "roomId": "library_discussion_room_a",
  "themeId": "future_research_lab",
  "objects": [
    {
      "objectId": "table_center_01",
      "source": "MRUKAnchor",
      "semanticLabel": "table",
      "functionTag": "support_surface",
      "confidence": 0.95,
      "collisionSensitive": true
    }
  ],
  "plan": [
    {
      "entryId": "plan_table_center_01",
      "objectId": "table_center_01",
      "replacementMode": "ProxyPrefab",
      "replacementId": "future_lab_table_shell",
      "preserveFootprint": true,
      "plannerConfidence": 0.92
    }
  ]
}
```

---

# 19. Strong rules for data ownership

## ThemeProfile owns
- artistic intent
- category mappings
- surface/material choices
- preferred prefabs and mood assets

## RoomSemanticSnapshot owns
- observed room state
- semantic labels
- function tags
- confidence and bounds

## StylizationPlan owns
- planner decisions
- warnings
- replacement rationale

## AppliedStylizationRecord owns
- scene-instantiated result
- runtime object references
- correction state

## GeneratedObjectRequest owns
- source image and crop metadata
- object scaffold data
- capture source mode
- prompt fields needed for reproducible backend input

## GeneratedAssetRecord owns
- async job state
- prompt/backend artifact paths
- generated image/model/import paths
- registration metadata and failure state

---

# 20. Debugging recommendations
For every `StylizationPlanEntry`, be able to answer:
- what did the system think this object was?
- which rule matched?
- what replacement was chosen?
- was footprint preservation required?
- is the object collision-sensitive?
- was the user forced to correct it?

For every `GeneratedAssetRecord`, be able to answer:
- which request produced this job?
- which source image and prompt were consumed?
- which backend mode produced the result?
- is the job still only image-ready, or has it reached model/import readiness?
- can the system fall back to the deterministic proxy?

---

# 21. First assets that should exist
Recommended first data assets:
- `ThemeProfile_FutureResearchLab.asset`
- `ThemeProfile_ArcaneKnowledgeChamber.asset`
- one exported room snapshot for the canonical room
- one debug stylization plan for inspection

---

# 22. Minimum viable data contracts
If time is tight, implement at least these first:
- `ThemeProfile`
- `RoomObjectRecord`
- `RoomSemanticSnapshot`
- `StylizationPlanEntry`
- `AppliedStylizationRecord`

For the optional generated-object branch, also keep:
- `GeneratedObjectRequest`
- `GeneratedAssetRecord`
- `GeneratedImageBackendSubmission`
- `GeneratedImageBackendResult`

Everything else can be added after the first working slice.
