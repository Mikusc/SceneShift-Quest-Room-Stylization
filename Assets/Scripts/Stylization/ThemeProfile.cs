using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "ThemeProfile_", menuName = "SceneShift/Theme Profile")]
public class ThemeProfile : ScriptableObject
{
    [Header("Identity")]
    public string ThemeId = "theme_id";
    public string DisplayName = "New Theme";
    [TextArea(2, 4)] public string ShortDescription = "Describe the style intent for this theme.";
    public ThemeCategory Category = ThemeCategory.Experimental;

    [Header("Palette")]
    public Color AccentColor = Color.cyan;
    public ThemeSurfaceMaterials SurfaceMaterials = new();
    public ThemeMoodSettings Mood = ThemeMoodSettings.Default;

    [Header("Semantic Proxies")]
    public GameObject DefaultTableProxy;
    public GameObject DefaultSeatProxy;
    public GameObject DefaultStorageProxy;
    public GameObject DefaultScreenTreatmentPrefab;
    public bool PreserveCollisionSensitiveFootprints = true;

    [Header("Rules")]
    public List<SemanticReplacementRule> ReplacementRules = new();

    public GameObject GetDefaultProxy(string semanticLabel)
    {
        if (string.IsNullOrWhiteSpace(semanticLabel))
        {
            return null;
        }

        return semanticLabel.ToLowerInvariant() switch
        {
            "table" => DefaultTableProxy,
            "storage" => DefaultStorageProxy,
            "seat" => DefaultSeatProxy,
            "seating" => DefaultSeatProxy,
            "screen" => DefaultScreenTreatmentPrefab,
            _ => null,
        };
    }
}

[Serializable]
public class ThemeSurfaceMaterials
{
    public Material WallMaterial;
    public Material FloorMaterial;
    public Material CeilingMaterial;
    public Material DoorFrameMaterial;
    public Material WindowFrameMaterial;
    public ThemeSurfacePatternFamily PatternFamily = ThemeSurfacePatternFamily.FutureLab;
    public Color WallColor = new(0.32f, 0.72f, 0.92f, 0.78f);
    public Color FloorColor = new(0.18f, 0.52f, 0.66f, 0.7f);
    public Color CeilingColor = new(0.78f, 0.9f, 0.98f, 0.6f);
    public Color DoorFrameColor = new(0.34f, 0.86f, 0.94f, 0.82f);
    public Color WindowFrameColor = new(0.68f, 0.9f, 0.98f, 0.72f);
    public Color WindowVistaColor = new(0.38f, 0.78f, 1f, 0.78f);
    [Range(0f, 1f)] public float SurfaceOpacity = 0.78f;
    [Range(0f, 4f)] public float EmissionIntensity = 0.18f;
    [Range(1f, 12f)] public float TextureTiling = 4f;
    [Range(0f, 1f)] public float PatternStrength = 0.72f;

    [Header("Roomify Prompt Hints")]
    [TextArea(2, 4)] public string WallTexturePromptHint = "Subtle seamless wall material that supports the theme without overpowering room readability.";
    [TextArea(2, 4)] public string FloorTexturePromptHint = "Durable seamless floor material that keeps walkable space legible.";
    [TextArea(2, 4)] public string CeilingTreatmentPromptHint = "Lightweight ceiling or ambient overhead treatment that supports the room mood without hiding real boundaries.";
    [TextArea(2, 4)] public string DoorFramePromptHint = "Stylized door frame trim treatment that preserves the open passage and does not block real-world affordances.";
    [TextArea(2, 4)] public string WindowFramePromptHint = "Stylized window frame trim or translucent glass-edge treatment that preserves the view and does not cover the opening.";
    [TextArea(2, 4)] public string WindowVistaPromptHint = "Wide stylized exterior vista seen beyond a real room window; distant scenery only, no window frame, no room interior.";

    public Material GetMaterialOverride(ThemeSurfaceKind surfaceKind)
    {
        return surfaceKind switch
        {
            ThemeSurfaceKind.Wall => WallMaterial,
            ThemeSurfaceKind.Floor => FloorMaterial,
            ThemeSurfaceKind.Ceiling => CeilingMaterial,
            ThemeSurfaceKind.DoorFrame => DoorFrameMaterial,
            ThemeSurfaceKind.WindowFrame => WindowFrameMaterial,
            ThemeSurfaceKind.WindowVista => null,
            _ => null,
        };
    }

    public Color GetTintColor(ThemeSurfaceKind surfaceKind)
    {
        return surfaceKind switch
        {
            ThemeSurfaceKind.Wall => ApplyOpacity(WallColor),
            ThemeSurfaceKind.Floor => ApplyOpacity(FloorColor),
            ThemeSurfaceKind.Ceiling => ApplyOpacity(CeilingColor),
            ThemeSurfaceKind.DoorFrame => ApplyOpacity(DoorFrameColor),
            ThemeSurfaceKind.WindowFrame => ApplyOpacity(WindowFrameColor),
            ThemeSurfaceKind.WindowVista => ApplyOpacity(WindowVistaColor),
            _ => Color.white,
        };
    }

    private Color ApplyOpacity(Color color)
    {
        color.a = Mathf.Clamp01(color.a * SurfaceOpacity);
        return color;
    }
}

[Serializable]
public class ThemeMoodSettings
{
    public static ThemeMoodSettings Default => new()
    {
        MainLightColor = Color.white,
        MainLightIntensity = 1f,
        AmbientSkyColor = new Color(0.2f, 0.22f, 0.26f),
        AmbientEquatorColor = new Color(0.14f, 0.15f, 0.18f),
        AmbientGroundColor = new Color(0.08f, 0.08f, 0.09f),
    };

    public Color MainLightColor = Color.white;
    [Min(0f)] public float MainLightIntensity = 1f;
    public Color AmbientSkyColor = new(0.2f, 0.22f, 0.26f);
    public Color AmbientEquatorColor = new(0.14f, 0.15f, 0.18f);
    public Color AmbientGroundColor = new(0.08f, 0.08f, 0.09f);
}

[Serializable]
public class SemanticReplacementRule
{
    public string SemanticLabel = "wall";
    public string FunctionTag = "boundary";
    public ReplacementMode Mode = ReplacementMode.Skip;
    public GameObject ProxyPrefab;
    public Material PrimaryMaterial;
    public Material SecondaryMaterial;
    public bool PreserveFootprint = true;
    public bool PreserveYawOrientation = true;
    public bool CollisionSensitive = true;
    [Header("Roomify Mapping")]
    public string ReplicaName;
    public string ReplicaFunction;
    [TextArea(3, 6)] public string AppearancePrompt;
    [TextArea(1, 3)] public string Notes;
}

public enum ReplacementMode
{
    Skip,
    MaterialOverride,
    ProxyPrefab,
    Overlay,
    FXOnly,
}

public enum ThemeCategory
{
    Future,
    Arcane,
    Minimal,
    Experimental,
}

public enum ThemeSurfaceKind
{
    Wall,
    Floor,
    Ceiling,
    DoorFrame,
    WindowFrame,
    WindowVista,
}

public enum ThemeSurfacePatternFamily
{
    FutureLab,
    ArcaneChamber,
    CleanPanels,
}
