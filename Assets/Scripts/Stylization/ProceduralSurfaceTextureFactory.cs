using UnityEngine;

public static class ProceduralSurfaceTextureFactory
{
    private const int TextureSize = 256;

    public static Texture2D CreateTexture(ThemeProfile theme, ThemeSurfaceKind surfaceKind)
    {
        if (theme == null)
        {
            return null;
        }

        var tint = theme.SurfaceMaterials.GetTintColor(surfaceKind);
        tint.a = 1f;

        var texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, true)
        {
            name = $"Runtime_{theme.ThemeId}_{surfaceKind}_Pattern",
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Bilinear,
            anisoLevel = 2,
        };

        var pixels = new Color32[TextureSize * TextureSize];
        for (var y = 0; y < TextureSize; y++)
        {
            var v = y / (TextureSize - 1f);
            for (var x = 0; x < TextureSize; x++)
            {
                var u = x / (TextureSize - 1f);
                var color = theme.SurfaceMaterials.PatternFamily switch
                {
                    ThemeSurfacePatternFamily.ArcaneChamber => SampleArcanePattern(u, v, surfaceKind, tint, theme.SurfaceMaterials.PatternStrength),
                    ThemeSurfacePatternFamily.CleanPanels => SampleCleanPanelPattern(u, v, surfaceKind, tint, theme.SurfaceMaterials.PatternStrength),
                    _ => SampleFuturePattern(u, v, surfaceKind, tint, theme.SurfaceMaterials.PatternStrength),
                };

                pixels[y * TextureSize + x] = color;
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(true, false);
        return texture;
    }

    private static Color SampleFuturePattern(float u, float v, ThemeSurfaceKind surfaceKind, Color tint, float strength)
    {
        var baseColor = Color.Lerp(new Color(0.025f, 0.045f, 0.06f, 1f), tint, 0.34f);
        var panelColor = Color.Lerp(baseColor, tint, 0.24f);
        var lineColor = Color.Lerp(tint, Color.white, 0.18f);
        var accentColor = Color.Lerp(tint, Color.white, 0.46f);

        var panelScale = surfaceKind == ThemeSurfaceKind.Floor ? 6f : 4f;
        var microScale = surfaceKind == ThemeSurfaceKind.Ceiling ? 8f : 10f;
        var panelLine = Mathf.Max(RepeatLine(u * panelScale, 0.018f), RepeatLine(v * panelScale, 0.018f));
        var microLine = Mathf.Max(RepeatLine(u * microScale + v * 0.35f, 0.006f), RepeatLine(v * microScale, 0.006f));
        var diagonal = RepeatLine((u + v) * (surfaceKind == ThemeSurfaceKind.Wall ? 3f : 4f), 0.007f) * 0.55f;
        var node = GridNode(u * panelScale, v * panelScale, 0.038f);
        var scan = 0.08f * Mathf.Sin((v + u * 0.12f) * Mathf.PI * 24f);

        var color = Color.Lerp(baseColor, panelColor, 0.28f + scan);
        color = Color.Lerp(color, lineColor, Mathf.Clamp01(panelLine * strength));
        color = Color.Lerp(color, accentColor, Mathf.Clamp01((microLine * 0.44f + diagonal + node) * strength));
        color.a = 1f;
        return color;
    }

    private static Color SampleArcanePattern(float u, float v, ThemeSurfaceKind surfaceKind, Color tint, float strength)
    {
        var baseColor = Color.Lerp(new Color(0.11f, 0.065f, 0.035f, 1f), tint, 0.26f);
        var stoneColor = Color.Lerp(baseColor, tint, 0.18f);
        var lineColor = Color.Lerp(tint, new Color(1f, 0.86f, 0.52f, 1f), 0.36f);
        var darkLine = Color.Lerp(Color.black, tint, 0.16f);

        var tileScale = surfaceKind == ThemeSurfaceKind.Floor ? 5f : 3.5f;
        var grout = Mathf.Max(RepeatLine(u * tileScale, 0.016f), RepeatLine(v * tileScale, 0.016f));
        var offsetBrick = Mathf.Max(
            RepeatLine((u + Mathf.Floor(v * tileScale) * 0.18f) * tileScale, 0.012f),
            RepeatLine(v * tileScale, 0.012f));
        var ring = Ring(u, v, 0.31f, 0.012f) + Ring(u, v, 0.18f, 0.01f);
        var diagonal = RepeatLine((u - v) * 4.5f, 0.006f) * 0.5f;
        var glyph = Mathf.Max(ring, diagonal) * (surfaceKind == ThemeSurfaceKind.Ceiling ? 0.45f : 0.78f);
        var grain = (Hash01((int)(u * 64f), (int)(v * 64f)) - 0.5f) * 0.11f;

        var color = Color.Lerp(baseColor, stoneColor, Mathf.Clamp01(0.5f + grain));
        color = Color.Lerp(color, darkLine, Mathf.Clamp01(offsetBrick * 0.55f * strength));
        color = Color.Lerp(color, lineColor, Mathf.Clamp01((grout + glyph) * strength));
        color.a = 1f;
        return color;
    }

    private static Color SampleCleanPanelPattern(float u, float v, ThemeSurfaceKind surfaceKind, Color tint, float strength)
    {
        var baseColor = Color.Lerp(new Color(0.12f, 0.13f, 0.14f, 1f), tint, 0.28f);
        var lineColor = Color.Lerp(tint, Color.white, 0.22f);
        var scale = surfaceKind == ThemeSurfaceKind.Floor ? 5f : 4f;
        var line = Mathf.Max(RepeatLine(u * scale, 0.012f), RepeatLine(v * scale, 0.012f));
        var softBand = 0.5f + 0.5f * Mathf.Sin((u + v) * Mathf.PI * 2f);
        var color = Color.Lerp(baseColor, Color.Lerp(baseColor, tint, 0.18f), softBand * 0.22f);
        color = Color.Lerp(color, lineColor, Mathf.Clamp01(line * strength));
        color.a = 1f;
        return color;
    }

    private static float RepeatLine(float scaledValue, float width)
    {
        var repeat = Mathf.Repeat(scaledValue, 1f);
        var distance = Mathf.Min(repeat, 1f - repeat);
        return 1f - Mathf.SmoothStep(width, width * 2f, distance);
    }

    private static float GridNode(float scaledU, float scaledV, float radius)
    {
        var u = Mathf.Repeat(scaledU, 1f);
        var v = Mathf.Repeat(scaledV, 1f);
        var distance = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f));
        return 1f - Mathf.SmoothStep(radius, radius * 1.8f, distance);
    }

    private static float Ring(float u, float v, float radius, float width)
    {
        var distance = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f));
        return 1f - Mathf.SmoothStep(width, width * 2f, Mathf.Abs(distance - radius));
    }

    private static float Hash01(int x, int y)
    {
        unchecked
        {
            var hash = x * 73856093 ^ y * 19349663;
            hash = (hash << 13) ^ hash;
            return 1f - ((hash * (hash * hash * 15731 + 789221) + 1376312589) & 0x7fffffff) / 1073741824f;
        }
    }
}
