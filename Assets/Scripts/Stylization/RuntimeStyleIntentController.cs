using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

[DisallowMultipleComponent]
public class RuntimeStyleIntentController : MonoBehaviour
{
    [Header("User Style Intent")]
    [SerializeField, TextArea(1, 3)] private string userStyleIntent = string.Empty;
    [SerializeField] private bool rebuildOnEnable = true;
    [SerializeField] private bool writeLlmHandoffArtifact;
    [SerializeField] private string llmJobFolderName = "StyleIntentJobs";

    [Header("User Style Catalog")]
    [SerializeField] private bool useStyleCatalog = true;
    [SerializeField] private List<RuntimeStyleOption> builtinStyles = new()
    {
        RuntimeStyleOption.CreateBuiltIn(
            "future_research_lab",
            "Future Research Lab",
            "future research lab",
            "A precise high-tech research workspace style with cool panels, cyan accents, clean surfaces, and functional lab readability."),
        RuntimeStyleOption.CreateBuiltIn(
            "arcane_knowledge_chamber",
            "Arcane Knowledge Chamber",
            "arcane knowledge chamber",
            "A warm scholarly archive style with carved materials, amber light, brass or wood details, and ritual-study atmosphere."),
    };
    [SerializeField] private int activeStyleIndex;
    [SerializeField] private bool includeCustomStyleSlot = true;
    [SerializeField, TextArea(1, 3)] private string customStyleIntent = string.Empty;

    [Header("Optional External Provider")]
    [SerializeField] private bool useDeepSeekStyleIntentProvider = true;
    [SerializeField] private DeepSeekStyleIntentProvider deepSeekStyleIntentProvider;

    [Header("Runtime State")]
    [SerializeField] private RuntimeStyleIntent currentIntent = new RuntimeStyleIntent();
    [SerializeField, TextArea(4, 8)] private string latestSummary = "[RuntimeStyleIntent]\nState: idle\nIntent: none";

    [NonSerialized] private string lastBuiltUserStyleIntent = string.Empty;
    [NonSerialized] private bool externalRequestInFlight;
    [NonSerialized] private string externalRequestedIntent = string.Empty;
    [NonSerialized] private string externalProviderStatus = string.Empty;

    public event Action StyleIntentChanged;

    public RuntimeStyleIntent CurrentIntent
    {
        get
        {
            EnsureIntentCurrent("lazy-refresh");
            return currentIntent;
        }
    }

    public string UserStyleIntent => userStyleIntent;
    public bool UsesStyleCatalog => useStyleCatalog;
    public int ActiveStyleIndex => Mathf.Clamp(activeStyleIndex, 0, Mathf.Max(0, GetStyleOptionCount() - 1));
    public int StyleOptionCount => GetStyleOptionCount();
    public string ActiveStyleDisplayName => ResolveActiveStyleDisplayName();
    public string LatestSummary => latestSummary;
    public bool HasActiveIntent
    {
        get
        {
            var intent = CurrentIntent;
            return intent != null && !string.IsNullOrWhiteSpace(intent.UserIntent);
        }
    }

    private void OnEnable()
    {
        ResolveReferences();
        PrepareStyleSelectionForRuntime();

        if (rebuildOnEnable)
        {
            RebuildStyleIntent("enabled");
        }
        else
        {
            PublishSummary("enabled");
        }
    }

    private void OnValidate()
    {
        PrepareStyleSelectionForRuntime();
        if (!Application.isPlaying)
        {
            currentIntent = BuildIntentForActiveStyle("inspector_preview");
            lastBuiltUserStyleIntent = BuildStyleSelectionSignature();
            PublishSummary("inspector-preview");
        }
    }

    public void SetUserStyleIntent(string value)
    {
        customStyleIntent = value ?? string.Empty;
        userStyleIntent = customStyleIntent;
        if (useStyleCatalog && includeCustomStyleSlot && !string.IsNullOrWhiteSpace(customStyleIntent))
        {
            activeStyleIndex = builtinStyles != null ? builtinStyles.Count : 0;
        }

        RebuildStyleIntent("set-user-intent");
    }

    public bool SelectStyleByIndex(int index)
    {
        EnsureStyleCatalogDefaults();
        var count = GetStyleOptionCount();
        if (!useStyleCatalog || index < 0 || index >= count)
        {
            return false;
        }

        if (activeStyleIndex == index)
        {
            ApplyActiveStyleSelectionToUserIntent();
            return true;
        }

        activeStyleIndex = index;
        ApplyActiveStyleSelectionToUserIntent();
        RebuildStyleIntent("select-style");
        return true;
    }

    public void CycleStyle(int direction)
    {
        EnsureStyleCatalogDefaults();
        var count = GetStyleOptionCount();
        if (!useStyleCatalog || count == 0)
        {
            return;
        }

        var start = Mathf.Clamp(activeStyleIndex, 0, count - 1);
        var next = (start + direction + count) % count;
        SelectStyleByIndex(next);
    }

    public List<string> GetStyleOptionLabels()
    {
        EnsureStyleCatalogDefaults();
        var labels = new List<string>();
        if (!useStyleCatalog)
        {
            if (!string.IsNullOrWhiteSpace(userStyleIntent))
            {
                labels.Add(RuntimeStyleIntentRequestUtility.BuildDisplayLabel(userStyleIntent));
            }

            return labels;
        }

        for (var index = 0; index < builtinStyles.Count; index++)
        {
            var style = builtinStyles[index];
            labels.Add(style != null && !string.IsNullOrWhiteSpace(style.DisplayName)
                ? style.DisplayName.Trim()
                : $"Style {index + 1}");
        }

        if (HasCustomStyleSlot())
        {
            labels.Add($"Custom: {RuntimeStyleIntentRequestUtility.BuildDisplayLabel(customStyleIntent)}");
        }

        return labels;
    }

    [ContextMenu("Rebuild Style Intent")]
    public void RebuildStyleIntent()
    {
        RebuildStyleIntent("manual");
    }

    private void RebuildStyleIntent(string reason)
    {
        EnsureStyleCatalogDefaults();
        ApplyActiveStyleSelectionToUserIntent();

        currentIntent = BuildIntentForActiveStyle("local_keyword_extractor_v1");
        lastBuiltUserStyleIntent = BuildStyleSelectionSignature();
        externalProviderStatus = string.Empty;

        if (writeLlmHandoffArtifact && !string.IsNullOrWhiteSpace(userStyleIntent))
        {
            currentIntent.LlmHandoffPromptPath = WriteLlmHandoffPrompt(currentIntent);
        }

        RequestExternalStyleIntentIfAvailable(currentIntent.UserIntent);
        PublishSummary(reason);
        StyleIntentChanged?.Invoke();
    }

    private void EnsureIntentCurrent(string reason)
    {
        EnsureStyleCatalogDefaults();
        ApplyActiveStyleSelectionToUserIntent();

        var currentSignature = BuildStyleSelectionSignature();
        if (currentIntent == null || !string.Equals(lastBuiltUserStyleIntent, currentSignature, StringComparison.Ordinal))
        {
            RebuildStyleIntent(reason);
        }
    }

    private void ResolveReferences()
    {
        if (deepSeekStyleIntentProvider == null)
        {
            deepSeekStyleIntentProvider = FindAnyObjectByType<DeepSeekStyleIntentProvider>();
        }
    }

    private void RequestExternalStyleIntentIfAvailable(string requestedIntent)
    {
        if (!Application.isPlaying ||
            !useDeepSeekStyleIntentProvider ||
            string.IsNullOrWhiteSpace(requestedIntent))
        {
            return;
        }

        ResolveReferences();
        if (deepSeekStyleIntentProvider == null)
        {
            externalProviderStatus = "DeepSeek: provider component not found.";
            return;
        }

        if (!deepSeekStyleIntentProvider.IsEnabled)
        {
            externalProviderStatus = "DeepSeek: provider disabled.";
            return;
        }

        if (!deepSeekStyleIntentProvider.HasApiKey())
        {
            externalProviderStatus = "DeepSeek: missing API key. Set DEEPSEEK_API_KEY or apiKeyOverride.";
            return;
        }

        externalRequestInFlight = true;
        externalRequestedIntent = requestedIntent;
        externalProviderStatus = $"DeepSeek: requesting {deepSeekStyleIntentProvider.Model}.";

        var started = deepSeekStyleIntentProvider.RequestStyleIntent(
            requestedIntent,
            intent => HandleExternalStyleIntentSuccess(requestedIntent, intent),
            error => HandleExternalStyleIntentFailure(requestedIntent, error));

        if (!started)
        {
            externalRequestInFlight = false;
            externalProviderStatus = "DeepSeek: request was not started.";
        }
    }

    private void HandleExternalStyleIntentSuccess(string requestedIntent, RuntimeStyleIntent intent)
    {
        externalRequestInFlight = false;
        ApplyActiveStyleSelectionToUserIntent();
        if (!string.Equals(externalRequestedIntent, requestedIntent, StringComparison.Ordinal) ||
            !string.Equals((userStyleIntent ?? string.Empty).Trim(), requestedIntent, StringComparison.Ordinal))
        {
            externalProviderStatus = "DeepSeek: ignored stale response.";
            PublishSummary("deepseek-stale-response");
            return;
        }

        currentIntent = intent;
        ApplyActiveStyleIdentity(currentIntent);
        lastBuiltUserStyleIntent = BuildStyleSelectionSignature();
        externalProviderStatus = $"DeepSeek: completed with {deepSeekStyleIntentProvider.Model}.";
        PublishSummary("deepseek-response");
        StyleIntentChanged?.Invoke();
    }

    private void HandleExternalStyleIntentFailure(string requestedIntent, string error)
    {
        if (!string.Equals(externalRequestedIntent, requestedIntent, StringComparison.Ordinal))
        {
            return;
        }

        externalRequestInFlight = false;
        externalProviderStatus = $"DeepSeek: failed, using local fallback. {error}";
        PublishSummary("deepseek-fallback");
        StyleIntentChanged?.Invoke();
    }

    private RuntimeStyleIntent BuildIntentForActiveStyle(string source)
    {
        var intent = BuildDeterministicIntent(userStyleIntent, ResolveActiveStyleSource(source));
        ApplyActiveStyleIdentity(intent);
        return intent;
    }

    private void PrepareStyleSelectionForRuntime()
    {
        EnsureStyleCatalogDefaults();
        if (!useStyleCatalog)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(userStyleIntent))
        {
            var matchingIndex = FindBuiltinStyleIndex(userStyleIntent);
            if (matchingIndex >= 0)
            {
                activeStyleIndex = matchingIndex;
            }
            else if (includeCustomStyleSlot)
            {
                customStyleIntent = userStyleIntent.Trim();
                activeStyleIndex = builtinStyles.Count;
            }
        }

        ApplyActiveStyleSelectionToUserIntent();
    }

    private void ApplyActiveStyleSelectionToUserIntent()
    {
        if (!useStyleCatalog)
        {
            return;
        }

        EnsureStyleCatalogDefaults();
        var style = ResolveActiveBuiltinStyle();
        if (style != null)
        {
            userStyleIntent = style.UserStyleIntent ?? string.Empty;
            return;
        }

        userStyleIntent = HasCustomStyleSlot() ? customStyleIntent ?? string.Empty : string.Empty;
    }

    private void ApplyActiveStyleIdentity(RuntimeStyleIntent intent)
    {
        if (intent == null || !useStyleCatalog)
        {
            return;
        }

        var style = ResolveActiveBuiltinStyle();
        if (style != null)
        {
            style.ApplyTo(intent);
        }
    }

    private RuntimeStyleOption ResolveActiveBuiltinStyle()
    {
        EnsureStyleCatalogDefaults();
        if (!useStyleCatalog || activeStyleIndex < 0 || activeStyleIndex >= builtinStyles.Count)
        {
            return null;
        }

        return builtinStyles[activeStyleIndex];
    }

    private string ResolveActiveStyleSource(string fallbackSource)
    {
        var style = ResolveActiveBuiltinStyle();
        if (style != null && !string.IsNullOrWhiteSpace(style.StyleIntentSource))
        {
            return style.StyleIntentSource.Trim();
        }

        return string.IsNullOrWhiteSpace(fallbackSource) ? "local_keyword_extractor_v1" : fallbackSource;
    }

    private string ResolveActiveStyleDisplayName()
    {
        var style = ResolveActiveBuiltinStyle();
        if (style != null && !string.IsNullOrWhiteSpace(style.DisplayName))
        {
            return style.DisplayName.Trim();
        }

        if (HasCustomStyleSlot())
        {
            return $"Custom: {RuntimeStyleIntentRequestUtility.BuildDisplayLabel(customStyleIntent)}";
        }

        return string.IsNullOrWhiteSpace(userStyleIntent)
            ? "No Style"
            : RuntimeStyleIntentRequestUtility.BuildDisplayLabel(userStyleIntent);
    }

    private string BuildStyleSelectionSignature()
    {
        return $"{activeStyleIndex}|{userStyleIntent ?? string.Empty}|{customStyleIntent ?? string.Empty}";
    }

    private int GetStyleOptionCount()
    {
        EnsureStyleCatalogDefaults();
        if (!useStyleCatalog)
        {
            return 0;
        }

        return builtinStyles.Count + (HasCustomStyleSlot() ? 1 : 0);
    }

    private bool HasCustomStyleSlot()
    {
        return includeCustomStyleSlot && !string.IsNullOrWhiteSpace(customStyleIntent);
    }

    private int FindBuiltinStyleIndex(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || builtinStyles == null)
        {
            return -1;
        }

        var trimmed = value.Trim();
        for (var index = 0; index < builtinStyles.Count; index++)
        {
            var style = builtinStyles[index];
            if (style == null)
            {
                continue;
            }

            if (string.Equals(style.StyleId, trimmed, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(style.DisplayName, trimmed, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(style.UserStyleIntent, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private void EnsureStyleCatalogDefaults()
    {
        if (builtinStyles == null)
        {
            builtinStyles = new List<RuntimeStyleOption>();
        }

        if (builtinStyles.Count == 0)
        {
            builtinStyles.Add(RuntimeStyleOption.CreateBuiltIn(
                "future_research_lab",
                "Future Research Lab",
                "future research lab",
                "A precise high-tech research workspace style with cool panels, cyan accents, clean surfaces, and functional lab readability."));
            builtinStyles.Add(RuntimeStyleOption.CreateBuiltIn(
                "arcane_knowledge_chamber",
                "Arcane Knowledge Chamber",
                "arcane knowledge chamber",
                "A warm scholarly archive style with carved materials, amber light, brass or wood details, and ritual-study atmosphere."));
        }

        if (useStyleCatalog && builtinStyles.Count > 0)
        {
            var count = builtinStyles.Count + (HasCustomStyleSlot() ? 1 : 0);
            activeStyleIndex = Mathf.Clamp(activeStyleIndex, 0, Mathf.Max(0, count - 1));
        }
    }

    private RuntimeStyleIntent BuildDeterministicIntent(string rawIntent, string source)
    {
        var intent = new RuntimeStyleIntent
        {
            UserIntent = (rawIntent ?? string.Empty).Trim(),
            Source = source,
            CreatedAtIsoUtc = DateTime.UtcNow.ToString("O"),
        };

        if (string.IsNullOrWhiteSpace(intent.UserIntent))
        {
            intent.GlobalStyleSummary = "No runtime style intent. Use the active ThemeProfile preset only.";
            return intent;
        }

        var normalized = Normalize(intent.UserIntent);
        AddUnique(intent.StyleKeywords, intent.UserIntent);

        if (ContainsAny(normalized, "cyberpunk", "cyber punk", "neonpunk"))
        {
            AddCyberpunk(intent);
        }

        if (ContainsAny(normalized, "steampunk", "steam punk"))
        {
            AddSteampunk(intent);
        }

        if (ContainsAny(normalized, "solarpunk", "solar punk"))
        {
            AddSolarpunk(intent);
        }

        if (ContainsAny(normalized, "minimal", "minimalist", "minimalism"))
        {
            AddMinimal(intent);
        }

        if (ContainsAny(normalized, "biophilic", "nature", "forest", "organic"))
        {
            AddBiophilic(intent);
        }

        if (ContainsAny(normalized, "space", "spaceship", "orbital", "sci fi", "sci-fi"))
        {
            AddSpace(intent);
        }

        if (ContainsAny(normalized, "underwater", "ocean", "aquatic"))
        {
            AddUnderwater(intent);
        }

        AddGenericTokens(intent, normalized);
        EnsureMinimumKeywords(intent);
        intent.GlobalStyleSummary = BuildGlobalSummary(intent);
        intent.ObjectStyleDirective = BuildObjectDirective(intent);
        return intent;
    }

    private string WriteLlmHandoffPrompt(RuntimeStyleIntent intent)
    {
        if (intent == null || string.IsNullOrWhiteSpace(intent.UserIntent))
        {
            return string.Empty;
        }

        var directory = Path.Combine(GetLibraryDirectory(), NormalizeFolderName(llmJobFolderName));
        Directory.CreateDirectory(directory);

        var id = $"{SanitizeToken(intent.UserIntent)}_{DateTime.UtcNow:yyyyMMddHHmmss}";
        var promptPath = Path.Combine(directory, $"{id}.style_intent.prompt.txt");
        var builder = new StringBuilder(2048);
        builder.AppendLine("TASK = \"Extract Roomify-style visual keywords from one user style intent.\"");
        builder.AppendLine();
        builder.AppendLine($"USER_STYLE_INTENT = \"{Escape(intent.UserIntent)}\"");
        builder.AppendLine();
        builder.AppendLine("OUTPUT_JSON_SCHEMA = {");
        builder.AppendLine("  \"global_style_summary\": \"one concise sentence\",");
        builder.AppendLine("  \"style_keywords\": [\"5-8 visual style keywords\"],");
        builder.AppendLine("  \"material_keywords\": [\"3-6 material or finish keywords\"],");
        builder.AppendLine("  \"color_keywords\": [\"3-6 color or lighting keywords\"],");
        builder.AppendLine("  \"motif_keywords\": [\"3-6 motif/detail keywords\"],");
        builder.AppendLine("  \"negative_style_keywords\": [\"3-6 styles to avoid\"],");
        builder.AppendLine("  \"object_style_directive\": \"one sentence explaining how any object should inherit the style while preserving function, footprint, proportions, and yaw\"");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("CONSTRAINTS = [");
        builder.AppendLine("  \"Do not invent object semantics.\",");
        builder.AppendLine("  \"Do not override room geometry constraints.\",");
        builder.AppendLine("  \"Keep keywords concrete and visual, not abstract mood words only.\",");
        builder.AppendLine("  \"Keep all collision-sensitive furniture functionally recognizable.\"");
        builder.AppendLine("]");

        File.WriteAllText(promptPath, builder.ToString().TrimEnd());
        return promptPath;
    }

    public string GetDebugSummary()
    {
        return latestSummary;
    }

    private void PublishSummary(string state)
    {
        if (currentIntent == null)
        {
            currentIntent = BuildIntentForActiveStyle("summary_fallback");
            lastBuiltUserStyleIntent = BuildStyleSelectionSignature();
        }

        var builder = new StringBuilder(512);
        builder.AppendLine("[RuntimeStyleIntent]");
        builder.AppendLine($"State: {state}");
        builder.AppendLine($"Style: {ActiveStyleDisplayName}");
        if (!string.IsNullOrWhiteSpace(currentIntent.StyleId))
        {
            builder.AppendLine($"Style Id: {currentIntent.StyleId}");
        }

        builder.AppendLine($"Intent: {(string.IsNullOrWhiteSpace(userStyleIntent) ? "none" : userStyleIntent.Trim())}");
        builder.AppendLine($"Source: {currentIntent.Source}");
        builder.AppendLine($"Keywords: {JoinPreview(currentIntent.StyleKeywords)}");
        builder.AppendLine($"Materials: {JoinPreview(currentIntent.MaterialKeywords)}");
        builder.AppendLine($"Colors: {JoinPreview(currentIntent.ColorKeywords)}");
        builder.AppendLine($"Motifs: {JoinPreview(currentIntent.MotifKeywords)}");
        if (!string.IsNullOrWhiteSpace(externalProviderStatus))
        {
            builder.AppendLine($"External: {externalProviderStatus}");
        }

        if (externalRequestInFlight)
        {
            builder.AppendLine("External State: request in flight");
        }

        if (deepSeekStyleIntentProvider != null && Application.isPlaying)
        {
            builder.AppendLine();
            builder.Append(deepSeekStyleIntentProvider.LatestStatus);
        }

        if (!string.IsNullOrWhiteSpace(currentIntent.LlmHandoffPromptPath))
        {
            builder.AppendLine($"LLM Handoff: {ShortenPath(currentIntent.LlmHandoffPromptPath)}");
        }

        latestSummary = builder.ToString().TrimEnd();
    }

    private static void AddCyberpunk(RuntimeStyleIntent intent)
    {
        AddUnique(intent.StyleKeywords, "cyberpunk");
        AddUnique(intent.StyleKeywords, "dense urban technology");
        AddUnique(intent.MaterialKeywords, "dark brushed metal");
        AddUnique(intent.MaterialKeywords, "black composite panels");
        AddUnique(intent.MaterialKeywords, "glass and acrylic overlays");
        AddUnique(intent.ColorKeywords, "neon cyan");
        AddUnique(intent.ColorKeywords, "magenta accents");
        AddUnique(intent.ColorKeywords, "rain-slick glow");
        AddUnique(intent.MotifKeywords, "holographic signage");
        AddUnique(intent.MotifKeywords, "exposed cable runs");
        AddUnique(intent.MotifKeywords, "thin luminous circuit traces");
        AddUnique(intent.NegativeStyleKeywords, "rustic cottage");
        AddUnique(intent.NegativeStyleKeywords, "medieval fantasy");
        AddUnique(intent.NegativeStyleKeywords, "pastoral natural wood");
    }

    private static void AddSteampunk(RuntimeStyleIntent intent)
    {
        AddUnique(intent.StyleKeywords, "steampunk");
        AddUnique(intent.StyleKeywords, "Victorian industrial workshop");
        AddUnique(intent.MaterialKeywords, "aged brass");
        AddUnique(intent.MaterialKeywords, "dark leather");
        AddUnique(intent.MaterialKeywords, "warm varnished wood");
        AddUnique(intent.ColorKeywords, "amber glow");
        AddUnique(intent.ColorKeywords, "smoked bronze");
        AddUnique(intent.MotifKeywords, "small gears");
        AddUnique(intent.MotifKeywords, "pressure gauges");
        AddUnique(intent.MotifKeywords, "riveted trim");
        AddUnique(intent.NegativeStyleKeywords, "clean sci-fi plastic");
    }

    private static void AddSolarpunk(RuntimeStyleIntent intent)
    {
        AddUnique(intent.StyleKeywords, "solarpunk");
        AddUnique(intent.StyleKeywords, "optimistic ecological technology");
        AddUnique(intent.MaterialKeywords, "light bamboo composite");
        AddUnique(intent.MaterialKeywords, "matte recycled metal");
        AddUnique(intent.MaterialKeywords, "translucent solar glass");
        AddUnique(intent.ColorKeywords, "warm daylight");
        AddUnique(intent.ColorKeywords, "leaf green");
        AddUnique(intent.ColorKeywords, "soft golden accents");
        AddUnique(intent.MotifKeywords, "integrated planters");
        AddUnique(intent.MotifKeywords, "subtle solar-cell patterns");
        AddUnique(intent.NegativeStyleKeywords, "dystopian grime");
    }

    private static void AddMinimal(RuntimeStyleIntent intent)
    {
        AddUnique(intent.StyleKeywords, "minimalist");
        AddUnique(intent.StyleKeywords, "quiet precise geometry");
        AddUnique(intent.MaterialKeywords, "matte white composite");
        AddUnique(intent.MaterialKeywords, "soft anodized metal");
        AddUnique(intent.ColorKeywords, "warm white");
        AddUnique(intent.ColorKeywords, "soft gray");
        AddUnique(intent.MotifKeywords, "clean seams");
        AddUnique(intent.MotifKeywords, "low visual noise");
        AddUnique(intent.NegativeStyleKeywords, "ornate clutter");
    }

    private static void AddBiophilic(RuntimeStyleIntent intent)
    {
        AddUnique(intent.StyleKeywords, "biophilic");
        AddUnique(intent.StyleKeywords, "organic study environment");
        AddUnique(intent.MaterialKeywords, "light natural wood");
        AddUnique(intent.MaterialKeywords, "woven fiber texture");
        AddUnique(intent.ColorKeywords, "moss green");
        AddUnique(intent.ColorKeywords, "sunlit beige");
        AddUnique(intent.MotifKeywords, "leaf-vein patterns");
        AddUnique(intent.MotifKeywords, "soft rounded edges");
        AddUnique(intent.NegativeStyleKeywords, "hard industrial machinery");
    }

    private static void AddSpace(RuntimeStyleIntent intent)
    {
        AddUnique(intent.StyleKeywords, "orbital research habitat");
        AddUnique(intent.StyleKeywords, "space station interior");
        AddUnique(intent.MaterialKeywords, "white ceramic composite");
        AddUnique(intent.MaterialKeywords, "dark carbon fiber");
        AddUnique(intent.ColorKeywords, "cool blue light");
        AddUnique(intent.ColorKeywords, "white status glow");
        AddUnique(intent.MotifKeywords, "modular panels");
        AddUnique(intent.MotifKeywords, "small status indicators");
        AddUnique(intent.NegativeStyleKeywords, "fantasy ornament");
    }

    private static void AddUnderwater(RuntimeStyleIntent intent)
    {
        AddUnique(intent.StyleKeywords, "underwater research lounge");
        AddUnique(intent.StyleKeywords, "aquatic atmosphere");
        AddUnique(intent.MaterialKeywords, "pearlescent shell-like finish");
        AddUnique(intent.MaterialKeywords, "translucent blue glass");
        AddUnique(intent.ColorKeywords, "deep teal");
        AddUnique(intent.ColorKeywords, "soft caustic blue");
        AddUnique(intent.MotifKeywords, "subtle wave patterns");
        AddUnique(intent.MotifKeywords, "bubble-like luminous details");
        AddUnique(intent.NegativeStyleKeywords, "dry desert palette");
    }

    private static void AddGenericTokens(RuntimeStyleIntent intent, string normalized)
    {
        var tokens = normalized.Split(new[] { ' ', ',', ';', '.', '/', '\\', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < tokens.Length && intent.StyleKeywords.Count < 8; index++)
        {
            var token = tokens[index].Trim();
            if (token.Length < 3 || IsStopWord(token))
            {
                continue;
            }

            AddUnique(intent.StyleKeywords, token);
        }
    }

    private static void EnsureMinimumKeywords(RuntimeStyleIntent intent)
    {
        if (intent.MaterialKeywords.Count == 0)
        {
            AddUnique(intent.MaterialKeywords, "theme-consistent material finish");
            AddUnique(intent.MaterialKeywords, "coherent surface language");
        }

        if (intent.ColorKeywords.Count == 0)
        {
            AddUnique(intent.ColorKeywords, "coherent accent lighting");
            AddUnique(intent.ColorKeywords, "controlled color palette");
        }

        if (intent.MotifKeywords.Count == 0)
        {
            AddUnique(intent.MotifKeywords, "repeated visual motif");
            AddUnique(intent.MotifKeywords, "consistent trim details");
        }

        if (intent.NegativeStyleKeywords.Count == 0)
        {
            AddUnique(intent.NegativeStyleKeywords, "mixed unrelated theme");
            AddUnique(intent.NegativeStyleKeywords, "random decorative clutter");
        }
    }

    private static string BuildGlobalSummary(RuntimeStyleIntent intent)
    {
        return $"Apply the user style intent \"{intent.UserIntent}\" through concrete keywords: {string.Join(", ", intent.StyleKeywords)}.";
    }

    private static string BuildObjectDirective(RuntimeStyleIntent intent)
    {
        return "Use the runtime style keywords as the primary visual style layer while preserving each object's semantic role, footprint, proportions, support/contact surfaces, and dominant yaw.";
    }

    private static string Normalize(string value)
    {
        return (value ?? string.Empty).Trim().ToLowerInvariant();
    }

    private static bool ContainsAny(string value, params string[] candidates)
    {
        if (string.IsNullOrWhiteSpace(value) || candidates == null)
        {
            return false;
        }

        for (var index = 0; index < candidates.Length; index++)
        {
            if (!string.IsNullOrWhiteSpace(candidates[index]) &&
                value.Contains(candidates[index], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsStopWord(string value)
    {
        switch (value)
        {
            case "the":
            case "and":
            case "for":
            case "with":
            case "room":
            case "style":
            case "theme":
            case "into":
            case "like":
                return true;
            default:
                return false;
        }
    }

    private static void AddUnique(List<string> list, string value)
    {
        if (list == null || string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var trimmed = value.Trim();
        for (var index = 0; index < list.Count; index++)
        {
            if (string.Equals(list[index], trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        list.Add(trimmed);
    }

    private static string JoinPreview(List<string> values)
    {
        if (values == null || values.Count == 0)
        {
            return "none";
        }

        var count = Mathf.Min(values.Count, 5);
        return string.Join(", ", values.GetRange(0, count));
    }

    private static string GetLibraryDirectory()
    {
        return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Library"));
    }

    private static string NormalizeFolderName(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "StyleIntentJobs" : value.Trim().Replace('\\', '/').Trim('/');
    }

    private static string SanitizeToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "style";
        }

        var builder = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            builder.Append(char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '_');
        }

        var sanitized = builder.ToString().Trim('_');
        return string.IsNullOrWhiteSpace(sanitized) ? "style" : sanitized;
    }

    private static string Escape(string value)
    {
        return (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private static string ShortenPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Length <= 64 ? value : $"...{value.Substring(value.Length - 61)}";
    }
}

[Serializable]
public class RuntimeStyleIntent
{
    public string StyleId;
    public string StyleDisplayName;
    public string StyleDescription;
    public string StyleVariantIdOverride;
    public string UserIntent;
    public string Source;
    public string GlobalStyleSummary;
    public List<string> StyleKeywords = new List<string>();
    public List<string> MaterialKeywords = new List<string>();
    public List<string> ColorKeywords = new List<string>();
    public List<string> MotifKeywords = new List<string>();
    public List<string> NegativeStyleKeywords = new List<string>();
    public string ObjectStyleDirective;
    public string LlmHandoffPromptPath;
    public string CreatedAtIsoUtc;
}

[Serializable]
public class RuntimeStyleOption
{
    public string StyleId;
    public string DisplayName;
    [TextArea(1, 3)] public string UserStyleIntent;
    [TextArea(2, 4)] public string Description;
    public string StyleIntentSource = "builtin_style_catalog";
    public string StyleVariantIdOverride = SurfaceTexturePromptBuilder.PresetStyleVariantId;

    public static RuntimeStyleOption CreateBuiltIn(
        string styleId,
        string displayName,
        string userStyleIntent,
        string description)
    {
        return new RuntimeStyleOption
        {
            StyleId = styleId,
            DisplayName = displayName,
            UserStyleIntent = userStyleIntent,
            Description = description,
            StyleIntentSource = "builtin_style_catalog",
            StyleVariantIdOverride = SurfaceTexturePromptBuilder.PresetStyleVariantId,
        };
    }

    public void ApplyTo(RuntimeStyleIntent intent)
    {
        if (intent == null)
        {
            return;
        }

        intent.StyleId = StyleId;
        intent.StyleDisplayName = DisplayName;
        intent.StyleDescription = Description;
        intent.StyleVariantIdOverride = StyleVariantIdOverride;
        if (!string.IsNullOrWhiteSpace(StyleIntentSource))
        {
            intent.Source = StyleIntentSource.Trim();
        }
    }
}

public static class RuntimeStyleIntentRequestUtility
{
    public static bool HasUserStyleIntent(RuntimeStyleIntent intent)
    {
        return intent != null &&
               (!string.IsNullOrWhiteSpace(intent.UserIntent) ||
                !string.IsNullOrWhiteSpace(intent.StyleId) ||
                !string.IsNullOrWhiteSpace(intent.StyleDisplayName));
    }

    public static string BuildEffectiveThemeId(ThemeProfile scaffoldTheme, RuntimeStyleIntent intent)
    {
        if (intent != null && !string.IsNullOrWhiteSpace(intent.StyleId))
        {
            return SurfaceTexturePromptBuilder.SanitizeFileName(intent.StyleId);
        }

        if (!HasUserStyleIntent(intent))
        {
            return scaffoldTheme != null && !string.IsNullOrWhiteSpace(scaffoldTheme.ThemeId)
                ? scaffoldTheme.ThemeId
                : "no_theme";
        }

        var label = SurfaceTexturePromptBuilder.SanitizeFileName(intent.UserIntent);
        if (label.Length > 40)
        {
            label = label.Substring(0, 40).Trim('_');
        }

        return string.IsNullOrWhiteSpace(label) ? "custom_style" : $"custom_{label}";
    }

    public static string BuildEffectiveThemeDisplayName(ThemeProfile scaffoldTheme, RuntimeStyleIntent intent)
    {
        if (intent != null && !string.IsNullOrWhiteSpace(intent.StyleDisplayName))
        {
            return intent.StyleDisplayName.Trim();
        }

        if (!HasUserStyleIntent(intent))
        {
            return scaffoldTheme != null && !string.IsNullOrWhiteSpace(scaffoldTheme.DisplayName)
                ? scaffoldTheme.DisplayName
                : "No Theme";
        }

        return string.IsNullOrWhiteSpace(intent.UserIntent)
            ? "Custom Style"
            : $"Custom: {BuildDisplayLabel(intent.UserIntent)}";
    }

    public static string BuildEffectiveThemeDescription(ThemeProfile scaffoldTheme, RuntimeStyleIntent intent)
    {
        if (intent != null && !string.IsNullOrWhiteSpace(intent.StyleDescription))
        {
            return intent.StyleDescription.Trim();
        }

        if (!HasUserStyleIntent(intent))
        {
            return scaffoldTheme != null ? scaffoldTheme.ShortDescription : string.Empty;
        }

        return !string.IsNullOrWhiteSpace(intent.GlobalStyleSummary)
            ? intent.GlobalStyleSummary.Trim()
            : $"User-defined room style: {intent.UserIntent.Trim()}";
    }

    public static void ApplyThemeIdentityToRequest(
        ThemeProfile scaffoldTheme,
        RuntimeStyleIntent intent,
        GeneratedObjectRequest request)
    {
        if (request == null)
        {
            return;
        }

        request.ThemeId = BuildEffectiveThemeId(scaffoldTheme, intent);
        request.ThemeDisplayName = BuildEffectiveThemeDisplayName(scaffoldTheme, intent);
        request.ThemeShortDescription = BuildEffectiveThemeDescription(scaffoldTheme, intent);
        ApplyToRequest(intent, request);
    }

    public static void ApplyToRequest(RuntimeStyleIntent intent, GeneratedObjectRequest request)
    {
        if (request == null)
        {
            return;
        }

        request.StyleVariantId = SurfaceTexturePromptBuilder.BuildStyleVariantId(intent);
        if (intent == null || string.IsNullOrWhiteSpace(intent.UserIntent))
        {
            return;
        }

        request.UserStyleIntent = intent.UserIntent;
        request.StyleIntentSource = intent.Source;
        request.GlobalStyleSummary = intent.GlobalStyleSummary;
        request.StyleKeywords = Copy(intent.StyleKeywords);
        request.MaterialKeywords = Copy(intent.MaterialKeywords);
        request.ColorKeywords = Copy(intent.ColorKeywords);
        request.MotifKeywords = Copy(intent.MotifKeywords);
        request.NegativeStyleKeywords = Copy(intent.NegativeStyleKeywords);
        request.ObjectStyleDirective = intent.ObjectStyleDirective;
    }

    private static List<string> Copy(List<string> source)
    {
        return source == null ? new List<string>() : new List<string>(source);
    }

    public static string BuildDisplayLabel(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Style";
        }

        var words = value.Trim().Split(new[] { ' ', '\t', '\r', '\n', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return "Style";
        }

        var builder = new StringBuilder(value.Length);
        for (var index = 0; index < words.Length; index++)
        {
            if (index > 0)
            {
                builder.Append(' ');
            }

            var word = words[index];
            if (word.Length == 0)
            {
                continue;
            }

            builder.Append(char.ToUpperInvariant(word[0]));
            if (word.Length > 1)
            {
                builder.Append(word.Substring(1));
            }
        }

        var label = builder.ToString();
        return label.Length <= 48 ? label : $"{label.Substring(0, 45).TrimEnd()}...";
    }
}
