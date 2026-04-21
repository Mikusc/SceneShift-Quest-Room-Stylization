using System;
using System.Collections.Generic;
using System.Text;
using Meta.XR.MRUtilityKit;
using UnityEngine;

[DisallowMultipleComponent]
public class StylizationPlanner : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private RoomSemanticBootstrap roomSemanticBootstrap;
    [SerializeField] private ThemeIntentController themeIntentController;

    [Header("Planner Settings")]
    [SerializeField] private bool includeInnerWallFaces;
    [SerializeField] private bool includeCeilingsInPlan = true;
    [SerializeField] private bool logPlanBuilds;

    [Header("Debug State")]
    [SerializeField] private StylizationPlan currentPlan = new();

    public event Action PlanChanged;

    public StylizationPlan CurrentPlan => currentPlan;
    public string LatestSummary => _latestSummary;

    private string _latestSummary = "[StylizationPlanner]\nState: waiting\nHint: enter Play and wait for room + theme.";

    private void Reset()
    {
        roomSemanticBootstrap = FindAnyObjectByType<RoomSemanticBootstrap>();
        themeIntentController = FindAnyObjectByType<ThemeIntentController>();
    }

    private void Awake()
    {
        if (roomSemanticBootstrap == null)
        {
            roomSemanticBootstrap = FindAnyObjectByType<RoomSemanticBootstrap>();
        }

        if (themeIntentController == null)
        {
            themeIntentController = FindAnyObjectByType<ThemeIntentController>();
        }
    }

    private void OnEnable()
    {
        Subscribe();
        RebuildPlan("enabled");
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    [ContextMenu("Rebuild Stylization Plan")]
    public void RebuildStylizationPlan()
    {
        RebuildPlan("manual");
    }

    private void Subscribe()
    {
        if (roomSemanticBootstrap != null)
        {
            roomSemanticBootstrap.SummaryChanged -= HandleInputsChanged;
            roomSemanticBootstrap.SummaryChanged += HandleInputsChanged;
        }

        if (themeIntentController != null)
        {
            themeIntentController.ThemeChanged -= HandleThemeChanged;
            themeIntentController.ThemeChanged += HandleThemeChanged;
        }
    }

    private void Unsubscribe()
    {
        if (roomSemanticBootstrap != null)
        {
            roomSemanticBootstrap.SummaryChanged -= HandleInputsChanged;
        }

        if (themeIntentController != null)
        {
            themeIntentController.ThemeChanged -= HandleThemeChanged;
        }
    }

    private void HandleInputsChanged()
    {
        RebuildPlan("room-updated");
    }

    private void HandleThemeChanged(ThemeProfile _)
    {
        RebuildPlan("theme-changed");
    }

    private void RebuildPlan(string reason)
    {
        if (roomSemanticBootstrap == null)
        {
            PublishWaitingState("missing-room-bootstrap");
            return;
        }

        if (themeIntentController == null)
        {
            PublishWaitingState("missing-theme-controller");
            return;
        }

        if (!roomSemanticBootstrap.HasReadyRoom || roomSemanticBootstrap.CurrentRoom == null)
        {
            PublishWaitingState("waiting-for-room");
            return;
        }

        var theme = themeIntentController.ActiveTheme;
        if (theme == null)
        {
            PublishWaitingState("waiting-for-theme");
            return;
        }

        BuildPlan(theme, roomSemanticBootstrap.CurrentRoom, reason);
    }

    private void BuildPlan(ThemeProfile theme, MRUKRoom room, string reason)
    {
        currentPlan = new StylizationPlan
        {
            PlanId = $"{theme.ThemeId}_{DateTime.UtcNow:yyyyMMddHHmmss}",
            ThemeId = theme.ThemeId,
            ThemeDisplayName = theme.DisplayName,
            RoomId = room.name,
            CreatedAtIsoUtc = DateTime.UtcNow.ToString("O"),
        };

        var coverage = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["wall"] = 0,
            ["floor"] = 0,
            ["ceiling"] = 0,
            ["table"] = 0,
            ["screen"] = 0,
            ["storage"] = 0,
            ["seating"] = 0,
        };

        for (var index = 0; index < room.Anchors.Count; index++)
        {
            var anchor = room.Anchors[index];
            if (!TryGetSemanticContext(anchor, out var semanticLabel, out var functionTag, out var collisionSensitive))
            {
                continue;
            }

            var entry = CreateEntry(theme, anchor, index, semanticLabel, functionTag, collisionSensitive, currentPlan.Warnings);
            if (entry == null)
            {
                continue;
            }

            currentPlan.Entries.Add(entry);
            coverage[semanticLabel] = coverage.TryGetValue(semanticLabel, out var count) ? count + 1 : 1;
        }

        _latestSummary = BuildSummary(reason, theme, room, coverage, currentPlan);
        PlanChanged?.Invoke();

        if (logPlanBuilds)
        {
            Debug.Log(_latestSummary, this);
        }
    }

    private string BuildSummary(
        string reason,
        ThemeProfile theme,
        MRUKRoom room,
        IReadOnlyDictionary<string, int> coverage,
        StylizationPlan plan)
    {
        var builder = new StringBuilder(512);
        builder.AppendLine("[StylizationPlanner]");
        builder.AppendLine("State: planned");
        builder.AppendLine($"Reason: {reason}");
        builder.AppendLine($"Theme: {theme.DisplayName}");
        builder.AppendLine($"Room: {room.name}");
        builder.AppendLine($"Entries: {plan.EntryCount}");
        builder.AppendLine($"Warnings: {plan.WarningCount}");
        builder.Append(
            $"Coverage: wall={coverage["wall"]}, floor={coverage["floor"]}, ceiling={coverage["ceiling"]}, table={coverage["table"]}, screen={coverage["screen"]}, storage={coverage["storage"]}, seating={coverage["seating"]}");

        if (plan.WarningCount > 0)
        {
            builder.AppendLine();
            var previewCount = Mathf.Min(3, plan.WarningCount);
            builder.Append("Warning Preview:");
            for (var index = 0; index < previewCount; index++)
            {
                builder.Append(index == 0 ? " " : " | ");
                builder.Append(plan.Warnings[index]);
            }
        }

        return builder.ToString();
    }

    private void PublishWaitingState(string reason)
    {
        currentPlan = new StylizationPlan();

        var builder = new StringBuilder(256);
        builder.AppendLine("[StylizationPlanner]");
        builder.AppendLine($"State: {reason}");
        builder.Append("Hint: planner waits for a ready room and active theme.");
        _latestSummary = builder.ToString();
        PlanChanged?.Invoke();
    }

    private bool TryGetSemanticContext(
        MRUKAnchor anchor,
        out string semanticLabel,
        out string functionTag,
        out bool collisionSensitive)
    {
        semanticLabel = null;
        functionTag = null;
        collisionSensitive = false;

        if (anchor == null)
        {
            return false;
        }

        if (anchor.HasAnyLabel(MRUKAnchor.SceneLabels.FLOOR))
        {
            semanticLabel = "floor";
            functionTag = "boundary";
            return true;
        }

        if (anchor.HasAnyLabel(MRUKAnchor.SceneLabels.CEILING))
        {
            if (!includeCeilingsInPlan)
            {
                return false;
            }

            semanticLabel = "ceiling";
            functionTag = "boundary";
            return true;
        }

        if (anchor.HasAnyLabel(MRUKAnchor.SceneLabels.WALL_FACE) ||
            (includeInnerWallFaces && anchor.HasAnyLabel(MRUKAnchor.SceneLabels.INNER_WALL_FACE)))
        {
            semanticLabel = "wall";
            functionTag = "boundary";
            return true;
        }

        if (anchor.HasAnyLabel(MRUKAnchor.SceneLabels.TABLE))
        {
            semanticLabel = "table";
            functionTag = "support_surface";
            collisionSensitive = true;
            return true;
        }

        if (anchor.HasAnyLabel(MRUKAnchor.SceneLabels.SCREEN))
        {
            semanticLabel = "screen";
            functionTag = "display_surface";
            collisionSensitive = true;
            return true;
        }

        if (anchor.HasAnyLabel(MRUKAnchor.SceneLabels.STORAGE))
        {
            semanticLabel = "storage";
            functionTag = "storage";
            collisionSensitive = true;
            return true;
        }

        if (anchor.HasAnyLabel(MRUKAnchor.SceneLabels.COUCH))
        {
            semanticLabel = "seating";
            functionTag = "seating";
            collisionSensitive = true;
            return true;
        }

        return false;
    }

    private StylizationPlanEntry CreateEntry(
        ThemeProfile theme,
        MRUKAnchor anchor,
        int index,
        string semanticLabel,
        string functionTag,
        bool collisionSensitive,
        ICollection<string> warnings)
    {
        var entry = new StylizationPlanEntry
        {
            EntryId = $"{theme.ThemeId}_{semanticLabel}_{index:D2}",
            ObjectId = $"{Sanitize(anchor.name)}_{index:D2}",
            OriginalSemanticLabel = semanticLabel,
            OriginalFunctionTag = functionTag,
            PreserveFootprint = collisionSensitive || semanticLabel is "wall" or "floor" or "ceiling",
            PreserveYawOrientation = collisionSensitive,
            CollisionSensitive = collisionSensitive,
            PlannerConfidence = collisionSensitive ? 0.9f : 0.98f,
        };

        entry.Parameters.Add(new StylizationPlanParameter { Key = "anchor_name", Value = anchor.name });
        entry.Parameters.Add(new StylizationPlanParameter { Key = "anchor_index", Value = index.ToString() });
        entry.Parameters.Add(new StylizationPlanParameter { Key = "semantic_label", Value = semanticLabel });
        entry.Parameters.Add(new StylizationPlanParameter { Key = "function_tag", Value = functionTag });

        var matchedRule = FindRule(theme, semanticLabel, functionTag);
        if (matchedRule != null && TryConfigureMatchedRule(theme, semanticLabel, matchedRule, entry, warnings))
        {
            return entry;
        }

        ConfigureFallbackEntry(theme, semanticLabel, entry, warnings);
        return entry;
    }

    private bool TryConfigureMatchedRule(
        ThemeProfile theme,
        string semanticLabel,
        SemanticReplacementRule matchedRule,
        StylizationPlanEntry entry,
        ICollection<string> warnings)
    {
        entry.PreserveFootprint = matchedRule.PreserveFootprint;
        entry.PreserveYawOrientation = matchedRule.PreserveYawOrientation;
        entry.CollisionSensitive = matchedRule.CollisionSensitive;
        entry.Rationale = string.IsNullOrWhiteSpace(matchedRule.Notes)
            ? $"Matched theme rule for {semanticLabel}."
            : matchedRule.Notes;

        switch (matchedRule.Mode)
        {
            case ReplacementMode.ProxyPrefab:
            {
                var resolvedProxy = matchedRule.ProxyPrefab != null
                    ? matchedRule.ProxyPrefab
                    : theme.GetDefaultProxy(semanticLabel);
                if (resolvedProxy == null)
                {
                    warnings.Add(
                        $"{semanticLabel.ToUpperInvariant()} fallback: theme {theme.DisplayName} requested ProxyPrefab but no proxy is assigned.");
                    return false;
                }

                entry.ReplacementMode = ReplacementMode.ProxyPrefab;
                entry.ReplacementId = resolvedProxy.name;
                entry.ReplacementDisplayName = resolvedProxy.name;
                entry.Parameters.Add(new StylizationPlanParameter
                {
                    Key = "proxy_source",
                    Value = matchedRule.ProxyPrefab != null ? "rule" : "theme_default",
                });
                return true;
            }

            case ReplacementMode.MaterialOverride:
                entry.ReplacementMode = ReplacementMode.MaterialOverride;
                entry.ReplacementId = matchedRule.PrimaryMaterial != null
                    ? matchedRule.PrimaryMaterial.name
                    : $"{theme.ThemeId}_{semanticLabel}_material";
                entry.ReplacementDisplayName = matchedRule.PrimaryMaterial != null
                    ? matchedRule.PrimaryMaterial.name
                    : $"{theme.DisplayName} {semanticLabel} material override";
                return true;

            case ReplacementMode.Overlay:
            {
                var overlayPrefab = matchedRule.ProxyPrefab != null
                    ? matchedRule.ProxyPrefab
                    : theme.GetDefaultProxy(semanticLabel);
                entry.ReplacementMode = ReplacementMode.Overlay;
                entry.ReplacementId = overlayPrefab != null
                    ? overlayPrefab.name
                    : $"{theme.ThemeId}_{semanticLabel}_overlay";
                entry.ReplacementDisplayName = overlayPrefab != null
                    ? overlayPrefab.name
                    : $"{theme.DisplayName} {semanticLabel} overlay";
                return true;
            }

            case ReplacementMode.FXOnly:
                entry.ReplacementMode = ReplacementMode.FXOnly;
                entry.ReplacementId = $"{theme.ThemeId}_{semanticLabel}_fx";
                entry.ReplacementDisplayName = $"{theme.DisplayName} {semanticLabel} FX";
                return true;

            case ReplacementMode.Skip:
                entry.ReplacementMode = ReplacementMode.Skip;
                entry.ReplacementId = $"{theme.ThemeId}_{semanticLabel}_skip";
                entry.ReplacementDisplayName = $"{semanticLabel} skipped";
                return true;

            default:
                return false;
        }
    }

    private SemanticReplacementRule FindRule(ThemeProfile theme, string semanticLabel, string functionTag)
    {
        if (theme.ReplacementRules == null)
        {
            return null;
        }

        for (var index = 0; index < theme.ReplacementRules.Count; index++)
        {
            var rule = theme.ReplacementRules[index];
            if (rule == null)
            {
                continue;
            }

            var semanticMatches = !string.IsNullOrWhiteSpace(rule.SemanticLabel) &&
                                  string.Equals(rule.SemanticLabel, semanticLabel, StringComparison.OrdinalIgnoreCase);
            var functionMatches = !string.IsNullOrWhiteSpace(rule.FunctionTag) &&
                                  string.Equals(rule.FunctionTag, functionTag, StringComparison.OrdinalIgnoreCase);
            if (semanticMatches || functionMatches)
            {
                return rule;
            }
        }

        return null;
    }

    private void ConfigureFallbackEntry(
        ThemeProfile theme,
        string semanticLabel,
        StylizationPlanEntry entry,
        ICollection<string> warnings)
    {
        switch (semanticLabel)
        {
            case "wall":
            case "floor":
            case "ceiling":
                entry.ReplacementMode = ReplacementMode.MaterialOverride;
                entry.ReplacementId = $"{theme.ThemeId}_{semanticLabel}_surface";
                entry.ReplacementDisplayName = $"{theme.DisplayName} {semanticLabel} treatment";
                entry.Rationale = $"{semanticLabel} uses theme surface colors to preserve room readability.";
                entry.Parameters.Add(new StylizationPlanParameter { Key = "surface", Value = semanticLabel });
                return;

            case "table":
                if (theme.DefaultTableProxy != null)
                {
                    entry.ReplacementMode = ReplacementMode.ProxyPrefab;
                    entry.ReplacementId = theme.DefaultTableProxy.name;
                    entry.ReplacementDisplayName = theme.DefaultTableProxy.name;
                    entry.Rationale = "Table uses the theme's default table proxy while preserving footprint.";
                }
                else
                {
                    entry.ReplacementMode = ReplacementMode.Overlay;
                    entry.ReplacementId = $"{theme.ThemeId}_table_overlay";
                    entry.ReplacementDisplayName = $"{theme.DisplayName} table overlay";
                    entry.Rationale = "Table falls back to overlay because no default table proxy is assigned.";
                    warnings.Add($"TABLE fallback: theme {theme.DisplayName} has no DefaultTableProxy.");
                }

                return;

            case "screen":
                if (theme.DefaultScreenTreatmentPrefab != null)
                {
                    entry.ReplacementMode = ReplacementMode.Overlay;
                    entry.ReplacementId = theme.DefaultScreenTreatmentPrefab.name;
                    entry.ReplacementDisplayName = theme.DefaultScreenTreatmentPrefab.name;
                    entry.Rationale = "Screen uses the theme's default screen treatment.";
                }
                else
                {
                    entry.ReplacementMode = ReplacementMode.FXOnly;
                    entry.ReplacementId = $"{theme.ThemeId}_screen_fx";
                    entry.ReplacementDisplayName = $"{theme.DisplayName} screen FX";
                    entry.Rationale = "Screen falls back to FX-only because no screen treatment prefab is assigned.";
                    warnings.Add($"SCREEN fallback: theme {theme.DisplayName} has no DefaultScreenTreatmentPrefab.");
                }

                return;

            case "storage":
                if (theme.DefaultStorageProxy != null)
                {
                    entry.ReplacementMode = ReplacementMode.ProxyPrefab;
                    entry.ReplacementId = theme.DefaultStorageProxy.name;
                    entry.ReplacementDisplayName = theme.DefaultStorageProxy.name;
                    entry.Rationale = "Storage uses the theme's default storage proxy while preserving footprint.";
                }
                else
                {
                    entry.ReplacementMode = ReplacementMode.Overlay;
                    entry.ReplacementId = $"{theme.ThemeId}_storage_overlay";
                    entry.ReplacementDisplayName = $"{theme.DisplayName} storage overlay";
                    entry.Rationale = "Storage falls back to overlay because no default storage proxy is assigned.";
                    warnings.Add($"STORAGE fallback: theme {theme.DisplayName} has no DefaultStorageProxy.");
                }

                return;

            case "seating":
                if (theme.DefaultSeatProxy != null)
                {
                    entry.ReplacementMode = ReplacementMode.ProxyPrefab;
                    entry.ReplacementId = theme.DefaultSeatProxy.name;
                    entry.ReplacementDisplayName = theme.DefaultSeatProxy.name;
                    entry.Rationale = "Seating uses the theme's default seat proxy while preserving walkable clearance.";
                }
                else
                {
                    entry.ReplacementMode = ReplacementMode.Overlay;
                    entry.ReplacementId = $"{theme.ThemeId}_seating_overlay";
                    entry.ReplacementDisplayName = $"{theme.DisplayName} seating overlay";
                    entry.Rationale = "Seating falls back to overlay because no default seat proxy is assigned.";
                    warnings.Add($"SEATING fallback: theme {theme.DisplayName} has no DefaultSeatProxy.");
                }

                return;

            default:
                entry.ReplacementMode = ReplacementMode.Skip;
                entry.ReplacementId = $"{theme.ThemeId}_{semanticLabel}_skip";
                entry.ReplacementDisplayName = $"{semanticLabel} skipped";
                entry.Rationale = $"No planner fallback exists for semantic label {semanticLabel}.";
                warnings.Add($"Unhandled semantic label in planner: {semanticLabel}");
                break;
        }
    }

    private static string Sanitize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "anchor";
        }

        return value.Replace(" ", "_");
    }
}
