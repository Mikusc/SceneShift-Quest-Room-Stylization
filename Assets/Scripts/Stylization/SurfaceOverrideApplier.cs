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
    [SerializeField] private RuntimeStyleIntentController runtimeStyleIntentController;
    [SerializeField] private Transform surfaceOverridesRoot;

    [Header("Surface Overrides")]
    [SerializeField] private bool applyWallOverrides = true;
    [SerializeField] private bool applyFloorOverrides = true;
    [SerializeField] private bool applyCeilingOverrides = true;
    [SerializeField] private bool applyDoorFrameOverrides = true;
    [SerializeField] private bool applyWindowFrameOverrides = true;
    [SerializeField] private bool applyWindowVistaOverlays = true;
    [SerializeField] private bool suppressOriginalSurfaceRenderers;
    [SerializeField] private bool preferGeneratedSurfaceTextures = true;
    [SerializeField] private string generatedSurfaceJobFolderName = "SurfaceTextureJobs";
    [SerializeField, Min(0f)] private float wallOutwardOffsetMeters = 0.05f;
    [SerializeField, Min(0f)] private float floorSurfaceOffsetMeters = 0.012f;
    [SerializeField, Min(0f)] private float ceilingSurfaceOffsetMeters = 0.012f;
    [SerializeField, Min(0f)] private float frameOutwardOffsetMeters = 0.035f;
    [SerializeField, Min(0.01f)] private float frameBorderWidthMeters = 0.08f;
    [SerializeField, Min(0f), Tooltip("Distance to push window vista back outside the room, behind the window frame.")]
    private float windowVistaOutwardOffsetMeters = 0.04f;
    [SerializeField, Min(0.1f), Tooltip("Expected generated exterior vista image aspect ratio. APIMart window vista jobs currently request 16:9.")]
    private float windowVistaAspectRatio = 16f / 9f;
    [SerializeField, Range(1f, 1.5f)] private float windowVistaScaleMultiplier = 1.12f;
    [SerializeField, Tooltip("Use the largest valid WINDOW_FRAME for the exterior vista to avoid small false-positive windows.")]
    private bool applyVistaToLargestWindowOnly = true;
    [SerializeField, Min(0f)] private float minimumWindowFrameMajorSizeMeters = 1.0f;
    [SerializeField, Min(0f)] private float minimumWindowFrameMinorSizeMeters = 0.55f;
    [SerializeField, Min(0.1f)] private float autoRefreshInterval = 0.75f;
    [SerializeField] private bool logApplications = true;

    [Header("Surface Visibility")]
    [SerializeField] private SurfaceVisibilityMode visibilityMode = SurfaceVisibilityMode.Background;
    [SerializeField, Range(0.05f, 1f)] private float backgroundWallAlpha = 1f;
    [SerializeField, Range(0.05f, 1f)] private float backgroundFloorAlpha = 1f;
    [SerializeField, Range(0.05f, 1f)] private float backgroundCeilingAlpha = 1f;
    [SerializeField, Range(0.05f, 1f)] private float backgroundWindowVistaAlpha = 1f;
    [SerializeField, Range(0.5f, 1.5f)] private float demoOpacityBoost = 1.1f;
    [SerializeField, Range(0.2f, 1f)] private float demoMinimumWallAlpha = 1f;
    [SerializeField, Range(0.2f, 1f)] private float demoMinimumFloorAlpha = 1f;
    [SerializeField, Range(0.2f, 1f)] private float demoMinimumCeilingAlpha = 1f;
    [SerializeField, Range(0.2f, 1f)] private float demoMinimumWindowVistaAlpha = 0.92f;
    [SerializeField, Range(0f, 2f)] private float windowVistaEmissionIntensity = 0.45f;

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
    private string _lastGeneratedTextureSignature = string.Empty;

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
        if (!Application.isPlaying)
        {
            return;
        }

        if (Time.unscaledTime < _nextRefreshTime)
        {
            return;
        }

        _nextRefreshTime = Time.unscaledTime + autoRefreshInterval;
        if (_needsRefresh)
        {
            RefreshOverrides("auto-refresh");
            return;
        }

        if (preferGeneratedSurfaceTextures && HasGeneratedTextureSignatureChanged())
        {
            RefreshOverrides("generated-texture-ready");
        }
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

        if (runtimeStyleIntentController == null)
        {
            runtimeStyleIntentController = FindAnyObjectByType<RuntimeStyleIntentController>();
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

        if (runtimeStyleIntentController != null)
        {
            runtimeStyleIntentController.StyleIntentChanged -= HandleStyleIntentChanged;
            runtimeStyleIntentController.StyleIntentChanged += HandleStyleIntentChanged;
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

        if (runtimeStyleIntentController != null)
        {
            runtimeStyleIntentController.StyleIntentChanged -= HandleStyleIntentChanged;
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

    private void HandleStyleIntentChanged()
    {
        _needsRefresh = true;
        RefreshOverrides("style-intent-changed");
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
        var doorFrameCount = 0;
        var windowFrameCount = 0;
        var windowVistaCount = 0;
        var skippedCount = 0;
        var materials = new Dictionary<ThemeSurfaceKind, Material>();
        var primaryVistaAnchor = applyVistaToLargestWindowOnly ? FindPrimaryWindowVistaAnchor(room) : null;

        for (var index = 0; index < room.Anchors.Count; index++)
        {
            var anchor = room.Anchors[index];
            if (!TryGetSurfaceKind(anchor, out var surfaceKind))
            {
                continue;
            }

            if (!anchor.PlaneRect.HasValue)
            {
                skippedCount++;
                continue;
            }

            if (surfaceKind == ThemeSurfaceKind.WindowFrame &&
                !IsWindowFrameEligible(anchor.PlaneRect.Value))
            {
                skippedCount++;
                continue;
            }

            var shouldApplySurface = ShouldApply(surfaceKind);
            var shouldApplyVista = surfaceKind == ThemeSurfaceKind.WindowFrame &&
                                   applyWindowVistaOverlays &&
                                   (!applyVistaToLargestWindowOnly || ReferenceEquals(anchor, primaryVistaAnchor));
            if (!shouldApplySurface && !shouldApplyVista)
            {
                skippedCount++;
                continue;
            }

            if (shouldApplySurface)
            {
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
                    case ThemeSurfaceKind.DoorFrame:
                        doorFrameCount++;
                        break;
                    case ThemeSurfaceKind.WindowFrame:
                        windowFrameCount++;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            if (shouldApplyVista)
            {
                if (!materials.TryGetValue(ThemeSurfaceKind.WindowVista, out var vistaMaterial))
                {
                    vistaMaterial = CreateSurfaceMaterial(theme, ThemeSurfaceKind.WindowVista);
                    materials[ThemeSurfaceKind.WindowVista] = vistaMaterial;
                }

                if (TryCreateOverridePlane(anchor, ThemeSurfaceKind.WindowVista, vistaMaterial, index))
                {
                    windowVistaCount++;
                }
                else
                {
                    skippedCount++;
                }
            }
        }

        _needsRefresh = visibilityMode != SurfaceVisibilityMode.Off &&
                        wallCount + floorCount + ceilingCount + doorFrameCount + windowFrameCount + windowVistaCount == 0;
        _lastGeneratedTextureSignature = BuildGeneratedTextureSignature(theme);
        _latestSummary = BuildSummary(reason, theme, wallCount, floorCount, ceilingCount, doorFrameCount, windowFrameCount, windowVistaCount, skippedCount);
        SummaryChanged?.Invoke();

        if (logApplications && wallCount + floorCount + ceilingCount + doorFrameCount + windowFrameCount + windowVistaCount > 0)
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

        var rect = GetSurfaceRect(anchor.PlaneRect.Value, surfaceKind);
        var offset = GetSurfaceOffset(surfaceKind);
        var surface = new GameObject($"SurfaceOverride_{surfaceKind}_{anchorIndex:D2}");
        surface.transform.SetParent(surfaceOverridesRoot, false);
        surface.transform.position = anchor.transform.position;
        surface.transform.rotation = anchor.transform.rotation;
        surface.transform.localScale = Vector3.one;

        var meshFilter = surface.AddComponent<MeshFilter>();
        var meshRenderer = surface.AddComponent<MeshRenderer>();
        var mesh = CreateOverrideMesh(rect, offset, surfaceKind, frameBorderWidthMeters);
        mesh.name = $"{surface.name}_Mesh";
        meshFilter.sharedMesh = mesh;
        meshRenderer.sharedMaterial = material;

        _runtimeMeshes.Add(mesh);
        _spawnedOverrides.Add(surface);
        return true;
    }

    private Rect GetSurfaceRect(Rect rect, ThemeSurfaceKind surfaceKind)
    {
        // Window vistas stay clipped to the MRUK window rect; cover-fit happens in UV space.
        return rect;
    }

    private MRUKAnchor FindPrimaryWindowVistaAnchor(MRUKRoom room)
    {
        if (room == null || !applyWindowVistaOverlays)
        {
            return null;
        }

        MRUKAnchor bestAnchor = null;
        var bestArea = 0f;
        foreach (var anchor in room.Anchors)
        {
            if (anchor == null ||
                !anchor.PlaneRect.HasValue ||
                !anchor.HasAnyLabel(MRUKAnchor.SceneLabels.WINDOW_FRAME) ||
                !IsWindowFrameEligible(anchor.PlaneRect.Value))
            {
                continue;
            }

            var rect = anchor.PlaneRect.Value;
            var area = Mathf.Abs(rect.width * rect.height);
            if (area <= bestArea)
            {
                continue;
            }

            bestArea = area;
            bestAnchor = anchor;
        }

        return bestAnchor;
    }

    private bool IsWindowFrameEligible(Rect rect)
    {
        var major = Mathf.Max(Mathf.Abs(rect.width), Mathf.Abs(rect.height));
        var minor = Mathf.Min(Mathf.Abs(rect.width), Mathf.Abs(rect.height));
        return major >= minimumWindowFrameMajorSizeMeters &&
               minor >= minimumWindowFrameMinorSizeMeters;
    }

    private Mesh CreateOverrideMesh(Rect rect, float normalOffset, ThemeSurfaceKind surfaceKind, float frameBorderWidth)
    {
        return surfaceKind is ThemeSurfaceKind.DoorFrame or ThemeSurfaceKind.WindowFrame
            ? CreateFrameMesh(rect, normalOffset, frameBorderWidth)
            : CreatePlaneMesh(rect, normalOffset, surfaceKind, windowVistaAspectRatio, windowVistaScaleMultiplier);
    }

    private static Mesh CreatePlaneMesh(
        Rect rect,
        float normalOffset,
        ThemeSurfaceKind surfaceKind,
        float windowVistaAspectRatio,
        float windowVistaScaleMultiplier)
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
        var uv = surfaceKind == ThemeSurfaceKind.WindowVista
            ? CreateWindowVistaUv(rect, windowVistaAspectRatio, windowVistaScaleMultiplier)
            : new[]
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

    private static Vector2[] CreateWindowVistaUv(Rect rect, float windowVistaAspectRatio, float windowVistaScaleMultiplier)
    {
        var targetWidth = Mathf.Max(0.01f, rect.width);
        var targetHeight = Mathf.Max(0.01f, rect.height);
        var targetAspect = targetWidth / targetHeight;
        var imageAspect = Mathf.Max(0.1f, windowVistaAspectRatio);
        var overscan = Mathf.Max(1f, windowVistaScaleMultiplier);
        var uMin = 0f;
        var uMax = 1f;
        var vMin = 0f;
        var vMax = 1f;

        if (targetAspect > imageAspect)
        {
            var visibleHeight = Mathf.Clamp01(imageAspect / targetAspect / overscan);
            vMin = (1f - visibleHeight) * 0.5f;
            vMax = 1f - vMin;
        }
        else
        {
            var visibleWidth = Mathf.Clamp01(targetAspect / imageAspect / overscan);
            uMin = (1f - visibleWidth) * 0.5f;
            uMax = 1f - uMin;
        }

        return new[]
        {
            new Vector2(uMin, vMin),
            new Vector2(uMax, vMin),
            new Vector2(uMax, vMax),
            new Vector2(uMin, vMax),
        };
    }

    private static Mesh CreateFrameMesh(Rect rect, float normalOffset, float frameBorderWidth)
    {
        var width = Mathf.Max(0.05f, rect.width);
        var height = Mathf.Max(0.05f, rect.height);
        var border = Mathf.Min(
            Mathf.Max(0.01f, frameBorderWidth),
            Mathf.Min(width, height) * 0.45f);

        var vertices = new List<Vector3>(16);
        var uv = new List<Vector2>(16);
        var triangles = new List<int>(24);

        AddQuad(vertices, uv, triangles, rect.xMin, rect.xMin + border, rect.yMin, rect.yMax, normalOffset);
        AddQuad(vertices, uv, triangles, rect.xMax - border, rect.xMax, rect.yMin, rect.yMax, normalOffset);
        AddQuad(vertices, uv, triangles, rect.xMin + border, rect.xMax - border, rect.yMax - border, rect.yMax, normalOffset);
        AddQuad(vertices, uv, triangles, rect.xMin + border, rect.xMax - border, rect.yMin, rect.yMin + border, normalOffset);

        var mesh = new Mesh
        {
            vertices = vertices.ToArray(),
            uv = uv.ToArray(),
            triangles = triangles.ToArray(),
        };

        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void AddQuad(
        ICollection<Vector3> vertices,
        ICollection<Vector2> uv,
        ICollection<int> triangles,
        float xMin,
        float xMax,
        float yMin,
        float yMax,
        float normalOffset)
    {
        var start = vertices.Count;
        vertices.Add(new Vector3(xMin, yMin, normalOffset));
        vertices.Add(new Vector3(xMax, yMin, normalOffset));
        vertices.Add(new Vector3(xMax, yMax, normalOffset));
        vertices.Add(new Vector3(xMin, yMax, normalOffset));

        uv.Add(new Vector2(xMin, yMin));
        uv.Add(new Vector2(xMax, yMin));
        uv.Add(new Vector2(xMax, yMax));
        uv.Add(new Vector2(xMin, yMax));

        triangles.Add(start);
        triangles.Add(start + 1);
        triangles.Add(start + 2);
        triangles.Add(start);
        triangles.Add(start + 2);
        triangles.Add(start + 3);
    }

    private Material CreateSurfaceMaterial(ThemeProfile theme, ThemeSurfaceKind surfaceKind)
    {
        if (preferGeneratedSurfaceTextures &&
            TryCreateGeneratedSurfaceMaterial(theme, surfaceKind, out var generatedMaterial))
        {
            _runtimeMaterials.Add(generatedMaterial);
            return generatedMaterial;
        }

        var sourceMaterial = theme.SurfaceMaterials.GetMaterialOverride(surfaceKind);
        Material material;

        if (sourceMaterial != null)
        {
            material = new Material(sourceMaterial);
        }
        else
        {
            var shader = GetSurfaceShader(surfaceKind);
            material = new Material(shader);
            ConfigureTransparentDoubleSidedMaterial(material, surfaceKind);

            var texture = ProceduralSurfaceTextureFactory.CreateTexture(theme, surfaceKind);
            if (texture != null)
            {
                _runtimeTextures.Add(texture);
                SetMaterialTexture(material, texture, GetTextureTiling(theme, surfaceKind));
            }
        }

        var tintColor = ApplyVisibilityBoost(theme.SurfaceMaterials.GetTintColor(surfaceKind), surfaceKind);
        SetMaterialColor(material, new Color(1f, 1f, 1f, tintColor.a));
        SetEmission(material, tintColor * GetEmissionIntensity(theme, surfaceKind));
        ConfigureTransparentDoubleSidedMaterial(material, surfaceKind);
        _runtimeMaterials.Add(material);
        return material;
    }

    private bool TryCreateGeneratedSurfaceMaterial(ThemeProfile theme, ThemeSurfaceKind surfaceKind, out Material material)
    {
        material = null;
        if (theme == null || !TryFindGeneratedSurfaceTexture(theme, surfaceKind, out var imagePath))
        {
            return false;
        }

        var bytes = System.IO.File.ReadAllBytes(imagePath);
        if (bytes == null || bytes.Length == 0)
        {
            return false;
        }

        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, true)
        {
            name = $"Generated_{theme.ThemeId}_{surfaceKind}_Texture",
            wrapMode = surfaceKind == ThemeSurfaceKind.WindowVista ? TextureWrapMode.Clamp : TextureWrapMode.Repeat,
            filterMode = FilterMode.Bilinear,
            anisoLevel = 4,
        };

        if (!ImageConversion.LoadImage(texture, bytes, false))
        {
            SafeDestroy(texture);
            return false;
        }

        _runtimeTextures.Add(texture);

        var shader = GetSurfaceShader(surfaceKind);
        material = new Material(shader)
        {
            name = $"Runtime_{theme.ThemeId}_{surfaceKind}_GeneratedSurface",
        };
        ConfigureTransparentDoubleSidedMaterial(material, surfaceKind);
        SetMaterialTexture(material, texture, GetTextureTiling(theme, surfaceKind));

        var tintColor = ApplyVisibilityBoost(theme.SurfaceMaterials.GetTintColor(surfaceKind), surfaceKind);
        SetMaterialColor(material, new Color(1f, 1f, 1f, tintColor.a));
        SetEmission(material, tintColor * GetEmissionIntensity(theme, surfaceKind));
        ConfigureTransparentDoubleSidedMaterial(material, surfaceKind);
        return true;
    }

    private float GetTextureTiling(ThemeProfile theme, ThemeSurfaceKind surfaceKind)
    {
        return surfaceKind == ThemeSurfaceKind.WindowVista ? 1f : theme.SurfaceMaterials.TextureTiling;
    }

    private float GetEmissionIntensity(ThemeProfile theme, ThemeSurfaceKind surfaceKind)
    {
        return surfaceKind == ThemeSurfaceKind.WindowVista
            ? Mathf.Max(0f, windowVistaEmissionIntensity)
            : Mathf.Max(0f, theme.SurfaceMaterials.EmissionIntensity);
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
            ThemeSurfaceKind.DoorFrame => applyDoorFrameOverrides,
            ThemeSurfaceKind.WindowFrame => applyWindowFrameOverrides,
            ThemeSurfaceKind.WindowVista => applyWindowVistaOverlays,
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
            ThemeSurfaceKind.DoorFrame => frameOutwardOffsetMeters,
            ThemeSurfaceKind.WindowFrame => frameOutwardOffsetMeters,
            ThemeSurfaceKind.WindowVista => -windowVistaOutwardOffsetMeters,
            _ => 0f,
        };
    }

    private Color ApplyVisibilityBoost(Color color, ThemeSurfaceKind surfaceKind)
    {
        if (IsOpaqueRoomSurface(surfaceKind))
        {
            color.a = 1f;
            return color;
        }

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
            ThemeSurfaceKind.DoorFrame => demoMinimumWallAlpha,
            ThemeSurfaceKind.WindowFrame => demoMinimumWallAlpha,
            ThemeSurfaceKind.WindowVista => demoMinimumWindowVistaAlpha,
            _ => 0f,
        };

        color.a = Mathf.Clamp01(Mathf.Max(color.a * Mathf.Max(0f, demoOpacityBoost), minimumAlpha));
        return color;
    }

    private static bool IsOpaqueRoomSurface(ThemeSurfaceKind surfaceKind)
    {
        return surfaceKind is ThemeSurfaceKind.Wall
            or ThemeSurfaceKind.Floor
            or ThemeSurfaceKind.Ceiling
            or ThemeSurfaceKind.DoorFrame
            or ThemeSurfaceKind.WindowFrame
            or ThemeSurfaceKind.WindowVista;
    }

    private float GetBackgroundAlpha(ThemeSurfaceKind surfaceKind)
    {
        return surfaceKind switch
        {
            ThemeSurfaceKind.Wall => backgroundWallAlpha,
            ThemeSurfaceKind.Floor => backgroundFloorAlpha,
            ThemeSurfaceKind.Ceiling => backgroundCeilingAlpha,
            ThemeSurfaceKind.DoorFrame => backgroundWallAlpha,
            ThemeSurfaceKind.WindowFrame => backgroundWallAlpha,
            ThemeSurfaceKind.WindowVista => backgroundWindowVistaAlpha,
            _ => 0f,
        };
    }

    private static bool TryGetSurfaceKind(MRUKAnchor anchor, out ThemeSurfaceKind surfaceKind)
    {
        if (anchor != null)
        {
            if (anchor.HasAnyLabel(MRUKAnchor.SceneLabels.DOOR_FRAME))
            {
                surfaceKind = ThemeSurfaceKind.DoorFrame;
                return true;
            }

            if (anchor.HasAnyLabel(MRUKAnchor.SceneLabels.WINDOW_FRAME))
            {
                surfaceKind = ThemeSurfaceKind.WindowFrame;
                return true;
            }

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

    private bool HasGeneratedTextureSignatureChanged()
    {
        if (themeIntentController == null || themeIntentController.ActiveTheme == null)
        {
            return false;
        }

        var signature = BuildGeneratedTextureSignature(themeIntentController.ActiveTheme);
        if (string.Equals(signature, _lastGeneratedTextureSignature, StringComparison.Ordinal))
        {
            return false;
        }

        _lastGeneratedTextureSignature = signature;
        return true;
    }

    private string BuildGeneratedTextureSignature(ThemeProfile theme)
    {
        if (theme == null)
        {
            return string.Empty;
        }

        var jobDirectory = GetGeneratedSurfaceJobDirectory();
        if (string.IsNullOrWhiteSpace(jobDirectory) || !System.IO.Directory.Exists(jobDirectory))
        {
            return "no-job-folder";
        }

        var builder = new StringBuilder(256);
        builder.Append(theme.ThemeId);
        var styleVariantId = GetActiveStyleVariantId();
        builder.Append('|').Append(styleVariantId);
        AppendGeneratedTextureSignature(builder, jobDirectory, theme, styleVariantId, ThemeSurfaceKind.Wall);
        AppendGeneratedTextureSignature(builder, jobDirectory, theme, styleVariantId, ThemeSurfaceKind.Floor);
        AppendGeneratedTextureSignature(builder, jobDirectory, theme, styleVariantId, ThemeSurfaceKind.Ceiling);
        AppendGeneratedTextureSignature(builder, jobDirectory, theme, styleVariantId, ThemeSurfaceKind.DoorFrame);
        AppendGeneratedTextureSignature(builder, jobDirectory, theme, styleVariantId, ThemeSurfaceKind.WindowFrame);
        AppendGeneratedTextureSignature(builder, jobDirectory, theme, styleVariantId, ThemeSurfaceKind.WindowVista);
        return builder.ToString();
    }

    private static void AppendGeneratedTextureSignature(
        StringBuilder builder,
        string jobDirectory,
        ThemeProfile theme,
        string styleVariantId,
        ThemeSurfaceKind surfaceKind)
    {
        var requestId = BuildSurfaceRequestId(theme.ThemeId, styleVariantId, surfaceKind);
        var jobPath = System.IO.Path.Combine(jobDirectory, $"{requestId}.surface.job.json");
        builder.Append('|').Append(requestId).Append(':');
        if (!System.IO.File.Exists(jobPath))
        {
            builder.Append("missing");
            return;
        }

        var record = JsonUtility.FromJson<SurfaceTextureJobRecord>(System.IO.File.ReadAllText(jobPath));
        if (record == null)
        {
            builder.Append("invalid");
            return;
        }

        builder.Append(record.State);
        if (!string.IsNullOrWhiteSpace(record.OutputImagePath) && System.IO.File.Exists(record.OutputImagePath))
        {
            builder.Append(':').Append(System.IO.File.GetLastWriteTimeUtc(record.OutputImagePath).Ticks);
        }
    }

    private bool TryFindGeneratedSurfaceTexture(ThemeProfile theme, ThemeSurfaceKind surfaceKind, out string imagePath)
    {
        imagePath = string.Empty;
        var jobDirectory = GetGeneratedSurfaceJobDirectory();
        if (string.IsNullOrWhiteSpace(jobDirectory) || !System.IO.Directory.Exists(jobDirectory))
        {
            return false;
        }

        var requestId = BuildSurfaceRequestId(theme.ThemeId, GetActiveStyleVariantId(), surfaceKind);
        var preferredPath = System.IO.Path.Combine(jobDirectory, $"{requestId}.surface.job.json");
        if (TryLoadGeneratedSurfaceJob(preferredPath, requestId, out imagePath))
        {
            return true;
        }

        var jobs = System.IO.Directory.GetFiles(jobDirectory, "*.surface.job.json", System.IO.SearchOption.TopDirectoryOnly);
        for (var index = 0; index < jobs.Length; index++)
        {
            if (TryLoadGeneratedSurfaceJob(jobs[index], requestId, out imagePath))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryLoadGeneratedSurfaceJob(string jobPath, string requestId, out string imagePath)
    {
        imagePath = string.Empty;
        if (string.IsNullOrWhiteSpace(jobPath) || !System.IO.File.Exists(jobPath))
        {
            return false;
        }

        var record = JsonUtility.FromJson<SurfaceTextureJobRecord>(System.IO.File.ReadAllText(jobPath));
        if (record == null ||
            !string.Equals(record.RequestId, requestId, StringComparison.OrdinalIgnoreCase) ||
            record.State is not (SurfaceTextureJobState.TextureReady or SurfaceTextureJobState.MaterialReady) ||
            string.IsNullOrWhiteSpace(record.OutputImagePath) ||
            !System.IO.File.Exists(record.OutputImagePath) ||
            new System.IO.FileInfo(record.OutputImagePath).Length == 0)
        {
            return false;
        }

        imagePath = record.OutputImagePath;
        return true;
    }

    private string GetGeneratedSurfaceJobDirectory()
    {
#if UNITY_EDITOR
        var projectRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, ".."));
        return System.IO.Path.Combine(projectRoot, "Library", string.IsNullOrWhiteSpace(generatedSurfaceJobFolderName) ? "SurfaceTextureJobs" : generatedSurfaceJobFolderName);
#else
        return System.IO.Path.Combine(Application.persistentDataPath, string.IsNullOrWhiteSpace(generatedSurfaceJobFolderName) ? "SurfaceTextureJobs" : generatedSurfaceJobFolderName);
#endif
    }

    private string GetActiveStyleVariantId()
    {
        return SurfaceTexturePromptBuilder.BuildStyleVariantId(
            runtimeStyleIntentController != null ? runtimeStyleIntentController.CurrentIntent : null);
    }

    private static string BuildSurfaceRequestId(string themeId, string styleVariantId, ThemeSurfaceKind surfaceKind)
    {
        return SurfaceTexturePromptBuilder.BuildRequestId(themeId, ToSemanticLabel(surfaceKind), styleVariantId);
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

    private static string SanitizeFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "surface";
        }

        foreach (var invalidChar in System.IO.Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalidChar, '_');
        }

        return value;
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

        ClearOrphanedOverrideChildren();

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
        int doorFrameCount,
        int windowFrameCount,
        int windowVistaCount,
        int skippedCount)
    {
        var builder = new StringBuilder(384);
        var totalCount = wallCount + floorCount + ceilingCount + doorFrameCount + windowFrameCount + windowVistaCount;
        builder.AppendLine("[SurfaceOverrideApplier]");
        builder.AppendLine(visibilityMode == SurfaceVisibilityMode.Off
            ? "State: off"
            : totalCount > 0 ? "State: applied" : "State: waiting-for-targets");
        builder.AppendLine($"Theme: {theme.DisplayName}");
        builder.AppendLine($"Reason: {reason}");
        builder.AppendLine($"Visibility Mode: {visibilityMode}");
        builder.AppendLine($"Override Planes: {totalCount}");
        builder.AppendLine($"Coverage: floor={floorCount}, wall={wallCount}, ceiling={ceilingCount}, door={doorFrameCount}, window={windowFrameCount}, vista={windowVistaCount}");
        builder.AppendLine($"Skipped: {skippedCount}");
        builder.AppendLine($"Wall Offset: {wallOutwardOffsetMeters:F3}m");
        builder.AppendLine($"Frame Offset: {frameOutwardOffsetMeters:F3}m, Border: {frameBorderWidthMeters:F3}m");
        builder.AppendLine($"Window Vista: enabled={applyWindowVistaOverlays}, outsideOffset={windowVistaOutwardOffsetMeters:F3}m, aspect={windowVistaAspectRatio:F2}, scale={windowVistaScaleMultiplier:F2}x, largestOnly={applyVistaToLargestWindowOnly}");
        builder.AppendLine($"Window Filter: major>={minimumWindowFrameMajorSizeMeters:F2}m, minor>={minimumWindowFrameMinorSizeMeters:F2}m");
        if (visibilityMode == SurfaceVisibilityMode.Background)
        {
            builder.AppendLine($"Background Alpha: wall/floor/ceiling=1.00 forced, vista={backgroundWindowVistaAlpha:F2}");
        }
        else if (visibilityMode == SurfaceVisibilityMode.DemoStrong)
        {
            builder.AppendLine($"Demo Opacity Boost: {demoOpacityBoost:F2}x");
            builder.AppendLine($"Demo Min Alpha: wall/floor/ceiling=1.00 forced, vista={demoMinimumWindowVistaAlpha:F2}");
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

    private static Shader GetSurfaceShader(ThemeSurfaceKind surfaceKind)
    {
        if (surfaceKind == ThemeSurfaceKind.WindowVista)
        {
            return Shader.Find("Universal Render Pipeline/Unlit") ??
                   Shader.Find("Unlit/Texture") ??
                   Shader.Find("Unlit/Color") ??
                   Shader.Find("Standard");
        }

        return Shader.Find("Universal Render Pipeline/Lit") ??
               Shader.Find("Universal Render Pipeline/Unlit") ??
               Shader.Find("Standard");
    }

    private static void ConfigureTransparentDoubleSidedMaterial(Material material, ThemeSurfaceKind surfaceKind = ThemeSurfaceKind.Wall)
    {
        var forceOpaqueBlend = surfaceKind is ThemeSurfaceKind.DoorFrame
            or ThemeSurfaceKind.WindowFrame
            or ThemeSurfaceKind.WindowVista;

        if (material.HasProperty("_Surface"))
        {
            material.SetFloat("_Surface", forceOpaqueBlend ? 0f : 1f);
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
            material.SetFloat("_SrcBlend", forceOpaqueBlend
                ? (float)UnityEngine.Rendering.BlendMode.One
                : (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        }

        if (material.HasProperty("_DstBlend"))
        {
            material.SetFloat("_DstBlend", forceOpaqueBlend
                ? (float)UnityEngine.Rendering.BlendMode.Zero
                : (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        }

        if (material.HasProperty("_ZWrite"))
        {
            material.SetFloat("_ZWrite", 0f);
        }

        if (material.HasProperty("_Cull"))
        {
            material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
        }

        material.renderQueue = GetRenderQueue(surfaceKind);
        material.SetOverrideTag("RenderType", forceOpaqueBlend ? "Opaque" : "Transparent");
    }

    private static int GetRenderQueue(ThemeSurfaceKind surfaceKind)
    {
        var transparent = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        return surfaceKind switch
        {
            ThemeSurfaceKind.WindowVista => transparent + 80,
            ThemeSurfaceKind.DoorFrame or ThemeSurfaceKind.WindowFrame => transparent + 100,
            _ => transparent,
        };
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

    private void ClearOrphanedOverrideChildren()
    {
        if (surfaceOverridesRoot == null)
        {
            return;
        }

        var tracked = new HashSet<GameObject>(_spawnedOverrides);
        var orphanedChildren = new List<GameObject>();
        for (var index = 0; index < surfaceOverridesRoot.childCount; index++)
        {
            var child = surfaceOverridesRoot.GetChild(index);
            if (child == null ||
                tracked.Contains(child.gameObject) ||
                !child.name.StartsWith("SurfaceOverride_", StringComparison.Ordinal))
            {
                continue;
            }

            orphanedChildren.Add(child.gameObject);
        }

        foreach (var child in orphanedChildren)
        {
            SafeDestroy(child);
        }
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
