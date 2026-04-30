using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[DisallowMultipleComponent]
public class ThemeIntentController : MonoBehaviour
{
    private const string GenericScaffoldThemeId = "generic_room_style_scaffold";
    private const string GenericScaffoldDisplayName = "Generic Room Style Scaffold";

    [Header("Themes")]
    [SerializeField] private List<ThemeProfile> availableThemes = new();
    [SerializeField] private int defaultThemeIndex;

    [Header("Generic Scaffold")]
    [SerializeField] private bool useGenericRoomStyleScaffold = true;
    [SerializeField] private ThemeProfile genericRoomStyleScaffold;
    [SerializeField] private bool cloneSourceProxyReferences = true;

    [Header("Debug Input")]
    [SerializeField] private bool selectDefaultOnEnable = true;
    [SerializeField] private bool enableKeyboardShortcuts = true;
    [SerializeField] private KeyCode previousThemeKey = KeyCode.LeftBracket;
    [SerializeField] private KeyCode nextThemeKey = KeyCode.RightBracket;

    public event Action<ThemeProfile> ThemeChanged;

    public IReadOnlyList<ThemeProfile> AvailableThemes => availableThemes;
    public ThemeProfile ActiveTheme => useGenericRoomStyleScaffold ? ResolveGenericScaffoldTheme() : _activeTheme;
    public ThemeProfile ActiveScaffoldSourceTheme => _activeTheme;
    public int ActiveThemeIndex => _activeThemeIndex;
    public bool UsesGenericRoomStyleScaffold => useGenericRoomStyleScaffold;

    private ThemeProfile _activeTheme;
    private int _activeThemeIndex = -1;
    private ThemeProfile _runtimeGenericScaffold;

    private void OnEnable()
    {
        if (selectDefaultOnEnable && availableThemes.Count > 0)
        {
            SelectThemeByIndex(Mathf.Clamp(defaultThemeIndex, 0, availableThemes.Count - 1));
        }
    }

    private void Update()
    {
        if (!enableKeyboardShortcuts || useGenericRoomStyleScaffold || availableThemes.Count == 0)
        {
            return;
        }

        if (WasShortcutPressed(previousThemeKey))
        {
            CycleTheme(-1);
        }

        if (WasShortcutPressed(nextThemeKey))
        {
            CycleTheme(1);
        }

        var maxShortcutCount = Mathf.Min(availableThemes.Count, 9);
        for (var index = 0; index < maxShortcutCount; index++)
        {
            var alphaKey = (KeyCode)((int)KeyCode.Alpha1 + index);
            var keypadKey = (KeyCode)((int)KeyCode.Keypad1 + index);
            if (WasShortcutPressed(alphaKey) || WasShortcutPressed(keypadKey))
            {
                SelectThemeByIndex(index);
                break;
            }
        }
    }

    public bool SelectThemeByIndex(int index)
    {
        if (index < 0 || index >= availableThemes.Count)
        {
            return false;
        }

        var theme = availableThemes[index];
        if (theme == null)
        {
            Debug.LogWarning($"[ThemeIntentController] Theme slot {index} is empty.", this);
            return false;
        }

        if (_activeTheme == theme && _activeThemeIndex == index)
        {
            return true;
        }

        _activeThemeIndex = index;
        _activeTheme = theme;
        ThemeChanged?.Invoke(ActiveTheme);
        var activeTheme = ActiveTheme;
        if (useGenericRoomStyleScaffold && activeTheme != null)
        {
            Debug.Log($"[ThemeIntentController] Active scaffold -> {activeTheme.DisplayName} (source preset: {theme.DisplayName})", this);
        }
        else
        {
            Debug.Log($"[ThemeIntentController] Active theme -> {theme.DisplayName}", this);
        }

        return true;
    }

    public void CycleTheme(int direction)
    {
        if (availableThemes.Count == 0)
        {
            return;
        }

        var startIndex = _activeThemeIndex >= 0 ? _activeThemeIndex : Mathf.Clamp(defaultThemeIndex, 0, availableThemes.Count - 1);
        var nextIndex = (startIndex + direction + availableThemes.Count) % availableThemes.Count;
        SelectThemeByIndex(nextIndex);
    }

    public string GetDebugSummary()
    {
        var builder = new StringBuilder(256);
        builder.AppendLine("Theme");

        var activeTheme = ActiveTheme;
        if (activeTheme == null)
        {
            builder.AppendLine("  Active: none");
        }
        else
        {
            builder.AppendLine($"  Active: {activeTheme.DisplayName}");
            builder.AppendLine($"  Category: {activeTheme.Category}");
            builder.AppendLine($"  Accent: #{ColorUtility.ToHtmlStringRGB(activeTheme.AccentColor)}");
            if (useGenericRoomStyleScaffold && _activeTheme != null)
            {
                builder.AppendLine($"  Source Preset: {_activeTheme.DisplayName}");
            }
        }

        if (availableThemes.Count > 0)
        {
            builder.Append(useGenericRoomStyleScaffold ? "  Source Presets:" : "  Options:");
            for (var index = 0; index < availableThemes.Count; index++)
            {
                var theme = availableThemes[index];
                var label = theme != null ? theme.DisplayName : "Missing";
                builder.Append($" [{index + 1}] {label}");
            }

            builder.AppendLine();
            builder.AppendLine(useGenericRoomStyleScaffold
                ? "  Keys: scaffold cycling disabled; switch Style from the runtime panel"
                : $"  Keys: {previousThemeKey} / {nextThemeKey} to cycle");
        }
        else
        {
            builder.AppendLine("  Options: no theme assets assigned");
        }

        return builder.ToString().TrimEnd();
    }

    private static bool WasShortcutPressed(KeyCode keyCode)
    {
#if ENABLE_INPUT_SYSTEM
        var keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return false;
        }

        var keyControl = keyCode switch
        {
            KeyCode.LeftBracket => keyboard.leftBracketKey,
            KeyCode.RightBracket => keyboard.rightBracketKey,
            KeyCode.Alpha1 => keyboard.digit1Key,
            KeyCode.Alpha2 => keyboard.digit2Key,
            KeyCode.Alpha3 => keyboard.digit3Key,
            KeyCode.Alpha4 => keyboard.digit4Key,
            KeyCode.Alpha5 => keyboard.digit5Key,
            KeyCode.Alpha6 => keyboard.digit6Key,
            KeyCode.Alpha7 => keyboard.digit7Key,
            KeyCode.Alpha8 => keyboard.digit8Key,
            KeyCode.Alpha9 => keyboard.digit9Key,
            KeyCode.Keypad1 => keyboard.numpad1Key,
            KeyCode.Keypad2 => keyboard.numpad2Key,
            KeyCode.Keypad3 => keyboard.numpad3Key,
            KeyCode.Keypad4 => keyboard.numpad4Key,
            KeyCode.Keypad5 => keyboard.numpad5Key,
            KeyCode.Keypad6 => keyboard.numpad6Key,
            KeyCode.Keypad7 => keyboard.numpad7Key,
            KeyCode.Keypad8 => keyboard.numpad8Key,
            KeyCode.Keypad9 => keyboard.numpad9Key,
            _ => null,
        };

        return keyControl != null && keyControl.wasPressedThisFrame;
#else
        return Input.GetKeyDown(keyCode);
#endif
    }

    private ThemeProfile ResolveGenericScaffoldTheme()
    {
        if (genericRoomStyleScaffold != null)
        {
            return genericRoomStyleScaffold;
        }

        if (_runtimeGenericScaffold != null)
        {
            return _runtimeGenericScaffold;
        }

        _runtimeGenericScaffold = CreateRuntimeGenericScaffold(ResolveSourceThemeForGenericScaffold());
        return _runtimeGenericScaffold;
    }

    private ThemeProfile ResolveSourceThemeForGenericScaffold()
    {
        if (_activeTheme != null)
        {
            return _activeTheme;
        }

        for (var index = 0; index < availableThemes.Count; index++)
        {
            if (availableThemes[index] != null)
            {
                return availableThemes[index];
            }
        }

        return null;
    }

    private ThemeProfile CreateRuntimeGenericScaffold(ThemeProfile sourceTheme)
    {
        var scaffold = ScriptableObject.CreateInstance<ThemeProfile>();
        scaffold.name = GenericScaffoldDisplayName;
        scaffold.hideFlags = HideFlags.DontSave;
        scaffold.ThemeId = GenericScaffoldThemeId;
        scaffold.DisplayName = GenericScaffoldDisplayName;
        scaffold.ShortDescription = "Internal neutral scaffold for room semantics, safe fallbacks, and spatial rules. User-facing visual identity comes from the active Style.";
        scaffold.Category = ThemeCategory.Experimental;
        scaffold.AccentColor = new Color(0.55f, 0.78f, 0.95f, 1f);
        scaffold.SurfaceMaterials = BuildGenericSurfaceMaterials();
        scaffold.Mood = ThemeMoodSettings.Default;
        scaffold.PreserveCollisionSensitiveFootprints = true;

        if (cloneSourceProxyReferences && sourceTheme != null)
        {
            scaffold.DefaultTableProxy = sourceTheme.DefaultTableProxy;
            scaffold.DefaultSeatProxy = sourceTheme.DefaultSeatProxy;
            scaffold.DefaultStorageProxy = sourceTheme.DefaultStorageProxy;
            scaffold.DefaultScreenTreatmentPrefab = sourceTheme.DefaultScreenTreatmentPrefab;
        }

        scaffold.ReplacementRules = BuildGenericReplacementRules();
        return scaffold;
    }

    private static ThemeSurfaceMaterials BuildGenericSurfaceMaterials()
    {
        return new ThemeSurfaceMaterials
        {
            PatternFamily = ThemeSurfacePatternFamily.CleanPanels,
            WallColor = new Color(0.72f, 0.82f, 0.9f, 1f),
            FloorColor = new Color(0.38f, 0.48f, 0.56f, 1f),
            CeilingColor = new Color(0.82f, 0.88f, 0.94f, 1f),
            DoorFrameColor = new Color(0.58f, 0.72f, 0.86f, 1f),
            WindowFrameColor = new Color(0.66f, 0.82f, 0.94f, 1f),
            WindowVistaColor = new Color(0.7f, 0.84f, 1f, 1f),
            SurfaceOpacity = 1f,
            EmissionIntensity = 0.16f,
            TextureTiling = 4f,
            PatternStrength = 0.65f,
            WallTexturePromptHint = "Neutral room-scale wall material scaffold. Let the active user Style decide the visual language while preserving readable real wall boundaries.",
            FloorTexturePromptHint = "Neutral walkable floor material scaffold. Let the active user Style decide materials and motifs while preserving path readability.",
            CeilingTreatmentPromptHint = "Neutral overhead surface scaffold. Let the active user Style decide ceiling mood while preserving the real ceiling plane.",
            DoorFramePromptHint = "Neutral full-door or portal-panel scaffold. Let the active user Style decide the door surface, silhouette cues, paneling, and hardware while keeping it flat and doorway-aligned.",
            WindowFramePromptHint = "Neutral window-frame scaffold. Let the active user Style decide trim, silhouette cues, and edge detail while keeping the opening visible.",
            WindowVistaPromptHint = "Wide exterior vista scaffold. Let the active user Style decide the outside scene; no window frame, room interior, people, or text.",
        };
    }

    private static List<SemanticReplacementRule> BuildGenericReplacementRules()
    {
        return new List<SemanticReplacementRule>
        {
            CreateGenericRule("wall", "boundary", ReplacementMode.MaterialOverride, false, "style-driven wall surface", "Apply the active user Style to the wall as a readable flat material or decal layer."),
            CreateGenericRule("floor", "boundary", ReplacementMode.MaterialOverride, false, "style-driven floor surface", "Apply the active user Style to the floor while preserving walkable-space clarity."),
            CreateGenericRule("ceiling", "boundary", ReplacementMode.MaterialOverride, false, "style-driven ceiling surface", "Apply the active user Style to the ceiling without hiding real boundaries."),
            CreateGenericRule("door_frame", "passage_frame", ReplacementMode.MaterialOverride, false, "style-driven door trim", "Apply the active user Style to the door-frame trim; keep the center open."),
            CreateGenericRule("window_frame", "view_frame", ReplacementMode.MaterialOverride, false, "style-driven window trim", "Apply the active user Style to the window-frame trim; keep the opening visible."),
            CreateGenericRule("table", "support_surface", ReplacementMode.ProxyPrefab, true, "style-driven support table", "Generate or fit a style-consistent table while preserving footprint, support height, and yaw."),
            CreateGenericRule("storage", "storage", ReplacementMode.ProxyPrefab, true, "style-driven storage unit", "Generate or fit a style-consistent storage unit while preserving footprint and clearance."),
            CreateGenericRule("screen", "display_surface", ReplacementMode.Overlay, false, "style-driven display surface", "Apply a style-consistent display or board treatment while preserving the display role."),
            CreateGenericRule("seating", "seating", ReplacementMode.ProxyPrefab, true, "style-driven seating", "Generate or fit style-consistent seating while preserving seating function and clearance."),
            CreateGenericRule("seat", "seating", ReplacementMode.ProxyPrefab, true, "style-driven seating", "Generate or fit style-consistent seating while preserving seating function and clearance."),
            CreateGenericRule("bed", "rest_surface", ReplacementMode.ProxyPrefab, true, "style-driven bed", "Generate or fit a style-consistent bed while preserving footprint and rest-surface role."),
            CreateGenericRule("couch", "seating", ReplacementMode.ProxyPrefab, true, "style-driven couch", "Generate or fit a style-consistent couch while preserving seating function and clearance."),
            CreateGenericRule("lamp", "lighting", ReplacementMode.ProxyPrefab, true, "style-driven lamp", "Generate or fit a style-consistent lamp while preserving its lighting-object role."),
            CreateGenericRule("plant", "decorative_plant", ReplacementMode.ProxyPrefab, true, "style-driven plant", "Generate or fit a style-consistent plant while preserving its decorative object role."),
        };
    }

    private static SemanticReplacementRule CreateGenericRule(
        string semanticLabel,
        string functionTag,
        ReplacementMode mode,
        bool collisionSensitive,
        string replicaName,
        string appearancePrompt)
    {
        return new SemanticReplacementRule
        {
            SemanticLabel = semanticLabel,
            FunctionTag = functionTag,
            Mode = mode,
            PreserveFootprint = true,
            PreserveYawOrientation = true,
            CollisionSensitive = collisionSensitive,
            ReplicaName = replicaName,
            ReplicaFunction = functionTag,
            AppearancePrompt = appearancePrompt,
            Notes = $"Generic scaffold rule for {semanticLabel}; visual identity comes from the active user Style.",
        };
    }
}
