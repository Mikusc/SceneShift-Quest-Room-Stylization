using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

[DisallowMultipleComponent]
public class QuestSurfaceGenerationClient : MonoBehaviour
{
    private const int OutputStateFailed = 5;
    private const int OutputStateSubmitted = 9;
    private const int OutputStateReady = 10;

    [Header("References")]
    [SerializeField] private SurfaceTexturePromptBuilder surfaceTexturePromptBuilder;
    [SerializeField] private SurfaceOverrideApplier surfaceOverrideApplier;
    [SerializeField] private GenerationQueueStatusService generationQueueStatusService;

    [Header("Backend")]
    [SerializeField] private string backendSubmitUrl = "https://www.mikusc.top/api/v1/surface-generations";
    [SerializeField, Min(1f)] private float pollIntervalSeconds = 5f;
    [SerializeField, Min(30f)] private float timeoutSeconds = 420f;
    [SerializeField, Min(1)] private int requestTimeoutSeconds = 60;
    [SerializeField] private bool reapplySurfacesAfterDownloads = true;

    public event Action SummaryChanged;

    public string LatestSummary => _latestSummary;
    public bool IsRunning => _runningCoroutine != null;

    private Coroutine _runningCoroutine;
    private SurfaceTexturePromptSet _activePromptSet;
    private readonly HashSet<string> _downloadedRequestIds = new(StringComparer.OrdinalIgnoreCase);
    private string _lastResultPath = string.Empty;
    private string _latestSummary =
        "[QuestSurfaceGenerationClient]\nState: waiting\nHint: submit room surfaces to the HTTPS backend; no provider keys are stored in the APK.";

    private void Reset()
    {
        ResolveReferences();
    }

    private void Awake()
    {
        ResolveReferences();
    }

    [ContextMenu("Submit Active Surface Generation")]
    public void SubmitActiveSurfaceGeneration()
    {
        ResolveReferences();
        if (_runningCoroutine != null)
        {
            PublishSummary("already-running", "Surface generation is already running.");
            return;
        }

        _runningCoroutine = StartCoroutine(SubmitAndPollActiveSurfaceGeneration());
    }

    public void SubmitActiveSurfaceGenerationAndReapply()
    {
        SubmitActiveSurfaceGeneration();
    }

    private IEnumerator SubmitAndPollActiveSurfaceGeneration()
    {
        _downloadedRequestIds.Clear();

        if (surfaceTexturePromptBuilder == null)
        {
            PublishSummary("missing-prompt-builder", "SurfaceTexturePromptBuilder is not available.");
            _runningCoroutine = null;
            yield break;
        }

        if (string.IsNullOrWhiteSpace(backendSubmitUrl))
        {
            PublishSummary("missing-backend-url", "Surface backend URL is empty.");
            _runningCoroutine = null;
            yield break;
        }

        surfaceTexturePromptBuilder.WriteActiveSurfaceTexturePrompts();
        _activePromptSet = surfaceTexturePromptBuilder.LatestPromptSet;
        if (_activePromptSet == null || _activePromptSet.Entries == null || _activePromptSet.Entries.Count == 0)
        {
            PublishSummary("missing-surface-entries", "No surface prompt entries are available for the active style.");
            _runningCoroutine = null;
            yield break;
        }

        var payload = JsonUtility.ToJson(_activePromptSet, prettyPrint: false);
        PublishSummary("submitting", $"Submitting {_activePromptSet.Entries.Count} surface texture jobs.");

        SurfaceBackendResult result = null;
        yield return SendJsonRequest(backendSubmitUrl, "POST", payload, parsed => result = parsed);
        if (result == null)
        {
            _runningCoroutine = null;
            yield break;
        }

        PersistBackendResult(result);
        MarkSubmittedJobs(result);
        DownloadReadySurfaceTextures(result);
        generationQueueStatusService?.Refresh();

        var started = Time.realtimeSinceStartup;
        while (!IsTerminal(result) && Time.realtimeSinceStartup - started < timeoutSeconds)
        {
            yield return new WaitForSeconds(pollIntervalSeconds);
            if (string.IsNullOrWhiteSpace(result.SurfaceBackendStatusUrl))
            {
                PublishSummary("missing-status-url", "Surface backend did not return a status URL.");
                break;
            }

            yield return SendJsonRequest(result.SurfaceBackendStatusUrl, "GET", null, parsed => result = parsed);
            if (result == null)
            {
                break;
            }

            PersistBackendResult(result);
            DownloadReadySurfaceTextures(result);
            generationQueueStatusService?.Refresh();
            PublishBackendProgress(result);
        }

        if (result != null && !IsTerminal(result))
        {
            PublishSummary("poll-timeout-paused", $"Polling paused after {timeoutSeconds:0}s; press Generate Room again to resume from the backend status URL.");
        }

        if (reapplySurfacesAfterDownloads)
        {
            surfaceOverrideApplier?.ReapplySurfaceOverrides();
        }

        generationQueueStatusService?.Refresh();
        _runningCoroutine = null;
    }

    private IEnumerator SendJsonRequest(string url, string method, string jsonPayload, Action<SurfaceBackendResult> onParsed)
    {
        using var request = new UnityWebRequest(url, method);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.timeout = Mathf.Max(1, requestTimeoutSeconds);
        if (!string.IsNullOrEmpty(jsonPayload))
        {
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonPayload));
            request.SetRequestHeader("Content-Type", "application/json");
        }

        request.SetRequestHeader("Accept", "application/json");
        yield return request.SendWebRequest();

        var responseText = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
        if (request.result != UnityWebRequest.Result.Success)
        {
            PublishSummary("request-failed", $"{method} {url} failed: {request.responseCode} {request.error} {Shorten(responseText, 240)}");
            onParsed?.Invoke(null);
            yield break;
        }

        SurfaceBackendResult parsed;
        try
        {
            parsed = JsonUtility.FromJson<SurfaceBackendResult>(responseText);
        }
        catch (Exception exception)
        {
            PublishSummary("invalid-response", $"Surface backend returned invalid JSON: {exception.Message}");
            onParsed?.Invoke(null);
            yield break;
        }

        if (parsed == null || string.IsNullOrWhiteSpace(parsed.SurfaceBackendJobId))
        {
            PublishSummary("invalid-response", $"Surface backend response did not include a job id: {Shorten(responseText, 240)}");
            onParsed?.Invoke(null);
            yield break;
        }

        onParsed?.Invoke(parsed);
    }

    private void DownloadReadySurfaceTextures(SurfaceBackendResult result)
    {
        if (result?.Surfaces == null)
        {
            return;
        }

        var downloadedAny = false;
        for (var index = 0; index < result.Surfaces.Count; index++)
        {
            var surface = result.Surfaces[index];
            if (surface == null || surface.OutputState != OutputStateReady || string.IsNullOrWhiteSpace(surface.OutputImageUrl))
            {
                if (surface != null && surface.OutputState == OutputStateFailed)
                {
                    MarkSurfaceJobFailed(surface, result);
                }
                continue;
            }

            if (_downloadedRequestIds.Contains(surface.RequestId))
            {
                continue;
            }

            var entry = FindPromptEntry(surface.RequestId);
            if (entry == null)
            {
                continue;
            }

            var outputPath = ResolveOutputPath(entry);
            if (HasUsableFile(outputPath))
            {
                MarkSurfaceJobTextureReady(entry, surface, result, outputPath);
                _downloadedRequestIds.Add(surface.RequestId);
                continue;
            }

            StartCoroutine(DownloadSurfaceTexture(entry, surface, result, outputPath));
            _downloadedRequestIds.Add(surface.RequestId);
            downloadedAny = true;
        }

        if (downloadedAny)
        {
            PublishBackendProgress(result);
        }
    }

    private IEnumerator DownloadSurfaceTexture(
        SurfaceTexturePromptEntry entry,
        SurfaceBackendSurfaceResult surface,
        SurfaceBackendResult result,
        string outputPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? Application.persistentDataPath);
        using var request = UnityWebRequest.Get(surface.OutputImageUrl);
        request.downloadHandler = new DownloadHandlerFile(outputPath);
        request.timeout = Mathf.Max(1, requestTimeoutSeconds);
        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success || !HasUsableFile(outputPath))
        {
            _downloadedRequestIds.Remove(surface.RequestId);
            MarkSurfaceJobFailed(surface, result, $"Download failed: {request.responseCode} {request.error}");
            PublishSummary("download-failed", $"Surface {surface.RequestId} download failed: {request.error}");
            yield break;
        }

        MarkSurfaceJobTextureReady(entry, surface, result, outputPath);
        generationQueueStatusService?.Refresh();
        if (reapplySurfacesAfterDownloads)
        {
            surfaceOverrideApplier?.ReapplySurfaceOverrides();
        }

        PublishSummary("texture-downloaded", $"Surface {surface.SemanticLabel} saved and applied.");
    }

    private void MarkSubmittedJobs(SurfaceBackendResult result)
    {
        if (_activePromptSet?.Entries == null || result == null)
        {
            return;
        }

        foreach (var entry in _activePromptSet.Entries)
        {
            var record = LoadOrCreateRecord(entry);
            if (record.State is SurfaceTextureJobState.TextureReady or SurfaceTextureJobState.MaterialReady)
            {
                continue;
            }

            record.State = SurfaceTextureJobState.BackendSubmitted;
            record.BackendAdapterName = nameof(QuestSurfaceGenerationClient);
            record.BackendTransformId = result.SurfaceBackendJobId;
            record.BackendResultPath = _lastResultPath;
            record.StatusNote = $"Surface backend job submitted: {result.SurfaceBackendJobId}.";
            record.FailureReason = string.Empty;
            record.UpdatedAtIsoUtc = DateTime.UtcNow.ToString("O");
            WriteRecord(record);
        }
    }

    private void MarkSurfaceJobTextureReady(
        SurfaceTexturePromptEntry entry,
        SurfaceBackendSurfaceResult surface,
        SurfaceBackendResult result,
        string outputPath)
    {
        var record = LoadOrCreateRecord(entry);
        record.State = SurfaceTextureJobState.TextureReady;
        record.BackendAdapterName = nameof(QuestSurfaceGenerationClient);
        record.BackendTransformId = result.SurfaceBackendJobId;
        record.BackendResultPath = _lastResultPath;
        record.OutputImagePath = outputPath;
        record.OutputImageUrl = surface.OutputImageUrl;
        record.StatusNote = string.IsNullOrWhiteSpace(surface.StatusNote)
            ? "Surface texture downloaded from SceneShift HTTPS backend."
            : surface.StatusNote;
        record.FailureReason = string.Empty;
        record.UpdatedAtIsoUtc = DateTime.UtcNow.ToString("O");
        WriteRecord(record);
    }

    private void MarkSurfaceJobFailed(SurfaceBackendSurfaceResult surface, SurfaceBackendResult result, string fallbackReason = null)
    {
        var entry = FindPromptEntry(surface.RequestId);
        if (entry == null)
        {
            return;
        }

        var record = LoadOrCreateRecord(entry);
        record.State = SurfaceTextureJobState.Failed;
        record.BackendAdapterName = nameof(QuestSurfaceGenerationClient);
        record.BackendTransformId = result.SurfaceBackendJobId;
        record.BackendResultPath = _lastResultPath;
        record.StatusNote = string.IsNullOrWhiteSpace(surface.StatusNote) ? "Surface backend failed." : surface.StatusNote;
        record.FailureReason = string.IsNullOrWhiteSpace(surface.FailureReason) ? fallbackReason ?? record.StatusNote : surface.FailureReason;
        record.UpdatedAtIsoUtc = DateTime.UtcNow.ToString("O");
        WriteRecord(record);
    }

    private SurfaceTextureJobRecord LoadOrCreateRecord(SurfaceTexturePromptEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.JobPath) && File.Exists(entry.JobPath))
        {
            var json = File.ReadAllText(entry.JobPath);
            if (!string.IsNullOrWhiteSpace(json))
            {
                var existing = JsonUtility.FromJson<SurfaceTextureJobRecord>(json);
                if (existing != null && !string.IsNullOrWhiteSpace(existing.RequestId))
                {
                    return existing;
                }
            }
        }

        return new SurfaceTextureJobRecord
        {
            RequestId = entry.RequestId,
            ThemeId = _activePromptSet?.ThemeId ?? string.Empty,
            ThemeDisplayName = _activePromptSet?.ThemeDisplayName ?? string.Empty,
            StyleVariantId = entry.StyleVariantId,
            UserStyleIntent = entry.UserStyleIntent,
            StyleIntentSource = entry.StyleIntentSource,
            SemanticLabel = entry.SemanticLabel,
            SurfaceKind = entry.SurfaceKind,
            OutputRole = entry.OutputRole,
            PromptVersion = entry.PromptVersion,
            ImageSize = entry.ImageSize,
            PromptArtifactPath = entry.PromptPath,
            JobPath = entry.JobPath,
            OutputImagePath = entry.OutputImagePath,
            State = SurfaceTextureJobState.PromptReady
        };
    }

    private static void WriteRecord(SurfaceTextureJobRecord record)
    {
        if (record == null || string.IsNullOrWhiteSpace(record.JobPath))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(record.JobPath) ?? Application.persistentDataPath);
        File.WriteAllText(record.JobPath, JsonUtility.ToJson(record, prettyPrint: true), Encoding.UTF8);
    }

    private SurfaceTexturePromptEntry FindPromptEntry(string requestId)
    {
        if (_activePromptSet?.Entries == null || string.IsNullOrWhiteSpace(requestId))
        {
            return null;
        }

        for (var index = 0; index < _activePromptSet.Entries.Count; index++)
        {
            var entry = _activePromptSet.Entries[index];
            if (entry != null && string.Equals(entry.RequestId, requestId, StringComparison.OrdinalIgnoreCase))
            {
                return entry;
            }
        }

        return null;
    }

    private string ResolveOutputPath(SurfaceTexturePromptEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.OutputImagePath))
        {
            return entry.OutputImagePath;
        }

        var outputDirectory = Path.Combine(Application.persistentDataPath, "SurfaceTextureOutputs");
        return Path.Combine(outputDirectory, $"{SurfaceTexturePromptBuilder.SanitizeFileName(entry.RequestId)}.surface.png");
    }

    private void PersistBackendResult(SurfaceBackendResult result)
    {
        if (result == null)
        {
            return;
        }

        var outputDirectory = ResolveResultDirectory();
        Directory.CreateDirectory(outputDirectory);
        _lastResultPath = Path.Combine(outputDirectory, $"{SurfaceTexturePromptBuilder.SanitizeFileName(result.SurfaceBackendJobId)}.surface.backend.result.json");
        File.WriteAllText(_lastResultPath, JsonUtility.ToJson(result, prettyPrint: true), Encoding.UTF8);
    }

    private string ResolveResultDirectory()
    {
        if (_activePromptSet?.Entries != null)
        {
            for (var index = 0; index < _activePromptSet.Entries.Count; index++)
            {
                var path = _activePromptSet.Entries[index]?.OutputImagePath;
                if (!string.IsNullOrWhiteSpace(path))
                {
                    return Path.GetDirectoryName(path) ?? Application.persistentDataPath;
                }
            }
        }

        return Path.Combine(Application.persistentDataPath, "SurfaceTextureOutputs");
    }

    private bool IsTerminal(SurfaceBackendResult result)
    {
        if (result?.Surfaces == null || result.Surfaces.Count == 0)
        {
            return true;
        }

        var terminal = 0;
        for (var index = 0; index < result.Surfaces.Count; index++)
        {
            var state = result.Surfaces[index].OutputState;
            if (state == OutputStateReady || state == OutputStateFailed)
            {
                terminal++;
            }
        }

        return terminal == result.Surfaces.Count;
    }

    private void PublishBackendProgress(SurfaceBackendResult result)
    {
        if (result == null)
        {
            return;
        }

        var ready = 0;
        var failed = 0;
        var total = result.Surfaces?.Count ?? 0;
        if (result.Surfaces != null)
        {
            for (var index = 0; index < result.Surfaces.Count; index++)
            {
                if (result.Surfaces[index].OutputState == OutputStateReady)
                {
                    ready++;
                }
                else if (result.Surfaces[index].OutputState == OutputStateFailed)
                {
                    failed++;
                }
            }
        }

        PublishSummary(
            result.OutputState == OutputStateReady ? "ready" : "polling",
            $"Surface backend job {result.SurfaceBackendJobId}: ready={ready}/{total}, failed={failed}, progress={result.Progress01:0.00}.");
    }

    private void PublishSummary(string state, string note)
    {
        _latestSummary = $"[QuestSurfaceGenerationClient]\nState: {state}\n{note}";
        SummaryChanged?.Invoke();
        Debug.Log(_latestSummary, this);
    }

    private void ResolveReferences()
    {
        if (surfaceTexturePromptBuilder == null)
        {
            surfaceTexturePromptBuilder = FindAnyObjectByType<SurfaceTexturePromptBuilder>();
        }

        if (surfaceOverrideApplier == null)
        {
            surfaceOverrideApplier = FindAnyObjectByType<SurfaceOverrideApplier>();
        }

        if (generationQueueStatusService == null)
        {
            generationQueueStatusService = FindAnyObjectByType<GenerationQueueStatusService>();
        }
    }

    private static bool HasUsableFile(string path)
    {
        return !string.IsNullOrWhiteSpace(path) && File.Exists(path) && new FileInfo(path).Length > 0;
    }

    private static string Shorten(string value, int maxLength)
    {
        var text = value ?? string.Empty;
        return text.Length <= maxLength ? text : $"{text.Substring(0, maxLength)}...";
    }

    [Serializable]
    private class SurfaceBackendResult
    {
        public string RequestId;
        public string ThemeId;
        public string StyleVariantId;
        public string SurfaceBackendJobId;
        public string SurfaceBackendStatusUrl;
        public float Progress01;
        public int OutputState;
        public string FailureReason;
        public string StatusNote;
        public string CreatedAtIsoUtc;
        public List<SurfaceBackendSurfaceResult> Surfaces = new();
    }

    [Serializable]
    private class SurfaceBackendSurfaceResult
    {
        public string RequestId;
        public string SemanticLabel;
        public int SurfaceKind;
        public string OutputRole;
        public string OutputImageUrl;
        public string OutputImageMimeType;
        public string OutputImageHash;
        public float Progress01;
        public int OutputState;
        public string FailureReason;
        public string StatusNote;
    }
}
