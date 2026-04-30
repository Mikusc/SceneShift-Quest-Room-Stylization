using System;
using System.IO;
using System.Text;
using UnityEngine;

[DisallowMultipleComponent]
public class SurfaceTexturePromptBuilder : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private ThemeIntentController themeIntentController;
    [SerializeField] private RuntimeStyleIntentController runtimeStyleIntentController;

    [Header("Prompt Export")]
    [SerializeField] private bool writePromptsOnThemeChanged = true;
    [SerializeField] private string jobFolderName = "SurfaceTextureJobs";
    [SerializeField] private string outputFolderName = "SurfaceTextureOutputs";

    public event Action SummaryChanged;

    public SurfaceTexturePromptSet LatestPromptSet => _latestPromptSet;
    public string LatestSummary => _latestSummary;

    public const string PromptVersion = "surface_texture_v3_room_scale_openings";
    public const string PresetStyleVariantId = "preset";

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
        ResolveReferences();
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

        if (runtimeStyleIntentController == null)
        {
            runtimeStyleIntentController = FindAnyObjectByType<RuntimeStyleIntentController>();
        }
    }

    private void Subscribe()
    {
        if (themeIntentController != null)
        {
            themeIntentController.ThemeChanged -= HandleThemeChanged;
            themeIntentController.ThemeChanged += HandleThemeChanged;
        }

        if (runtimeStyleIntentController != null)
        {
            runtimeStyleIntentController.StyleIntentChanged -= HandleStyleIntentChanged;
            runtimeStyleIntentController.StyleIntentChanged += HandleStyleIntentChanged;
        }
    }

    private void Unsubscribe()
    {
        if (themeIntentController != null)
        {
            themeIntentController.ThemeChanged -= HandleThemeChanged;
        }

        if (runtimeStyleIntentController != null)
        {
            runtimeStyleIntentController.StyleIntentChanged -= HandleStyleIntentChanged;
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

    private void HandleStyleIntentChanged()
    {
        if (themeIntentController == null || themeIntentController.ActiveTheme == null)
        {
            PublishWaitingState("waiting-for-theme");
            return;
        }

        if (!writePromptsOnThemeChanged)
        {
            BuildPromptSet(themeIntentController.ActiveTheme, ResolveJobFolder());
            PublishSummary("style-intent-changed", wroteFiles: false);
            return;
        }

        BuildAndWrite(themeIntentController.ActiveTheme, "style-intent-changed");
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
            WriteJobRecord(entry);
        }

        var jsonPath = Path.Combine(jobFolder, $"{SanitizeFileName(_latestPromptSet.ThemeId)}_surface_prompts.json");
        if (_latestPromptSet != null && !string.Equals(_latestPromptSet.StyleVariantId, PresetStyleVariantId, StringComparison.Ordinal))
        {
            jsonPath = Path.Combine(jobFolder, $"{SanitizeFileName(_latestPromptSet.ThemeId)}_{SanitizeFileName(_latestPromptSet.StyleVariantId)}_surface_prompts.json");
        }

        File.WriteAllText(jsonPath, JsonUtility.ToJson(_latestPromptSet, prettyPrint: true), Utf8NoBom);

        PublishSummary(reason, wroteFiles: true, jsonPath);
    }

    private void BuildPromptSet(ThemeProfile theme, string jobFolder)
    {
        var runtimeIntent = runtimeStyleIntentController != null ? runtimeStyleIntentController.CurrentIntent : null;
        var styleVariantId = BuildStyleVariantId(runtimeIntent);
        var effectiveThemeId = RuntimeStyleIntentRequestUtility.BuildEffectiveThemeId(theme, runtimeIntent);
        var effectiveThemeDisplayName = RuntimeStyleIntentRequestUtility.BuildEffectiveThemeDisplayName(theme, runtimeIntent);
        var effectiveThemeDescription = RuntimeStyleIntentRequestUtility.BuildEffectiveThemeDescription(theme, runtimeIntent);
        _latestPromptSet = new SurfaceTexturePromptSet
        {
            ThemeId = effectiveThemeId,
            ThemeDisplayName = effectiveThemeDisplayName,
            ThemeDescription = effectiveThemeDescription,
            StyleVariantId = styleVariantId,
            UserStyleIntent = runtimeIntent != null ? runtimeIntent.UserIntent : string.Empty,
            StyleIntentSource = runtimeIntent != null ? runtimeIntent.Source : string.Empty,
            CreatedAtIsoUtc = DateTime.UtcNow.ToString("O"),
            JobFolder = jobFolder,
        };

        _latestPromptSet.Entries.Add(BuildEntry(theme, runtimeIntent, effectiveThemeId, effectiveThemeDisplayName, effectiveThemeDescription, styleVariantId, ThemeSurfaceKind.Wall, "wall", "large_scale_wall_material"));
        _latestPromptSet.Entries.Add(BuildEntry(theme, runtimeIntent, effectiveThemeId, effectiveThemeDisplayName, effectiveThemeDescription, styleVariantId, ThemeSurfaceKind.Floor, "floor", "large_scale_floor_material"));
        _latestPromptSet.Entries.Add(BuildEntry(theme, runtimeIntent, effectiveThemeId, effectiveThemeDisplayName, effectiveThemeDescription, styleVariantId, ThemeSurfaceKind.Ceiling, "ceiling", "large_scale_ceiling_treatment"));
        _latestPromptSet.Entries.Add(BuildEntry(theme, runtimeIntent, effectiveThemeId, effectiveThemeDisplayName, effectiveThemeDescription, styleVariantId, ThemeSurfaceKind.DoorFrame, "door_frame", "full_door_panel_or_portal_texture"));
        _latestPromptSet.Entries.Add(BuildEntry(theme, runtimeIntent, effectiveThemeId, effectiveThemeDisplayName, effectiveThemeDescription, styleVariantId, ThemeSurfaceKind.WindowFrame, "window_frame", "window_frame_trim_overlay_texture"));
        _latestPromptSet.Entries.Add(BuildEntry(theme, runtimeIntent, effectiveThemeId, effectiveThemeDisplayName, effectiveThemeDescription, styleVariantId, ThemeSurfaceKind.WindowVista, "window_vista", "wide_window_exterior_vista_overlay"));
    }

    private SurfaceTexturePromptEntry BuildEntry(
        ThemeProfile theme,
        RuntimeStyleIntent runtimeIntent,
        string effectiveThemeId,
        string effectiveThemeDisplayName,
        string effectiveThemeDescription,
        string styleVariantId,
        ThemeSurfaceKind surfaceKind,
        string semanticLabel,
        string outputRole)
    {
        var promptHint = GetPromptHint(theme, surfaceKind);
        var requestId = BuildRequestId(effectiveThemeId, semanticLabel, styleVariantId);
        var promptPath = Path.Combine(
            ResolveJobFolder(),
            $"{requestId}.prompt.txt");

        return new SurfaceTexturePromptEntry
        {
            RequestId = requestId,
            StyleVariantId = styleVariantId,
            UserStyleIntent = runtimeIntent != null ? runtimeIntent.UserIntent : string.Empty,
            StyleIntentSource = runtimeIntent != null ? runtimeIntent.Source : string.Empty,
            SemanticLabel = semanticLabel,
            SurfaceKind = surfaceKind,
            OutputRole = outputRole,
            PromptVersion = PromptVersion,
            Prompt = BuildPrompt(theme, runtimeIntent, effectiveThemeDisplayName, effectiveThemeDescription, surfaceKind, promptHint),
            NegativePrompt = BuildNegativePrompt(surfaceKind),
            ImageSize = GetImageSize(surfaceKind),
            PromptPath = promptPath,
            JobPath = Path.Combine(ResolveJobFolder(), $"{requestId}.surface.job.json"),
            OutputImagePath = Path.Combine(ResolveOutputFolder(), $"{requestId}.surface.png"),
            SeamlessTileable = surfaceKind is ThemeSurfaceKind.Wall or ThemeSurfaceKind.Floor,
            PbrMaterial = surfaceKind is ThemeSurfaceKind.Wall or ThemeSurfaceKind.Floor or ThemeSurfaceKind.Ceiling,
            RuntimeFallbackAvailable = true,
        };
    }

    private static string BuildPrompt(
        ThemeProfile theme,
        RuntimeStyleIntent runtimeIntent,
        string effectiveThemeDisplayName,
        string effectiveThemeDescription,
        ThemeSurfaceKind surfaceKind,
        string promptHint)
    {
        var surfaceLabel = ToSemanticLabel(surfaceKind);
        var colorHex = ColorUtility.ToHtmlStringRGB(RuntimeStyleColorUtility.ResolveAccentColor(theme, runtimeIntent));
        var runtimeStyleBlock = BuildRuntimeStyleBlock(runtimeIntent);
        var scaffoldLine = RuntimeStyleIntentRequestUtility.HasUserStyleIntent(runtimeIntent)
            ? $"Internal scaffold ThemeProfile: {theme.DisplayName}. Use it only for functional/material fallback; the user-facing visual identity is the active Style.\n"
            : string.Empty;
        var tileableRequirement = surfaceKind switch
        {
            ThemeSurfaceKind.Wall => "Create a seamless room-scale wall material. One visual repeat should feel roughly 2-3 meters wide, with large calm panels or broad material fields rather than tiny wallpaper motifs.",
            ThemeSurfaceKind.Floor => "Create a seamless room-scale floor material. One visual repeat should feel roughly 2 meters wide, with readable walking surfaces and broad panels rather than dense micro-patterns.",
            ThemeSurfaceKind.Ceiling => "Create a subtle room-scale ceiling material or overhead treatment. Use broad panels, soft lighting fields, or large motifs; keep real room boundaries readable.",
            ThemeSurfaceKind.DoorFrame => "Create a full stylized door panel or portal surface fitted to a real MRUK door opening. It may use a rectangular, arched, rounded, organic, or sci-fi silhouette implied by the texture, but it must stay flat and aligned to the doorway.",
            ThemeSurfaceKind.WindowFrame => "Create a stylized window-frame trim/decal treatment with an explicitly open/transparent center concept. The frame shape may be arched, rounded, organic, or sci-fi, but it must not block the view or daylight cues.",
            ThemeSurfaceKind.WindowVista => "Create a wide stylized exterior vista that appears beyond a real mixed-reality window opening; distant scenery only, soft depth, readable at room scale.",
            _ => "Create a seamless, tileable PBR-style material texture suitable for repeated use on large room surfaces. Avoid dense repeated wallpaper.",
        };
        var outputInstruction = surfaceKind switch
        {
            ThemeSurfaceKind.WindowVista =>
                "Output should read as an exterior panorama/backdrop layer, not a room interior. No window frame, no curtains, no foreground furniture, no people, no text.",
            ThemeSurfaceKind.DoorFrame =>
                "Output should read as a complete door/portal panel treatment, not just a thin frame. Include broad readable surface areas, optional inset panels, hardware, or style-specific ornament. No product render, no perspective scene.",
            ThemeSurfaceKind.WindowFrame =>
                "Output should read as reusable trim/frame material, edge glow, panel linework, or decal detail with a clear open center. No solid window slab, no filled center.",
            _ =>
                "Output should be style-consistent with furniture proxies, readable in mixed reality, and not overly eye-catching. Prefer low-frequency composition over small high-contrast repeated details.",
        };
        var pbrInstruction = surfaceKind == ThemeSurfaceKind.WindowVista
            ? "Use a 16:9 composition with a stable horizon and soft atmospheric depth so it can sit behind multiple window openings."
            : "Prefer albedo/base color plus matching normal and roughness/metallic guidance when the backend supports PBR maps.";

        return
            $"Target style: {effectiveThemeDisplayName}\n" +
            $"Style intent: {effectiveThemeDescription}\n" +
            $"{scaffoldLine}" +
            $"{runtimeStyleBlock}" +
            $"Accent color: #{colorHex}\n" +
            $"Surface: {surfaceLabel}\n" +
            $"Roomify role: boundary element aligned to a real MRUK spatial scaffold.\n" +
            $"{tileableRequirement}\n" +
            $"{outputInstruction}\n" +
            $"{pbrInstruction}\n" +
            $"Theme-specific hint: {promptHint}";
    }

    private static string BuildNegativePrompt(ThemeSurfaceKind surfaceKind)
    {
        var common = "no furniture, no people, no logos, no readable text, no perspective room render, no strong shadows, no object silhouettes";
        return surfaceKind switch
        {
            ThemeSurfaceKind.Ceiling => $"{common}, no opaque virtual ceiling that hides real room boundaries",
            ThemeSurfaceKind.DoorFrame => $"{common}, no thin-frame-only design, no handle-only product render, no perspective door photo, no protruding 3D geometry",
            ThemeSurfaceKind.WindowFrame => $"{common}, no opaque window cover, no blocked view, no filled center, no exterior scenery render",
            ThemeSurfaceKind.WindowVista => "no window frame, no room interior, no furniture, no people, no logos, no readable text, no close foreground objects, no black border, no UI",
            ThemeSurfaceKind.Wall => $"{common}, no seams, no non-tileable borders, no tiny wallpaper, no dense brick grid, no repeated small tiles",
            ThemeSurfaceKind.Floor => $"{common}, no seams, no non-tileable borders, no tiny floor tiles, no dense grid, no busy carpet pattern",
            _ => $"{common}, no seams, no non-tileable borders, no large focal objects, no dense repeated micro-pattern",
        };
    }

    private static string GetPromptHint(ThemeProfile theme, ThemeSurfaceKind surfaceKind)
    {
        return surfaceKind switch
        {
            ThemeSurfaceKind.Wall => theme.SurfaceMaterials.WallTexturePromptHint,
            ThemeSurfaceKind.Floor => theme.SurfaceMaterials.FloorTexturePromptHint,
            ThemeSurfaceKind.Ceiling => theme.SurfaceMaterials.CeilingTreatmentPromptHint,
            ThemeSurfaceKind.DoorFrame => theme.SurfaceMaterials.DoorFramePromptHint,
            ThemeSurfaceKind.WindowFrame => theme.SurfaceMaterials.WindowFramePromptHint,
            ThemeSurfaceKind.WindowVista => theme.SurfaceMaterials.WindowVistaPromptHint,
            _ => string.Empty,
        };
    }

    private static string GetImageSize(ThemeSurfaceKind surfaceKind)
    {
        return surfaceKind == ThemeSurfaceKind.WindowVista ? "16:9" : "1:1";
    }

    private void WriteJobRecord(SurfaceTexturePromptEntry entry)
    {
        if (_latestPromptSet == null || entry == null || string.IsNullOrWhiteSpace(entry.JobPath))
        {
            return;
        }

        var existingOutput = HasUsableOutputFile(entry.OutputImagePath);
        if (File.Exists(entry.JobPath))
        {
            var existing = JsonUtility.FromJson<SurfaceTextureJobRecord>(File.ReadAllText(entry.JobPath));
            if (ShouldPreserveExistingJob(existing))
            {
                return;
            }
        }

        var record = new SurfaceTextureJobRecord
        {
            RequestId = entry.RequestId,
            ThemeId = _latestPromptSet.ThemeId,
            ThemeDisplayName = _latestPromptSet.ThemeDisplayName,
            StyleVariantId = entry.StyleVariantId,
            UserStyleIntent = entry.UserStyleIntent,
            StyleIntentSource = entry.StyleIntentSource,
            SemanticLabel = entry.SemanticLabel,
            SurfaceKind = entry.SurfaceKind,
            OutputRole = entry.OutputRole,
            State = existingOutput ? SurfaceTextureJobState.TextureReady : SurfaceTextureJobState.PromptReady,
            PromptVersion = entry.PromptVersion,
            ImageSize = entry.ImageSize,
            PromptArtifactPath = entry.PromptPath,
            JobPath = entry.JobPath,
            OutputImagePath = entry.OutputImagePath,
            BackendAdapterName = string.Empty,
            StatusNote = existingOutput
                ? "Existing generated surface texture is available in the output cache."
                : "Surface texture prompt is ready for an image backend.",
            UpdatedAtIsoUtc = DateTime.UtcNow.ToString("O"),
        };

        File.WriteAllText(entry.JobPath, JsonUtility.ToJson(record, prettyPrint: true), Utf8NoBom);
    }

    private static bool ShouldPreserveExistingJob(SurfaceTextureJobRecord existing)
    {
        if (existing == null)
        {
            return false;
        }

        if (existing.State == SurfaceTextureJobState.BackendSubmitted)
        {
            return true;
        }

        if (existing.State is SurfaceTextureJobState.TextureReady or SurfaceTextureJobState.MaterialReady)
        {
            return HasUsableOutputFile(existing.OutputImagePath);
        }

        return false;
    }

    private static bool HasUsableOutputFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        return new FileInfo(path).Length > 0;
    }

    private static string FormatPromptFile(SurfaceTexturePromptSet promptSet, SurfaceTexturePromptEntry entry)
    {
        var builder = new StringBuilder(1024);
        builder.AppendLine("[Surface Texture Prompt]");
        builder.AppendLine($"Theme: {promptSet.ThemeDisplayName} ({promptSet.ThemeId})");
        builder.AppendLine($"Style Variant: {promptSet.StyleVariantId}");
        if (!string.IsNullOrWhiteSpace(promptSet.UserStyleIntent))
        {
            builder.AppendLine($"User Style Intent: {promptSet.UserStyleIntent}");
            builder.AppendLine($"Style Intent Source: {promptSet.StyleIntentSource}");
        }

        builder.AppendLine($"Semantic: {entry.SemanticLabel}");
        builder.AppendLine($"Output Role: {entry.OutputRole}");
        builder.AppendLine($"Prompt Version: {entry.PromptVersion}");
        builder.AppendLine($"Image Size: {entry.ImageSize}");
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
        builder.AppendLine("- Wall/floor/ceiling textures should read at room scale; avoid tiny repeated wallpaper, dense brick grids, or noisy micro-detail.");
        builder.AppendLine("- Door entries are applied as flat full-door/portal panels fitted to MRUK door anchors, not just thin frames.");
        builder.AppendLine("- Window frames should preserve transparent/open center areas and never block view.");
        builder.AppendLine("- Window vista images are exterior backdrops placed behind WINDOW_FRAME anchors, not wall materials or generated 3D objects.");
        return builder.ToString();
    }

    private string ResolveJobFolder()
    {
#if UNITY_EDITOR
        var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
        return Path.Combine(projectRoot, "Library", string.IsNullOrWhiteSpace(jobFolderName) ? "SurfaceTextureJobs" : jobFolderName);
#else
        return Path.Combine(Application.persistentDataPath, string.IsNullOrWhiteSpace(jobFolderName) ? "SurfaceTextureJobs" : jobFolderName);
#endif
    }

    private string ResolveOutputFolder()
    {
#if UNITY_EDITOR
        var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
        return Path.Combine(projectRoot, "Library", string.IsNullOrWhiteSpace(outputFolderName) ? "SurfaceTextureOutputs" : outputFolderName);
#else
        return Path.Combine(Application.persistentDataPath, string.IsNullOrWhiteSpace(outputFolderName) ? "SurfaceTextureOutputs" : outputFolderName);
#endif
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
        builder.AppendLine($"Style Variant: {_latestPromptSet?.StyleVariantId ?? "none"}");
        builder.AppendLine($"Entries: {count}");
        builder.AppendLine($"Folder: {_latestPromptSet?.JobFolder ?? "none"}");
        if (!string.IsNullOrWhiteSpace(jsonPath))
        {
            builder.Append($"JSON: {jsonPath}");
        }

        _latestSummary = builder.ToString().TrimEnd();
        SummaryChanged?.Invoke();
    }

    public static string SanitizeFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "theme";
        }

        var builder = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            builder.Append(char.IsLetterOrDigit(character) || character == '_' || character == '-'
                ? char.ToLowerInvariant(character)
                : '_');
        }

        var sanitized = builder.ToString().Trim('_');
        return string.IsNullOrWhiteSpace(sanitized) ? "theme" : sanitized;
    }

    public static string BuildStyleVariantId(RuntimeStyleIntent runtimeIntent)
    {
        if (runtimeIntent != null && !string.IsNullOrWhiteSpace(runtimeIntent.StyleVariantIdOverride))
        {
            return SanitizeFileName(runtimeIntent.StyleVariantIdOverride);
        }

        if (runtimeIntent == null || string.IsNullOrWhiteSpace(runtimeIntent.UserIntent))
        {
            return PresetStyleVariantId;
        }

        var label = SanitizeFileName(runtimeIntent.UserIntent);
        if (label.Length > 32)
        {
            label = label.Substring(0, 32).Trim('_');
        }

        return $"style_{label}_{ComputeStableHash(BuildStyleSignature(runtimeIntent)):x8}";
    }

    public static string BuildRequestId(string themeId, string semanticLabel, string styleVariantId)
    {
        var themeToken = SanitizeFileName(themeId);
        var semanticToken = SanitizeFileName(semanticLabel);
        var styleToken = string.IsNullOrWhiteSpace(styleVariantId)
            ? PresetStyleVariantId
            : SanitizeFileName(styleVariantId);

        return string.Equals(styleToken, PresetStyleVariantId, StringComparison.Ordinal)
            ? $"{themeToken}_{semanticToken}_{PromptVersion}"
            : $"{themeToken}_{styleToken}_{semanticToken}_{PromptVersion}";
    }

    private static string BuildStyleSignature(RuntimeStyleIntent runtimeIntent)
    {
        var builder = new StringBuilder(1024);
        AppendNormalized(builder, runtimeIntent.UserIntent);
        AppendNormalized(builder, runtimeIntent.Source);
        AppendNormalized(builder, runtimeIntent.GlobalStyleSummary);
        AppendNormalized(builder, runtimeIntent.ObjectStyleDirective);
        AppendNormalizedList(builder, runtimeIntent.StyleKeywords);
        AppendNormalizedList(builder, runtimeIntent.MaterialKeywords);
        AppendNormalizedList(builder, runtimeIntent.ColorKeywords);
        AppendNormalizedList(builder, runtimeIntent.MotifKeywords);
        AppendNormalizedList(builder, runtimeIntent.NegativeStyleKeywords);
        return builder.ToString();
    }

    private static void AppendNormalizedList(StringBuilder builder, System.Collections.Generic.List<string> values)
    {
        if (values == null)
        {
            builder.Append('|');
            return;
        }

        for (var index = 0; index < values.Count; index++)
        {
            AppendNormalized(builder, values[index]);
        }
    }

    private static void AppendNormalized(StringBuilder builder, string value)
    {
        builder.Append('|');
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        builder.Append(value.Trim().ToLowerInvariant());
    }

    private static uint ComputeStableHash(string value)
    {
        unchecked
        {
            const uint offsetBasis = 2166136261u;
            const uint prime = 16777619u;
            var hash = offsetBasis;
            for (var index = 0; index < value.Length; index++)
            {
                hash ^= value[index];
                hash *= prime;
            }

            return hash;
        }
    }

    private static string ToSemanticLabel(ThemeSurfaceKind surfaceKind)
    {
        return surfaceKind switch
        {
            ThemeSurfaceKind.DoorFrame => "door_frame",
            ThemeSurfaceKind.WindowFrame => "window_frame",
            ThemeSurfaceKind.WindowVista => "window_vista",
            _ => surfaceKind.ToString().ToLowerInvariant(),
        };
    }

    private static string BuildRuntimeStyleBlock(RuntimeStyleIntent runtimeIntent)
    {
        if (runtimeIntent == null || string.IsNullOrWhiteSpace(runtimeIntent.UserIntent))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(512);
        builder.AppendLine($"Runtime user style intent: {runtimeIntent.UserIntent}");
        AppendListLine(builder, "Runtime style keywords", runtimeIntent.StyleKeywords);
        AppendListLine(builder, "Runtime material keywords", runtimeIntent.MaterialKeywords);
        AppendListLine(builder, "Runtime color keywords", runtimeIntent.ColorKeywords);
        AppendListLine(builder, "Runtime motif keywords", runtimeIntent.MotifKeywords);
        AppendListLine(builder, "Runtime negative style keywords", runtimeIntent.NegativeStyleKeywords);
        return builder.ToString();
    }

    private static void AppendListLine(StringBuilder builder, string label, System.Collections.Generic.List<string> values)
    {
        if (values == null || values.Count == 0)
        {
            return;
        }

        builder.Append(label);
        builder.Append(": ");
        builder.AppendLine(string.Join(", ", values));
    }
}
