using System;
using System.Collections.Generic;
using System.Text;
using Meta.XR.MRUtilityKit;
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
    [SerializeField, Min(0.1f)] private float proxyFootprintPadding = 0.92f;
    [SerializeField, Min(0.1f)] private float proxyHeightPadding = 0.96f;

    public event Action SummaryChanged;

    public string LatestSummary => _latestSummary;
    public int LastAppliedAnchorCount => _lastAppliedAnchorCount;
    public int LastAppliedRendererCount => _lastAppliedRendererCount;
    public int LastAppliedTableProxyCount => _lastAppliedTableProxyCount;

    private readonly Dictionary<Renderer, Material[]> _originalSharedMaterials = new();
    private readonly Dictionary<Renderer, bool> _originalRendererStates = new();
    private readonly List<Material> _runtimeMaterials = new();
    private readonly List<GameObject> _spawnedProxyRoots = new();

    private string _latestSummary = "[AnchorThemeApplier]\nState: waiting\nHint: enter Play and wait for room + theme.";
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
            return 0;
        }

        var proxyCount = 0;
        for (var index = 0; index < room.Anchors.Count; index++)
        {
            var anchor = room.Anchors[index];
            if (anchor == null || !anchor.HasAnyLabel(MRUKAnchor.SceneLabels.TABLE) || !anchor.VolumeBounds.HasValue)
            {
                continue;
            }

            var planEntry = FindPlanEntry(plan, theme.ThemeId, "table", index);
            if (planEntry == null || planEntry.ReplacementMode != ReplacementMode.ProxyPrefab)
            {
                continue;
            }

            var proxyPrefab = ResolveProxyPrefab(theme, planEntry);
            if (proxyPrefab == null)
            {
                continue;
            }

            if (TrySpawnTableProxy(anchor, proxyPrefab, planEntry, theme))
            {
                proxyCount++;
            }
        }

        return proxyCount;
    }

    private bool TrySpawnTableProxy(
        MRUKAnchor anchor,
        GameObject proxyPrefab,
        StylizationPlanEntry planEntry,
        ThemeProfile theme)
    {
        if (proxyObjectsRoot == null || proxyPrefab == null || !anchor.VolumeBounds.HasValue)
        {
            return false;
        }

        var proxyRoot = new GameObject($"TableProxy_{planEntry.EntryId}");
        proxyRoot.transform.SetParent(proxyObjectsRoot, false);

        var volumeBounds = anchor.VolumeBounds.Value;
        proxyRoot.transform.position = anchor.transform.TransformPoint(volumeBounds.center);

        var yaw = planEntry.PreserveYawOrientation ? anchor.transform.eulerAngles.y : 0f;
        proxyRoot.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

        var proxyInstance = Instantiate(proxyPrefab, proxyRoot.transform);
        proxyInstance.name = $"{proxyPrefab.name}_{planEntry.EntryId}";
        proxyInstance.transform.localPosition = Vector3.zero;
        proxyInstance.transform.localRotation = Quaternion.identity;
        proxyInstance.transform.localScale = Vector3.one;

        if (!FitProxyToAnchor(proxyRoot.transform, proxyInstance.transform, volumeBounds.size))
        {
            SafeDestroy(proxyRoot);
            return false;
        }

        ApplyProxyAccent(proxyInstance, theme);

        if (hideOriginalVolumeVisuals)
        {
            SuppressAnchorRenderers(anchor);
        }

        _spawnedProxyRoots.Add(proxyRoot);
        return true;
    }

    private bool FitProxyToAnchor(Transform proxyRoot, Transform proxyInstance, Vector3 anchorSize)
    {
        if (!TryCalculateLocalBounds(proxyRoot, out var initialBounds))
        {
            return false;
        }

        var sourceSize = initialBounds.size;
        var targetSize = new Vector3(
            Mathf.Max(anchorSize.x * proxyFootprintPadding, 0.05f),
            Mathf.Max(anchorSize.y * proxyHeightPadding, 0.05f),
            Mathf.Max(anchorSize.z * proxyFootprintPadding, 0.05f));

        var scale = new Vector3(
            sourceSize.x > 0.001f ? targetSize.x / sourceSize.x : 1f,
            sourceSize.y > 0.001f ? targetSize.y / sourceSize.y : 1f,
            sourceSize.z > 0.001f ? targetSize.z / sourceSize.z : 1f);

        proxyInstance.localScale = Vector3.Scale(proxyInstance.localScale, scale);

        if (!TryCalculateLocalBounds(proxyRoot, out var fittedBounds))
        {
            return false;
        }

        proxyInstance.localPosition -= fittedBounds.center;
        return true;
    }

    private void ApplyProxyAccent(GameObject proxyInstance, ThemeProfile theme)
    {
        if (proxyInstance == null || theme == null)
        {
            return;
        }

        var accentTint = Color.Lerp(Color.white, theme.AccentColor, 0.18f);
        var accentEmission = theme.AccentColor * Mathf.Max(0.05f, theme.SurfaceMaterials.EmissionIntensity);
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

                SetMaterialColor(materialInstance, accentTint);
                SetEmission(materialInstance, accentEmission);
                themedMaterials[index] = materialInstance;
                _runtimeMaterials.Add(materialInstance);
            }

            renderer.sharedMaterials = themedMaterials;
        }
    }

    private void SuppressAnchorRenderers(MRUKAnchor anchor)
    {
        if (anchor == null)
        {
            return;
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

    private static GameObject ResolveProxyPrefab(ThemeProfile theme, StylizationPlanEntry planEntry)
    {
        if (theme == null || planEntry == null)
        {
            return null;
        }

        var matchedRule = FindRule(theme, planEntry.OriginalSemanticLabel, planEntry.OriginalFunctionTag);
        return matchedRule?.ProxyPrefab != null
            ? matchedRule.ProxyPrefab
            : theme.GetDefaultProxy(planEntry.OriginalSemanticLabel);
    }

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
            if (renderer == null || renderer is ParticleSystemRenderer)
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
            SetMaterialTexture(materialInstance);
        }

        var tintColor = theme.SurfaceMaterials.GetTintColor(surfaceKind);
        SetMaterialColor(materialInstance, tintColor);
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
        _originalSharedMaterials.Clear();
        _lastAppliedAnchorCount = 0;
        _lastAppliedRendererCount = 0;
    }

    private void ResetSuppressedRenderers()
    {
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

    private static void SetMaterialTexture(Material materialInstance)
    {
        if (materialInstance.HasProperty("_BaseMap"))
        {
            materialInstance.SetTexture("_BaseMap", Texture2D.whiteTexture);
        }

        if (materialInstance.HasProperty("_MainTex"))
        {
            materialInstance.SetTexture("_MainTex", Texture2D.whiteTexture);
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
