using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

[DisallowMultipleComponent]
public class ApimartSurfaceTextureBackendAdapter : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private ThemeIntentController themeIntentController;
    [SerializeField] private RuntimeStyleIntentController runtimeStyleIntentController;

    [Header("APIMart Authentication")]
    [SerializeField] private string apiKeyEnvironmentVariable = "APIMART_API_KEY";

    [Header("Image Generation")]
    [SerializeField] private bool autoProcessJobsInPlay = true;
    [SerializeField, Min(1)] private int maxConcurrentSurfaceImageJobs = 2;
    [SerializeField] private bool processActiveThemeOnly = true;
    [SerializeField] private bool processActiveStyleOnly = true;
    [SerializeField] private string generationEndpoint = "https://api.apimart.ai/v1/images/generations";
    [SerializeField] private string taskEndpointBase = "https://api.apimart.ai/v1/tasks";
    [SerializeField] private string model = "gpt-image-2";
    [SerializeField] private string size = "1:1";
    [SerializeField, Min(1)] private int imageCount = 1;

    [Header("Polling")]
    [SerializeField, Min(1f)] private float pollIntervalSeconds = 5f;
    [SerializeField, Min(15f)] private float timeoutSeconds = 300f;

    [Header("Folders")]
    [SerializeField] private string jobFolderName = "SurfaceTextureJobs";
    [SerializeField] private string outputFolderName = "SurfaceTextureOutputs";

    public event Action SummaryChanged;

    public string LatestSummary => _latestSummary;
    public SurfaceTextureJobRecord LastProcessedRecord => _lastProcessedRecord;

    private SurfaceTextureJobRecord _lastProcessedRecord = new SurfaceTextureJobRecord();
    private readonly HashSet<string> _activeJobPaths = new(StringComparer.OrdinalIgnoreCase);
    private bool _isProcessing;
    private float _nextPollTime;
    private string _latestSummary =
        "[ApimartSurfaceTextureBackendAdapter]\nState: waiting\nHint: select a theme to write surface jobs, then APIMart can generate wall/floor/ceiling/frame textures.";

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
        ResolveReferences();
        PublishSummary("enabled");
    }

    private void Update()
    {
        if (!Application.isPlaying || !autoProcessJobsInPlay)
        {
            return;
        }

        if (Time.unscaledTime < _nextPollTime)
        {
            return;
        }

        _nextPollTime = Time.unscaledTime + pollIntervalSeconds;
        ProcessNextJob();
    }

    [ContextMenu("Process Next Surface Texture Job")]
    public void ProcessNextJob()
    {
        if (!HasSurfaceImageCapacity())
        {
            PublishSummary("at-surface-image-capacity");
            return;
        }

        ResolveReferences();
        var activeThemeId = GetActiveThemeId();
        var activeStyleVariantId = GetActiveStyleVariantId();
        if (processActiveThemeOnly && string.IsNullOrWhiteSpace(activeThemeId))
        {
            PublishSummary("waiting-for-active-theme");
            return;
        }

        var jobDirectory = GetJobDirectory();
        if (!Directory.Exists(jobDirectory))
        {
            PublishSummary("waiting-for-job-folder");
            return;
        }

        var startedCount = 0;
        var jobPaths = Directory.GetFiles(jobDirectory, "*.surface.job.json", SearchOption.TopDirectoryOnly);
        for (var index = 0; index < jobPaths.Length; index++)
        {
            if (IsJobActive(jobPaths[index]))
            {
                continue;
            }

            if (!TryLoadJob(jobPaths[index], out var record))
            {
                continue;
            }

            if (!ShouldProcessJob(record, activeThemeId, activeStyleVariantId))
            {
                continue;
            }

            if (record.State == SurfaceTextureJobState.PromptReady)
            {
                StartCoroutine(RunTrackedJob(jobPaths[index], record, submitNewTask: true));
                startedCount++;
                if (!HasSurfaceImageCapacity())
                {
                    break;
                }

                continue;
            }

            if (record.State == SurfaceTextureJobState.BackendSubmitted &&
                string.Equals(record.BackendAdapterName, nameof(ApimartSurfaceTextureBackendAdapter), StringComparison.Ordinal))
            {
                StartCoroutine(RunTrackedJob(jobPaths[index], record, submitNewTask: false));
                startedCount++;
                if (!HasSurfaceImageCapacity())
                {
                    break;
                }
            }
        }

        PublishSummary(startedCount > 0 ? "processing-batch" : "waiting-for-prompt-ready-job");
    }

    private IEnumerator RunTrackedJob(string jobPath, SurfaceTextureJobRecord record, bool submitNewTask)
    {
        var key = NormalizeJobPath(jobPath);
        _activeJobPaths.Add(key);
        _isProcessing = true;

        yield return submitNewTask
            ? SubmitJob(jobPath, record)
            : PollSubmittedJob(jobPath, record);

        _activeJobPaths.Remove(key);
        _isProcessing = _activeJobPaths.Count > 0;
        PublishSummary(_isProcessing ? "processing-batch" : "idle");
    }

    private bool HasSurfaceImageCapacity()
    {
        return _activeJobPaths.Count < Mathf.Max(1, maxConcurrentSurfaceImageJobs);
    }

    private bool IsJobActive(string jobPath)
    {
        return _activeJobPaths.Contains(NormalizeJobPath(jobPath));
    }

    private static string NormalizeJobPath(string jobPath)
    {
        return string.IsNullOrWhiteSpace(jobPath) ? string.Empty : Path.GetFullPath(jobPath);
    }

    private void ResolveReferences()
    {
        if (themeIntentController == null)
        {
            themeIntentController = FindAnyObjectByType<ThemeIntentController>();
        }

        if (runtimeStyleIntentController == null)
        {
            runtimeStyleIntentController = FindAnyObjectByType<RuntimeStyleIntentController>();
        }
    }

    private string GetActiveThemeId()
    {
        return themeIntentController != null && themeIntentController.ActiveTheme != null
            ? RuntimeStyleIntentRequestUtility.BuildEffectiveThemeId(
                themeIntentController.ActiveTheme,
                runtimeStyleIntentController != null ? runtimeStyleIntentController.CurrentIntent : null)
            : string.Empty;
    }

    private string GetActiveStyleVariantId()
    {
        return SurfaceTexturePromptBuilder.BuildStyleVariantId(
            runtimeStyleIntentController != null ? runtimeStyleIntentController.CurrentIntent : null);
    }

    private bool ShouldProcessJob(SurfaceTextureJobRecord record, string activeThemeId, string activeStyleVariantId)
    {
        if (record == null)
        {
            return false;
        }

        if (processActiveThemeOnly &&
            !string.IsNullOrWhiteSpace(activeThemeId) &&
            !string.Equals(record.ThemeId, activeThemeId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!processActiveStyleOnly)
        {
            return true;
        }

        var recordStyleVariantId = string.IsNullOrWhiteSpace(record.StyleVariantId)
            ? SurfaceTexturePromptBuilder.PresetStyleVariantId
            : record.StyleVariantId;
        return string.Equals(recordStyleVariantId, activeStyleVariantId, StringComparison.OrdinalIgnoreCase);
    }

    private IEnumerator SubmitJob(string jobPath, SurfaceTextureJobRecord record)
    {
        _isProcessing = true;
        _lastProcessedRecord = record;
        PublishSummary("submitting");

        var apiKey = Environment.GetEnvironmentVariable(apiKeyEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            record.StatusNote = $"Waiting for environment variable {apiKeyEnvironmentVariable}.";
            record.UpdatedAtIsoUtc = DateTime.UtcNow.ToString("O");
            File.WriteAllText(jobPath, JsonUtility.ToJson(record, true));
            PublishSummary("missing-api-key");
            _isProcessing = false;
            yield break;
        }

        if (string.IsNullOrWhiteSpace(record.PromptArtifactPath) || !File.Exists(record.PromptArtifactPath))
        {
            FailJob(jobPath, record, "Surface texture job is missing its prompt artifact.");
            _isProcessing = false;
            yield break;
        }

        var outputDirectory = GetOutputDirectory();
        Directory.CreateDirectory(outputDirectory);
        var requestPath = Path.Combine(outputDirectory, $"{record.RequestId}.apimart.surface.request.json");
        var resultPath = Path.Combine(outputDirectory, $"{record.RequestId}.apimart.surface.result.json");
        var requestJson = BuildCreateTaskJson(record);
        File.WriteAllText(requestPath, requestJson);

        string responseJson;
        using (var request = new UnityWebRequest(generationEndpoint, UnityWebRequest.kHttpVerbPOST))
        {
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(requestJson));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", $"Bearer {apiKey}");

            yield return request.SendWebRequest();

            responseJson = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
            File.WriteAllText(resultPath, responseJson ?? string.Empty);
            if (request.result != UnityWebRequest.Result.Success)
            {
                FailJob(jobPath, record, $"APIMart surface request failed: {request.responseCode} {request.error} {Shorten(responseJson, 240)}");
                _isProcessing = false;
                yield break;
            }
        }

        if (!TryExtractTaskId(responseJson, out var taskId))
        {
            FailJob(jobPath, record, $"APIMart surface response did not include task_id: {Shorten(responseJson, 240)}");
            _isProcessing = false;
            yield break;
        }

        record.State = SurfaceTextureJobState.BackendSubmitted;
        record.BackendAdapterName = nameof(ApimartSurfaceTextureBackendAdapter);
        record.BackendRequestPath = requestPath;
        record.BackendResultPath = resultPath;
        record.BackendTransformId = taskId;
        record.StatusNote = $"APIMart {model} surface texture task submitted; polling task {taskId}.";
        record.UpdatedAtIsoUtc = DateTime.UtcNow.ToString("O");
        File.WriteAllText(jobPath, JsonUtility.ToJson(record, true));

        yield return PollSubmittedJob(jobPath, record);
        _isProcessing = false;
    }

    private IEnumerator PollSubmittedJob(string jobPath, SurfaceTextureJobRecord record)
    {
        if (record == null || string.IsNullOrWhiteSpace(record.BackendTransformId))
        {
            yield break;
        }

        _isProcessing = true;
        _lastProcessedRecord = record;
        PublishSummary("polling");

        var apiKey = Environment.GetEnvironmentVariable(apiKeyEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            record.StatusNote = $"Waiting for environment variable {apiKeyEnvironmentVariable}.";
            record.UpdatedAtIsoUtc = DateTime.UtcNow.ToString("O");
            File.WriteAllText(jobPath, JsonUtility.ToJson(record, true));
            PublishSummary("missing-api-key");
            _isProcessing = false;
            yield break;
        }

        var resultPath = record.BackendResultPath;
        if (string.IsNullOrWhiteSpace(resultPath))
        {
            Directory.CreateDirectory(GetOutputDirectory());
            resultPath = Path.Combine(GetOutputDirectory(), $"{record.RequestId}.apimart.surface.result.json");
            record.BackendResultPath = resultPath;
        }

        var startedAt = Time.realtimeSinceStartup;
        var queryUrl = $"{taskEndpointBase.TrimEnd('/')}/{UnityWebRequest.EscapeURL(record.BackendTransformId)}";
        while (Time.realtimeSinceStartup - startedAt < timeoutSeconds)
        {
            yield return new WaitForSeconds(pollIntervalSeconds);

            string pollResponse;
            using (var pollRequest = UnityWebRequest.Get(queryUrl))
            {
                pollRequest.SetRequestHeader("Authorization", $"Bearer {apiKey}");
                yield return pollRequest.SendWebRequest();

                pollResponse = pollRequest.downloadHandler != null ? pollRequest.downloadHandler.text : string.Empty;
                if (pollRequest.result != UnityWebRequest.Result.Success)
                {
                    if (pollRequest.responseCode == 0)
                    {
                        record.StatusNote = $"APIMart surface poll had a transient network error and will retry: {pollRequest.error}";
                        record.UpdatedAtIsoUtc = DateTime.UtcNow.ToString("O");
                        File.WriteAllText(jobPath, JsonUtility.ToJson(record, true));
                        PublishSummary("polling-retry");
                        continue;
                    }

                    File.WriteAllText(resultPath, pollResponse ?? string.Empty);
                    FailJob(jobPath, record, $"APIMart surface poll failed: {pollRequest.responseCode} {pollRequest.error} {Shorten(pollResponse, 240)}");
                    _isProcessing = false;
                    yield break;
                }

                File.WriteAllText(resultPath, pollResponse ?? string.Empty);
            }

            if (IsFailureStatus(pollResponse, out var failureStatus))
            {
                FailJob(jobPath, record, $"APIMart surface task failed with status '{failureStatus}'.");
                _isProcessing = false;
                yield break;
            }

            if (!IsSuccessStatus(pollResponse))
            {
                record.StatusNote = $"APIMart surface task {record.BackendTransformId} is still running.";
                record.UpdatedAtIsoUtc = DateTime.UtcNow.ToString("O");
                File.WriteAllText(jobPath, JsonUtility.ToJson(record, true));
                PublishSummary("polling");
                continue;
            }

            if (!TryExtractImageUrl(pollResponse, out var imageUrl))
            {
                FailJob(jobPath, record, $"APIMart surface task succeeded but no image URL was found: {Shorten(pollResponse, 240)}");
                _isProcessing = false;
                yield break;
            }

            yield return DownloadTexture(jobPath, record, imageUrl);
            _isProcessing = false;
            yield break;
        }

        record.StatusNote = $"APIMart surface polling paused after {timeoutSeconds:0}s. Re-run polling to resume task {record.BackendTransformId}.";
        record.UpdatedAtIsoUtc = DateTime.UtcNow.ToString("O");
        File.WriteAllText(jobPath, JsonUtility.ToJson(record, true));
        PublishSummary("poll-timeout-paused");
        _isProcessing = false;
    }

    private IEnumerator DownloadTexture(string jobPath, SurfaceTextureJobRecord record, string imageUrl)
    {
        var outputDirectory = GetOutputDirectory();
        Directory.CreateDirectory(outputDirectory);
        var outputPath = !string.IsNullOrWhiteSpace(record.OutputImagePath)
            ? record.OutputImagePath
            : Path.Combine(outputDirectory, $"{record.RequestId}.surface.png");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? outputDirectory);

        using (var downloadRequest = UnityWebRequest.Get(imageUrl))
        {
            downloadRequest.downloadHandler = new DownloadHandlerFile(outputPath);
            yield return downloadRequest.SendWebRequest();

            if (downloadRequest.result != UnityWebRequest.Result.Success)
            {
                FailJob(jobPath, record, $"APIMart surface image download failed: {downloadRequest.responseCode} {downloadRequest.error}");
                yield break;
            }
        }

        if (!File.Exists(outputPath) || new FileInfo(outputPath).Length == 0)
        {
            FailJob(jobPath, record, $"APIMart surface image download produced an empty PNG: {outputPath}");
            yield break;
        }

        record.State = SurfaceTextureJobState.TextureReady;
        record.BackendAdapterName = nameof(ApimartSurfaceTextureBackendAdapter);
        record.OutputImagePath = outputPath;
        record.OutputImageUrl = imageUrl;
        record.StatusNote = "APIMart generated surface texture PNG downloaded locally; SurfaceOverrideApplier can use it at runtime.";
        record.FailureReason = string.Empty;
        record.UpdatedAtIsoUtc = DateTime.UtcNow.ToString("O");
        File.WriteAllText(jobPath, JsonUtility.ToJson(record, true));

        _lastProcessedRecord = record;
        PublishSummary("texture-ready");
        Debug.Log($"[ApimartSurfaceTextureBackendAdapter] Surface texture ready for {record.RequestId} -> {outputPath}", this);
    }

    private string BuildCreateTaskJson(SurfaceTextureJobRecord record)
    {
        var prompt = File.ReadAllText(record.PromptArtifactPath);
        var builder = new StringBuilder(4096);
        builder.Append('{');
        AppendJsonProperty(builder, "model", model);
        builder.Append(',');
        AppendJsonProperty(builder, "prompt", prompt);
        builder.Append(',');
        builder.Append("\"n\":").Append(Mathf.Max(1, imageCount));
        builder.Append(',');
        AppendJsonProperty(builder, "size", string.IsNullOrWhiteSpace(record.ImageSize) ? size : record.ImageSize);
        builder.Append('}');
        return builder.ToString();
    }

    private void FailJob(string jobPath, SurfaceTextureJobRecord record, string reason)
    {
        record.State = SurfaceTextureJobState.Failed;
        record.BackendAdapterName = nameof(ApimartSurfaceTextureBackendAdapter);
        record.FailureReason = reason;
        record.StatusNote = reason;
        record.UpdatedAtIsoUtc = DateTime.UtcNow.ToString("O");
        File.WriteAllText(jobPath, JsonUtility.ToJson(record, true));

        _lastProcessedRecord = record;
        PublishSummary("failed");
        Debug.LogWarning($"[ApimartSurfaceTextureBackendAdapter] Job failed for request {record.RequestId}: {reason}", this);
    }

    private bool TryLoadJob(string jobPath, out SurfaceTextureJobRecord record)
    {
        record = null;
        if (string.IsNullOrWhiteSpace(jobPath) || !File.Exists(jobPath))
        {
            return false;
        }

        var json = File.ReadAllText(jobPath);
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        record = JsonUtility.FromJson<SurfaceTextureJobRecord>(json);
        return record != null && !string.IsNullOrWhiteSpace(record.RequestId);
    }

    private string GetJobDirectory()
    {
        var libraryRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Library"));
        return Path.Combine(libraryRoot, jobFolderName);
    }

    private string GetOutputDirectory()
    {
        var libraryRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Library"));
        return Path.Combine(libraryRoot, outputFolderName);
    }

    private static bool TryExtractTaskId(string json, out string taskId)
    {
        return TryExtractString(json, "task_id", out taskId) ||
               TryExtractString(json, "id", out taskId);
    }

    private static bool TryExtractImageUrl(string json, out string imageUrl)
    {
        imageUrl = string.Empty;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        var urlArrayMatch = Regex.Match(
            json,
            "\"url\"\\s*:\\s*\\[\\s*\"(?<value>https?://[^\"]+)\"",
            RegexOptions.IgnoreCase);
        if (urlArrayMatch.Success)
        {
            imageUrl = Regex.Unescape(urlArrayMatch.Groups["value"].Value);
            return true;
        }

        var directUrlMatch = Regex.Match(
            json,
            "\"(?<value>https?://[^\"]+)\"",
            RegexOptions.IgnoreCase);
        if (directUrlMatch.Success)
        {
            imageUrl = Regex.Unescape(directUrlMatch.Groups["value"].Value);
            return true;
        }

        return false;
    }

    private static bool IsSuccessStatus(string json)
    {
        return TryExtractString(json, "status", out var status) &&
               (status.Equals("succeeded", StringComparison.OrdinalIgnoreCase) ||
                status.Equals("success", StringComparison.OrdinalIgnoreCase) ||
                status.Equals("completed", StringComparison.OrdinalIgnoreCase) ||
                status.Equals("done", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsFailureStatus(string json, out string status)
    {
        if (!TryExtractString(json, "status", out status))
        {
            return false;
        }

        return status.Equals("failed", StringComparison.OrdinalIgnoreCase) ||
               status.Equals("error", StringComparison.OrdinalIgnoreCase) ||
               status.Equals("cancelled", StringComparison.OrdinalIgnoreCase) ||
               status.Equals("canceled", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryExtractString(string json, string key, out string value)
    {
        value = string.Empty;
        if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var match = Regex.Match(
            json,
            $"\"{Regex.Escape(key)}\"\\s*:\\s*\"(?<value>(?:\\\\.|[^\"])*)\"",
            RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return false;
        }

        value = Regex.Unescape(match.Groups["value"].Value);
        return !string.IsNullOrWhiteSpace(value);
    }

    private static void AppendJsonProperty(StringBuilder builder, string name, string value)
    {
        AppendJsonString(builder, name);
        builder.Append(':');
        AppendJsonString(builder, value);
    }

    private static void AppendJsonString(StringBuilder builder, string value)
    {
        builder.Append('"').Append(EscapeJson(value)).Append('"');
    }

    private static string EscapeJson(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }

    private static string Shorten(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value ?? string.Empty;
        }

        return value.Substring(0, maxLength) + "...";
    }

    private void PublishSummary(string state)
    {
        var builder = new StringBuilder(512);
        builder.AppendLine("[ApimartSurfaceTextureBackendAdapter]");
        builder.AppendLine($"State: {state}");
        builder.AppendLine($"Auto Process: {autoProcessJobsInPlay}");
        builder.AppendLine($"Active Jobs: {_activeJobPaths.Count}/{Mathf.Max(1, maxConcurrentSurfaceImageJobs)}");
        builder.AppendLine($"Active Theme Only: {processActiveThemeOnly}");
        builder.AppendLine($"Active Theme: {GetActiveThemeId()}");
        builder.AppendLine($"Active Style Only: {processActiveStyleOnly}");
        builder.AppendLine($"Active Style: {GetActiveStyleVariantId()}");
        builder.AppendLine($"Model: {model}");

        if (!string.IsNullOrWhiteSpace(_lastProcessedRecord.RequestId))
        {
            builder.AppendLine($"Last Request: {_lastProcessedRecord.RequestId}");
            builder.AppendLine($"Last State: {_lastProcessedRecord.State}");
            builder.AppendLine($"Image Size: {_lastProcessedRecord.ImageSize}");
            builder.AppendLine($"Task Id: {_lastProcessedRecord.BackendTransformId}");
            builder.AppendLine($"Texture: {_lastProcessedRecord.OutputImagePath}");
            builder.AppendLine($"Status Note: {_lastProcessedRecord.StatusNote}");
        }
        else
        {
            builder.AppendLine("Last Request: none");
        }

        _latestSummary = builder.ToString().TrimEnd();
        SummaryChanged?.Invoke();
    }
}
