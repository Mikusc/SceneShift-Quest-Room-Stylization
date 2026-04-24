using System;
using System.IO;
using System.Text;
using UnityEngine;

[DisallowMultipleComponent]
public class SurfaceTexturePromptBuilder : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private ThemeIntentController themeIntentController;

    [Header("Prompt Export")]
    [SerializeField] private bool writePromptsOnThemeChanged = true;
    [SerializeField] private string jobFolderName = "SurfaceTextureJobs";

    public event Action SummaryChanged;

    public SurfaceTexturePromptSet LatestPromptSet => _latestPromptSet;
    public string LatestSummary => _latestSummary;

    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private SurfaceTexturePromptSet _latestPromptSet;
    private string _latestSummary = "[SurfaceTexturePromptBuilder]\nState: waiting\nHint: select a theme to write Roomify-style surface prompts.";

    private void Reset()
    {
        ResolveReferences();
    }

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        Subscribe();

        if (writePromptsOnThemeChanged && themeIntentController != null && themeIntentController.ActiveTheme != null)
        {
            BuildAndWrite(themeIntentController.ActiveTheme, "enabled");
        }
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    [ContextMenu("Write Active Surface Texture Prompts")]
    public void WriteActiveSurfaceTexturePrompts()
    {
        ResolveReferences();
        if (themeIntentController == null || themeIntentController.ActiveTheme == null)
        {
            PublishWaitingState("waiting-for-theme");
            return;
        }

        BuildAndWrite(themeIntentController.ActiveTheme, "manual");
    }

    private void ResolveReferences()
    {
        if (themeIntentController == null)
        {
            themeIntentController = FindAnyObjectByType<ThemeIntentController>();
        }
    }

    private void Subscribe()
    {
        if (themeIntentController == null)
        {
            return;
        }

        themeIntentController.ThemeChanged -= HandleThemeChanged;
        themeIntentController.ThemeChanged += HandleThemeChanged;
    }

    private void Unsubscribe()
    {
        if (themeIntentController != null)
        {
            themeIntentController.ThemeChanged -= HandleThemeChanged;
        }
    }

    private void HandleThemeChanged(ThemeProfile theme)
    {
        if (!writePromptsOnThemeChanged)
        {
            BuildPromptSet(theme, ResolveJobFolder());
            PublishSummary("theme-changed", wroteFiles: false);
            return;
        }

        BuildAndWrite(theme, "theme-changed");
    }

    private void BuildAndWrite(ThemeProfile theme, string reason)
    {
        if (theme == null)
        {
            PublishWaitingState("missing-theme");
            return;
        }

        var jobFolder = ResolveJobFolder();
        Directory.CreateDirectory(jobFolder);
        BuildPromptSet(theme, jobFolder);

        foreach (var entry in _latestPromptSet.Entries)
        {
            File.WriteAllText(entry.PromptPath, FormatPromptFile(_latestPromptSet, entry), Utf8NoBom);
        }

        var jsonPath = Path.Combine(jobFolder, $"{SanitizeFileName(theme.ThemeId)}_surface_prompts.json");
        File.WriteAllText(jsonPath, JsonUtility.ToJson(_latestPromptSet, prettyPrint: true), Utf8NoBom);

        PublishSummary(reason, wroteFiles: true, jsonPath);
    }

    private void BuildPromptSet(ThemeProfile theme, string jobFolder)
    {
        _latestPromptSet = new SurfaceTexturePromptSet
        {
            ThemeId = theme.ThemeId,
            ThemeDisplayName = theme.DisplayName,
            ThemeDescription = theme.ShortDescription,
            CreatedAtIsoUtc = DateTime.UtcNow.ToString("O"),
            JobFolder = jobFolder,
        };

        _latestPromptSet.Entries.Add(BuildEntry(theme, ThemeSurfaceKind.Wall, "wall", "seamless_wall_pbr_texture"));
        _latestPromptSet.Entries.Add(BuildEntry(theme, ThemeSurfaceKind.Floor, "floor", "seamless_floor_pbr_texture"));
        _latestPromptSet.Entries.Add(BuildEntry(theme, ThemeSurfaceKind.Ceiling, "ceiling", "ceiling_treatment_or_skybox_prompt"));
    }

    private SurfaceTexturePromptEntry BuildEntry(
        ThemeProfile theme,
        ThemeSurfaceKind surfaceKind,
        string semanticLabel,
        string outputRole)
    {
        var promptHint = GetPromptHint(theme, surfaceKind);
        var promptPath = Path.Combine(
            ResolveJobFolder(),
            $"{SanitizeFileName(theme.ThemeId)}_{semanticLabel}.prompt.txt");

        return new SurfaceTexturePromptEntry
        {
            SemanticLabel = semanticLabel,
            SurfaceKind = surfaceKind,
            OutputRole = outputRole,
            Prompt = BuildPrompt(theme, surfaceKind, promptHint),
            NegativePrompt = BuildNegativePrompt(surfaceKind),
            PromptPath = promptPath,
            SeamlessTileable = surfaceKind != ThemeSurfaceKind.Ceiling,
            PbrMaterial = surfaceKind != ThemeSurfaceKind.Ceiling,
            RuntimeFallbackAvailable = true,
        };
    }

    private static string BuildPrompt(ThemeProfile theme, ThemeSurfaceKind surfaceKind, string promptHint)
    {
        var surfaceLabel = surfaceKind.ToString().ToLowerInvariant();
        var colorHex = ColorUtility.ToHtmlStringRGB(theme.AccentColor);
        var tileableRequirement = surfaceKind == ThemeSurfaceKind.Ceiling
            ? "If producing a ceiling material, keep it subtle and tileable; if producing a skybox concept, keep the real room boundary readable."
            : "Generate a seamless, tileable PBR material texture suitable for repeated use on large room surfaces.";

        return
            $"Target style: {theme.DisplayName}\n" +
            $"Style intent: {theme.ShortDescription}\n" +
            $"Accent color: #{colorHex}\n" +
            $"Surface: {surfaceLabel}\n" +
            $"Roomify role: boundary element aligned to a real MRUK spatial scaffold.\n" +
            $"{tileableRequirement}\n" +
            "Output should be style-consistent with furniture proxies, readable in mixed reality, and not overly eye-catching.\n" +
            "Prefer albedo/base color plus matching normal and roughness/metallic guidance when the backend supports PBR maps.\n" +
            $"Theme-specific hint: {promptHint}";
    }

    private static string BuildNegativePrompt(ThemeSurfaceKind surfaceKind)
    {
        var common = "no furniture, no people, no logos, no readable text, no perspective room render, no strong shadows, no object silhouettes";
        return surfaceKind == ThemeSurfaceKind.Ceiling
            ? $"{common}, no opaque virtual ceiling that hides real room boundaries"
            : $"{common}, no seams, no non-tileable borders, no large focal objects";
    }

    private static string GetPromptHint(ThemeProfile theme, ThemeSurfaceKind surfaceKind)
    {
        return surfaceKind switch
        {
            ThemeSurfaceKind.Wall => theme.SurfaceMaterials.WallTexturePromptHint,
            ThemeSurfaceKind.Floor => theme.SurfaceMaterials.FloorTexturePromptHint,
            ThemeSurfaceKind.Ceiling => theme.SurfaceMaterials.CeilingTreatmentPromptHint,
            _ => string.Empty,
        };
    }

    private static string FormatPromptFile(SurfaceTexturePromptSet promptSet, SurfaceTexturePromptEntry entry)
    {
        var builder = new StringBuilder(1024);
        builder.AppendLine("[Surface Texture Prompt]");
        builder.AppendLine($"Theme: {promptSet.ThemeDisplayName} ({promptSet.ThemeId})");
        builder.AppendLine($"Semantic: {entry.SemanticLabel}");
        builder.AppendLine($"Output Role: {entry.OutputRole}");
        builder.AppendLine();
        builder.AppendLine("Prompt:");
        builder.AppendLine(entry.Prompt);
        builder.AppendLine();
        builder.AppendLine("Negative Prompt:");
        builder.AppendLine(entry.NegativePrompt);
        builder.AppendLine();
        builder.AppendLine("Implementation Notes:");
        builder.AppendLine("- Save generated materials as theme assets before using them in a demo build.");
        builder.AppendLine("- Keep runtime procedural textures as the deterministic fallback.");
        builder.AppendLine("- Walls should be applied to MRUK wall scaffolds with a 0.05m outward offset.");
        return builder.ToString();
    }

    private string ResolveJobFolder()
    {
        var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
        return Path.Combine(projectRoot, "Library", string.IsNullOrWhiteSpace(jobFolderName) ? "SurfaceTextureJobs" : jobFolderName);
    }

    private void PublishWaitingState(string reason)
    {
        _latestSummary = $"[SurfaceTexturePromptBuilder]\nState: {reason}\nHint: assign/select a theme before writing surface prompts.";
        SummaryChanged?.Invoke();
    }

    private void PublishSummary(string reason, bool wroteFiles, string jsonPath = null)
    {
        var count = _latestPromptSet?.Entries?.Count ?? 0;
        var builder = new StringBuilder(512);
        builder.AppendLine("[SurfaceTexturePromptBuilder]");
        builder.AppendLine(wroteFiles ? "State: prompts-written" : "State: prompts-built");
        builder.AppendLine($"Reason: {reason}");
        builder.AppendLine($"Theme: {_latestPromptSet?.ThemeDisplayName ?? "none"}");
        builder.AppendLine($"Entries: {count}");
        builder.AppendLine($"Folder: {_latestPromptSet?.JobFolder ?? "none"}");
        if (!string.IsNullOrWhiteSpace(jsonPath))
        {
            builder.Append($"JSON: {jsonPath}");
        }

        _latestSummary = builder.ToString().TrimEnd();
        SummaryChanged?.Invoke();
    }

    private static string SanitizeFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "theme";
        }

        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalidChar, '_');
        }

        return value;
    }
}
