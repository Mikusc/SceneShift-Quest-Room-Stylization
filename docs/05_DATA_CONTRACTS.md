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

# 10. JSON export shape recommendation

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

# 11. Strong rules for data ownership

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

---

# 12. Debugging recommendations
For every `StylizationPlanEntry`, be able to answer:
- what did the system think this object was?
- which rule matched?
- what replacement was chosen?
- was footprint preservation required?
- is the object collision-sensitive?
- was the user forced to correct it?

---

# 13. First assets that should exist
Recommended first data assets:
- `ThemeProfile_FutureResearchLab.asset`
- `ThemeProfile_ArcaneKnowledgeChamber.asset`
- one exported room snapshot for the canonical room
- one debug stylization plan for inspection

---

# 14. Minimum viable data contracts
If time is tight, implement at least these first:
- `ThemeProfile`
- `RoomObjectRecord`
- `RoomSemanticSnapshot`
- `StylizationPlanEntry`
- `AppliedStylizationRecord`

Everything else can be added after the first working slice.
