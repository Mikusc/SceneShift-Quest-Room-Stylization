using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

[DisallowMultipleComponent]
public class RoomStyleCacheService : MonoBehaviour
{
    private const int ExpectedSurfaceKinds = 6;

    [Header("References")]
    [SerializeField] private RoomSemanticBootstrap roomSemanticBootstrap;
    [SerializeField] private ThemeIntentController themeIntentController;
    [SerializeField] private RuntimeStyleIntentController runtimeStyleIntentController;

    [Header("Folders")]
    [SerializeField] private string generatedObjectJobFolderName = "GeneratedObjectJobs";
    [SerializeField] private string surfaceTextureJobFolderName = "SurfaceTextureJobs";

    [Header("Refresh")]
    [SerializeField, Min(0.25f)] private float refreshIntervalSeconds = 1f;
    [SerializeField] private bool filterFurnitureByRoomId = true;

    public event Action SummaryChanged;

    public string LatestSummary => _latestSummary;
    public string CurrentRoomId => ResolveCurrentRoomId();
    public string CurrentStyleVariantId => ResolveCurrentStyleVariantId();

    private readonly Dictionary<string, CacheCounts> _countsByKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly StringBuilder _builder = new(1024);
    private float _nextRefreshTime;
    private string _latestSummary = "[RoomStyleCache]\nState: waiting";

    private void Reset()
    {
        ResolveReferences();
    }

    private void Awake()
    {
        ResolveReferences();
        Refresh();
    }

    private void OnEnable()
    {
        ResolveReferences();
        Subscribe();
        Refresh();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void Update()
    {
        var now = Application.isPlaying ? Time.unscaledTime : Time.realtimeSinceStartup;
        if (now < _nextRefreshTime)
        {
            return;
        }

        _nextRefreshTime = now + refreshIntervalSeconds;
        Refresh();
    }

    [ContextMenu("Refresh Room Style Cache")]
    public void Refresh()
    {
        ResolveReferences();
        _countsByKey.Clear();

        ScanSurfaceJobs();
        ScanFurnitureJobs();
        PublishSummary();
    }

    public string GetThemeCacheStatus(ThemeProfile theme)
    {
        if (theme == null)
        {
            return "missing";
        }

        var counts = GetCounts(theme.ThemeId, ResolveCurrentStyleVariantId());
        if (counts.SurfaceReady >= ExpectedSurfaceKinds && counts.FurnitureReady > 0)
        {
            return "cached";
        }

        if (counts.SurfaceReady > 0 || counts.FurnitureReady > 0)
        {
            return "partial";
        }

        if (counts.SurfaceGenerating > 0 || counts.FurnitureGenerating > 0 || counts.SurfacePromptReady > 0 || counts.FurnitureQueued > 0)
        {
            return "generating";
        }

        return "missing";
    }

    public string GetThemeCacheLine(ThemeProfile theme)
    {
        if (theme == null)
        {
            return "Theme cache: missing theme";
        }

        var counts = GetCounts(theme.ThemeId, ResolveCurrentStyleVariantId());
        return $"{theme.DisplayName}: {GetThemeCacheStatus(theme)} | surfaces {counts.SurfaceReady}/{ExpectedSurfaceKinds} ready | furniture ready={counts.FurnitureReady}, running={counts.FurnitureGenerating}, queued={counts.FurnitureQueued}";
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
    }

    private void Subscribe()
    {
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

        if (roomSemanticBootstrap != null)
        {
            roomSemanticBootstrap.SummaryChanged -= HandleRoomChanged;
            roomSemanticBootstrap.SummaryChanged += HandleRoomChanged;
        }
    }

    private void Unsubscribe()
    {
        if (themeIntentController != null)
        {
            themeIntentController.ThemeChanged -= HandleThemeChanged;
        }

        if (runtimeStyleIntentController != null)
        {
            runtimeStyleIntentController.StyleIntentChanged -= HandleStyleIntentChanged;
        }

        if (roomSemanticBootstrap != null)
        {
            roomSemanticBootstrap.SummaryChanged -= HandleRoomChanged;
        }
    }

    private void HandleThemeChanged(ThemeProfile _)
    {
        Refresh();
    }

    private void HandleStyleIntentChanged()
    {
        Refresh();
    }

    private void HandleRoomChanged()
    {
        Refresh();
    }

    private void ScanSurfaceJobs()
    {
        var directory = GetLibraryDirectory(surfaceTextureJobFolderName);
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var jobPath in Directory.GetFiles(directory, "*.surface.job.json", SearchOption.TopDirectoryOnly))
        {
            var record = TryReadJson<SurfaceTextureJobRecord>(jobPath);
            if (record == null || string.IsNullOrWhiteSpace(record.ThemeId))
            {
                continue;
            }

            var counts = GetOrCreateCounts(record.ThemeId, NormalizeStyleVariantId(record.StyleVariantId));
            switch (record.State)
            {
                case SurfaceTextureJobState.TextureReady:
                case SurfaceTextureJobState.MaterialReady:
                    counts.SurfaceReady++;
                    break;
                case SurfaceTextureJobState.BackendSubmitted:
                    counts.SurfaceGenerating++;
                    break;
                case SurfaceTextureJobState.PromptReady:
                    counts.SurfacePromptReady++;
                    break;
                case SurfaceTextureJobState.Failed:
                    counts.SurfaceFailed++;
                    break;
            }
        }
    }

    private void ScanFurnitureJobs()
    {
        var directory = GetLibraryDirectory(generatedObjectJobFolderName);
        if (!Directory.Exists(directory))
        {
            return;
        }

        var currentRoomId = ResolveCurrentRoomId();
        foreach (var jobPath in Directory.GetFiles(directory, "*.job.json", SearchOption.TopDirectoryOnly))
        {
            var record = TryReadJson<GeneratedAssetRecord>(jobPath);
            if (record == null || string.IsNullOrWhiteSpace(record.ThemeId))
            {
                continue;
            }

            if (filterFurnitureByRoomId && !DoesFurnitureRecordMatchCurrentRoom(record, currentRoomId))
            {
                continue;
            }

            var counts = GetOrCreateCounts(record.ThemeId, NormalizeStyleVariantId(record.StyleVariantId));
            switch (record.State)
            {
                case GeneratedObjectJobState.Imported:
                case GeneratedObjectJobState.NeedsReview:
                    counts.FurnitureReady++;
                    break;
                case GeneratedObjectJobState.BackendSubmitted:
                case GeneratedObjectJobState.ModelGenerationSubmitted:
                case GeneratedObjectJobState.ModelReady:
                case GeneratedObjectJobState.StylizedImageReady:
                    counts.FurnitureGenerating++;
                    break;
                case GeneratedObjectJobState.CaptureReady:
                case GeneratedObjectJobState.Pending:
                    counts.FurnitureQueued++;
                    break;
                case GeneratedObjectJobState.Failed:
                    counts.FurnitureFailed++;
                    break;
            }
        }
    }

    private bool DoesFurnitureRecordMatchCurrentRoom(GeneratedAssetRecord record, string currentRoomId)
    {
        if (string.IsNullOrWhiteSpace(currentRoomId) || string.Equals(currentRoomId, "unknown_room", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var request = TryReadJson<GeneratedObjectRequest>(record.SourceRequestPath);
        if (request == null)
        {
            return true;
        }

        return string.IsNullOrWhiteSpace(request.RoomId) ||
               string.Equals(request.RoomId, "unknown_room", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(request.RoomId, currentRoomId, StringComparison.OrdinalIgnoreCase);
    }

    private CacheCounts GetCounts(string themeId, string styleVariantId)
    {
        var key = BuildKey(themeId, styleVariantId);
        return _countsByKey.TryGetValue(key, out var counts) ? counts : new CacheCounts();
    }

    private CacheCounts GetOrCreateCounts(string themeId, string styleVariantId)
    {
        var key = BuildKey(themeId, styleVariantId);
        if (_countsByKey.TryGetValue(key, out var counts))
        {
            return counts;
        }

        counts = new CacheCounts();
        _countsByKey[key] = counts;
        return counts;
    }

    private void PublishSummary()
    {
        var activeTheme = themeIntentController != null ? themeIntentController.ActiveTheme : null;
        var styleVariantId = ResolveCurrentStyleVariantId();

        _builder.Clear();
        _builder.AppendLine("[RoomStyleCache]");
        _builder.AppendLine($"Room: {ResolveCurrentRoomId()}");
        _builder.AppendLine($"Style Variant: {styleVariantId}");
        if (activeTheme != null)
        {
            _builder.AppendLine($"Active: {GetThemeCacheLine(activeTheme)}");
        }

        if (themeIntentController != null && themeIntentController.AvailableThemes != null)
        {
            _builder.Append("Themes:");
            foreach (var theme in themeIntentController.AvailableThemes)
            {
                if (theme == null)
                {
                    continue;
                }

                _builder.Append(' ');
                _builder.Append(theme.DisplayName);
                _builder.Append('=');
                _builder.Append(GetThemeCacheStatus(theme));
                _builder.Append(';');
            }
        }

        var summary = _builder.ToString().TrimEnd();
        if (!string.Equals(summary, _latestSummary, StringComparison.Ordinal))
        {
            _latestSummary = summary;
            SummaryChanged?.Invoke();
        }
    }

    private string ResolveCurrentRoomId()
    {
        return roomSemanticBootstrap != null && roomSemanticBootstrap.CurrentRoom != null
            ? roomSemanticBootstrap.CurrentRoom.name
            : "unknown_room";
    }

    private string ResolveCurrentStyleVariantId()
    {
        return SurfaceTexturePromptBuilder.BuildStyleVariantId(
            runtimeStyleIntentController != null ? runtimeStyleIntentController.CurrentIntent : null);
    }

    private static string NormalizeStyleVariantId(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? SurfaceTexturePromptBuilder.PresetStyleVariantId
            : value;
    }

    private static string BuildKey(string themeId, string styleVariantId)
    {
        return $"{themeId ?? string.Empty}|{NormalizeStyleVariantId(styleVariantId)}";
    }

    private static T TryReadJson<T>(string path) where T : class
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return null;
            }

            var json = File.ReadAllText(path);
            return string.IsNullOrWhiteSpace(json) ? null : JsonUtility.FromJson<T>(json);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[RoomStyleCache] Failed to read {path}: {exception.Message}");
            return null;
        }
    }

    private static string GetLibraryDirectory(string folderName)
    {
        var libraryRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Library"));
        return Path.Combine(libraryRoot, string.IsNullOrWhiteSpace(folderName) ? "GenerationJobs" : folderName);
    }

    private sealed class CacheCounts
    {
        public int SurfaceReady;
        public int SurfaceGenerating;
        public int SurfacePromptReady;
        public int SurfaceFailed;
        public int FurnitureReady;
        public int FurnitureGenerating;
        public int FurnitureQueued;
        public int FurnitureFailed;
    }
}
