using System;
using System.Collections;
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
    [SerializeField, Min(0f), Tooltip("Forces door/window frames to sit slightly on the room-facing side of opaque wall overlays. Window vistas still use the opposite/outside direction.")]
    private float frameInteriorOffsetBeyondWallMeters = 0.025f;
    [SerializeField, Min(0.01f)] private float frameBorderWidthMeters = 0.08f;
    [SerializeField] private bool applyArchitecturalTrimOverlays = true;
    [SerializeField, Min(0f), Tooltip("Extends wall planes past MRUK PlaneRect edges so offset walls overlap at convex corners instead of exposing black gaps.")]
    private float wallPlaneHorizontalOverlapMeters = 0.14f;
    [SerializeField, Min(0f), Tooltip("Slightly extends wall planes into floor/ceiling boundaries to hide small MRUK plane gaps.")]
    private float wallPlaneVerticalOverlapMeters = 0.025f;
    [SerializeField, Min(0f), Tooltip("Extends floor and ceiling override planes a little past their MRUK rect to reduce boundary cracks.")]
    private float floorCeilingPlaneOverlapMeters = 0.05f;
    [SerializeField, Tooltip("Cut valid window openings out of opaque wall override meshes so generated vistas are not hidden behind the wall material. Doors stay as interior overlays on a full wall surface.")]
    private bool cutOpeningsFromWallOverrides = true;
    [SerializeField, Min(0f)] private float wallOpeningCutoutPaddingMeters = 0.06f;
    [SerializeField, Min(0f), Tooltip("Keeps a small strip of wall below window cutouts so floor/window junctions do not expose passthrough through MRUK plane gaps.")]
    private float minimumWindowWallSillMeters = 0.08f;
    [SerializeField, Range(0.5f, 1f)] private float wallOpeningNormalDotThreshold = 0.92f;
    [SerializeField, Min(0f)] private float wallOpeningPlaneDistanceMeters = 0.18f;
    [SerializeField, Min(0.01f)] private float baseboardHeightMeters = 0.1f;
    [SerializeField, Min(0.01f)] private float crownTrimHeightMeters = 0.07f;
    [SerializeField, Min(0.01f)] private float cornerTrimWidthMeters = 0.14f;
    [SerializeField, Tooltip("Vertical corner strips can over-accent protruding MRUK wall corners. Keep this off for the current room-scale material pass; wall plane overlap already hides most gaps.")]
    private bool applyVerticalCornerTrimOverlays;
    [SerializeField, Min(0f), Tooltip("If vertical corner strips are enabled later, skip them on narrow wall returns and columns below this width.")]
    private float minimumWallWidthForVerticalCornerTrimMeters = 1.35f;
    [SerializeField, Min(0f)] private float trimAdditionalOffsetMeters = 0.012f;
    [SerializeField, Range(0f, 1f), Tooltip("Scales visible seam trim after the geometric overlap has already hidden cracks. Lower values keep seams from becoming bright outlines.")]
    private float architecturalTrimVisualScale = 0.38f;
    [SerializeField, Range(0f, 1f), Tooltip("How much current style accent color is mixed into dark seam trim. Avoids white/cyan scaffold borders in warm styles.")]
    private float architecturalTrimAccentBlend = 0.28f;
    [SerializeField, Range(0f, 1f)] private float architecturalTrimEmissionScale = 0.12f;
    [SerializeField, Min(0.1f), Tooltip("World-space width of one wall texture repeat. Larger values avoid dense wallpaper patterns.")]
    private float wallTextureTileSizeMeters = 2.8f;
    [SerializeField, Min(0.1f), Tooltip("World-space width of one floor texture repeat. Larger values make floor panels read at room scale.")]
    private float floorTextureTileSizeMeters = 2.2f;
    [SerializeField, Min(0.1f), Tooltip("World-space width of one ceiling texture repeat. Larger values avoid dense ceiling noise.")]
    private float ceilingTextureTileSizeMeters = 2.6f;
    [SerializeField, Min(0.1f), Tooltip("World-space width of one opening trim texture repeat.")]
    private float openingTextureTileSizeMeters = 1.1f;
    [SerializeField, Range(0f, 0.3f), Tooltip("0 keeps the door rectangular; higher values create a subtle arched/portal top.")]
    private float doorPanelArchDepthRatio = 0.08f;
    [SerializeField, Min(0f), Tooltip("Distance to push window vista back outside the room, behind the window frame.")]
    private float windowVistaOutwardOffsetMeters = 0.04f;
    [SerializeField, Min(0.1f), Tooltip("Expected generated exterior vista image aspect ratio. APIMart window vista jobs currently request 16:9.")]
    private float windowVistaAspectRatio = 16f / 9f;
    [SerializeField, Range(1f, 1.5f)] private float windowVistaScaleMultiplier = 1.12f;
    [SerializeField, Tooltip("Only stylize the largest valid WINDOW_FRAME. This filters small false-positive scan windows in the canonical office.")]
    private bool applyWindowFrameToLargestWindowOnly = true;
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

    [Header("Memory")]
    [SerializeField, Tooltip("On style/theme changes, release old generated surface textures before creating the next set to avoid Unity texture allocation spikes.")]
    private bool unloadUnusedAssetsBeforeStyleRefresh = true;

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
    private bool _isCleaningBeforeRefresh;
    private Coroutine _deferredRefreshCoroutine;

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
        CancelDeferredRefresh();
        ResetOverrides();
    }

    private void Update()
    {
        if (!Application.isPlaying || _isCleaningBeforeRefresh)
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
        QueueRefreshAfterMemoryCleanup("theme-changed");
    }

    private void HandleStyleIntentChanged()
    {
        QueueRefreshAfterMemoryCleanup("style-intent-changed");
    }

    private void QueueRefreshAfterMemoryCleanup(string reason)
    {
        _needsRefresh = true;

        if (!Application.isPlaying || !unloadUnusedAssetsBeforeStyleRefresh)
        {
            RefreshOverrides(reason);
            return;
        }

        if (_deferredRefreshCoroutine != null)
        {
            StopCoroutine(_deferredRefreshCoroutine);
        }

        _deferredRefreshCoroutine = StartCoroutine(RefreshAfterUnusedAssetUnload(reason));
    }

    private IEnumerator RefreshAfterUnusedAssetUnload(string reason)
    {
        _isCleaningBeforeRefresh = true;
        _needsRefresh = false;
        ResetOverrides();
        _latestSummary = $"[SurfaceOverrideApplier]\nState: {reason}-releasing-old-assets\nHint: releasing old generated surface textures before applying the next style.";
        SummaryChanged?.Invoke();

        yield return null;
        yield return Resources.UnloadUnusedAssets();
        GC.Collect();

        _isCleaningBeforeRefresh = false;
        _deferredRefreshCoroutine = null;
        _needsRefresh = true;
        _nextRefreshTime = 0f;
        RefreshOverrides($"{reason}-after-release");
    }

    private void CancelDeferredRefresh()
    {
        if (_deferredRefreshCoroutine != null)
        {
            StopCoroutine(_deferredRefreshCoroutine);
            _deferredRefreshCoroutine = null;
        }

        _isCleaningBeforeRefresh = false;
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
        var trimCount = 0;
        var skippedCount = 0;
        var materials = new Dictionary<ThemeSurfaceKind, Material>();
        Material trimMaterial = null;
        var primaryWindowAnchor = applyWindowFrameToLargestWindowOnly || applyVistaToLargestWindowOnly
            ? FindPrimaryWindowAnchor(room)
            : null;
        var wallOpeningCutouts = BuildWallOpeningCutouts(room, primaryWindowAnchor);

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

            if (surfaceKind == ThemeSurfaceKind.WindowFrame &&
                applyWindowFrameToLargestWindowOnly &&
                !ReferenceEquals(anchor, primaryWindowAnchor))
            {
                skippedCount++;
                continue;
            }

            var shouldApplySurface = ShouldApply(surfaceKind);
            var shouldApplyVista = surfaceKind == ThemeSurfaceKind.WindowFrame &&
                                   applyWindowVistaOverlays &&
                                   (!applyVistaToLargestWindowOnly || ReferenceEquals(anchor, primaryWindowAnchor));
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

                wallOpeningCutouts.TryGetValue(anchor, out var openingCutouts);
                if (!TryCreateOverridePlane(anchor, surfaceKind, material, index, openingCutouts))
                {
                    skippedCount++;
                    continue;
                }

                if (suppressOriginalSurfaceRenderers)
                {
                    SuppressAnchorRenderers(anchor);
                }

                if (surfaceKind == ThemeSurfaceKind.Wall && applyArchitecturalTrimOverlays)
                {
                    trimMaterial ??= CreateArchitecturalTrimMaterial(theme);
                    trimCount += CreateWallTrimOverlays(anchor, trimMaterial, index);
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
        _latestSummary = BuildSummary(reason, theme, wallCount, floorCount, ceilingCount, doorFrameCount, windowFrameCount, windowVistaCount, trimCount, skippedCount);
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
        int anchorIndex,
        IReadOnlyList<Rect> wallOpeningCutouts = null)
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
        var mesh = surfaceKind == ThemeSurfaceKind.Wall &&
                   wallOpeningCutouts != null &&
                   wallOpeningCutouts.Count > 0
            ? CreatePlaneMeshWithCutouts(rect, offset, wallOpeningCutouts)
            : CreateOverrideMesh(rect, offset, surfaceKind, frameBorderWidthMeters);
        mesh.name = $"{surface.name}_Mesh";
        meshFilter.sharedMesh = mesh;
        meshRenderer.sharedMaterial = material;

        _runtimeMeshes.Add(mesh);
        _spawnedOverrides.Add(surface);
        return true;
    }

    private Rect GetSurfaceRect(Rect rect, ThemeSurfaceKind surfaceKind)
    {
        return surfaceKind switch
        {
            ThemeSurfaceKind.Wall => ExpandRect(rect, wallPlaneHorizontalOverlapMeters, wallPlaneVerticalOverlapMeters),
            ThemeSurfaceKind.Floor or ThemeSurfaceKind.Ceiling => ExpandRect(rect, floorCeilingPlaneOverlapMeters, floorCeilingPlaneOverlapMeters),
            // Window vistas stay clipped to the MRUK window rect; cover-fit happens in UV space.
            _ => rect,
        };
    }

    private static Rect ExpandRect(Rect rect, float horizontalOverlap, float verticalOverlap)
    {
        var xMin = rect.xMin - Mathf.Max(0f, horizontalOverlap);
        var xMax = rect.xMax + Mathf.Max(0f, horizontalOverlap);
        var yMin = rect.yMin - Mathf.Max(0f, verticalOverlap);
        var yMax = rect.yMax + Mathf.Max(0f, verticalOverlap);
        return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
    }

    private Dictionary<MRUKAnchor, List<Rect>> BuildWallOpeningCutouts(MRUKRoom room, MRUKAnchor primaryWindowAnchor)
    {
        var result = new Dictionary<MRUKAnchor, List<Rect>>();
        if (!cutOpeningsFromWallOverrides || room == null)
        {
            return result;
        }

        var walls = new List<MRUKAnchor>();
        var openings = new List<MRUKAnchor>();
        foreach (var anchor in room.Anchors)
        {
            if (anchor == null || !anchor.PlaneRect.HasValue)
            {
                continue;
            }

            if (anchor.HasAnyLabel(MRUKAnchor.SceneLabels.WALL_FACE))
            {
                walls.Add(anchor);
                continue;
            }

            if (ShouldUseOpeningAsWallCutout(anchor, primaryWindowAnchor))
            {
                openings.Add(anchor);
            }
        }

        foreach (var wall in walls)
        {
            foreach (var opening in openings)
            {
                if (!TryProjectOpeningToWall(wall, opening, out var cutout))
                {
                    continue;
                }

                if (!result.TryGetValue(wall, out var cutouts))
                {
                    cutouts = new List<Rect>();
                    result[wall] = cutouts;
                }

                cutouts.Add(cutout);
            }
        }

        return result;
    }

    private bool ShouldUseOpeningAsWallCutout(MRUKAnchor anchor, MRUKAnchor primaryWindowAnchor)
    {
        if (anchor.HasAnyLabel(MRUKAnchor.SceneLabels.DOOR_FRAME))
        {
            // Doors are decorative interior overlays. Keeping the host wall intact
            // preserves identical wall material/tiling logic across all walls.
            return false;
        }

        if (!anchor.HasAnyLabel(MRUKAnchor.SceneLabels.WINDOW_FRAME) ||
            !anchor.PlaneRect.HasValue ||
            !IsWindowFrameEligible(anchor.PlaneRect.Value))
        {
            return false;
        }

        if (applyWindowFrameToLargestWindowOnly && !ReferenceEquals(anchor, primaryWindowAnchor))
        {
            return false;
        }

        return applyWindowFrameOverrides || applyWindowVistaOverlays;
    }

    private bool TryProjectOpeningToWall(MRUKAnchor wall, MRUKAnchor opening, out Rect cutout)
    {
        cutout = default;
        if (wall == null ||
            opening == null ||
            !wall.PlaneRect.HasValue ||
            !opening.PlaneRect.HasValue)
        {
            return false;
        }

        var wallNormal = wall.transform.forward.normalized;
        var openingNormal = opening.transform.forward.normalized;
        if (Mathf.Abs(Vector3.Dot(wallNormal, openingNormal)) < wallOpeningNormalDotThreshold)
        {
            return false;
        }

        var planeDistance = Mathf.Abs(Vector3.Dot(opening.transform.position - wall.transform.position, wallNormal));
        if (planeDistance > wallOpeningPlaneDistanceMeters)
        {
            return false;
        }

        var openingRect = opening.PlaneRect.Value;
        var corners = new[]
        {
            new Vector3(openingRect.xMin, openingRect.yMin, 0f),
            new Vector3(openingRect.xMax, openingRect.yMin, 0f),
            new Vector3(openingRect.xMax, openingRect.yMax, 0f),
            new Vector3(openingRect.xMin, openingRect.yMax, 0f),
        };

        var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        foreach (var corner in corners)
        {
            var wallLocal = wall.transform.InverseTransformPoint(opening.transform.TransformPoint(corner));
            min = Vector2.Min(min, new Vector2(wallLocal.x, wallLocal.y));
            max = Vector2.Max(max, new Vector2(wallLocal.x, wallLocal.y));
        }

        var padding = Mathf.Max(0f, wallOpeningCutoutPaddingMeters);
        var wallRect = wall.PlaneRect.Value;
        var cutoutYMin = Mathf.Max(
            min.y - padding,
            wallRect.yMin + Mathf.Max(0f, minimumWindowWallSillMeters));
        cutout = Rect.MinMaxRect(min.x - padding, cutoutYMin, max.x + padding, max.y + padding);
        var expandedWallRect = ExpandRect(
            wallRect,
            wallPlaneHorizontalOverlapMeters + padding,
            wallPlaneVerticalOverlapMeters + padding);
        return cutout.height > 0.005f && cutout.Overlaps(expandedWallRect);
    }

    private MRUKAnchor FindPrimaryWindowAnchor(MRUKRoom room)
    {
        if (room == null)
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
        return surfaceKind switch
        {
            ThemeSurfaceKind.DoorFrame => CreateDoorPanelMesh(rect, normalOffset, doorPanelArchDepthRatio),
            ThemeSurfaceKind.WindowFrame => CreateFrameMesh(rect, normalOffset, frameBorderWidth),
            _ => CreatePlaneMesh(rect, normalOffset, surfaceKind, windowVistaAspectRatio, windowVistaScaleMultiplier),
        };
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

    private static Mesh CreatePlaneMeshWithCutouts(Rect rect, float normalOffset, IReadOnlyList<Rect> cutouts)
    {
        if (cutouts == null || cutouts.Count == 0)
        {
            return CreatePlaneMesh(rect, normalOffset, ThemeSurfaceKind.Wall, 1f, 1f);
        }

        var xEdges = new List<float> { rect.xMin, rect.xMax };
        var yEdges = new List<float> { rect.yMin, rect.yMax };
        foreach (var cutout in cutouts)
        {
            var clipped = IntersectRect(rect, cutout);
            if (clipped.width <= 0.01f || clipped.height <= 0.01f)
            {
                continue;
            }

            xEdges.Add(clipped.xMin);
            xEdges.Add(clipped.xMax);
            yEdges.Add(clipped.yMin);
            yEdges.Add(clipped.yMax);
        }

        xEdges.Sort();
        yEdges.Sort();
        RemoveNearDuplicateEdges(xEdges);
        RemoveNearDuplicateEdges(yEdges);

        var vertices = new List<Vector3>();
        var uv = new List<Vector2>();
        var triangles = new List<int>();
        for (var xIndex = 0; xIndex < xEdges.Count - 1; xIndex++)
        {
            for (var yIndex = 0; yIndex < yEdges.Count - 1; yIndex++)
            {
                var cell = Rect.MinMaxRect(xEdges[xIndex], yEdges[yIndex], xEdges[xIndex + 1], yEdges[yIndex + 1]);
                if (cell.width <= 0.005f || cell.height <= 0.005f || IsCellInsideAnyCutout(cell, cutouts))
                {
                    continue;
                }

                AddSurfaceQuad(vertices, uv, triangles, rect, cell, normalOffset);
            }
        }

        if (vertices.Count == 0)
        {
            return CreatePlaneMesh(rect, normalOffset, ThemeSurfaceKind.Wall, 1f, 1f);
        }

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

    private static Rect IntersectRect(Rect a, Rect b)
    {
        var xMin = Mathf.Max(a.xMin, b.xMin);
        var yMin = Mathf.Max(a.yMin, b.yMin);
        var xMax = Mathf.Min(a.xMax, b.xMax);
        var yMax = Mathf.Min(a.yMax, b.yMax);
        return xMax <= xMin || yMax <= yMin
            ? Rect.zero
            : Rect.MinMaxRect(xMin, yMin, xMax, yMax);
    }

    private static void RemoveNearDuplicateEdges(List<float> edges)
    {
        for (var index = edges.Count - 1; index > 0; index--)
        {
            if (Mathf.Abs(edges[index] - edges[index - 1]) < 0.002f)
            {
                edges.RemoveAt(index);
            }
        }
    }

    private static bool IsCellInsideAnyCutout(Rect cell, IReadOnlyList<Rect> cutouts)
    {
        var center = cell.center;
        foreach (var cutout in cutouts)
        {
            if (cutout.Contains(center))
            {
                return true;
            }
        }

        return false;
    }

    private static void AddSurfaceQuad(
        ICollection<Vector3> vertices,
        ICollection<Vector2> uv,
        ICollection<int> triangles,
        Rect fullRect,
        Rect quadRect,
        float normalOffset)
    {
        var start = vertices.Count;
        vertices.Add(new Vector3(quadRect.xMin, quadRect.yMin, normalOffset));
        vertices.Add(new Vector3(quadRect.xMax, quadRect.yMin, normalOffset));
        vertices.Add(new Vector3(quadRect.xMax, quadRect.yMax, normalOffset));
        vertices.Add(new Vector3(quadRect.xMin, quadRect.yMax, normalOffset));

        uv.Add(new Vector2(
            Mathf.InverseLerp(fullRect.xMin, fullRect.xMax, quadRect.xMin),
            Mathf.InverseLerp(fullRect.yMin, fullRect.yMax, quadRect.yMin)));
        uv.Add(new Vector2(
            Mathf.InverseLerp(fullRect.xMin, fullRect.xMax, quadRect.xMax),
            Mathf.InverseLerp(fullRect.yMin, fullRect.yMax, quadRect.yMin)));
        uv.Add(new Vector2(
            Mathf.InverseLerp(fullRect.xMin, fullRect.xMax, quadRect.xMax),
            Mathf.InverseLerp(fullRect.yMin, fullRect.yMax, quadRect.yMax)));
        uv.Add(new Vector2(
            Mathf.InverseLerp(fullRect.xMin, fullRect.xMax, quadRect.xMin),
            Mathf.InverseLerp(fullRect.yMin, fullRect.yMax, quadRect.yMax)));

        triangles.Add(start);
        triangles.Add(start + 1);
        triangles.Add(start + 2);
        triangles.Add(start);
        triangles.Add(start + 2);
        triangles.Add(start + 3);
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

    private static Mesh CreateDoorPanelMesh(Rect rect, float normalOffset, float archDepthRatio)
    {
        var width = Mathf.Max(0.05f, rect.width);
        var height = Mathf.Max(0.05f, rect.height);
        var archDepth = Mathf.Clamp01(archDepthRatio) * height;
        if (archDepth <= 0.001f)
        {
            return CreateNormalizedPanelMesh(rect, normalOffset);
        }

        const int archSegments = 8;
        var points = new List<Vector2>(archSegments + 4)
        {
            new(rect.xMin, rect.yMin),
            new(rect.xMax, rect.yMin),
            new(rect.xMax, rect.yMax - archDepth),
        };

        var halfWidth = width * 0.5f;
        var centerX = rect.center.x;
        for (var segment = 1; segment < archSegments; segment++)
        {
            var t = segment / (float)archSegments;
            var x = Mathf.Lerp(rect.xMax, rect.xMin, t);
            var normalized = Mathf.Abs((x - centerX) / halfWidth);
            var y = rect.yMax - archDepth * normalized * normalized;
            points.Add(new Vector2(x, y));
        }

        points.Add(new Vector2(rect.xMin, rect.yMax - archDepth));

        var center = rect.center;
        var vertices = new List<Vector3>(points.Count + 1)
        {
            new(center.x, center.y, normalOffset),
        };
        var uv = new List<Vector2>(points.Count + 1)
        {
            new(0.5f, 0.5f),
        };
        var triangles = new List<int>(points.Count * 3);

        for (var index = 0; index < points.Count; index++)
        {
            var point = points[index];
            vertices.Add(new Vector3(point.x, point.y, normalOffset));
            uv.Add(new Vector2(
                Mathf.InverseLerp(rect.xMin, rect.xMax, point.x),
                Mathf.InverseLerp(rect.yMin, rect.yMax, point.y)));
        }

        for (var index = 1; index <= points.Count; index++)
        {
            var next = index == points.Count ? 1 : index + 1;
            triangles.Add(0);
            triangles.Add(index);
            triangles.Add(next);
        }

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

    private static Mesh CreateNormalizedPanelMesh(Rect rect, float normalOffset)
    {
        var vertices = new[]
        {
            new Vector3(rect.xMin, rect.yMin, normalOffset),
            new Vector3(rect.xMax, rect.yMin, normalOffset),
            new Vector3(rect.xMax, rect.yMax, normalOffset),
            new Vector3(rect.xMin, rect.yMax, normalOffset),
        };
        var mesh = new Mesh
        {
            vertices = vertices,
            uv = new[]
            {
                Vector2.zero,
                Vector2.right,
                Vector2.one,
                Vector2.up,
            },
            triangles = new[] { 0, 1, 2, 0, 2, 3 },
        };

        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private int CreateWallTrimOverlays(MRUKAnchor anchor, Material material, int anchorIndex)
    {
        if (anchor == null || material == null || !anchor.PlaneRect.HasValue)
        {
            return 0;
        }

        var rect = anchor.PlaneRect.Value;
        var width = Mathf.Abs(rect.width);
        var height = Mathf.Abs(rect.height);
        if (width <= 0.05f || height <= 0.05f)
        {
            return 0;
        }

        var trimScale = Mathf.Clamp01(architecturalTrimVisualScale);
        if (trimScale <= 0.005f)
        {
            return 0;
        }

        var count = 0;
        var offset = wallOutwardOffsetMeters + trimAdditionalOffsetMeters;
        var baseboardHeight = Mathf.Min(baseboardHeightMeters, height * 0.18f) * trimScale;
        var crownHeight = Mathf.Min(crownTrimHeightMeters, height * 0.14f) * trimScale;
        var cornerWidth = Mathf.Min(cornerTrimWidthMeters, width * 0.12f) * trimScale;
        count += TryCreateTrimStrip(anchor, material, anchorIndex, "Baseboard",
            new Rect(rect.xMin, rect.yMin, rect.width, baseboardHeight), offset);
        count += TryCreateTrimStrip(anchor, material, anchorIndex, "Crown",
            new Rect(rect.xMin, rect.yMax - crownHeight, rect.width, crownHeight), offset);

        if (applyVerticalCornerTrimOverlays && width >= minimumWallWidthForVerticalCornerTrimMeters)
        {
            count += TryCreateTrimStrip(anchor, material, anchorIndex, "LeftCorner",
                new Rect(rect.xMin, rect.yMin, cornerWidth, rect.height), offset);
            count += TryCreateTrimStrip(anchor, material, anchorIndex, "RightCorner",
                new Rect(rect.xMax - cornerWidth, rect.yMin, cornerWidth, rect.height), offset);
        }

        return count;
    }

    private int TryCreateTrimStrip(MRUKAnchor anchor, Material material, int anchorIndex, string suffix, Rect rect, float normalOffset)
    {
        if (rect.width <= 0.005f || rect.height <= 0.005f)
        {
            return 0;
        }

        var trim = new GameObject($"SurfaceOverride_WallTrim_{suffix}_{anchorIndex:D2}");
        trim.transform.SetParent(surfaceOverridesRoot, false);
        trim.transform.position = anchor.transform.position;
        trim.transform.rotation = anchor.transform.rotation;
        trim.transform.localScale = Vector3.one;

        var meshFilter = trim.AddComponent<MeshFilter>();
        var meshRenderer = trim.AddComponent<MeshRenderer>();
        var mesh = CreatePlaneMesh(rect, normalOffset, ThemeSurfaceKind.Wall, 1f, 1f);
        mesh.name = $"{trim.name}_Mesh";
        meshFilter.sharedMesh = mesh;
        meshRenderer.sharedMaterial = material;

        _runtimeMeshes.Add(mesh);
        _spawnedOverrides.Add(trim);
        return 1;
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
            SetMaterialTextureScale(material, GetTextureTiling(theme, surfaceKind));
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

        var effectiveThemeId = GetEffectiveThemeId(theme);
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
        {
            name = $"Generated_{effectiveThemeId}_{surfaceKind}_Texture",
            wrapMode = surfaceKind is ThemeSurfaceKind.DoorFrame or ThemeSurfaceKind.WindowVista
                ? TextureWrapMode.Clamp
                : TextureWrapMode.Repeat,
            filterMode = FilterMode.Bilinear,
            anisoLevel = 1,
        };

        if (!ImageConversion.LoadImage(texture, bytes, true))
        {
            SafeDestroy(texture);
            return false;
        }

        _runtimeTextures.Add(texture);

        var shader = GetGeneratedSurfaceShader(surfaceKind);
        material = new Material(shader)
        {
            name = $"Runtime_{effectiveThemeId}_{surfaceKind}_GeneratedSurface",
        };
        ConfigureTransparentDoubleSidedMaterial(material, surfaceKind);
        SetMaterialTexture(material, texture, GetTextureTiling(theme, surfaceKind));

        var surfaceColor = GetGeneratedSurfaceColor(theme, surfaceKind);
        SetMaterialColor(material, surfaceColor);
        SetEmission(material, GetGeneratedSurfaceEmission(theme, surfaceKind));
        ConfigureTransparentDoubleSidedMaterial(material, surfaceKind);
        return true;
    }

    private float GetTextureTiling(ThemeProfile theme, ThemeSurfaceKind surfaceKind)
    {
        var tileSize = surfaceKind switch
        {
            ThemeSurfaceKind.Wall => wallTextureTileSizeMeters,
            ThemeSurfaceKind.Floor => floorTextureTileSizeMeters,
            ThemeSurfaceKind.Ceiling => ceilingTextureTileSizeMeters,
            ThemeSurfaceKind.DoorFrame => 1f,
            ThemeSurfaceKind.WindowFrame => openingTextureTileSizeMeters,
            ThemeSurfaceKind.WindowVista => 1f,
            _ => Mathf.Max(0.1f, theme.SurfaceMaterials.TextureTiling),
        };

        return surfaceKind is ThemeSurfaceKind.DoorFrame or ThemeSurfaceKind.WindowVista
            ? 1f
            : 1f / Mathf.Max(0.1f, tileSize);
    }

    private static Shader GetGeneratedSurfaceShader(ThemeSurfaceKind surfaceKind)
    {
        // Generated surface textures are already baked artwork. Unlit preserves their
        // color across style switches instead of letting room lighting wash out dark themes.
        return Shader.Find("Universal Render Pipeline/Unlit") ??
               Shader.Find("Unlit/Texture") ??
               GetSurfaceShader(surfaceKind);
    }

    private Material CreateArchitecturalTrimMaterial(ThemeProfile theme)
    {
        var shader = GetGeneratedSurfaceShader(ThemeSurfaceKind.WindowFrame);
        var material = new Material(shader)
        {
            name = $"Runtime_{GetEffectiveThemeId(theme)}_ArchitecturalTrim",
        };

        ConfigureTransparentDoubleSidedMaterial(material, ThemeSurfaceKind.WindowFrame);
        var color = Color.Lerp(
            RuntimeStyleColorUtility.ResolveTrimBaseColor(theme, GetCurrentStyleIntent()),
            GetStyleAccentColor(theme),
            architecturalTrimAccentBlend);
        color.a = 1f;
        SetMaterialColor(material, color);
        SetEmission(material, GetStyleAccentColor(theme) * Mathf.Max(0f, theme.SurfaceMaterials.EmissionIntensity * architecturalTrimEmissionScale));
        _runtimeMaterials.Add(material);
        return material;
    }

    private Color GetGeneratedSurfaceColor(ThemeProfile theme, ThemeSurfaceKind surfaceKind)
    {
        if (surfaceKind is ThemeSurfaceKind.DoorFrame or ThemeSurfaceKind.WindowFrame)
        {
            var color = Color.Lerp(
                RuntimeStyleColorUtility.ResolveTrimBaseColor(theme, GetCurrentStyleIntent()),
                GetStyleAccentColor(theme),
                0.38f);
            color.a = 1f;
            return color;
        }

        return Color.white;
    }

    private Color GetGeneratedSurfaceEmission(ThemeProfile theme, ThemeSurfaceKind surfaceKind)
    {
        if (surfaceKind is ThemeSurfaceKind.DoorFrame or ThemeSurfaceKind.WindowFrame)
        {
            return GetStyleAccentColor(theme) * Mathf.Max(0f, GetEmissionIntensity(theme, surfaceKind) * 0.18f);
        }

        if (surfaceKind == ThemeSurfaceKind.WindowVista)
        {
            return Color.white * Mathf.Max(0f, windowVistaEmissionIntensity * 0.25f);
        }

        return Color.black;
    }

    private Color GetStyleAccentColor(ThemeProfile theme)
    {
        return RuntimeStyleColorUtility.ResolveAccentColor(theme, GetCurrentStyleIntent());
    }

    private RuntimeStyleIntent GetCurrentStyleIntent()
    {
        return runtimeStyleIntentController != null ? runtimeStyleIntentController.CurrentIntent : null;
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
            ThemeSurfaceKind.DoorFrame => GetInteriorFrameOffset(),
            ThemeSurfaceKind.WindowFrame => GetInteriorFrameOffset(),
            ThemeSurfaceKind.WindowVista => -windowVistaOutwardOffsetMeters,
            _ => 0f,
        };
    }

    private float GetInteriorFrameOffset()
    {
        return Mathf.Max(frameOutwardOffsetMeters, wallOutwardOffsetMeters + frameInteriorOffsetBeyondWallMeters);
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
        var effectiveThemeId = GetEffectiveThemeId(theme);
        builder.Append(effectiveThemeId);
        var styleVariantId = GetActiveStyleVariantId();
        builder.Append('|').Append(styleVariantId);
        AppendGeneratedTextureSignature(builder, jobDirectory, effectiveThemeId, styleVariantId, ThemeSurfaceKind.Wall);
        AppendGeneratedTextureSignature(builder, jobDirectory, effectiveThemeId, styleVariantId, ThemeSurfaceKind.Floor);
        AppendGeneratedTextureSignature(builder, jobDirectory, effectiveThemeId, styleVariantId, ThemeSurfaceKind.Ceiling);
        AppendGeneratedTextureSignature(builder, jobDirectory, effectiveThemeId, styleVariantId, ThemeSurfaceKind.DoorFrame);
        AppendGeneratedTextureSignature(builder, jobDirectory, effectiveThemeId, styleVariantId, ThemeSurfaceKind.WindowFrame);
        AppendGeneratedTextureSignature(builder, jobDirectory, effectiveThemeId, styleVariantId, ThemeSurfaceKind.WindowVista);
        return builder.ToString();
    }

    private static void AppendGeneratedTextureSignature(
        StringBuilder builder,
        string jobDirectory,
        string themeId,
        string styleVariantId,
        ThemeSurfaceKind surfaceKind)
    {
        var requestId = BuildSurfaceRequestId(themeId, styleVariantId, surfaceKind);
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

        var requestId = BuildSurfaceRequestId(GetEffectiveThemeId(theme), GetActiveStyleVariantId(), surfaceKind);
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

    private string GetEffectiveThemeId(ThemeProfile theme)
    {
        return RuntimeStyleIntentRequestUtility.BuildEffectiveThemeId(
            theme,
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
        int trimCount,
        int skippedCount)
    {
        var builder = new StringBuilder(384);
        var totalCount = wallCount + floorCount + ceilingCount + doorFrameCount + windowFrameCount + windowVistaCount + trimCount;
        builder.AppendLine("[SurfaceOverrideApplier]");
        builder.AppendLine(visibilityMode == SurfaceVisibilityMode.Off
            ? "State: off"
            : totalCount > 0 ? "State: applied" : "State: waiting-for-targets");
        builder.AppendLine($"Theme: {theme.DisplayName}");
        builder.AppendLine($"Reason: {reason}");
        builder.AppendLine($"Visibility Mode: {visibilityMode}");
        builder.AppendLine($"Override Planes: {totalCount}");
        builder.AppendLine($"Coverage: floor={floorCount}, wall={wallCount}, ceiling={ceilingCount}, door={doorFrameCount}, window={windowFrameCount}, vista={windowVistaCount}, trim={trimCount}");
        builder.AppendLine($"Skipped: {skippedCount}");
        builder.AppendLine($"Wall Offset: {wallOutwardOffsetMeters:F3}m");
        builder.AppendLine($"Frame Offset: configured={frameOutwardOffsetMeters:F3}m, interiorEffective={GetInteriorFrameOffset():F3}m, Border={frameBorderWidthMeters:F3}m");
        builder.AppendLine($"Texture Tile Size: wall={wallTextureTileSizeMeters:F2}m, floor={floorTextureTileSizeMeters:F2}m, ceiling={ceilingTextureTileSizeMeters:F2}m, openings={openingTextureTileSizeMeters:F2}m");
        builder.AppendLine($"Seam Overlap: wallX={wallPlaneHorizontalOverlapMeters:F2}m, wallY={wallPlaneVerticalOverlapMeters:F2}m, floor/ceiling={floorCeilingPlaneOverlapMeters:F2}m");
        builder.AppendLine($"Window Opening Cutouts: enabled={cutOpeningsFromWallOverrides}, doors=false, padding={wallOpeningCutoutPaddingMeters:F2}m, sill={minimumWindowWallSillMeters:F2}m, planeDistance<={wallOpeningPlaneDistanceMeters:F2}m");
        builder.AppendLine($"Trim: enabled={applyArchitecturalTrimOverlays}, base={baseboardHeightMeters:F2}m, crown={crownTrimHeightMeters:F2}m, verticalCorners={applyVerticalCornerTrimOverlays}, corner={cornerTrimWidthMeters:F2}m");
        builder.AppendLine($"Window Frame: enabled={applyWindowFrameOverrides}, largestOnly={applyWindowFrameToLargestWindowOnly}");
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
        var forceOpaqueBlend = surfaceKind is ThemeSurfaceKind.Wall
            or ThemeSurfaceKind.Floor
            or ThemeSurfaceKind.Ceiling
            or ThemeSurfaceKind.DoorFrame
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
            material.SetFloat("_ZWrite", forceOpaqueBlend ? 1f : 0f);
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
            ThemeSurfaceKind.Wall or ThemeSurfaceKind.Floor or ThemeSurfaceKind.Ceiling =>
                (int)UnityEngine.Rendering.RenderQueue.GeometryLast,
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

    private static void SetMaterialTextureScale(Material material, float tiling)
    {
        var textureScale = Vector2.one * Mathf.Max(0.1f, tiling);
        if (material.HasProperty("_BaseMap") && material.GetTexture("_BaseMap") != null)
        {
            material.SetTextureScale("_BaseMap", textureScale);
        }

        if (material.HasProperty("_MainTex") && material.GetTexture("_MainTex") != null)
        {
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
