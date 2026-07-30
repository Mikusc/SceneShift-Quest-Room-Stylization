using System;
using System.Collections;
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
    [SerializeField] private RuntimeStyleIntentController runtimeStyleIntentController;
    [SerializeField] private BestViewCaptureService bestViewCaptureService;
    [SerializeField] private DevicePassthroughCaptureService devicePassthroughCaptureService;
    [SerializeField] private Transform proxyObjectsRoot;

    [Header("Surface Targets")]
    [SerializeField] private bool includeInnerWallFaces;
    [SerializeField] private bool logApplications = true;
    [SerializeField, Min(0.1f)] private float autoRefreshInterval = 0.75f;

    [Header("Proxy Targets")]
    [SerializeField] private bool applyTableProxies = true;
    [SerializeField, Tooltip("Allows generated/imported storage cabinet proxies to be placed on matching MRUK STORAGE anchors.")]
    private bool applyStorageProxies = true;
    [SerializeField, Tooltip("Allows generated/imported display proxies to be placed on matching MRUK SCREEN anchors when a usable scaffold exists.")]
    private bool applyScreenGeneratedProxies = true;
    [SerializeField, Tooltip("Allows generated/imported seating proxies to be placed on matching MRUK COUCH anchors.")]
    private bool applySeatingGeneratedProxies = true;
    [SerializeField, Tooltip("Allows generated/imported bed proxies to be placed on matching MRUK BED anchors.")]
    private bool applyBedGeneratedProxies = true;
    [SerializeField, Tooltip("Allows generated/imported lamp proxies to be placed on matching MRUK LAMP anchors.")]
    private bool applyLampGeneratedProxies = true;
    [SerializeField, Tooltip("Allows generated/imported plant proxies to be placed on matching MRUK PLANT anchors.")]
    private bool applyPlantGeneratedProxies = true;
    [SerializeField, Tooltip("Allows request-locked generated assets to be placed on MRUK OTHER anchors with generic bounds fitting only.")]
    private bool applyOtherGeneratedProxies = true;
    [SerializeField, Tooltip("For storage/cabinets, fit generated assets to the visible MRUK volume shell renderer when available instead of only MRUK VolumeBounds.")]
    private bool storageFitToVisibleShellBounds = true;
    [SerializeField] private bool hideOriginalVolumeVisuals = true;
    [SerializeField, Min(0.1f)] private float proxyFootprintPadding = 1f;
    [SerializeField, Min(0.1f)] private float proxyHeightPadding = 1f;
    [SerializeField, Tooltip("FloorToAnchorTop fits generated tables from the MRUK floor plane up to the MRUK table top, which is better for full-height generated assets.")]
    private TableProxyVerticalFitMode tableVerticalFitMode = TableProxyVerticalFitMode.FloorToAnchorTop;
    [SerializeField, Range(1f, 1.25f), Tooltip("Horizontal safety expansion applied after the MRUK table footprint so the virtual table slightly covers the real one.")]
    private float tableFootprintSafetyScale = 1.08f;
    [SerializeField, Range(1f, 1.15f), Tooltip("Horizontal safety expansion for generated storage/cabinet assets. Keep close to 1.0 to avoid blocking walkable space.")]
    private float storageFootprintSafetyScale = 1f;
    [SerializeField, Range(0.5f, 2.5f), Tooltip("Optional local X correction for room-specific table width mismatch.")]
    private float tableLocalXScale = 1f;
    [SerializeField, Range(0.5f, 2.5f), Tooltip("Optional local Z correction for room-specific table depth mismatch.")]
    private float tableLocalZScale = 1f;
    [SerializeField, Range(0.5f, 2.5f), Tooltip("Optional local X correction for generated storage/cabinet length mismatch.")]
    private float storageLocalXScale = 1f;
    [SerializeField, Range(0.5f, 2.5f), Tooltip("Optional local Z correction for generated storage/cabinet depth mismatch.")]
    private float storageLocalZScale = 1f;
    [SerializeField, Min(0f), Tooltip("Small clearance above the MRUK floor plane used when fitting full-height generated tables.")]
    private float tableFloorClearanceMeters = 0.005f;
    [SerializeField, Tooltip("Final world-space Y offset for manual room calibration after automatic fitting.")]
    private float tableProxyVerticalOffsetMeters;
    [SerializeField, Range(-180f, 180f)] private float tableProxyYawOffsetDegrees = 90f;
    [SerializeField, Range(-180f, 180f)] private float storageProxyYawOffsetDegrees;
    [SerializeField, Tooltip("For imported generated tables, rotate the visual model before fitting when its local long axis does not match the MRUK table long axis.")]
    private bool autoAlignGeneratedTableLongAxis = true;
    [SerializeField, Tooltip("For generated-object validation, only spawn imported generated furniture on matching captured anchors instead of replacing every MRUK furniture anchor.")]
    private bool onlyReplaceGeneratedTableTarget = true;
    [SerializeField, Tooltip("After generated furniture is successfully placed, keep that anchor bound to the same generated prefab until locks are cleared.")]
    private bool lockPlacedGeneratedTables = true;
    [SerializeField] private bool augmentFlatTableProxies = true;
    [SerializeField, Range(0.1f, 0.6f)] private float flatTableHeightThreshold = 0.35f;

    [Header("Memory")]
    [SerializeField, Tooltip("On style/theme changes, release old generated furniture proxies before loading the next style to reduce texture allocation spikes.")]
    private bool unloadUnusedAssetsBeforeStyleRefresh = true;
    [SerializeField, Tooltip("Clear generated prefab locks on style/theme changes so old style prefab asset references can be unloaded.")]
    private bool clearGeneratedPrefabLocksOnStyleSwitch = true;
#if UNITY_EDITOR
    [SerializeField] private bool preferImportedGeneratedTablePrefabs = true;
    [SerializeField] private bool lockGeneratedTablePrefabsToActiveCapture = true;
    [SerializeField, Tooltip("When no active capture exists, still load the newest generated prefab that matches each table ObjectId. Disable this to require a fresh capture in every Play session.")]
    private bool allowLatestGeneratedTableWhenNoActiveCapture;
    [SerializeField, Tooltip("Allows request-locked generated tables that failed automatic quality review to be placed for visual validation. Some visually acceptable generated assets still land in NeedsReview because the automatic gate is conservative.")]
    private bool allowNeedsReviewGeneratedTablesForValidation;
    [SerializeField, Tooltip("Debug only: allows the newest generated table to appear on the current best-view target even when ObjectId does not match. Keep disabled for multi-table validation.")]
    private bool allowUnmatchedLatestGeneratedTableForDebug;
    [SerializeField] private string generatedObjectJobFolderName = "GeneratedObjectJobs";
#endif

    private const float LongAxisRatioThreshold = 1.1f;

    private enum HorizontalLongAxis
    {
        Balanced,
        X,
        Z,
    }

    private sealed class LockedGeneratedFurniturePrefab
    {
        public string ThemeId;
        public string EntryId;
        public string ObjectId;
        public GameObject Prefab;
        public string PrefabName;
        public string LockedAtIsoUtc;

        public string ShortStatus
        {
            get
            {
                var entry = string.IsNullOrWhiteSpace(EntryId)
                    ? "none"
                    : EntryId.Length <= 18 ? EntryId : EntryId[..18];
                return $"{entry}:{(string.IsNullOrWhiteSpace(PrefabName) ? "prefab" : PrefabName)}";
            }
        }
    }

    public event Action SummaryChanged;

    public string LatestSummary => _latestSummary;
    public int LastAppliedAnchorCount => _lastAppliedAnchorCount;
    public int LastAppliedRendererCount => _lastAppliedRendererCount;
    public int LastAppliedTableProxyCount => _lastAppliedTableProxyCount;
    public string LastTableProxyStatus => _lastTableProxyStatus;
    public int LockedGeneratedTableCount => _lockedGeneratedTablePrefabs.Count;
    public int ForcedDeterministicFallbackCount => _forcedDeterministicFallbackObjectIds.Count;

    private readonly Dictionary<Renderer, Material[]> _originalSharedMaterials = new();
    private readonly Dictionary<Renderer, bool> _originalRendererStates = new();
    private readonly Dictionary<GameObject, bool> _originalVisualObjectStates = new();
    private readonly List<Material> _runtimeMaterials = new();
    private readonly List<Texture2D> _runtimeTextures = new();
    private readonly List<GameObject> _spawnedProxyRoots = new();
    private readonly List<Renderer> _proxyAccentRenderers = new();
    private readonly Dictionary<string, LockedGeneratedFurniturePrefab> _lockedGeneratedTablePrefabs = new(StringComparer.Ordinal);
    private readonly HashSet<string> _forcedDeterministicFallbackObjectIds = new(StringComparer.OrdinalIgnoreCase);

    private string _latestSummary = "[AnchorThemeApplier]\nState: waiting\nHint: enter Play and wait for room + theme.";
    private string _lastTableProxyStatus = "idle";
    private string _lastGeneratedTableSelectionStatus = "idle";
    private int _lastAppliedAnchorCount;
    private int _lastAppliedRendererCount;
    private int _lastAppliedTableProxyCount;
    private bool _needsRefresh = true;
    private float _nextRefreshTime;
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

        if (runtimeStyleIntentController == null)
        {
            runtimeStyleIntentController = FindAnyObjectByType<RuntimeStyleIntentController>();
        }

        if (bestViewCaptureService == null)
        {
            bestViewCaptureService = FindAnyObjectByType<BestViewCaptureService>();
        }

        if (devicePassthroughCaptureService == null)
        {
            devicePassthroughCaptureService = FindAnyObjectByType<DevicePassthroughCaptureService>();
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
        CancelDeferredRefresh();
        ResetAppliedState();
    }

    private void Update()
    {
        if (!Application.isPlaying || !_needsRefresh || _isCleaningBeforeRefresh)
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

    [ContextMenu("Clear Locked Generated Tables")]
    public void ClearLockedGeneratedTables()
    {
        _lockedGeneratedTablePrefabs.Clear();
        _lastGeneratedTableSelectionStatus = "locks_cleared";
        _needsRefresh = true;
        RefreshApplication("clear-generated-table-locks");
    }

    public bool ForceDeterministicFallbackForObject(string objectId, out string detail)
    {
        detail = string.Empty;
        if (string.IsNullOrWhiteSpace(objectId))
        {
            detail = "objectId is empty.";
            return false;
        }

        var normalizedObjectId = objectId.Trim();
        _forcedDeterministicFallbackObjectIds.Add(normalizedObjectId);
        var removedLocks = RemoveGeneratedFurnitureLocksForObject(normalizedObjectId);
        _lastGeneratedTableSelectionStatus = $"forced_deterministic_fallback(object={normalizedObjectId}, removedLocks={removedLocks})";
        _needsRefresh = true;
        RefreshApplication("runtime-reset-fallback");

        var fallbackVisible = HasActiveDeterministicFallbackForObject(normalizedObjectId);
        detail = $"object={normalizedObjectId}, fallbackVisible={fallbackVisible}, forced={_forcedDeterministicFallbackObjectIds.Count}, removedLocks={removedLocks}, proxies={_lastAppliedTableProxyCount}, status={_lastTableProxyStatus}";
        return fallbackVisible;
    }

    public bool HasActiveDeterministicFallbackForObject(string objectId)
    {
        if (string.IsNullOrWhiteSpace(objectId))
        {
            return false;
        }

        var normalizedObjectId = objectId.Trim();
        var instances = FindObjectsByType<StylizedFurnitureInstance>(FindObjectsInactive.Exclude);
        foreach (var instance in instances)
        {
            if (instance == null ||
                !string.Equals(instance.ObjectId, normalizedObjectId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(instance.PrefabSource, "generated_import", StringComparison.Ordinal))
            {
                continue;
            }

            if (instance.gameObject.activeInHierarchy)
            {
                return true;
            }
        }

        return false;
    }

    public bool IsDeterministicFallbackForcedForObject(string objectId)
    {
        return IsDeterministicFallbackForced(objectId);
    }

    public bool ClearDeterministicFallbackForObject(string objectId, out string detail)
    {
        detail = string.Empty;
        if (string.IsNullOrWhiteSpace(objectId))
        {
            detail = "objectId is empty.";
            return false;
        }

        var normalizedObjectId = objectId.Trim();
        var removed = _forcedDeterministicFallbackObjectIds.Remove(normalizedObjectId);
        _lastGeneratedTableSelectionStatus = removed
            ? $"cleared_deterministic_fallback(object={normalizedObjectId})"
            : $"deterministic_fallback_not_forced(object={normalizedObjectId})";
        _needsRefresh = true;
        RefreshApplication("clear-runtime-reset-fallback");
        detail = $"object={normalizedObjectId}, removed={removed}, forced={_forcedDeterministicFallbackObjectIds.Count}, proxies={_lastAppliedTableProxyCount}, status={_lastTableProxyStatus}";
        return removed;
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

        if (runtimeStyleIntentController != null)
        {
            runtimeStyleIntentController.StyleIntentChanged -= HandleStyleIntentChanged;
        }
    }

    private void HandleRoomSummaryChanged()
    {
        _needsRefresh = true;
        RefreshApplication("room-summary");
    }

    private void HandleThemeChanged(ThemeProfile _)
    {
        QueueRefreshAfterMemoryCleanup("theme-changed");
    }

    private void HandlePlanChanged()
    {
        _needsRefresh = true;
        RefreshApplication("plan-changed");
    }

    private void HandleStyleIntentChanged()
    {
        QueueRefreshAfterMemoryCleanup("style-intent-changed");
    }

    private void QueueRefreshAfterMemoryCleanup(string reason)
    {
        _needsRefresh = true;
        ClearGeneratedPrefabLocksForStyleSwitch();

        if (!Application.isPlaying || !unloadUnusedAssetsBeforeStyleRefresh)
        {
            RefreshApplication(reason);
            return;
        }

        if (_deferredRefreshCoroutine != null)
        {
            StopCoroutine(_deferredRefreshCoroutine);
        }

        _deferredRefreshCoroutine = StartCoroutine(RefreshApplicationAfterUnusedAssetUnload(reason));
    }

    private IEnumerator RefreshApplicationAfterUnusedAssetUnload(string reason)
    {
        _isCleaningBeforeRefresh = true;
        _needsRefresh = false;
        ResetAppliedState();
        _latestSummary = $"[AnchorThemeApplier]\nState: {reason}-releasing-old-assets\nHint: releasing old generated furniture proxies before applying the next style.";
        SummaryChanged?.Invoke();

        yield return null;
        yield return Resources.UnloadUnusedAssets();
        GC.Collect();

        _isCleaningBeforeRefresh = false;
        _deferredRefreshCoroutine = null;
        _needsRefresh = true;
        _nextRefreshTime = 0f;
        RefreshApplication($"{reason}-after-release");
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

    private void ClearGeneratedPrefabLocksForStyleSwitch()
    {
        if (!clearGeneratedPrefabLocksOnStyleSwitch || _lockedGeneratedTablePrefabs.Count == 0)
        {
            return;
        }

        var clearedCount = _lockedGeneratedTablePrefabs.Count;
        _lockedGeneratedTablePrefabs.Clear();
        _lastGeneratedTableSelectionStatus = $"locks_cleared_for_style_switch:{clearedCount}";
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

        _lastAppliedTableProxyCount = applyTableProxies ||
                                      applyStorageProxies ||
                                      applyScreenGeneratedProxies ||
                                      applySeatingGeneratedProxies ||
                                      applyBedGeneratedProxies ||
                                      applyLampGeneratedProxies ||
                                      applyPlantGeneratedProxies ||
                                      applyOtherGeneratedProxies
            ? ApplyFurnitureProxies(theme, room, plan)
            : 0;

        var builder = new StringBuilder(256);
        builder.AppendLine("[AnchorThemeApplier]");
        builder.AppendLine(_lastAppliedRendererCount > 0 || _lastAppliedTableProxyCount > 0
            ? "State: applied"
            : "State: waiting-for-targets");
        builder.AppendLine($"Theme: {theme.DisplayName}");
        builder.AppendLine($"Reason: {reason}");
        builder.AppendLine($"Surface Anchors: {_lastAppliedAnchorCount}");
        builder.AppendLine($"Renderers: {_lastAppliedRendererCount}");
        builder.AppendLine($"Furniture Proxies: {_lastAppliedTableProxyCount}");
        builder.AppendLine($"Plan Entries: {plan?.EntryCount ?? 0}");
        builder.AppendLine($"Furniture Status: {_lastTableProxyStatus}");
        builder.Append($"Coverage: floor={floorCount}, wall={wallCount}, ceiling={ceilingCount}");
        _latestSummary = builder.ToString();
        _needsRefresh = _lastAppliedRendererCount == 0;

        SummaryChanged?.Invoke();

        if (logApplications && (_lastAppliedRendererCount > 0 || _lastAppliedTableProxyCount > 0))
        {
            Debug.Log(_latestSummary, this);
        }
    }

    private int ApplyFurnitureProxies(ThemeProfile theme, MRUKRoom room, StylizationPlan plan)
    {
        if (theme == null || room == null || plan == null || proxyObjectsRoot == null)
        {
            _lastTableProxyStatus = $"blocked(theme={theme != null}, room={room != null}, plan={plan != null}, proxyRoot={proxyObjectsRoot != null})";
            return 0;
        }

        var proxyCount = 0;
        var furnitureAnchorCount = 0;
        var matchedPlanCount = 0;
        var resolvedPrefabCount = 0;
        var lastEntryId = "none";
        var lastPrefabName = "none";
        var lastPrefabSource = "none";
        var lastFailure = "none";
        var lastAugmentation = "none";
        var lastFit = "none";
        var lockedAppliedCount = 0;
        var lastSemanticLabel = "none";

        for (var index = 0; index < room.Anchors.Count; index++)
        {
            var anchor = room.Anchors[index];
            if (anchor == null ||
                !anchor.VolumeBounds.HasValue ||
                !TryGetGeneratedFurnitureSemantic(anchor, out var semanticLabel) ||
                !IsGeneratedFurnitureSemanticEnabled(semanticLabel))
            {
                continue;
            }

            furnitureAnchorCount++;
            lastSemanticLabel = semanticLabel;
            var planEntry = FindPlanEntry(plan, theme.ThemeId, semanticLabel, index);
            if (planEntry == null)
            {
                lastFailure = $"missing_plan_{index}";
                continue;
            }

            matchedPlanCount++;
            lastEntryId = planEntry.EntryId;
            var forceDeterministicFallback = IsDeterministicFallbackForced(planEntry.ObjectId);
            var prefabSource = "none";
            var proxyPrefab = forceDeterministicFallback
                ? null
                : ResolveLockedGeneratedFurniturePrefab(theme, planEntry, out prefabSource);
            var usedLockedGeneratedPrefab = proxyPrefab != null;
            if (!usedLockedGeneratedPrefab)
            {
                proxyPrefab = forceDeterministicFallback
                    ? ResolveDeterministicFallbackProxyPrefab(theme, planEntry, out prefabSource)
                    : ResolveProxyPrefab(theme, planEntry, out prefabSource);
            }

            var usingGeneratedPrefab = string.Equals(prefabSource, "generated_import", StringComparison.Ordinal);
            if (planEntry.ReplacementMode != ReplacementMode.ProxyPrefab && !usingGeneratedPrefab)
            {
                lastFailure = $"mode_{planEntry.ReplacementMode}";
                continue;
            }

            if (onlyReplaceGeneratedTableTarget && !usingGeneratedPrefab && !forceDeterministicFallback)
            {
                lastFailure = $"not_generated_target_{planEntry.EntryId}";
                continue;
            }

            if (proxyPrefab == null)
            {
                lastFailure = $"missing_prefab_{planEntry.EntryId}";
                continue;
            }

            resolvedPrefabCount++;
            lastPrefabName = proxyPrefab.name;
            lastPrefabSource = prefabSource;
            if (TrySpawnFurnitureProxy(anchor, room, proxyPrefab, prefabSource, planEntry, theme, semanticLabel, out var augmentationStatus, out var fitStatus))
            {
                proxyCount++;
                if (usedLockedGeneratedPrefab)
                {
                    lockedAppliedCount++;
                }

                RegisterLockedGeneratedFurniturePrefab(theme, planEntry, proxyPrefab, prefabSource);
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
            $"anchors={furnitureAnchorCount}, plans={matchedPlanCount}, prefabs={resolvedPrefabCount}, spawned={proxyCount}, locks={_lockedGeneratedTablePrefabs.Count}, lockedApplied={lockedAppliedCount}, semantic={lastSemanticLabel}, entry={lastEntryId}, prefab={lastPrefabName}, source={lastPrefabSource}, generated={_lastGeneratedTableSelectionStatus}, augment={lastAugmentation}, fit={lastFit}, failure={lastFailure}";
        return proxyCount;
    }

    private GameObject ResolveLockedGeneratedFurniturePrefab(
        ThemeProfile theme,
        StylizationPlanEntry planEntry,
        out string prefabSource)
    {
        prefabSource = "none";
        if (!lockPlacedGeneratedTables || theme == null || planEntry == null)
        {
            return null;
        }

        var effectiveThemeId = GetEffectiveThemeId(theme);
        var lockKey = GetGeneratedFurnitureLockKey(effectiveThemeId, planEntry.EntryId);
        if (!_lockedGeneratedTablePrefabs.TryGetValue(lockKey, out var lockedPrefab) ||
            lockedPrefab == null ||
            lockedPrefab.Prefab == null)
        {
            return null;
        }

        prefabSource = "generated_import";
        _lastGeneratedTableSelectionStatus = $"locked_anchor({lockedPrefab.ShortStatus})";
        return lockedPrefab.Prefab;
    }

    private void RegisterLockedGeneratedFurniturePrefab(
        ThemeProfile theme,
        StylizationPlanEntry planEntry,
        GameObject proxyPrefab,
        string prefabSource)
    {
        if (!lockPlacedGeneratedTables ||
            theme == null ||
            planEntry == null ||
            proxyPrefab == null ||
            !string.Equals(prefabSource, "generated_import", StringComparison.Ordinal))
        {
            return;
        }

        var effectiveThemeId = GetEffectiveThemeId(theme);
        var lockKey = GetGeneratedFurnitureLockKey(effectiveThemeId, planEntry.EntryId);
        _lockedGeneratedTablePrefabs[lockKey] = new LockedGeneratedFurniturePrefab
        {
            ThemeId = effectiveThemeId,
            EntryId = planEntry.EntryId,
            ObjectId = planEntry.ObjectId,
            Prefab = proxyPrefab,
            PrefabName = proxyPrefab.name,
            LockedAtIsoUtc = DateTime.UtcNow.ToString("O"),
        };
    }

    private int RemoveGeneratedFurnitureLocksForObject(string objectId)
    {
        if (string.IsNullOrWhiteSpace(objectId) || _lockedGeneratedTablePrefabs.Count == 0)
        {
            return 0;
        }

        var keysToRemove = new List<string>();
        foreach (var pair in _lockedGeneratedTablePrefabs)
        {
            if (pair.Value != null &&
                string.Equals(pair.Value.ObjectId, objectId, StringComparison.OrdinalIgnoreCase))
            {
                keysToRemove.Add(pair.Key);
            }
        }

        foreach (var key in keysToRemove)
        {
            _lockedGeneratedTablePrefabs.Remove(key);
        }

        return keysToRemove.Count;
    }

    private bool IsDeterministicFallbackForced(string objectId)
    {
        return !string.IsNullOrWhiteSpace(objectId) &&
               _forcedDeterministicFallbackObjectIds.Contains(objectId.Trim());
    }

    private static string GetGeneratedFurnitureLockKey(string themeId, string entryId)
    {
        return $"{themeId ?? string.Empty}:{entryId ?? string.Empty}";
    }

    private string GetEffectiveThemeId(ThemeProfile theme)
    {
        return RuntimeStyleIntentRequestUtility.BuildEffectiveThemeId(
            theme,
            runtimeStyleIntentController != null ? runtimeStyleIntentController.CurrentIntent : null);
    }

    private bool TrySpawnFurnitureProxy(
        MRUKAnchor anchor,
        MRUKRoom room,
        GameObject proxyPrefab,
        string prefabSource,
        StylizationPlanEntry planEntry,
        ThemeProfile theme,
        string semanticLabel,
        out string augmentationStatus,
        out string fitStatus)
    {
        augmentationStatus = "none";
        fitStatus = "none";

        if (proxyObjectsRoot == null || proxyPrefab == null || !anchor.VolumeBounds.HasValue)
        {
            return false;
        }

        var proxyRoot = new GameObject($"{ToTitleCase(semanticLabel)}Proxy_{planEntry.EntryId}");
        proxyRoot.transform.SetParent(proxyObjectsRoot, false);
        proxyRoot.AddComponent<StylizedFurnitureInstance>().Initialize(
            planEntry.EntryId,
            planEntry.ObjectId,
            semanticLabel,
            prefabSource,
            proxyPrefab.name);

        var volumeBounds = anchor.VolumeBounds.Value;
        proxyRoot.transform.position = anchor.transform.TransformPoint(volumeBounds.center);

        proxyRoot.transform.rotation = GetFurnitureProxyRotation(
            anchor,
            planEntry.PreserveYawOrientation,
            GetFurnitureYawOffsetDegrees(semanticLabel));

        var proxyInstance = Instantiate(proxyPrefab, proxyRoot.transform);
        proxyInstance.name = $"{proxyPrefab.name}_{planEntry.EntryId}";
        proxyInstance.transform.localPosition = Vector3.zero;
        proxyInstance.transform.localRotation = Quaternion.identity;
        proxyInstance.transform.localScale = Vector3.one;

        var targetBoundsSource = "volume_bounds";
        var rawTargetBounds = default(Bounds);
        var hasShellTargetBounds = IsStorageSemantic(semanticLabel) &&
                                   storageFitToVisibleShellBounds &&
                                   TryCalculateAnchorShellRendererTargetBounds(proxyRoot.transform, anchor, out rawTargetBounds);
        if (hasShellTargetBounds)
        {
            targetBoundsSource = "visible_shell_bounds";
        }
        else if (!TryCalculateAnchorTargetBounds(proxyRoot.transform, anchor.transform, volumeBounds, out rawTargetBounds))
        {
            SafeDestroy(proxyRoot);
            return false;
        }

        if (!TryAdjustFurnitureTargetBoundsForVerticalFit(proxyRoot.transform, room, rawTargetBounds, semanticLabel, out var targetBounds, out var verticalFitStatus))
        {
            SafeDestroy(proxyRoot);
            return false;
        }

        var axisAlignmentStatus = AutoAlignGeneratedFurnitureLongAxis(proxyRoot.transform, proxyInstance.transform, targetBounds, prefabSource, semanticLabel);

        if (!FitProxyToFurnitureAnchor(proxyRoot.transform, proxyInstance.transform, targetBounds, semanticLabel, out var fittedBounds, out var targetSize, out var sourceSize, out var appliedScale))
        {
            SafeDestroy(proxyRoot);
            return false;
        }

        if (Mathf.Abs(tableProxyVerticalOffsetMeters) > 0.0001f)
        {
            proxyRoot.transform.position += Vector3.up * tableProxyVerticalOffsetMeters;
        }

        var bottomDelta = fittedBounds.min.y - targetBounds.min.y;
        fitStatus = $"target={FormatSize(targetSize)}, source={FormatSize(sourceSize)}, scale={FormatSize(appliedScale)}, bottomDelta={FormatMeters(bottomDelta)}, sourceBounds={targetBoundsSource}, vertical={verticalFitStatus}, axis={axisAlignmentStatus}, safety={GetFurnitureFootprintSafetyScale(semanticLabel):0.###}, offsetY={FormatMeters(tableProxyVerticalOffsetMeters)}";

        ApplyProxyAccent(proxyInstance, theme);
        augmentationStatus = IsTableSemantic(semanticLabel)
            ? AugmentFlatTableProxy(proxyRoot.transform, proxyInstance.transform, fittedBounds, targetSize, theme)
            : "not_table";

        if (hideOriginalVolumeVisuals)
        {
            SuppressAnchorRenderers(anchor);
        }

        _spawnedProxyRoots.Add(proxyRoot);
        return true;
    }

    private string AutoAlignGeneratedFurnitureLongAxis(
        Transform proxyRoot,
        Transform proxyInstance,
        Bounds targetBounds,
        string prefabSource,
        string semanticLabel)
    {
        if (IsOtherSemantic(semanticLabel))
        {
            return "generic_no_axis_search";
        }

        if (!autoAlignGeneratedTableLongAxis)
        {
            return "auto_disabled";
        }

        if (!string.Equals(prefabSource, "generated_import", StringComparison.Ordinal))
        {
            return "theme_prefab";
        }

        if (proxyRoot == null || proxyInstance == null)
        {
            return "missing_transform";
        }

        if (!TryCalculateLocalBounds(proxyRoot, out var sourceBounds))
        {
            return "missing_source_bounds";
        }

        var sourceAxis = GetHorizontalLongAxis(sourceBounds.size);
        var targetAxis = GetHorizontalLongAxis(targetBounds.size);
        if (sourceAxis == HorizontalLongAxis.Balanced || targetAxis == HorizontalLongAxis.Balanced)
        {
            return $"balanced(source={FormatSize(sourceBounds.size)}, target={FormatSize(targetBounds.size)})";
        }

        if (sourceAxis == targetAxis)
        {
            return $"aligned({sourceAxis})";
        }

        proxyInstance.localRotation = Quaternion.Euler(0f, 90f, 0f) * proxyInstance.localRotation;
        return $"rotated90(source={sourceAxis}, target={targetAxis})";
    }

    private static HorizontalLongAxis GetHorizontalLongAxis(Vector3 size)
    {
        if (size.x > size.z * LongAxisRatioThreshold)
        {
            return HorizontalLongAxis.X;
        }

        if (size.z > size.x * LongAxisRatioThreshold)
        {
            return HorizontalLongAxis.Z;
        }

        return HorizontalLongAxis.Balanced;
    }

    private bool FitProxyToFurnitureAnchor(
        Transform proxyRoot,
        Transform proxyInstance,
        Bounds targetBounds,
        string semanticLabel,
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
        var footprintScale = Mathf.Max(0.01f, proxyFootprintPadding * GetFurnitureFootprintSafetyScale(semanticLabel));
        targetSize = new Vector3(
            Mathf.Max(targetBounds.size.x * footprintScale * GetFurnitureLocalXScale(semanticLabel), 0.05f),
            Mathf.Max(targetBounds.size.y * proxyHeightPadding, 0.05f),
            Mathf.Max(targetBounds.size.z * footprintScale * GetFurnitureLocalZScale(semanticLabel), 0.05f));

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

        var fallbackFootprintScale = Mathf.Min(xScale, zScale);
        var yScale = sourceSize.y > 0.001f ? targetSize.y / sourceSize.y : fallbackFootprintScale;
        if (!IsUsableScale(yScale))
        {
            yScale = fallbackFootprintScale;
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

    private static bool TryCalculateAnchorShellRendererTargetBounds(
        Transform proxyRoot,
        MRUKAnchor anchor,
        out Bounds targetBounds)
    {
        targetBounds = default;
        if (proxyRoot == null || anchor == null)
        {
            return false;
        }

        var renderers = anchor.GetComponentsInChildren<Renderer>(true);
        var hasBounds = false;
        for (var index = 0; index < renderers.Length; index++)
        {
            var renderer = renderers[index];
            if (!ShouldUseRendererForShellTargetBounds(renderer))
            {
                continue;
            }

            var localBounds = renderer.localBounds;
            var min = localBounds.min;
            var max = localBounds.max;
            for (var x = 0; x <= 1; x++)
            {
                for (var y = 0; y <= 1; y++)
                {
                    for (var z = 0; z <= 1; z++)
                    {
                        var rendererLocalPoint = new Vector3(
                            x == 0 ? min.x : max.x,
                            y == 0 ? min.y : max.y,
                            z == 0 ? min.z : max.z);
                        var worldPoint = renderer.transform.TransformPoint(rendererLocalPoint);
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
        }

        return hasBounds;
    }

    private static bool ShouldUseRendererForShellTargetBounds(Renderer renderer)
    {
        if (renderer == null || renderer is ParticleSystemRenderer)
        {
            return false;
        }

        var current = renderer.transform;
        while (current != null)
        {
            if (current.name.StartsWith("Volume(", StringComparison.Ordinal))
            {
                return true;
            }

            current = current.parent;
        }

        return false;
    }

    private bool TryAdjustFurnitureTargetBoundsForVerticalFit(
        Transform proxyRoot,
        MRUKRoom room,
        Bounds sourceTargetBounds,
        string semanticLabel,
        out Bounds adjustedTargetBounds,
        out string fitStatus)
    {
        adjustedTargetBounds = sourceTargetBounds;
        fitStatus = tableVerticalFitMode.ToString();

        if (proxyRoot == null)
        {
            fitStatus = "missing_proxy_root";
            return false;
        }

        if (!IsTableSemantic(semanticLabel))
        {
            fitStatus = $"AnchorBoundsBottom({semanticLabel})";
            return true;
        }

        if (tableVerticalFitMode == TableProxyVerticalFitMode.AnchorBoundsBottom)
        {
            return true;
        }

        if (tableVerticalFitMode == TableProxyVerticalFitMode.ManualOffsetOnly)
        {
            fitStatus = "ManualOffsetOnly";
            return true;
        }

        if (!TryGetFloorWorldY(room, out var floorWorldY))
        {
            fitStatus = "floor_missing_fallback_anchor_bounds";
            return true;
        }

        var targetTopWorldY = proxyRoot.TransformPoint(new Vector3(
            sourceTargetBounds.center.x,
            sourceTargetBounds.max.y,
            sourceTargetBounds.center.z)).y;
        var targetBottomWorldY = floorWorldY + tableFloorClearanceMeters;
        if (targetTopWorldY <= targetBottomWorldY + 0.05f)
        {
            fitStatus = "floor_to_top_invalid_fallback_anchor_bounds";
            return true;
        }

        var localBottomY = proxyRoot.InverseTransformPoint(new Vector3(
            proxyRoot.position.x,
            targetBottomWorldY,
            proxyRoot.position.z)).y;
        var localTopY = proxyRoot.InverseTransformPoint(new Vector3(
            proxyRoot.position.x,
            targetTopWorldY,
            proxyRoot.position.z)).y;

        var min = adjustedTargetBounds.min;
        var max = adjustedTargetBounds.max;
        min.y = Mathf.Min(localBottomY, localTopY);
        max.y = Mathf.Max(localBottomY, localTopY);
        adjustedTargetBounds.SetMinMax(min, max);
        fitStatus = $"FloorToAnchorTop(floor={FormatMeters(floorWorldY)}, clear={FormatMeters(tableFloorClearanceMeters)})";
        return true;
    }

    private static bool TryGetFloorWorldY(MRUKRoom room, out float floorWorldY)
    {
        floorWorldY = default;
        if (room == null || room.Anchors == null)
        {
            return false;
        }

        var sampleCount = 0;
        var yTotal = 0f;
        for (var index = 0; index < room.Anchors.Count; index++)
        {
            var anchor = room.Anchors[index];
            if (anchor == null || !anchor.HasAnyLabel(MRUKAnchor.SceneLabels.FLOOR))
            {
                continue;
            }

            if (anchor.PlaneRect.HasValue)
            {
                var rect = anchor.PlaneRect.Value;
                yTotal += anchor.transform.TransformPoint(new Vector3(rect.xMin, rect.yMin, 0f)).y;
                yTotal += anchor.transform.TransformPoint(new Vector3(rect.xMax, rect.yMin, 0f)).y;
                yTotal += anchor.transform.TransformPoint(new Vector3(rect.xMax, rect.yMax, 0f)).y;
                yTotal += anchor.transform.TransformPoint(new Vector3(rect.xMin, rect.yMax, 0f)).y;
                sampleCount += 4;
                continue;
            }

            yTotal += anchor.transform.position.y;
            sampleCount++;
        }

        if (sampleCount == 0)
        {
            return false;
        }

        floorWorldY = yTotal / sampleCount;
        return true;
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

    private static Quaternion GetFurnitureProxyRotation(
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
            for (var index = 0; index < sourceMaterials.Length; index++)
            {
                var sourceMaterial = sourceMaterials[index];
                var block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block, index);
                ApplyProxyAccentBlock(block, sourceMaterial, accentTint, accentEmission);
                renderer.SetPropertyBlock(block, index);
            }

            if (!_proxyAccentRenderers.Contains(renderer))
            {
                _proxyAccentRenderers.Add(renderer);
            }
        }
    }

    private static void ApplyProxyAccentBlock(
        MaterialPropertyBlock block,
        Material sourceMaterial,
        Color accentTint,
        Color accentEmission)
    {
        if (block == null)
        {
            return;
        }

        var baseColor = Color.white;
        if (sourceMaterial != null)
        {
            TryGetMaterialColor(sourceMaterial, out baseColor);
        }

        var tintedColor = Color.Lerp(baseColor, accentTint, 0.28f);
        tintedColor.a = baseColor.a > 0.001f ? baseColor.a : 1f;
        block.SetColor("_BaseColor", tintedColor);
        block.SetColor("_Color", tintedColor);
        block.SetColor("_EmissionColor", accentEmission);
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

        if (anchor.HasAnyLabel(MRUKAnchor.SceneLabels.SCREEN))
        {
            return "SCREEN";
        }

        if (anchor.HasAnyLabel(MRUKAnchor.SceneLabels.STORAGE))
        {
            return "STORAGE";
        }

        if (anchor.HasAnyLabel(MRUKAnchor.SceneLabels.COUCH))
        {
            return "COUCH";
        }

        if (anchor.HasAnyLabel(MRUKAnchor.SceneLabels.BED))
        {
            return "BED";
        }

        if (anchor.HasAnyLabel(MRUKAnchor.SceneLabels.LAMP))
        {
            return "LAMP";
        }

        if (anchor.HasAnyLabel(MRUKAnchor.SceneLabels.PLANT))
        {
            return "PLANT";
        }

        if (anchor.HasAnyLabel(MRUKAnchor.SceneLabels.OTHER))
        {
            return "OTHER";
        }

        return null;
    }

    private static bool TryGetGeneratedFurnitureSemantic(MRUKAnchor anchor, out string semanticLabel)
    {
        semanticLabel = string.Empty;
        if (anchor == null)
        {
            return false;
        }

        if (anchor.HasAnyLabel(MRUKAnchor.SceneLabels.TABLE))
        {
            semanticLabel = "table";
            return true;
        }

        if (anchor.HasAnyLabel(MRUKAnchor.SceneLabels.SCREEN))
        {
            semanticLabel = "screen";
            return true;
        }

        if (anchor.HasAnyLabel(MRUKAnchor.SceneLabels.STORAGE))
        {
            semanticLabel = "storage";
            return true;
        }

        if (anchor.HasAnyLabel(MRUKAnchor.SceneLabels.COUCH))
        {
            semanticLabel = "seating";
            return true;
        }

        if (anchor.HasAnyLabel(MRUKAnchor.SceneLabels.BED))
        {
            semanticLabel = "bed";
            return true;
        }

        if (anchor.HasAnyLabel(MRUKAnchor.SceneLabels.LAMP))
        {
            semanticLabel = "lamp";
            return true;
        }

        if (anchor.HasAnyLabel(MRUKAnchor.SceneLabels.PLANT))
        {
            semanticLabel = "plant";
            return true;
        }

        if (anchor.HasAnyLabel(MRUKAnchor.SceneLabels.OTHER))
        {
            semanticLabel = "other";
            return true;
        }

        return false;
    }

    private bool IsGeneratedFurnitureSemanticEnabled(string semanticLabel)
    {
        if (IsTableSemantic(semanticLabel))
        {
            return applyTableProxies;
        }

        if (IsStorageSemantic(semanticLabel))
        {
            return applyStorageProxies;
        }

        if (IsScreenSemantic(semanticLabel))
        {
            return applyScreenGeneratedProxies;
        }

        if (IsSeatingSemantic(semanticLabel))
        {
            return applySeatingGeneratedProxies;
        }

        if (IsBedSemantic(semanticLabel))
        {
            return applyBedGeneratedProxies;
        }

        if (IsLampSemantic(semanticLabel))
        {
            return applyLampGeneratedProxies;
        }

        if (IsPlantSemantic(semanticLabel))
        {
            return applyPlantGeneratedProxies;
        }

        return IsOtherSemantic(semanticLabel) && applyOtherGeneratedProxies;
    }

    private static bool IsGeneratedFurnitureEntry(StylizationPlanEntry planEntry)
    {
        return planEntry != null &&
               (IsTableSemantic(planEntry.OriginalSemanticLabel) ||
                IsScreenSemantic(planEntry.OriginalSemanticLabel) ||
                IsStorageSemantic(planEntry.OriginalSemanticLabel) ||
                IsSeatingSemantic(planEntry.OriginalSemanticLabel) ||
                IsBedSemantic(planEntry.OriginalSemanticLabel) ||
                IsLampSemantic(planEntry.OriginalSemanticLabel) ||
                IsPlantSemantic(planEntry.OriginalSemanticLabel) ||
                IsOtherSemantic(planEntry.OriginalSemanticLabel));
    }

    private static bool IsTableSemantic(string semanticLabel)
    {
        return string.Equals(semanticLabel, "table", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsStorageSemantic(string semanticLabel)
    {
        return string.Equals(semanticLabel, "storage", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsScreenSemantic(string semanticLabel)
    {
        return string.Equals(semanticLabel, "screen", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSeatingSemantic(string semanticLabel)
    {
        return string.Equals(semanticLabel, "seating", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBedSemantic(string semanticLabel)
    {
        return string.Equals(semanticLabel, "bed", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLampSemantic(string semanticLabel)
    {
        return string.Equals(semanticLabel, "lamp", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPlantSemantic(string semanticLabel)
    {
        return string.Equals(semanticLabel, "plant", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOtherSemantic(string semanticLabel)
    {
        return string.Equals(semanticLabel, "other", StringComparison.OrdinalIgnoreCase);
    }

    private float GetFurnitureFootprintSafetyScale(string semanticLabel)
    {
        return IsTableSemantic(semanticLabel) ? tableFootprintSafetyScale : storageFootprintSafetyScale;
    }

    private float GetFurnitureYawOffsetDegrees(string semanticLabel)
    {
        return IsTableSemantic(semanticLabel) ? tableProxyYawOffsetDegrees : storageProxyYawOffsetDegrees;
    }

    private float GetFurnitureLocalXScale(string semanticLabel)
    {
        return IsTableSemantic(semanticLabel) ? tableLocalXScale : storageLocalXScale;
    }

    private float GetFurnitureLocalZScale(string semanticLabel)
    {
        return IsTableSemantic(semanticLabel) ? tableLocalZScale : storageLocalZScale;
    }

    private static string ToTitleCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Object";
        }

        var normalized = value.Trim();
        return char.ToUpperInvariant(normalized[0]) + (normalized.Length > 1 ? normalized[1..] : string.Empty);
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
            IsGeneratedFurnitureEntry(planEntry) &&
            TryLoadImportedGeneratedFurniturePrefab(theme, planEntry, out var importedGeneratedPrefab))
        {
            prefabSource = "generated_import";
            return importedGeneratedPrefab;
        }
#endif

        return ResolveDeterministicFallbackProxyPrefab(theme, planEntry, out prefabSource);
    }

    private GameObject ResolveDeterministicFallbackProxyPrefab(ThemeProfile theme, StylizationPlanEntry planEntry, out string prefabSource)
    {
        prefabSource = "none";
        if (theme == null || planEntry == null)
        {
            return null;
        }

        var matchedRule = FindRule(theme, planEntry.OriginalSemanticLabel, planEntry.OriginalFunctionTag);
        if (matchedRule?.ProxyPrefab != null)
        {
            prefabSource = "theme_rule";
            return matchedRule.ProxyPrefab;
        }

        prefabSource = "theme_default";
        return theme.GetDefaultProxy(planEntry.OriginalSemanticLabel);
    }

#if UNITY_EDITOR
    private bool TryLoadImportedGeneratedFurniturePrefab(
        ThemeProfile theme,
        StylizationPlanEntry planEntry,
        out GameObject prefab)
    {
        prefab = null;
        var jobDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Library", generatedObjectJobFolderName));
        if (!Directory.Exists(jobDirectory))
        {
            _lastGeneratedTableSelectionStatus = "no_job_directory";
            return false;
        }

        GeneratedObjectRequest activeRequest = null;
        var activeRequestSource = "none";
        var hasActiveCaptureRequest = lockGeneratedTablePrefabsToActiveCapture &&
                                      TryGetActiveGeneratedFurnitureRequest(out activeRequest, out activeRequestSource);
        if (lockGeneratedTablePrefabsToActiveCapture &&
            !hasActiveCaptureRequest &&
            !allowLatestGeneratedTableWhenNoActiveCapture)
        {
            _lastGeneratedTableSelectionStatus = "locked(no_active_capture)";
            return false;
        }

        var allowUnmatchedDebugFallback = !hasActiveCaptureRequest &&
                                          allowUnmatchedLatestGeneratedTableForDebug &&
                                          IsCurrentBestGeneratedFurnitureTarget(planEntry);
        _lastGeneratedTableSelectionStatus = hasActiveCaptureRequest
            ? $"locked({activeRequestSource}:{ShortId(activeRequest.RequestId)})"
            : allowUnmatchedDebugFallback ? "debug_unmatched_latest_best_target" : "per_object_fallback";

        GeneratedAssetRecord bestRecord = null;
        DateTime bestUpdatedAt = DateTime.MinValue;
        var effectiveThemeId = GetEffectiveThemeId(theme);
        var effectiveStyleVariantId = GetActiveStyleVariantId();
        var currentRoomId = roomSemanticBootstrap != null && roomSemanticBootstrap.CurrentRoom != null
            ? roomSemanticBootstrap.CurrentRoom.name
            : "unknown_room";
        foreach (var jobPath in Directory.GetFiles(jobDirectory, "*.job.json", SearchOption.TopDirectoryOnly))
        {
            var record = TryReadGeneratedAssetRecord(jobPath);
            if (record == null ||
                !IsUsableGeneratedTableRecord(
                    record,
                    theme,
                    effectiveThemeId,
                    effectiveStyleVariantId,
                    currentRoomId,
                    planEntry,
                    activeRequest,
                    hasActiveCaptureRequest,
                    allowNeedsReviewGeneratedTablesForValidation,
                    allowUnmatchedDebugFallback))
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
            _lastGeneratedTableSelectionStatus += ", match=none";
            return false;
        }

        prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabAssetPath);
        _lastGeneratedTableSelectionStatus += prefab != null
            ? $", match={ShortId(bestRecord.RequestId)}, object={bestRecord.ObjectId}, state={bestRecord.State}"
            : $", load_failed={ShortId(bestRecord.RequestId)}";
        return prefab != null;
    }

    private bool IsCurrentBestGeneratedFurnitureTarget(StylizationPlanEntry planEntry)
    {
        if (planEntry == null || devicePassthroughCaptureService == null || !devicePassthroughCaptureService.HasBestCandidate)
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(planEntry.ObjectId) &&
               string.Equals(
                   planEntry.ObjectId,
                   devicePassthroughCaptureService.BestAnchorObjectId,
                   StringComparison.OrdinalIgnoreCase);
    }

    private bool TryGetActiveGeneratedFurnitureRequest(out GeneratedObjectRequest request, out string source)
    {
        request = null;
        source = "none";

        var deviceRequest = devicePassthroughCaptureService != null
            ? devicePassthroughCaptureService.LastGeneratedRequest
            : null;
        if (IsActiveGeneratedFurnitureRequest(deviceRequest))
        {
            request = deviceRequest;
            source = "device";
            return true;
        }

        var simulatorRequest = bestViewCaptureService != null
            ? bestViewCaptureService.LastGeneratedRequest
            : null;
        if (IsActiveGeneratedFurnitureRequest(simulatorRequest))
        {
            request = simulatorRequest;
            source = "sim";
            return true;
        }

        return false;
    }

    private static bool IsActiveGeneratedFurnitureRequest(GeneratedObjectRequest request)
    {
        return request != null &&
               !string.IsNullOrWhiteSpace(request.RequestId) &&
               (string.Equals(request.SemanticLabel, "table", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(request.SemanticLabel, "screen", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(request.SemanticLabel, "storage", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(request.SemanticLabel, "seating", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(request.SemanticLabel, "bed", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(request.SemanticLabel, "lamp", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(request.SemanticLabel, "plant", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(request.SemanticLabel, "other", StringComparison.OrdinalIgnoreCase));
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
        string effectiveThemeId,
        string effectiveStyleVariantId,
        string currentRoomId,
        StylizationPlanEntry planEntry,
        GeneratedObjectRequest activeRequest,
        bool hasActiveCaptureRequest,
        bool allowNeedsReviewForValidation,
        bool allowUnmatchedDebugFallback)
    {
        if (record == null || theme == null || planEntry == null)
        {
            return false;
        }

        if (record.State != GeneratedObjectJobState.Imported &&
            !(allowNeedsReviewForValidation && record.State == GeneratedObjectJobState.NeedsReview))
        {
            return false;
        }

        if (!string.Equals(record.ThemeId, effectiveThemeId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.Equals(
                NormalizeStyleVariant(record.StyleVariantId),
                NormalizeStyleVariant(effectiveStyleVariantId),
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(record.ImportedPrefabPath) || !File.Exists(record.ImportedPrefabPath))
        {
            return false;
        }

        if (!DoesRecordMatchCurrentRoom(record, currentRoomId))
        {
            return false;
        }

        if (hasActiveCaptureRequest && DoesPlanEntryMatchActiveRequest(planEntry, activeRequest))
        {
            return DoesRecordMatchActiveRequest(record, activeRequest);
        }

        if (allowUnmatchedDebugFallback)
        {
            return true;
        }

        return DoesRecordMatchPlanEntry(record, planEntry);
    }

    private static bool DoesPlanEntryMatchActiveRequest(StylizationPlanEntry planEntry, GeneratedObjectRequest activeRequest)
    {
        return planEntry != null &&
               activeRequest != null &&
               !string.IsNullOrWhiteSpace(planEntry.ObjectId) &&
               !string.IsNullOrWhiteSpace(activeRequest.ObjectId) &&
               string.Equals(planEntry.ObjectId, activeRequest.ObjectId, StringComparison.OrdinalIgnoreCase);
    }

    private static bool DoesRecordMatchPlanEntry(GeneratedAssetRecord record, StylizationPlanEntry planEntry)
    {
        return record != null &&
               planEntry != null &&
               !string.IsNullOrWhiteSpace(record.ObjectId) &&
               !string.IsNullOrWhiteSpace(planEntry.ObjectId) &&
               string.Equals(record.ObjectId, planEntry.ObjectId, StringComparison.OrdinalIgnoreCase);
    }

    private static bool DoesRecordMatchActiveRequest(GeneratedAssetRecord record, GeneratedObjectRequest activeRequest)
    {
        return record != null &&
               activeRequest != null &&
               !string.IsNullOrWhiteSpace(record.RequestId) &&
               !string.IsNullOrWhiteSpace(activeRequest.RequestId) &&
               string.Equals(record.RequestId, activeRequest.RequestId, StringComparison.OrdinalIgnoreCase) &&
               !string.IsNullOrWhiteSpace(record.ObjectId) &&
               !string.IsNullOrWhiteSpace(activeRequest.ObjectId) &&
               string.Equals(record.ObjectId, activeRequest.ObjectId, StringComparison.OrdinalIgnoreCase);
    }

    private static bool DoesRecordMatchCurrentRoom(GeneratedAssetRecord record, string currentRoomId)
    {
        if (string.IsNullOrWhiteSpace(currentRoomId) ||
            string.Equals(currentRoomId, "unknown_room", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (record == null ||
            string.IsNullOrWhiteSpace(record.SourceRequestPath) ||
            !File.Exists(record.SourceRequestPath))
        {
            return false;
        }

        try
        {
            var json = File.ReadAllText(record.SourceRequestPath);
            var request = string.IsNullOrWhiteSpace(json)
                ? null
                : JsonUtility.FromJson<GeneratedObjectRequest>(json);
            return request != null &&
                   !string.IsNullOrWhiteSpace(request.RoomId) &&
                   !string.Equals(request.RoomId, "unknown_room", StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(request.RoomId, currentRoomId, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                $"[AnchorThemeApplier] Failed to verify generated-object room context '{record.SourceRequestPath}': {exception.Message}");
            return false;
        }
    }

    private static string ShortId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "none";
        }

        return value.Length <= 18 ? value : value[..18];
    }

    private string GetActiveStyleVariantId()
    {
        return SurfaceTexturePromptBuilder.BuildStyleVariantId(
            runtimeStyleIntentController != null ? runtimeStyleIntentController.CurrentIntent : null);
    }

    private static string NormalizeStyleVariant(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? SurfaceTexturePromptBuilder.PresetStyleVariantId : value.Trim();
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

            var localBounds = renderer.localBounds;
            var min = localBounds.min;
            var max = localBounds.max;
            for (var x = 0; x <= 1; x++)
            {
                for (var y = 0; y <= 1; y++)
                {
                    for (var z = 0; z <= 1; z++)
                    {
                        var rendererLocalPoint = new Vector3(
                            x == 0 ? min.x : max.x,
                            y == 0 ? min.y : max.y,
                            z == 0 ? min.z : max.z);
                        var worldPoint = renderer.transform.TransformPoint(rendererLocalPoint);
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
        ResetProxyAccentBlocks();

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

    private void ResetProxyAccentBlocks()
    {
        foreach (var renderer in _proxyAccentRenderers)
        {
            if (renderer == null)
            {
                continue;
            }

            renderer.SetPropertyBlock(null);
        }

        _proxyAccentRenderers.Clear();
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

public enum TableProxyVerticalFitMode
{
    AnchorBoundsBottom,
    FloorToAnchorTop,
    ManualOffsetOnly,
}
