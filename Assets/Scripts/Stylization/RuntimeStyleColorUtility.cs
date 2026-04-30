using System.Text;
using UnityEngine;

public static class RuntimeStyleColorUtility
{
    public static Color ResolveAccentColor(ThemeProfile scaffoldTheme, RuntimeStyleIntent runtimeIntent)
    {
        var signature = BuildSignature(runtimeIntent);
        if (ContainsAny(signature, "arcane", "archive", "occult", "gothic", "mystical", "amber", "gold", "brass", "burgundy", "parchment"))
        {
            return new Color(0.88f, 0.58f, 0.20f, 1f);
        }

        if (ContainsAny(signature, "future", "research", "lab", "cyber", "cyan", "holographic", "sci fi", "sci-fi"))
        {
            return new Color(0.22f, 0.87f, 0.96f, 1f);
        }

        if (ContainsAny(signature, "underwater", "ocean", "aquatic", "teal"))
        {
            return new Color(0.12f, 0.76f, 0.82f, 1f);
        }

        if (ContainsAny(signature, "solar", "biophilic", "forest", "organic", "green"))
        {
            return new Color(0.42f, 0.74f, 0.34f, 1f);
        }

        return scaffoldTheme != null ? scaffoldTheme.AccentColor : Color.cyan;
    }

    public static Color ResolveTrimBaseColor(ThemeProfile scaffoldTheme, RuntimeStyleIntent runtimeIntent)
    {
        var signature = BuildSignature(runtimeIntent);
        if (ContainsAny(signature, "arcane", "archive", "occult", "gothic", "mystical", "amber", "gold", "brass", "burgundy", "parchment"))
        {
            return new Color(0.18f, 0.10f, 0.055f, 1f);
        }

        if (ContainsAny(signature, "future", "research", "lab", "cyber", "cyan", "holographic", "sci fi", "sci-fi"))
        {
            return new Color(0.07f, 0.19f, 0.24f, 1f);
        }

        if (ContainsAny(signature, "underwater", "ocean", "aquatic", "teal"))
        {
            return new Color(0.04f, 0.18f, 0.20f, 1f);
        }

        if (ContainsAny(signature, "solar", "biophilic", "forest", "organic", "green"))
        {
            return new Color(0.18f, 0.26f, 0.13f, 1f);
        }

        if (scaffoldTheme == null)
        {
            return new Color(0.12f, 0.14f, 0.16f, 1f);
        }

        var color = Color.Lerp(Color.black, scaffoldTheme.AccentColor, 0.32f);
        color.a = 1f;
        return color;
    }

    private static string BuildSignature(RuntimeStyleIntent runtimeIntent)
    {
        if (runtimeIntent == null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(256);
        Append(builder, runtimeIntent.StyleId);
        Append(builder, runtimeIntent.StyleDisplayName);
        Append(builder, runtimeIntent.UserIntent);
        Append(builder, runtimeIntent.GlobalStyleSummary);
        Append(builder, runtimeIntent.StyleKeywords);
        Append(builder, runtimeIntent.MaterialKeywords);
        Append(builder, runtimeIntent.ColorKeywords);
        Append(builder, runtimeIntent.MotifKeywords);
        return builder.ToString().ToLowerInvariant();
    }

    private static void Append(StringBuilder builder, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            builder.Append(' ').Append(value.Trim());
        }
    }

    private static void Append(StringBuilder builder, System.Collections.Generic.IEnumerable<string> values)
    {
        if (values == null)
        {
            return;
        }

        foreach (var value in values)
        {
            Append(builder, value);
        }
    }

    private static bool ContainsAny(string value, params string[] candidates)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        for (var index = 0; index < candidates.Length; index++)
        {
            if (!string.IsNullOrWhiteSpace(candidates[index]) && value.Contains(candidates[index]))
            {
                return true;
            }
        }

        return false;
    }
}
