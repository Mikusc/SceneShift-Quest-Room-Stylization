using System;
using System.Collections.Generic;
using System.Text;
using Meta.XR.MRUtilityKit;
using UnityEngine;

[DisallowMultipleComponent]
public class SurfaceOverrideApplier : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private RoomSemanticBootstrap roomSemanticBootstrap;
    [SerializeField] private ThemeIntentController themeIntentController;
    [SerializeField] private Transform surfaceOverridesRoot;

    [Header("Surface Overrides")]
    [SerializeField] private bool applyWallOverrides = true;
    [SerializeField] private bool applyFloorOverrides = true;
    [SerializeField] private bool applyCeilingOverrides = true;
    [SerializeField] private bool suppressOriginalSurfaceRenderers;
    [SerializeField, Min(0f)] private float wallOutwardOffsetMeters = 0.05f;
    [SerializeField, Min(0f)] private float floorSurfaceOffsetMeters = 0.012f;
    [SerializeField, Min(0f)] private float ceilingSurfaceOffsetMeters = 0.012f;
    [SerializeField, Min(0.1f)] private float autoRefreshInterval = 0.75f;
    [SerializeField] private bool logApplications = true;

    [Header("Surface Visibility")]
    [SerializeField] private SurfaceVisibilityMode visibilityMode = SurfaceVisibilityMode.Background;
    [SerializeField, Range(0.05f, 1f)] private float backgroundWallAlpha = 0.3f;
    [SerializeField, Range(0.05f, 1f)] private float backgroundFloorAlpha = 0.24f;
    [SerializeField, Range(0.05f, 1f)] private float backgroundCeilingAlpha = 0.2f;
    [SerializeField, Range(0.5f, 1.5f)] private float demoOpacityBoost = 1.1f;
    [SerializeField, Range(0.2f, 1f)] private float demoMinimumWallAlpha = 0.94f;
    [SerializeField, Range(0.2f, 1f)] private float demoMinimumFloorAlpha = 0.92f;
    [SerializeField, Range(0.2f, 1f)] private float demoMinimumCeilingAlpha = 0.86f;

    public event Action SummaryChanged;

    public string LatestSummary => _latestSummary;

    private readonly List<GameObject> _spawnedOverrides = new();
    private readonly List<Mesh> _runtimeMeshes = new();
    private readonly List<Material> _runtimeMaterials = new();
    private readonly List<Texture2D> _runtimeTextures = new();
    private readonly Dictionary<Renderer, bool> _originalRendererStates = new();

    private string _latestSummary = "[SurfaceOverrideApplier]\nState: waiting\nHint: enter Play and wait for room + theme.";
    private bool _needsRefresh = true;
    private float _nextRefreshTime;

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
        _needsRefresh = true;
        _nextRefreshTime = 0f;
        Subscribe();
        RefreshOverrides("enabled");
    }

    private void OnDisable()
    {
        Unsubscribe();
        ResetOverrides();
    }

    private void Update()
    {
        if (!Application.isPlaying || !_needsRefresh)
        {
            return;
        }

        if (Time.unscaledTime < _nextRefreshTime)
        {
            return;
        }

        _nextRefreshTime = Time.unscaledTime + autoRefreshInterval;
        RefreshOverrides("auto-refresh");
    }

    [ContextMenu("Reapply Surface Overrides")]
    public void ReapplySurfaceOverrides()
    {
        RefreshOverrides("manual");
    }

    [ContextMenu("Reset Surface Overrides")]
    public void ResetSurfaceOverrides()
    {
        ResetOverrides();
        PublishWaitingState("reset");
    }

    private void ResolveReferences()
    {
        if (roomSemanticBootstrap == null)
        {
            roomSemanticBootstrap = FindAnyObjectByType<RoomSemanticBootstrap>();
        }

        if (themeIntentController == null)
        {
            themeIntentController = FindAnyObjectByType<ThemeIntentController>();
        }

        if (surfaceOverridesRoot == null)
        {
            var rootObject = GameObject.Find("SurfaceOverrides");
            if (rootObject != null)
            {
                surfaceOverridesRoot = rootObject.transform;
            }
        }
    }

    private void Subscribe()
    {
        if (roomSemanticBootstrap != null)
        {
            roomSemanticBootstrap.SummaryChanged -= HandleRoomChanged;
            roomSemanticBootstrap.SummaryChanged += HandleRoomChanged;
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
            roomSemanticBootstrap.SummaryChanged -= HandleRoomChanged;
        }

        if (themeIntentController != null)
        {
            themeIntentController.ThemeChanged -= HandleThemeChanged;
        }
    }

    private void HandleRoomChanged()
    {
        _needsRefresh = true;
        RefreshOverrides("room-changed");
    }

    private void HandleThemeChanged(ThemeProfile _)
    {
        _needsRefresh = true;
        RefreshOverrides("theme-changed");
    }

    private void RefreshOverrides(string reason)
    {
        ResolveReferences();

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

        if (surfaceOverridesRoot == null)
        {
            PublishWaitingState("missing-surface-overrides-root");
            return;
        }

        if (!roomSemanticBootstrap.HasReadyRoom || roomSemanticBootstrap.CurrentRoom == null)
        {
            ResetOverrides();
            PublishWaitingState("waiting-for-room");
            return;
        }

        if (themeIntentController.ActiveTheme == null)
        {
            ResetOverrides();
            PublishWaitingState("waiting-for-theme");
            return;
        }

        ApplyOverrides(themeIntentController.ActiveTheme, roomSemanticBootstrap.CurrentRoom, reason);
    }

    private void ApplyOverrides(ThemeProfile theme, MRUKRoom room, string reason)
    {
        ResetOverrides();

        var wallCount = 0;
        var floorCount = 0;
        var ceilingCount = 0;
        var skippedCount = 0;
        var materials = new Dictionary<ThemeSurfaceKind, Material>();

        for (var index = 0; index < room.Anchors.Count; index++)
        {
            var anchor = room.Anchors[index];
            if (!TryGetSurfaceKind(anchor, out var surfaceKind))
            {
                continue;
            }

            if (!ShouldApply(surfaceKind) || !anchor.PlaneRect.HasValue)
            {
                skippedCount++;
                continue;
            }

            if (!materials.TryGetValue(surfaceKind, out var material))
            {
                material = CreateSurfaceMaterial(theme, surfaceKind);
                materials[surfaceKind] = material;
            }

            if (!TryCreateOverridePlane(anchor, surfaceKind, material, index))
            {
                skippedCount++;
                continue;
            }

            if (suppressOriginalSurfaceRenderers)
            {
                SuppressAnchorRenderers(anchor);
            }

            switch (surfaceKind)
            {
                case ThemeSurfaceKind.Wall:
                    wallCount++;
                    break;
                case ThemeSurfaceKind.Floor:
                    floorCount++;
                    break;
                case ThemeSurfaceKind.Ceiling:
                    ceilingCount++;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        _needsRefresh = visibilityMode != SurfaceVisibilityMode.Off && wallCount + floorCount + ceilingCount == 0;
        _latestSummary = BuildSummary(reason, theme, wallCount, floorCount, ceilingCount, skippedCount);
        SummaryChanged?.Invoke();

        if (logApplications && wallCount + floorCount + ceilingCount > 0)
        {
            Debug.Log(_latestSummary, this);
        }
    }

    private bool TryCreateOverridePlane(
        MRUKAnchor anchor,
        ThemeSurfaceKind surfaceKind,
        Material material,
        int anchorIndex)
    {
        if (anchor == null || material == null || surfaceOverridesRoot == null || !anchor.PlaneRect.HasValue)
        {
            return false;
        }

        var rect = anchor.PlaneRect.Value;
        var offset = GetSurfaceOffset(surfaceKind);
        var surface = new GameObject($"SurfaceOverride_{surfaceKind}_{anchorIndex:D2}");
        surface.transform.SetParent(surfaceOverridesRoot, false);
        surface.transform.position = anchor.transform.position;
        surface.transform.rotation = anchor.transform.rotation;
        surface.transform.localScale = Vector3.one;

        var meshFilter = surface.AddComponent<MeshFilter>();
        var meshRenderer = surface.AddComponent<MeshRenderer>();
        var mesh = CreatePlaneMesh(rect, offset, surfaceKind);
        mesh.name = $"{surface.name}_Mesh";
        meshFilter.sharedMesh = mesh;
        meshRenderer.sharedMaterial = material;

        _runtimeMeshes.Add(mesh);
        _spawnedOverrides.Add(surface);
        return true;
    }

    private static Mesh CreatePlaneMesh(Rect rect, float normalOffset, ThemeSurfaceKind surfaceKind)
    {
        var vertices = new[]
        {
            new Vector3(rect.xMin, rect.yMin, normalOffset),
            new Vector3(rect.xMax, rect.yMin, normalOffset),
            new Vector3(rect.xMax, rect.yMax, normalOffset),
            new Vector3(rect.xMin, rect.yMax, normalOffset),
        };

        var width = Mathf.Max(0.05f, rect.width);
        var height = Mathf.Max(0.05f, rect.height);
        var uv = new[]
        {
            Vector2.zero,
            new Vector2(width, 0f),
            new Vector2(width, height),
            new Vector2(0f, height),
        };

        var mesh = new Mesh
        {
            vertices = vertices,
            uv = uv,
            triangles = new[] { 0, 1, 2, 0, 2, 3 },
        };

        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        if (surfaceKind == ThemeSurfaceKind.Ceiling)
        {
            mesh.bounds = new Bounds(mesh.bounds.center, mesh.bounds.size + Vector3.one * 0.001f);
        }

        return mesh;
    }

    private Material CreateSurfaceMaterial(ThemeProfile theme, ThemeSurfaceKind surfaceKind)
    {
        var sourceMaterial = theme.SurfaceMaterials.GetMaterialOverride(surfaceKind);
        Material material;

        if (sourceMaterial != null)
        {
            material = new Material(sourceMaterial);
        }
        else
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ??
                         Shader.Find("Universal Render Pipeline/Unlit") ??
                         Shader.Find("Standard");
            material = new Material(shader);
            ConfigureTransparentDoubleSidedMaterial(material);

            var texture = ProceduralSurfaceTextureFactory.CreateTexture(theme, surfaceKind);
            if (texture != null)
            {
                _runtimeTextures.Add(texture);
                SetMaterialTexture(material, texture, theme.SurfaceMaterials.TextureTiling);
            }
        }

        var tintColor = ApplyVisibilityBoost(theme.SurfaceMaterials.GetTintColor(surfaceKind), surfaceKind);
        SetMaterialColor(material, new Color(1f, 1f, 1f, tintColor.a));
        SetEmission(material, tintColor * Mathf.Max(0f, theme.SurfaceMaterials.EmissionIntensity));
        ConfigureTransparentDoubleSidedMaterial(material);
        _runtimeMaterials.Add(material);
        return material;
    }

    private bool ShouldApply(ThemeSurfaceKind surfaceKind)
    {
        if (visibilityMode == SurfaceVisibilityMode.Off)
        {
            return false;
        }

        return surfaceKind switch
        {
            ThemeSurfaceKind.Wall => applyWallOverrides,
            ThemeSurfaceKind.Floor => applyFloorOverrides,
            ThemeSurfaceKind.Ceiling => applyCeilingOverrides,
            _ => false,
        };
    }

    private float GetSurfaceOffset(ThemeSurfaceKind surfaceKind)
    {
        return surfaceKind switch
        {
            ThemeSurfaceKind.Wall => wallOutwardOffsetMeters,
            ThemeSurfaceKind.Floor => floorSurfaceOffsetMeters,
            ThemeSurfaceKind.Ceiling => ceilingSurfaceOffsetMeters,
            _ => 0f,
        };
    }

    private Color ApplyVisibilityBoost(Color color, ThemeSurfaceKind surfaceKind)
    {
        if (visibilityMode == SurfaceVisibilityMode.Background)
        {
            color.a = Mathf.Clamp01(Mathf.Min(color.a, GetBackgroundAlpha(surfaceKind)));
            return color;
        }

        var minimumAlpha = surfaceKind switch
        {
            ThemeSurfaceKind.Wall => demoMinimumWallAlpha,
            ThemeSurfaceKind.Floor => demoMinimumFloorAlpha,
            ThemeSurfaceKind.Ceiling => demoMinimumCeilingAlpha,
            _ => 0f,
        };

        color.a = Mathf.Clamp01(Mathf.Max(color.a * Mathf.Max(0f, demoOpacityBoost), minimumAlpha));
        return color;
    }

    private float GetBackgroundAlpha(ThemeSurfaceKind surfaceKind)
    {
        return surfaceKind switch
        {
            ThemeSurfaceKind.Wall => backgroundWallAlpha,
            ThemeSurfaceKind.Floor => backgroundFloorAlpha,
            ThemeSurfaceKind.Ceiling => backgroundCeilingAlpha,
            _ => 0f,
        };
    }

    private static bool TryGetSurfaceKind(MRUKAnchor anchor, out ThemeSurfaceKind surfaceKind)
    {
        if (anchor != null)
        {
            if (anchor.HasAnyLabel(MRUKAnchor.SceneLabels.WALL_FACE))
            {
                surfaceKind = ThemeSurfaceKind.Wall;
                return true;
            }

            if (anchor.HasAnyLabel(MRUKAnchor.SceneLabels.FLOOR))
            {
                surfaceKind = ThemeSurfaceKind.Floor;
                return true;
            }

            if (anchor.HasAnyLabel(MRUKAnchor.SceneLabels.CEILING))
            {
                surfaceKind = ThemeSurfaceKind.Ceiling;
                return true;
            }
        }

        surfaceKind = default;
        return false;
    }

    private void SuppressAnchorRenderers(MRUKAnchor anchor)
    {
        var renderers = anchor.GetComponentsInChildren<Renderer>(true);
        foreach (var renderer in renderers)
        {
            if (renderer == null || renderer is ParticleSystemRenderer)
            {
                continue;
            }

            if (!_originalRendererStates.ContainsKey(renderer))
            {
                _originalRendererStates[renderer] = renderer.enabled;
            }

            renderer.enabled = false;
        }
    }

    private void ResetOverrides()
    {
        foreach (var pair in _originalRendererStates)
        {
            if (pair.Key != null)
            {
                pair.Key.enabled = pair.Value;
            }
        }

        _originalRendererStates.Clear();

        foreach (var spawnedOverride in _spawnedOverrides)
        {
            if (spawnedOverride != null)
            {
                SafeDestroy(spawnedOverride);
            }
        }

        _spawnedOverrides.Clear();

        foreach (var mesh in _runtimeMeshes)
        {
            if (mesh != null)
            {
                SafeDestroy(mesh);
            }
        }

        _runtimeMeshes.Clear();

        foreach (var material in _runtimeMaterials)
        {
            if (material != null)
            {
                SafeDestroy(material);
            }
        }

        _runtimeMaterials.Clear();

        foreach (var texture in _runtimeTextures)
        {
            if (texture != null)
            {
                SafeDestroy(texture);
            }
        }

        _runtimeTextures.Clear();
    }

    private string BuildSummary(
        string reason,
        ThemeProfile theme,
        int wallCount,
        int floorCount,
        int ceilingCount,
        int skippedCount)
    {
        var builder = new StringBuilder(384);
        builder.AppendLine("[SurfaceOverrideApplier]");
        builder.AppendLine(visibilityMode == SurfaceVisibilityMode.Off
            ? "State: off"
            : wallCount + floorCount + ceilingCount > 0 ? "State: applied" : "State: waiting-for-targets");
        builder.AppendLine($"Theme: {theme.DisplayName}");
        builder.AppendLine($"Reason: {reason}");
        builder.AppendLine($"Visibility Mode: {visibilityMode}");
        builder.AppendLine($"Override Planes: {wallCount + floorCount + ceilingCount}");
        builder.AppendLine($"Coverage: floor={floorCount}, wall={wallCount}, ceiling={ceilingCount}");
        builder.AppendLine($"Skipped: {skippedCount}");
        builder.AppendLine($"Wall Offset: {wallOutwardOffsetMeters:F3}m");
        if (visibilityMode == SurfaceVisibilityMode.Background)
        {
            builder.AppendLine($"Background Alpha: wall={backgroundWallAlpha:F2}, floor={backgroundFloorAlpha:F2}, ceiling={backgroundCeilingAlpha:F2}");
        }
        else if (visibilityMode == SurfaceVisibilityMode.DemoStrong)
        {
            builder.AppendLine($"Demo Opacity Boost: {demoOpacityBoost:F2}x");
            builder.AppendLine($"Demo Min Alpha: wall={demoMinimumWallAlpha:F2}, floor={demoMinimumFloorAlpha:F2}, ceiling={demoMinimumCeilingAlpha:F2}");
        }

        builder.Append($"Root: {(surfaceOverridesRoot != null ? surfaceOverridesRoot.name : "none")}");
        return builder.ToString();
    }

    private void PublishWaitingState(string reason)
    {
        _needsRefresh = true;
        _latestSummary = $"[SurfaceOverrideApplier]\nState: {reason}\nHint: wait for MRUK room, theme, and SurfaceOverrides root.";
        SummaryChanged?.Invoke();
    }

    private static void ConfigureTransparentDoubleSidedMaterial(Material material)
    {
        if (material.HasProperty("_Surface"))
        {
            material.SetFloat("_Surface", 1f);
        }

        if (material.HasProperty("_Blend"))
        {
            material.SetFloat("_Blend", 0f);
        }

        if (material.HasProperty("_AlphaClip"))
        {
            material.SetFloat("_AlphaClip", 0f);
        }

        if (material.HasProperty("_SrcBlend"))
        {
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        }

        if (material.HasProperty("_DstBlend"))
        {
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        }

        if (material.HasProperty("_ZWrite"))
        {
            material.SetFloat("_ZWrite", 0f);
        }

        if (material.HasProperty("_Cull"))
        {
            material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
        }

        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
    }

    private static void SetMaterialColor(Material material, Color color)
    {
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }

        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }
    }

    private static void SetMaterialTexture(Material material, Texture texture, float tiling)
    {
        var textureScale = Vector2.one * Mathf.Max(0.1f, tiling);
        if (material.HasProperty("_BaseMap"))
        {
            material.SetTexture("_BaseMap", texture);
            material.SetTextureScale("_BaseMap", textureScale);
        }

        if (material.HasProperty("_MainTex"))
        {
            material.SetTexture("_MainTex", texture);
            material.SetTextureScale("_MainTex", textureScale);
        }
    }

    private static void SetEmission(Material material, Color emissionColor)
    {
        if (!material.HasProperty("_EmissionColor"))
        {
            return;
        }

        material.EnableKeyword("_EMISSION");
        material.SetColor("_EmissionColor", emissionColor);
    }

    private static void SafeDestroy(UnityEngine.Object target)
    {
        if (target == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(target);
        }
        else
        {
            DestroyImmediate(target);
        }
    }
}

public enum SurfaceVisibilityMode
{
    Off,
    Background,
    DemoStrong,
}
