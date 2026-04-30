# 06 Custom MCP Tools

## Why this file exists
The built-in Unity MCP tools are useful, but they operate at a relatively low level:
- scene management
- GameObject manipulation
- asset operations
- console inspection

That is enough to automate editor work, but not enough to make Codex reliably think in the language of **this project**.

You should eventually add a few **project-specific Unity MCP tools** so Codex can call high-level operations such as:
- read the current room semantics
- build a stylization plan
- apply a theme preset
- run a smoke test

This reduces agent drift and makes future tasks safer.

---

# 1. Implementation location
Recommended location:
- `Assets/Scripts/Editor/McpTools/`

Recommended registration method:
- typed parameter classes with `[McpTool]`

Why:
- clearer schemas
- safer parameter validation
- easier for Codex to call correctly

---

# 2. Recommended first custom tools

## Tool A — `edr_validate_setup`
### Purpose
Validate that the project is in a usable state before feature work.

### Inputs
```csharp
public class ValidateSetupParams
{
    public bool CheckPackages { get; set; } = true;
    public bool CheckScenes { get; set; } = true;
    public bool CheckConsole { get; set; } = true;
}
```

### Expected behavior
- inspect package availability
- verify canonical scene exists
- report Unity console errors/warnings summary
- report whether MRUK-related scene objects are present

### Return shape
```json
{
  "ok": true,
  "missingPackages": [],
  "sceneExists": true,
  "consoleErrors": 0,
  "notes": []
}
```

---

## Tool B — `edr_export_room_semantics`
### Purpose
Read the current scene’s room semantics and export a normalized snapshot.

### Inputs
```csharp
public class ExportRoomSemanticsParams
{
    public string OutputPath { get; set; }
    public bool IncludeObservedObjects { get; set; } = true;
}
```

### Expected behavior
- collect MRUK semantic records
- collect optional segmentation/detection records
- merge them into one `RoomSemanticSnapshot`
- optionally write JSON to disk

### Return shape
```json
{
  "ok": true,
  "roomId": "unnc_ieb_office_a",
  "objectCount": 18,
  "categories": {
    "wall": 4,
    "floor": 1,
    "table": 2,
    "screen": 1
  },
  "outputPath": "Assets/Data/Debug/room_snapshot.json"
}
```

---

## Tool C — `edr_create_theme_profile`
### Purpose
Create a starter theme asset from simple parameters.

### Inputs
```csharp
public class CreateThemeProfileParams
{
    public string ThemeId { get; set; }
    public string DisplayName { get; set; }
    public string Category { get; set; }
    public string AssetPath { get; set; }
}
```

### Expected behavior
- create a `ThemeProfile` asset
- fill reasonable defaults
- save in `Assets/Data/ThemeProfiles/`

### Use case
Good for bootstrapping initial themes without manual asset creation.

---

## Tool D — `edr_build_stylization_plan`
### Purpose
Generate a stylization plan from the current room snapshot and active theme.

### Inputs
```csharp
public class BuildStylizationPlanParams
{
    public string ThemeId { get; set; }
    public bool UseCurrentSceneSnapshot { get; set; } = true;
}
```

### Expected behavior
- load current room snapshot
- resolve active theme asset
- run `StylizationPlanner`
- return summary of plan entries and warnings

### Return shape
```json
{
  "ok": true,
  "themeId": "future_research_lab",
  "entryCount": 9,
  "warnings": [
    "No rule found for plant_01"
  ]
}
```

---

## Tool E — `edr_apply_theme_preset`
### Purpose
Apply a theme to the canonical scene.

### Inputs
```csharp
public class ApplyThemePresetParams
{
    public string ThemeId { get; set; }
    public bool ResetFirst { get; set; } = true;
    public bool ApplyMood { get; set; } = true;
}
```

### Expected behavior
- optionally clear previous stylization
- build or load a stylization plan
- apply surface overrides and proxies
- update runtime debug state

### Return shape
```json
{
  "ok": true,
  "appliedCount": 7,
  "skippedCount": 2,
  "warnings": []
}
```

---

## Tool F — `edr_toggle_debug_overlay`
### Purpose
Turn the semantic / planning / correction debug UI on or off.

### Inputs
```csharp
public class ToggleDebugOverlayParams
{
    public bool Visible { get; set; }
}
```

### Expected behavior
- toggle debug HUD and scene overlays

---

## Tool G — `edr_run_smoke_test`
### Purpose
Run a compact repository-specific health check.

### Inputs
```csharp
public class RunSmokeTestParams
{
    public bool ValidateSetup { get; set; } = true;
    public bool ValidateThemeAssets { get; set; } = true;
    public bool ValidateCanonicalScene { get; set; } = true;
}
```

### Expected behavior
- run project-specific checks
- return pass/fail and action list

---

# 3. Recommended future tools after Phase 1
Add only after the current stylization slice is stable:
- `edr_spawn_npc_anchor`
- `edr_bind_whiteboard_keywords`
- `edr_save_correction_snapshot`
- `edr_restore_correction_snapshot`

For the generated-object side branch, consider these only after the file protocol is stable:
- `edr_export_generated_object_request`
- `edr_validate_generated_object_job`
- `edr_import_generated_proxy_candidate`
- `edr_run_generated_object_registration_test`

---

# 4. Strong design rules for custom tools

## Rule 1
Tools should be **high-level** and project-specific.

Bad:
- `move_gameobject_precisely`

Good:
- `edr_apply_theme_preset`

## Rule 2
Tools should be **safe to call repeatedly**.

That means:
- no duplicate stylized content on repeated calls
- reset paths exist
- output clearly reports what happened

## Rule 3
Tools should be **inspectable**.

Return:
- counts
- warnings
- paths
- IDs
- next recommended actions

## Rule 4
Avoid destructive default behavior.

If a tool may overwrite data:
- make it explicit in params
- document it

---

# 5. Example typed registration pattern
This is the recommended style for this repository.

```csharp
using Unity.AI.MCP.Editor.ToolRegistry;

[McpTool("edr_validate_setup", "Validate the SceneShift office room project setup")]
public static object ValidateSetup(ValidateSetupParams parameters)
{
    return new
    {
        ok = true,
        consoleErrors = 0,
        notes = new[] { "Stub result" }
    };
}

public class ValidateSetupParams
{
    [McpDescription("Check package setup", Required = false)]
    public bool CheckPackages { get; set; } = true;

    [McpDescription("Check canonical scenes", Required = false)]
    public bool CheckScenes { get; set; } = true;

    [McpDescription("Check console messages", Required = false)]
    public bool CheckConsole { get; set; } = true;
}
```

---

# 6. Order for implementing custom tools
Implement in this order:
1. `edr_validate_setup`
2. `edr_export_room_semantics`
3. `edr_build_stylization_plan`
4. `edr_apply_theme_preset`
5. `edr_run_smoke_test`
6. `edr_toggle_debug_overlay`

This sequence mirrors the real development flow.

---

# 7. When Codex should use built-in tools instead
Use built-in Unity MCP tools when:
- creating folders / files
- editing scripts
- creating simple GameObjects
- reading console output
- saving/opening scenes

Use custom project tools when:
- reasoning about room semantics
- planning stylization
- applying themes
- validating this project’s own rules

---

# 8. Immediate recommendation
The project now has the minimum scene/bootstrap/planner/theme foundation, so custom MCP tools are useful but still not blocking feature work.

The first useful custom tools are:
- `edr_validate_setup`
- `edr_run_smoke_test`

After that, add tools only where they remove repeated manual risk:
- semantic snapshot export,
- generated-object job validation,
- smoke-test/report generation.

Do not add MCP tools as a substitute for stabilizing the Unity scene or documenting the manual workflow.
