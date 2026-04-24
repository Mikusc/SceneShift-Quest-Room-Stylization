using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Meta.XR.MRUtilityKit;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

[DisallowMultipleComponent]
public class AnchorThemeApplier : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private RoomSemanticBootstrap roomSemanticBootstrap;
    [SerializeField] private ThemeIntentController themeIntentController;
    [SerializeField] private StylizationPlanner stylizationPlanner;
    [SerializeField] private Transform proxyObjectsRoot;

    [Header("Surface Targets")]
    [SerializeField] private bool includeInnerWallFaces;
    [SerializeField] private bool logApplications = true;
    [SerializeField, Min(0.1f)] private float autoRefreshInterval = 0.75f;

    [Header("Proxy Targets")]
    [SerializeField] private bool applyTableProxies = true;
    [SerializeField] private bool hideOriginalVolumeVisuals = true;
    [SerializeField, Min(0.1f)] private float proxyFootprintPadding = 1f;
    [SerializeField, Min(0.1f)] private float proxyHeightPadding = 1f;
    [SerializeField, Range(-180f, 180f)] private float tableProxyYawOffsetDegrees = 90f;
    [SerializeField] private bool augmentFlatTableProxies = true;
    [SerializeField, Range(0.1f, 0.6f)] private float flatTableHeightThreshold = 0.35f;
#if UNITY_EDITOR
    [SerializeField] private bool preferImportedGeneratedTablePrefabs = true;
    [SerializeField] private string generatedObjectJobFolderName = "GeneratedObjectJobs";
#endif

    public event Action SummaryChanged;

    public string LatestSummary => _latestSummary;
    public int LastAppliedAnchorCount => _lastAppliedAnchorCount;
    public int LastAppliedRendererCount => _lastAppliedRendererCount;
    public int LastAppliedTableProxyCount => _lastAppliedTableProxyCount;
    public string LastTableProxyStatus => _lastTableProxyStatus;

    private readonly Dictionary<Renderer, Material[]> _originalSharedMaterials = new();
    private readonly Dictionary<Renderer, bool> _originalRendererStates = new();
    private readonly Dictionary<GameObject, bool> _originalVisualObjectStates = new();
    private readonly List<Material> _runtimeMaterials = new();
    private readonly List<Texture2D> _runtimeTextures = new();
    private readonly List<GameObject> _spawnedProxyRoots = new();

    private string _latestSummary = "[AnchorThemeApplier]\nState: waiting\nHint: enter Play and wait for room + theme.";
    private string _lastTableProxyStatus = "idle";
    private int _lastAppliedAnchorCount;
    private int _lastAppliedRendererCount;
    private int _lastAppliedTableProxyCount;
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

        if (stylizationPlanner == null)
        {
            stylizationPlanner = FindAnyObjectByType<StylizationPlanner>();
        }

        if (proxyObjectsRoot == null)
        {
            var proxyRootObject = GameObject.Find("ProxyObjects");
            if (proxyRootObject != null)
            {
                proxyObjectsRoot = proxyRootObject.transform;
            }
        }
    }

    private void OnEnable()
    {
        _needsRefresh = true;
        _nextRefreshTime = 0f;
        Subscribe();
        RefreshApplication("enabled");
    }

    private void OnDisable()
    {
        Unsubscribe();
        ResetAppliedState();
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
        RefreshApplication("auto-refresh");
    }

    [ContextMenu("Reapply Active Theme")]
    public void ReapplyActiveTheme()
    {
        RefreshApplication("manual");
    }

    private void Subscribe()
    {
        if (roomSemanticBootstrap != null)
        {
            roomSemanticBootstrap.SummaryChanged -= HandleRoomSummaryChanged;
            roomSemanticBootstrap.SummaryChanged += HandleRoomSummaryChanged;
        }

        if (themeIntentController != null)
        {
            themeIntentController.ThemeChanged -= HandleThemeChanged;
            themeIntentController.ThemeChanged += HandleThemeChanged;
        }

        if (stylizationPlanner != null)
        {
            stylizationPlanner.PlanChanged -= HandlePlanChanged;
            stylizationPlanner.PlanChanged += HandlePlanChanged;
        }
    }

    private void Unsubscribe()
    {
        if (roomSemanticBootstrap != null)
        {
            roomSemanticBootstrap.SummaryChanged -= HandleRoomSummaryChanged;
        }

        if (themeIntentController != null)
        {
            themeIntentController.ThemeChanged -= HandleThemeChanged;
        }

        if (stylizationPlanner != null)
        {
            stylizationPlanner.PlanChanged -= HandlePlanChanged;
        }
    }

    private void HandleRoomSummaryChanged()
    {
        _needsRefresh = true;
        RefreshApplication("room-summary");
    }

    private void HandleThemeChanged(ThemeProfile _)
    {
        _needsRefresh = true;
        RefreshApplication("theme-changed");
    }

    private void HandlePlanChanged()
    {
        _needsRefresh = true;
        RefreshApplication("plan-changed");
    }

    private void RefreshApplication(string reason)
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

        if (!roomSemanticBootstrap.HasReadyRoom || roomSemanticBootstrap.CurrentRoom == null)
        {
            ResetAppliedState();
            PublishWaitingState("waiting-for-room");
            return;
        }

        if (themeIntentController.ActiveTheme == null)
        {
            ResetAppliedState();
            PublishWaitingState("waiting-for-theme");
            return;
        }

        ApplyThemeToRoom(
            themeIntentController.ActiveTheme,
            roomSemanticBootstrap.CurrentRoom,
            stylizationPlanner != null ? stylizationPlanner.CurrentPlan : null,
            reason);
    }

    private void ApplyThemeToRoom(ThemeProfile theme, MRUKRoom room, StylizationPlan plan, string reason)
    {
        ResetAppliedState();

        var floorCount = 0;
        var wallCount = 0;
        var ceilingCount = 0;
        var countedSurfaceRoots = new HashSet<Transform>();

        var renderers = FindObjectsByType<Renderer>(FindObjectsInactive.Include);
        foreach (var renderer in renderers)
        {
            if (renderer == null || renderer is ParticleSystemRenderer)
            {
                continue;
            }

            if (!TryGetSurfaceContext(renderer.transform, out var surfaceKind, out var surfaceRoot))
            {
                continue;
            }

            ApplySurfaceTheme(renderer, theme, surfaceKind);
            _lastAppliedRendererCount++;

            if (countedSurfaceRoots.Add(surfaceRoot))
            {
                _lastAppliedAnchorCount++;
                switch (surfaceKind)
                {
                    case ThemeSurfaceKind.Floor:
                        floorCount++;
                        break;
                    case ThemeSurfaceKind.Wall:
                        wallCount++;
                        break;
                    case ThemeSurfaceKind.Ceiling:
                        ceilingCount++;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        _lastAppliedTableProxyCount = applyTableProxies ? ApplyTableProxies(theme, room, plan) : 0;

        var builder = new StringBuilder(256);
        builder.AppendLine("[AnchorThemeApplier]");
        builder.AppendLine(_lastAppliedRendererCount > 0 || _lastAppliedTableProxyCount > 0
            ? "State: applied"
            : "State: waiting-for-targets");
        builder.AppendLine($"Theme: {theme.DisplayName}");
        builder.AppendLine($"Reason: {reason}");
        builder.AppendLine($"Surface Anchors: {_lastAppliedAnchorCount}");
        builder.AppendLine($"Renderers: {_lastAppliedRendererCount}");
        builder.AppendLine($"Table Proxies: {_lastAppliedTableProxyCount}");
        builder.AppendLine($"Plan Entries: {plan?.EntryCount ?? 0}");
        builder.AppendLine($"Table Status: {_lastTableProxyStatus}");
        builder.Append($"Coverage: floor={floorCount}, wall={wallCount}, ceiling={ceilingCount}");
        _latestSummary = builder.ToString();
        _needsRefresh = _lastAppliedRendererCount == 0;

        SummaryChanged?.Invoke();

        if (logApplications && (_lastAppliedRendererCount > 0 || _lastAppliedTableProxyCount > 0))
        {
            Debug.Log(_latestSummary, this);
        }
    }

    private int ApplyTableProxies(ThemeProfile theme, MRUKRoom room, StylizationPlan plan)
    {
        if (theme == null || room == null || plan == null || proxyObjectsRoot == null)
        {
            _lastTableProxyStatus = $"blocked(theme={theme != null}, room={room != null}, plan={plan != null}, proxyRoot={proxyObjectsRoot != null})";
            return 0;
        }

        var proxyCount = 0;
        var tableAnchorCount = 0;
        var matchedPlanCount = 0;
        var resolvedPrefabCount = 0;
        var lastEntryId = "none";
        var lastPrefabName = "none";
        var lastPrefabSource = "none";
        var lastFailure = "none";
        var lastAugmentation = "none";
        var lastFit = "none";

        for (var index = 0; index < room.Anchors.Count; index++)
        {
            var anchor = room.Anchors[index];
            if (anchor == null || !anchor.HasAnyLabel(MRUKAnchor.SceneLabels.TABLE) || !anchor.VolumeBounds.HasValue)
            {
                continue;
            }

            tableAnchorCount++;
            var planEntry = FindPlanEntry(plan, theme.ThemeId, "table", index);
            if (planEntry == null || planEntry.ReplacementMode != ReplacementMode.ProxyPrefab)
            {
                lastFailure = planEntry == null ? $"missing_plan_{index}" : $"mode_{planEntry.ReplacementMode}";
                continue;
            }

            matchedPlanCount++;
            lastEntryId = planEntry.EntryId;
            var proxyPrefab = ResolveProxyPrefab(theme, planEntry, out var prefabSource);
            if (proxyPrefab == null)
            {
                lastFailure = $"missing_prefab_{planEntry.EntryId}";
                continue;
            }

            resolvedPrefabCount++;
            lastPrefabName = proxyPrefab.name;
            lastPrefabSource = prefabSource;
            if (TrySpawnTableProxy(anchor, proxyPrefab, planEntry, theme, out var augmentationStatus, out var fitStatus))
            {
                proxyCount++;
                lastFailure = "none";
                lastAugmentation = augmentationStatus;
                lastFit = fitStatus;
            }
            else
            {
                lastFailure = $"spawn_failed_{planEntry.EntryId}";
                lastFit = "spawn_failed";
            }
        }

        _lastTableProxyStatus =
            $"anchors={tableAnchorCount}, plans={matchedPlanCount}, prefabs={resolvedPrefabCount}, spawned={proxyCount}, entry={lastEntryId}, prefab={lastPrefabName}, source={lastPrefabSource}, augment={lastAugmentation}, fit={lastFit}, failure={lastFailure}";
        return proxyCount;
    }

    private bool TrySpawnTableProxy(
        MRUKAnchor anchor,
        GameObject proxyPrefab,
        StylizationPlanEntry planEntry,
        ThemeProfile theme,
        out string augmentationStatus,
        out string fitStatus)
    {
        augmentationStatus = "none";
        fitStatus = "none";

        if (proxyObjectsRoot == null || proxyPrefab == null || !anchor.VolumeBounds.HasValue)
        {
            return false;
        }

        var proxyRoot = new GameObject($"TableProxy_{planEntry.EntryId}");
        proxyRoot.transform.SetParent(proxyObjectsRoot, false);

        var volumeBounds = anchor.VolumeBounds.Value;
        proxyRoot.transform.position = anchor.transform.TransformPoint(volumeBounds.center);

        proxyRoot.transform.rotation = GetTableProxyRotation(anchor, planEntry.PreserveYawOrientation, tableProxyYawOffsetDegrees);

        var proxyInstance = Instantiate(proxyPrefab, proxyRoot.transform);
        proxyInstance.name = $"{proxyPrefab.name}_{planEntry.EntryId}";
        proxyInstance.transform.localPosition = Vector3.zero;
        proxyInstance.transform.localRotation = Quaternion.identity;
        proxyInstance.transform.localScale = Vector3.one;

        if (!TryCalculateAnchorTargetBounds(proxyRoot.transform, anchor.transform, volumeBounds, out var targetBounds) ||
            !FitProxyToTableAnchor(proxyRoot.transform, proxyInstance.transform, targetBounds, out var fittedBounds, out var targetSize, out var sourceSize, out var appliedScale))
        {
            SafeDestroy(proxyRoot);
            return false;
        }

        var bottomDelta = fittedBounds.min.y - targetBounds.min.y;
        fitStatus = $"target={FormatSize(targetSize)}, source={FormatSize(sourceSize)}, scale={FormatSize(appliedScale)}, bottomDelta={FormatMeters(bottomDelta)}";

        ApplyProxyAccent(proxyInstance, theme);
        augmentationStatus = AugmentFlatTableProxy(proxyRoot.transform, proxyInstance.transform, fittedBounds, targetSize, theme);

        if (hideOriginalVolumeVisuals)
        {
            SuppressAnchorRenderers(anchor);
        }

        _spawnedProxyRoots.Add(proxyRoot);
        return true;
    }

    private bool FitProxyToTableAnchor(
        Transform proxyRoot,
        Transform proxyInstance,
        Bounds targetBounds,
        out Bounds fittedBounds,
        out Vector3 targetSize,
        out Vector3 sourceSize,
        out Vector3 appliedScale)
    {
        fittedBounds = default;
        targetSize = default;
        sourceSize = default;
        appliedScale = Vector3.one;

        if (!TryCalculateLocalBounds(proxyRoot, out var initialBounds))
        {
            return false;
        }

        sourceSize = initialBounds.size;
        targetSize = new Vector3(
            Mathf.Max(targetBounds.size.x * proxyFootprintPadding, 0.05f),
            Mathf.Max(targetBounds.size.y * proxyHeightPadding, 0.05f),
            Mathf.Max(targetBounds.size.z * proxyFootprintPadding, 0.05f));

        var xScale = sourceSize.x > 0.001f ? targetSize.x / sourceSize.x : 1f;
        var zScale = sourceSize.z > 0.001f ? targetSize.z / sourceSize.z : 1f;
        if (!IsUsableScale(xScale))
        {
            xScale = 1f;
        }

        if (!IsUsableScale(zScale))
        {
            zScale = 1f;
        }

        var footprintScale = Mathf.Min(xScale, zScale);
        var yScale = sourceSize.y > 0.001f ? targetSize.y / sourceSize.y : footprintScale;
        if (!IsUsableScale(yScale))
        {
            yScale = footprintScale;
        }

        var scale = new Vector3(xScale, yScale, zScale);
        appliedScale = scale;

        proxyInstance.localScale = Vector3.Scale(proxyInstance.localScale, scale);

        if (!TryCalculateLocalBounds(proxyRoot, out fittedBounds))
        {
            return false;
        }

        proxyInstance.localPosition = new Vector3(
            targetBounds.center.x - fittedBounds.center.x,
            targetBounds.min.y - fittedBounds.min.y,
            targetBounds.center.z - fittedBounds.center.z);

        if (!TryCalculateLocalBounds(proxyRoot, out fittedBounds))
        {
            return false;
        }

        return true;
    }

    private static bool TryCalculateAnchorTargetBounds(
        Transform proxyRoot,
        Transform anchorTransform,
        Bounds anchorLocalBounds,
        out Bounds targetBounds)
    {
        targetBounds = default;
        if (proxyRoot == null || anchorTransform == null)
        {
            return false;
        }

        var hasBounds = false;
        var min = anchorLocalBounds.min;
        var max = anchorLocalBounds.max;
        for (var x = 0; x <= 1; x++)
        {
            for (var y = 0; y <= 1; y++)
            {
                for (var z = 0; z <= 1; z++)
                {
                    var anchorLocalPoint = new Vector3(
                        x == 0 ? min.x : max.x,
                        y == 0 ? min.y : max.y,
                        z == 0 ? min.z : max.z);
                    var worldPoint = anchorTransform.TransformPoint(anchorLocalPoint);
                    var targetLocalPoint = proxyRoot.InverseTransformPoint(worldPoint);

                    if (!hasBounds)
                    {
                        targetBounds = new Bounds(targetLocalPoint, Vector3.zero);
                        hasBounds = true;
                    }
                    else
                    {
                        targetBounds.Encapsulate(targetLocalPoint);
                    }
                }
            }
        }

        return hasBounds;
    }

    private static bool IsUsableScale(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value) && value > 0.001f;
    }

    private static string FormatSize(Vector3 value)
    {
        return FormattableString.Invariant($"{value.x:0.###}x{value.y:0.###}x{value.z:0.###}");
    }

    private static string FormatMeters(float value)
    {
        return FormattableString.Invariant($"{value:0.###}m");
    }

    private string AugmentFlatTableProxy(
        Transform proxyRoot,
        Transform proxyInstance,
        Bounds fittedBounds,
        Vector3 targetSize,
        ThemeProfile theme)
    {
        if (!augmentFlatTableProxies)
        {
            return "disabled";
        }

        if (proxyRoot == null || theme == null)
        {
            return "missing_context";
        }

        if (fittedBounds.size.y >= targetSize.y * flatTableHeightThreshold)
        {
            return "not_needed";
        }

        SetProxyVisualState(proxyInstance, false);

        var tabletopMaterial = CreateGeneratedTableSupportMaterial(theme, 0.16f, 0.7f);
        var legMaterial = CreateGeneratedTableSupportMaterial(theme, 0.34f, 1.35f);
        var railMaterial = CreateGeneratedTableSupportMaterial(theme, 0.48f, 0.85f);
        if (tabletopMaterial == null || legMaterial == null || railMaterial == null)
        {
            return "material_failed";
        }

        var floorY = -targetSize.y * 0.5f;
        var undersideY = Mathf.Clamp(fittedBounds.min.y, floorY + 0.08f, targetSize.y * 0.5f);
        var legHeight = undersideY - floorY;
        if (legHeight <= 0.05f)
        {
            return "insufficient_height";
        }

        var tabletopThickness = Mathf.Clamp(targetSize.y * 0.08f, 0.035f, 0.08f);
        var tabletopCenterY = targetSize.y * 0.5f - tabletopThickness * 0.5f;
        var legThickness = Mathf.Clamp(Mathf.Min(targetSize.x, targetSize.z) * 0.1f, 0.05f, 0.12f);
        var railThickness = Mathf.Clamp(legThickness * 0.55f, 0.03f, 0.08f);
        var xOffset = Mathf.Max(targetSize.x * 0.5f - legThickness * 1.25f, legThickness * 0.75f);
        var zOffset = Mathf.Max(targetSize.z * 0.5f - legThickness * 1.25f, legThickness * 0.75f);
        var legCenterY = floorY + legHeight * 0.5f;
        var railY = undersideY - railThickness * 0.75f;

        CreateGeneratedTablePart(
            proxyRoot,
            "GeneratedTableTop",
            new Vector3(0f, tabletopCenterY, 0f),
            new Vector3(Mathf.Max(targetSize.x, 0.1f), tabletopThickness, Mathf.Max(targetSize.z, 0.1f)),
            tabletopMaterial);
        CreateGeneratedTablePart(
            proxyRoot,
            "GeneratedTableLeg_FL",
            new Vector3(-xOffset, legCenterY, zOffset),
            new Vector3(legThickness, legHeight, legThickness),
            legMaterial);
        CreateGeneratedTablePart(
            proxyRoot,
            "GeneratedTableLeg_FR",
            new Vector3(xOffset, legCenterY, zOffset),
            new Vector3(legThickness, legHeight, legThickness),
            legMaterial);
        CreateGeneratedTablePart(
            proxyRoot,
            "GeneratedTableLeg_BL",
            new Vector3(-xOffset, legCenterY, -zOffset),
            new Vector3(legThickness, legHeight, legThickness),
            legMaterial);
        CreateGeneratedTablePart(
            proxyRoot,
            "GeneratedTableLeg_BR",
            new Vector3(xOffset, legCenterY, -zOffset),
            new Vector3(legThickness, legHeight, legThickness),
            legMaterial);

        CreateGeneratedTablePart(
            proxyRoot,
            "GeneratedTableRail_Front",
            new Vector3(0f, railY, zOffset),
            new Vector3(Mathf.Max(targetSize.x - legThickness * 1.8f, legThickness), railThickness, railThickness),
            railMaterial);
        CreateGeneratedTablePart(
            proxyRoot,
            "GeneratedTableRail_Back",
            new Vector3(0f, railY, -zOffset),
            new Vector3(Mathf.Max(targetSize.x - legThickness * 1.8f, legThickness), railThickness, railThickness),
            railMaterial);
        CreateGeneratedTablePart(
            proxyRoot,
            "GeneratedTableRail_Left",
            new Vector3(-xOffset, railY, 0f),
            new Vector3(railThickness, railThickness, Mathf.Max(targetSize.z - legThickness * 1.8f, legThickness)),
            railMaterial);
        CreateGeneratedTablePart(
            proxyRoot,
            "GeneratedTableRail_Right",
            new Vector3(xOffset, railY, 0f),
            new Vector3(railThickness, railThickness, Mathf.Max(targetSize.z - legThickness * 1.8f, legThickness)),
            railMaterial);

        return "generated_supports";
    }

    private static Quaternion GetTableProxyRotation(
        MRUKAnchor anchor,
        bool preserveYawOrientation,
        float yawOffsetDegrees)
    {
        if (anchor == null)
        {
            return Quaternion.identity;
        }

        if (!preserveYawOrientation)
        {
            return Quaternion.identity;
        }

        var forward = Vector3.ProjectOnPlane(anchor.transform.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.0001f)
        {
            forward = Vector3.ProjectOnPlane(anchor.transform.right, Vector3.up);
        }

        if (forward.sqrMagnitude < 0.0001f)
        {
            return Quaternion.identity;
        }

        return Quaternion.LookRotation(forward.normalized, Vector3.up) * Quaternion.Euler(0f, yawOffsetDegrees, 0f);
    }

    private void ApplyProxyAccent(GameObject proxyInstance, ThemeProfile theme)
    {
        if (proxyInstance == null || theme == null)
        {
            return;
        }

        var accentTint = Color.Lerp(Color.white, theme.AccentColor, 0.12f);
        var accentEmission = theme.AccentColor * Mathf.Max(0.12f, theme.SurfaceMaterials.EmissionIntensity);
        var renderers = proxyInstance.GetComponentsInChildren<Renderer>(true);
        foreach (var renderer in renderers)
        {
            if (renderer == null || renderer is ParticleSystemRenderer)
            {
                continue;
            }

            var sourceMaterials = renderer.sharedMaterials;
            var themedMaterials = new Material[sourceMaterials.Length];
            for (var index = 0; index < sourceMaterials.Length; index++)
            {
                var sourceMaterial = sourceMaterials[index];
                var materialInstance = sourceMaterial != null ? new Material(sourceMaterial) : null;
                if (materialInstance == null)
                {
                    continue;
                }

                ApplyProxyMaterialTone(materialInstance, accentTint);
                SetEmission(materialInstance, accentEmission);
                themedMaterials[index] = materialInstance;
                _runtimeMaterials.Add(materialInstance);
            }

            renderer.sharedMaterials = themedMaterials;
        }
    }

    private static void ApplyProxyMaterialTone(Material materialInstance, Color accentTint)
    {
        if (!TryGetMaterialColor(materialInstance, out var baseColor))
        {
            baseColor = Color.white;
        }

        var tintedColor = Color.Lerp(baseColor, accentTint, 0.28f);
        tintedColor.a = baseColor.a > 0.001f ? baseColor.a : 1f;
        SetMaterialColor(materialInstance, tintedColor);
    }

    private Material CreateGeneratedTableSupportMaterial(ThemeProfile theme, float valueBlend, float emissionScale)
    {
        var fallbackShader = Shader.Find("Universal Render Pipeline/Lit") ??
                             Shader.Find("Universal Render Pipeline/Unlit") ??
                             Shader.Find("Standard");
        if (fallbackShader == null)
        {
            return null;
        }

        var material = new Material(fallbackShader);
        var baseColor = Color.Lerp(new Color(0.12f, 0.14f, 0.18f, 1f), theme.AccentColor, valueBlend);
        baseColor.a = 1f;
        SetMaterialColor(material, baseColor);
        SetMaterialTexture(material);
        SetEmission(material, theme.AccentColor * Mathf.Max(0.08f, theme.SurfaceMaterials.EmissionIntensity * emissionScale));
        ConfigureOpaqueProxyMaterial(material);
        _runtimeMaterials.Add(material);
        return material;
    }

    private void CreateGeneratedTablePart(
        Transform parent,
        string partName,
        Vector3 localPosition,
        Vector3 localScale,
        Material sharedMaterial)
    {
        var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
        part.name = partName;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPosition;
        part.transform.localRotation = Quaternion.identity;
        part.transform.localScale = localScale;

        var collider = part.GetComponent<Collider>();
        if (collider != null)
        {
            SafeDestroy(collider);
        }

        var renderer = part.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = sharedMaterial;
        }
    }

    private static void SetProxyVisualState(Transform root, bool isVisible)
    {
        if (root == null)
        {
            return;
        }

        var renderers = root.GetComponentsInChildren<Renderer>(true);
        foreach (var renderer in renderers)
        {
            if (renderer != null)
            {
                renderer.enabled = isVisible;
            }
        }

        var colliders = root.GetComponentsInChildren<Collider>(true);
        foreach (var collider in colliders)
        {
            if (collider != null)
            {
                collider.enabled = isVisible;
            }
        }
    }

    private void SuppressAnchorRenderers(MRUKAnchor anchor)
    {
        if (anchor == null)
        {
            return;
        }

        var visuals = anchor.GetComponentsInChildren<Transform>(true);
        foreach (var current in visuals)
        {
            if (current == null || current == anchor.transform)
            {
                continue;
            }

            if (!ShouldSuppressVisualRoot(current))
            {
                continue;
            }

            var visualObject = current.gameObject;
            if (!_originalVisualObjectStates.ContainsKey(visualObject))
            {
                _originalVisualObjectStates[visualObject] = visualObject.activeSelf;
            }

            visualObject.SetActive(false);
        }

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

        SuppressRoomModelSemanticVisual(anchor);
    }

    private void SuppressRoomModelSemanticVisual(MRUKAnchor anchor)
    {
        var semanticRootName = GetDebugSemanticRootName(anchor);
        if (string.IsNullOrEmpty(semanticRootName))
        {
            return;
        }

        var transforms = FindObjectsByType<Transform>(FindObjectsInactive.Include);
        Transform closestMatch = null;
        var closestDistance = float.MaxValue;

        foreach (var current in transforms)
        {
            if (current == null || !string.Equals(current.name, semanticRootName, StringComparison.Ordinal))
            {
                continue;
            }

            if (!HasAncestorNamed(current, "RoomModel"))
            {
                continue;
            }

            var distance = Vector3.SqrMagnitude(current.position - anchor.transform.position);
            if (distance >= closestDistance)
            {
                continue;
            }

            closestDistance = distance;
            closestMatch = current;
        }

        if (closestMatch == null)
        {
            return;
        }

        var visualObject = closestMatch.gameObject;
        if (!_originalVisualObjectStates.ContainsKey(visualObject))
        {
            _originalVisualObjectStates[visualObject] = visualObject.activeSelf;
        }

        visualObject.SetActive(false);
    }

    private static string GetDebugSemanticRootName(MRUKAnchor anchor)
    {
        if (anchor == null)
        {
            return null;
        }

        if (anchor.HasAnyLabel(MRUKAnchor.SceneLabels.TABLE))
        {
            return "TABLE";
        }

        return null;
    }

    private static bool HasAncestorNamed(Transform current, string ancestorName)
    {
        while (current != null)
        {
            if (string.Equals(current.name, ancestorName, StringComparison.Ordinal))
            {
                return true;
            }

            current = current.parent;
        }

        return false;
    }

    private static StylizationPlanEntry FindPlanEntry(
        StylizationPlan plan,
        string themeId,
        string semanticLabel,
        int anchorIndex)
    {
        if (plan?.Entries == null)
        {
            return null;
        }

        var entryId = $"{themeId}_{semanticLabel}_{anchorIndex:D2}";
        for (var index = 0; index < plan.Entries.Count; index++)
        {
            var entry = plan.Entries[index];
            if (entry != null && string.Equals(entry.EntryId, entryId, StringComparison.Ordinal))
            {
                return entry;
            }
        }

        return null;
    }

    private GameObject ResolveProxyPrefab(ThemeProfile theme, StylizationPlanEntry planEntry, out string prefabSource)
    {
        prefabSource = "none";
        if (theme == null || planEntry == null)
        {
            return null;
        }

#if UNITY_EDITOR
        if (preferImportedGeneratedTablePrefabs &&
            IsTableEntry(planEntry) &&
            TryLoadImportedGeneratedTablePrefab(theme, planEntry, out var importedGeneratedPrefab))
        {
            prefabSource = "generated_import";
            return importedGeneratedPrefab;
        }
#endif

        var matchedRule = FindRule(theme, planEntry.OriginalSemanticLabel, planEntry.OriginalFunctionTag);
        if (matchedRule?.ProxyPrefab != null)
        {
            prefabSource = "theme_rule";
            return matchedRule.ProxyPrefab;
        }

        prefabSource = "theme_default";
        return theme.GetDefaultProxy(planEntry.OriginalSemanticLabel);
    }

    private static bool IsTableEntry(StylizationPlanEntry planEntry)
    {
        return planEntry != null &&
               string.Equals(planEntry.OriginalSemanticLabel, "table", StringComparison.OrdinalIgnoreCase);
    }

#if UNITY_EDITOR
    private bool TryLoadImportedGeneratedTablePrefab(
        ThemeProfile theme,
        StylizationPlanEntry planEntry,
        out GameObject prefab)
    {
        prefab = null;
        var jobDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Library", generatedObjectJobFolderName));
        if (!Directory.Exists(jobDirectory))
        {
            return false;
        }

        GeneratedAssetRecord bestRecord = null;
        DateTime bestUpdatedAt = DateTime.MinValue;
        foreach (var jobPath in Directory.GetFiles(jobDirectory, "*.job.json", SearchOption.TopDirectoryOnly))
        {
            var record = TryReadGeneratedAssetRecord(jobPath);
            if (record == null || !IsUsableGeneratedTableRecord(record, theme, planEntry))
            {
                continue;
            }

            var updatedAt = DateTime.TryParse(record.UpdatedAtIsoUtc, out var parsed)
                ? parsed.ToUniversalTime()
                : DateTime.MinValue;
            if (bestRecord != null && updatedAt <= bestUpdatedAt)
            {
                continue;
            }

            bestRecord = record;
            bestUpdatedAt = updatedAt;
        }

        if (bestRecord == null || !TryGetProjectRelativePath(bestRecord.ImportedPrefabPath, out var prefabAssetPath))
        {
            return false;
        }

        prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabAssetPath);
        return prefab != null;
    }

    private static GeneratedAssetRecord TryReadGeneratedAssetRecord(string jobPath)
    {
        if (string.IsNullOrWhiteSpace(jobPath) || !File.Exists(jobPath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(jobPath);
            return string.IsNullOrWhiteSpace(json)
                ? null
                : JsonUtility.FromJson<GeneratedAssetRecord>(json);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[AnchorThemeApplier] Failed to read generated-object job '{jobPath}': {exception.Message}");
            return null;
        }
    }

    private static bool IsUsableGeneratedTableRecord(
        GeneratedAssetRecord record,
        ThemeProfile theme,
        StylizationPlanEntry planEntry)
    {
        if (record == null || theme == null || planEntry == null)
        {
            return false;
        }

        if (record.State != GeneratedObjectJobState.Imported)
        {
            return false;
        }

        if (!string.Equals(record.ThemeId, theme.ThemeId, StringComparison.Ordinal))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(record.ImportedPrefabPath) || !File.Exists(record.ImportedPrefabPath))
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(record.ObjectId) ||
               string.IsNullOrWhiteSpace(planEntry.ObjectId) ||
               record.ObjectId.Contains("TABLE", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(record.ObjectId, planEntry.ObjectId, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetProjectRelativePath(string absoluteOrRelativePath, out string assetPath)
    {
        assetPath = string.Empty;
        if (string.IsNullOrWhiteSpace(absoluteOrRelativePath))
        {
            return false;
        }

        if (absoluteOrRelativePath.StartsWith("Assets/", StringComparison.Ordinal))
        {
            assetPath = absoluteOrRelativePath;
            return true;
        }

        var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        var fullPath = Path.GetFullPath(absoluteOrRelativePath);
        if (!fullPath.StartsWith(projectRoot, StringComparison.Ordinal))
        {
            return false;
        }

        assetPath = fullPath[(projectRoot.Length + 1)..].Replace(Path.DirectorySeparatorChar, '/');
        return assetPath.StartsWith("Assets/", StringComparison.Ordinal);
    }
#endif

    private static SemanticReplacementRule FindRule(ThemeProfile theme, string semanticLabel, string functionTag)
    {
        if (theme?.ReplacementRules == null)
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

    private static bool TryCalculateLocalBounds(Transform root, out Bounds bounds)
    {
        bounds = default;

        if (root == null)
        {
            return false;
        }

        var renderers = root.GetComponentsInChildren<Renderer>(true);
        var hasBounds = false;
        for (var index = 0; index < renderers.Length; index++)
        {
            var renderer = renderers[index];
            if (!ShouldUseRendererForBounds(renderer))
            {
                continue;
            }

            var worldBounds = renderer.bounds;
            var min = worldBounds.min;
            var max = worldBounds.max;
            for (var x = 0; x <= 1; x++)
            {
                for (var y = 0; y <= 1; y++)
                {
                    for (var z = 0; z <= 1; z++)
                    {
                        var worldPoint = new Vector3(
                            x == 0 ? min.x : max.x,
                            y == 0 ? min.y : max.y,
                            z == 0 ? min.z : max.z);
                        var localPoint = root.InverseTransformPoint(worldPoint);
                        if (!hasBounds)
                        {
                            bounds = new Bounds(localPoint, Vector3.zero);
                            hasBounds = true;
                        }
                        else
                        {
                            bounds.Encapsulate(localPoint);
                        }
                    }
                }
            }
        }

        return hasBounds;
    }

    private static bool ShouldUseRendererForBounds(Renderer renderer)
    {
        if (renderer == null || renderer is ParticleSystemRenderer)
        {
            return false;
        }

        // Ignore baked shadow helper meshes so they do not distort proxy fitting.
        return !renderer.name.Contains("shadow", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldSuppressVisualRoot(Transform current)
    {
        return current.name.Contains("(PrefabSpawner Clone)", StringComparison.Ordinal) ||
               current.name.StartsWith("Volume(", StringComparison.Ordinal) ||
               current.name.StartsWith("PlaneMesh(", StringComparison.Ordinal);
    }

    private void ApplySurfaceTheme(Renderer renderer, ThemeProfile theme, ThemeSurfaceKind surfaceKind)
    {
        if (!_originalSharedMaterials.TryGetValue(renderer, out var originalMaterials))
        {
            originalMaterials = renderer.sharedMaterials;
            _originalSharedMaterials[renderer] = originalMaterials;
        }

        var themedMaterials = new Material[originalMaterials.Length];
        for (var index = 0; index < originalMaterials.Length; index++)
        {
            themedMaterials[index] = CreateThemedMaterial(originalMaterials[index], theme, surfaceKind);
        }

        renderer.sharedMaterials = themedMaterials;
    }

    private Material CreateThemedMaterial(Material sourceMaterial, ThemeProfile theme, ThemeSurfaceKind surfaceKind)
    {
        var overrideMaterial = theme.SurfaceMaterials.GetMaterialOverride(surfaceKind);
        var baseMaterial = overrideMaterial;

        Material materialInstance;
        if (baseMaterial != null)
        {
            materialInstance = new Material(baseMaterial);
        }
        else
        {
            var fallbackShader = Shader.Find("Universal Render Pipeline/Unlit") ??
                                 Shader.Find("Universal Render Pipeline/Lit") ??
                                 Shader.Find("Standard");
            materialInstance = new Material(fallbackShader);
            ConfigureFallbackSurfaceMaterial(materialInstance);
            var proceduralTexture = ProceduralSurfaceTextureFactory.CreateTexture(theme, surfaceKind);
            if (proceduralTexture != null)
            {
                _runtimeTextures.Add(proceduralTexture);
            }

            SetMaterialTexture(materialInstance, proceduralTexture, theme.SurfaceMaterials.TextureTiling);
        }

        var tintColor = theme.SurfaceMaterials.GetTintColor(surfaceKind);
        SetMaterialColor(materialInstance, baseMaterial != null ? tintColor : new Color(1f, 1f, 1f, tintColor.a));
        SetEmission(materialInstance, tintColor * theme.SurfaceMaterials.EmissionIntensity);

        _runtimeMaterials.Add(materialInstance);
        return materialInstance;
    }

    private void PublishWaitingState(string reason)
    {
        _needsRefresh = true;
        _latestSummary = $"[AnchorThemeApplier]\nState: {reason}\nHint: wait for room + theme before applying surface overrides.";
        SummaryChanged?.Invoke();
    }

    private void ResetAppliedState()
    {
        ResetAppliedMaterials();
        ResetSuppressedRenderers();
        ResetSpawnedProxies();
    }

    private void ResetAppliedMaterials()
    {
        foreach (var pair in _originalSharedMaterials)
        {
            if (pair.Key == null)
            {
                continue;
            }

            pair.Key.sharedMaterials = pair.Value;
        }

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
        _originalSharedMaterials.Clear();
        _lastAppliedAnchorCount = 0;
        _lastAppliedRendererCount = 0;
    }

    private void ResetSuppressedRenderers()
    {
        foreach (var pair in _originalVisualObjectStates)
        {
            if (pair.Key == null)
            {
                continue;
            }

            pair.Key.SetActive(pair.Value);
        }

        _originalVisualObjectStates.Clear();

        foreach (var pair in _originalRendererStates)
        {
            if (pair.Key == null)
            {
                continue;
            }

            pair.Key.enabled = pair.Value;
        }

        _originalRendererStates.Clear();
    }

    private void ResetSpawnedProxies()
    {
        foreach (var proxyRoot in _spawnedProxyRoots)
        {
            if (proxyRoot != null)
            {
                SafeDestroy(proxyRoot);
            }
        }

        _spawnedProxyRoots.Clear();
        _lastAppliedTableProxyCount = 0;
        _lastTableProxyStatus = "reset";
    }

    private bool TryGetSurfaceContext(Transform current, out ThemeSurfaceKind surfaceKind, out Transform surfaceRoot)
    {
        Transform visualSurfaceRoot = null;
        while (current != null)
        {
            if (visualSurfaceRoot == null && current.name.Contains("(PrefabSpawner Clone)", StringComparison.Ordinal))
            {
                visualSurfaceRoot = current;
            }

            switch (current.name)
            {
                case "FLOOR":
                    if (visualSurfaceRoot == null)
                    {
                        break;
                    }

                    surfaceKind = ThemeSurfaceKind.Floor;
                    surfaceRoot = visualSurfaceRoot != null ? visualSurfaceRoot : current;
                    return true;
                case "CEILING":
                    if (visualSurfaceRoot == null)
                    {
                        break;
                    }

                    surfaceKind = ThemeSurfaceKind.Ceiling;
                    surfaceRoot = visualSurfaceRoot != null ? visualSurfaceRoot : current;
                    return true;
                case "WALL_FACE":
                    if (visualSurfaceRoot == null)
                    {
                        break;
                    }

                    surfaceKind = ThemeSurfaceKind.Wall;
                    surfaceRoot = visualSurfaceRoot != null ? visualSurfaceRoot : current;
                    return true;
                case "INNER_WALL_FACE" when includeInnerWallFaces:
                    if (visualSurfaceRoot == null)
                    {
                        break;
                    }

                    surfaceKind = ThemeSurfaceKind.Wall;
                    surfaceRoot = visualSurfaceRoot != null ? visualSurfaceRoot : current;
                    return true;
            }

            current = current.parent;
        }

        surfaceKind = default;
        surfaceRoot = null;
        return false;
    }

    private static void SetMaterialColor(Material materialInstance, Color tintColor)
    {
        if (materialInstance.HasProperty("_BaseColor"))
        {
            materialInstance.SetColor("_BaseColor", tintColor);
        }

        if (materialInstance.HasProperty("_Color"))
        {
            materialInstance.SetColor("_Color", tintColor);
        }
    }

    private static bool TryGetMaterialColor(Material materialInstance, out Color color)
    {
        if (materialInstance.HasProperty("_BaseColor"))
        {
            color = materialInstance.GetColor("_BaseColor");
            return true;
        }

        if (materialInstance.HasProperty("_Color"))
        {
            color = materialInstance.GetColor("_Color");
            return true;
        }

        color = Color.white;
        return false;
    }

    private static void SetMaterialTexture(Material materialInstance, Texture texture = null, float tiling = 1f)
    {
        var targetTexture = texture != null ? texture : Texture2D.whiteTexture;
        var textureScale = Vector2.one * Mathf.Max(0.1f, tiling);
        if (materialInstance.HasProperty("_BaseMap"))
        {
            materialInstance.SetTexture("_BaseMap", targetTexture);
            materialInstance.SetTextureScale("_BaseMap", textureScale);
        }

        if (materialInstance.HasProperty("_MainTex"))
        {
            materialInstance.SetTexture("_MainTex", targetTexture);
            materialInstance.SetTextureScale("_MainTex", textureScale);
        }
    }

    private static void ConfigureFallbackSurfaceMaterial(Material materialInstance)
    {
        if (materialInstance.HasProperty("_Surface"))
        {
            materialInstance.SetFloat("_Surface", 1f);
        }

        if (materialInstance.HasProperty("_Blend"))
        {
            materialInstance.SetFloat("_Blend", 0f);
        }

        if (materialInstance.HasProperty("_AlphaClip"))
        {
            materialInstance.SetFloat("_AlphaClip", 0f);
        }

        if (materialInstance.HasProperty("_SrcBlend"))
        {
            materialInstance.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        }

        if (materialInstance.HasProperty("_DstBlend"))
        {
            materialInstance.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        }

        if (materialInstance.HasProperty("_ZWrite"))
        {
            materialInstance.SetFloat("_ZWrite", 0f);
        }

        materialInstance.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
    }

    private static void ConfigureOpaqueProxyMaterial(Material materialInstance)
    {
        if (materialInstance.HasProperty("_Surface"))
        {
            materialInstance.SetFloat("_Surface", 0f);
        }

        if (materialInstance.HasProperty("_Blend"))
        {
            materialInstance.SetFloat("_Blend", 0f);
        }

        if (materialInstance.HasProperty("_AlphaClip"))
        {
            materialInstance.SetFloat("_AlphaClip", 0f);
        }

        if (materialInstance.HasProperty("_SrcBlend"))
        {
            materialInstance.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
        }

        if (materialInstance.HasProperty("_DstBlend"))
        {
            materialInstance.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero);
        }

        if (materialInstance.HasProperty("_ZWrite"))
        {
            materialInstance.SetFloat("_ZWrite", 1f);
        }

        materialInstance.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
    }

    private static void SetEmission(Material materialInstance, Color emissionColor)
    {
        if (!materialInstance.HasProperty("_EmissionColor"))
        {
            return;
        }

        materialInstance.EnableKeyword("_EMISSION");
        materialInstance.SetColor("_EmissionColor", emissionColor);
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
