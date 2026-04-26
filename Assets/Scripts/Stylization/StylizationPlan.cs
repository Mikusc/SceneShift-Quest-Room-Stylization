using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

[Serializable]
public class StylizationPlan
{
    public string PlanId;
    public string ThemeId;
    public string ThemeDisplayName;
    public string RoomId;
    public string CreatedAtIsoUtc;
    public List<StylizationPlanEntry> Entries = new();
    public List<string> Warnings = new();

    public int EntryCount => Entries?.Count ?? 0;
    public int WarningCount => Warnings?.Count ?? 0;

    public string BuildDebugSummary(int maxWarnings = 3, int maxEntries = 5)
    {
        var builder = new StringBuilder(512);
        builder.AppendLine("[StylizationPlan]");
        builder.AppendLine($"Theme: {ThemeDisplayName} ({ThemeId})");
        builder.AppendLine($"Room: {RoomId}");
        builder.AppendLine($"Entries: {EntryCount}");
        builder.AppendLine($"Warnings: {WarningCount}");

        if (WarningCount > 0)
        {
            builder.Append("Warning Preview:");
            var warningLimit = Mathf.Min(maxWarnings, WarningCount);
            for (var index = 0; index < warningLimit; index++)
            {
                builder.Append(index == 0 ? " " : " | ");
                builder.Append(Warnings[index]);
            }

            builder.AppendLine();
        }

        if (EntryCount > 0)
        {
            builder.AppendLine("Entry Preview:");
            var entryLimit = Mathf.Min(maxEntries, EntryCount);
            for (var index = 0; index < entryLimit; index++)
            {
                var entry = Entries[index];
                builder.Append("  - ");
                builder.Append(entry.OriginalSemanticLabel);
                builder.Append(" -> ");
                builder.Append(entry.ReplacementMode);
                builder.Append(" (");
                builder.Append(entry.ReplacementDisplayName);
                builder.AppendLine(")");
            }
        }

        return builder.ToString().TrimEnd();
    }
}

[Serializable]
public class StylizationPlanEntry
{
    public string EntryId;
    public string ObjectId;
    public string OriginalSemanticLabel;
    public string OriginalFunctionTag;
    public ReplacementMode ReplacementMode;
    public string ReplacementId;
    public string ReplacementDisplayName;
    public string ReplicaName;
    public string ReplicaFunction;
    [TextArea(2, 6)] public string AppearancePrompt;
    public bool PreserveFootprint;
    public bool PreserveYawOrientation;
    public bool CollisionSensitive;
    [Range(0f, 1f)] public float PlannerConfidence = 1f;
    [TextArea(1, 3)] public string Rationale;
    public List<StylizationPlanParameter> Parameters = new();
}

[Serializable]
public class StylizationPlanParameter
{
    public string Key;
    public string Value;
}
