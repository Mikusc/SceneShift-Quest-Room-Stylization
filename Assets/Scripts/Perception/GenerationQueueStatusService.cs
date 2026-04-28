using System;
using System.IO;
using System.Text;
using UnityEngine;

[DisallowMultipleComponent]
public class GenerationQueueStatusService : MonoBehaviour
{
    [Header("Folders")]
    [SerializeField] private string generatedObjectJobFolderName = "GeneratedObjectJobs";
    [SerializeField] private string surfaceTextureJobFolderName = "SurfaceTextureJobs";

    [Header("Refresh")]
    [SerializeField, Min(0.25f)] private float refreshIntervalSeconds = 1f;

    public event Action SummaryChanged;

    public string LatestSummary => _latestSummary;

    private float _nextRefreshTime;
    private string _latestSummary = "[GenerationQueueStatus]\nState: waiting";

    private void OnEnable()
    {
        Refresh();
    }

    private void Update()
    {
        if (!Application.isPlaying && Time.realtimeSinceStartup < _nextRefreshTime)
        {
            return;
        }

        if (Application.isPlaying && Time.unscaledTime < _nextRefreshTime)
        {
            return;
        }

        _nextRefreshTime = (Application.isPlaying ? Time.unscaledTime : Time.realtimeSinceStartup) + refreshIntervalSeconds;
        Refresh();
    }

    [ContextMenu("Refresh Generation Queue Status")]
    public void Refresh()
    {
        var objectCounts = CountGeneratedObjectJobs();
        var surfaceCounts = CountSurfaceTextureJobs();

        var builder = new StringBuilder(768);
        builder.AppendLine("[GenerationQueueStatus]");
        builder.AppendLine($"Object Jobs: total={objectCounts.Total}, waitingImage={objectCounts.CaptureReady}, imageRunning={objectCounts.BackendSubmitted}, uploadReady={objectCounts.StylizedImageReady}, seed3DRunning={objectCounts.ModelGenerationSubmitted}, modelReady={objectCounts.ModelReady}, imported={objectCounts.Imported}, review={objectCounts.NeedsReview}, failed={objectCounts.Failed}");
        builder.AppendLine($"Surface Jobs: total={surfaceCounts.Total}, promptReady={surfaceCounts.PromptReady}, imageRunning={surfaceCounts.BackendSubmitted}, textureReady={surfaceCounts.TextureReady}, materialReady={surfaceCounts.MaterialReady}, failed={surfaceCounts.Failed}");
        builder.AppendLine($"Object Folder: {GetLibraryDirectory(generatedObjectJobFolderName)}");
        builder.Append($"Surface Folder: {GetLibraryDirectory(surfaceTextureJobFolderName)}");

        var summary = builder.ToString();
        if (!string.Equals(_latestSummary, summary, StringComparison.Ordinal))
        {
            _latestSummary = summary;
            SummaryChanged?.Invoke();
        }
    }

    private GeneratedObjectQueueCounts CountGeneratedObjectJobs()
    {
        var counts = new GeneratedObjectQueueCounts();
        var directory = GetLibraryDirectory(generatedObjectJobFolderName);
        if (!Directory.Exists(directory))
        {
            return counts;
        }

        foreach (var jobPath in Directory.GetFiles(directory, "*.job.json", SearchOption.TopDirectoryOnly))
        {
            var record = TryReadJson<GeneratedAssetRecord>(jobPath);
            if (record == null || string.IsNullOrWhiteSpace(record.RequestId))
            {
                continue;
            }

            counts.Total++;
            switch (record.State)
            {
                case GeneratedObjectJobState.CaptureReady:
                    counts.CaptureReady++;
                    break;
                case GeneratedObjectJobState.BackendSubmitted:
                    counts.BackendSubmitted++;
                    break;
                case GeneratedObjectJobState.StylizedImageReady:
                    counts.StylizedImageReady++;
                    break;
                case GeneratedObjectJobState.ModelGenerationSubmitted:
                    counts.ModelGenerationSubmitted++;
                    break;
                case GeneratedObjectJobState.ModelReady:
                    counts.ModelReady++;
                    break;
                case GeneratedObjectJobState.Imported:
                    counts.Imported++;
                    break;
                case GeneratedObjectJobState.NeedsReview:
                    counts.NeedsReview++;
                    break;
                case GeneratedObjectJobState.Failed:
                    counts.Failed++;
                    break;
            }
        }

        return counts;
    }

    private SurfaceTextureQueueCounts CountSurfaceTextureJobs()
    {
        var counts = new SurfaceTextureQueueCounts();
        var directory = GetLibraryDirectory(surfaceTextureJobFolderName);
        if (!Directory.Exists(directory))
        {
            return counts;
        }

        foreach (var jobPath in Directory.GetFiles(directory, "*.surface.job.json", SearchOption.TopDirectoryOnly))
        {
            var record = TryReadJson<SurfaceTextureJobRecord>(jobPath);
            if (record == null || string.IsNullOrWhiteSpace(record.RequestId))
            {
                continue;
            }

            counts.Total++;
            switch (record.State)
            {
                case SurfaceTextureJobState.PromptReady:
                    counts.PromptReady++;
                    break;
                case SurfaceTextureJobState.BackendSubmitted:
                    counts.BackendSubmitted++;
                    break;
                case SurfaceTextureJobState.TextureReady:
                    counts.TextureReady++;
                    break;
                case SurfaceTextureJobState.MaterialReady:
                    counts.MaterialReady++;
                    break;
                case SurfaceTextureJobState.Failed:
                    counts.Failed++;
                    break;
            }
        }

        return counts;
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
            Debug.LogWarning($"[GenerationQueueStatus] Failed to read job file {path}: {exception.Message}");
            return null;
        }
    }

    private static string GetLibraryDirectory(string folderName)
    {
        var libraryRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Library"));
        return Path.Combine(libraryRoot, string.IsNullOrWhiteSpace(folderName) ? "GenerationJobs" : folderName);
    }

    private struct GeneratedObjectQueueCounts
    {
        public int Total;
        public int CaptureReady;
        public int BackendSubmitted;
        public int StylizedImageReady;
        public int ModelGenerationSubmitted;
        public int ModelReady;
        public int Imported;
        public int NeedsReview;
        public int Failed;
    }

    private struct SurfaceTextureQueueCounts
    {
        public int Total;
        public int PromptReady;
        public int BackendSubmitted;
        public int TextureReady;
        public int MaterialReady;
        public int Failed;
    }
}
