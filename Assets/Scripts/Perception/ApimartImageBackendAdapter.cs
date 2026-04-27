using System;
using System.Collections;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

[DisallowMultipleComponent]
public class ApimartImageBackendAdapter : MonoBehaviour
{
    [Header("APIMart Authentication")]
    [SerializeField] private string apiKeyEnvironmentVariable = "APIMART_API_KEY";

    [Header("Image Generation")]
    [SerializeField] private bool autoProcessJobsInPlay = true;
    [SerializeField] private string generationEndpoint = "https://api.apimart.ai/v1/images/generations";
    [SerializeField] private string taskEndpointBase = "https://api.apimart.ai/v1/tasks";
    [SerializeField] private string model = "gpt-image-2";
    [SerializeField] private string size = "1:1";
    [SerializeField, Min(1)] private int imageCount = 1;
    [SerializeField] private bool includeReferenceImage = true;

    [Header("Polling")]
    [SerializeField, Min(1f)] private float pollIntervalSeconds = 5f;
    [SerializeField, Min(15f)] private float timeoutSeconds = 300f;

    [Header("Folders")]
    [SerializeField] private string jobFolderName = "GeneratedObjectJobs";
    [SerializeField] private string outputFolderName = "GeneratedObjectOutputs";

    public event Action SummaryChanged;

    public string LatestSummary => _latestSummary;
    public GeneratedAssetRecord LastProcessedRecord => _lastProcessedRecord;

    private GeneratedAssetRecord _lastProcessedRecord = new();
    private bool _isProcessing;
    private float _nextPollTime;
    private string _latestSummary =
        "[ApimartImageBackendAdapter]\nState: waiting\nHint: set APIMART_API_KEY, queue a CaptureReady job, then this adapter will request gpt-image-2.";

    private void OnEnable()
    {
        PublishSummary("enabled");
    }

    private void Update()
    {
        if (!Application.isPlaying || !autoProcessJobsInPlay || _isProcessing)
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

    [ContextMenu("Process Next APIMart Image Job")]
    public void ProcessNextJob()
    {
        if (_isProcessing)
        {
            PublishSummary("already-processing");
            return;
        }

        var jobDirectory = GetJobDirectory();
        if (!Directory.Exists(jobDirectory))
        {
            PublishSummary("waiting-for-job-folder");
            return;
        }

        var jobPaths = Directory.GetFiles(jobDirectory, "*.job.json", SearchOption.TopDirectoryOnly);
        for (var index = 0; index < jobPaths.Length; index++)
        {
            var jobPath = jobPaths[index];
            if (!TryLoadJob(jobPath, out var record))
            {
                continue;
            }

            if (record.State == GeneratedObjectJobState.CaptureReady)
            {
                StartCoroutine(SubmitJob(jobPath, record));
                return;
            }

            if (record.State == GeneratedObjectJobState.BackendSubmitted &&
                string.Equals(record.BackendAdapterName, nameof(ApimartImageBackendAdapter), StringComparison.Ordinal))
            {
                StartCoroutine(PollSubmittedJob(jobPath, record));
                return;
            }
        }

        PublishSummary("waiting-for-capture-ready-job");
    }

    public string GetDebugSummary()
    {
        return _latestSummary;
    }

    private IEnumerator SubmitJob(string jobPath, GeneratedAssetRecord record)
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
            FailJob(jobPath, record, "APIMart image job is missing its prompt artifact.");
            _isProcessing = false;
            yield break;
        }

        if (includeReferenceImage &&
            (string.IsNullOrWhiteSpace(record.SourceInputImagePath) || !File.Exists(record.SourceInputImagePath)))
        {
            FailJob(jobPath, record, "APIMart image job is missing its source reference image.");
            _isProcessing = false;
            yield break;
        }

        var outputDirectory = GetOutputDirectory();
        Directory.CreateDirectory(outputDirectory);
        var requestPath = Path.Combine(outputDirectory, $"{record.RequestId}.apimart.request.json");
        var resultPath = Path.Combine(outputDirectory, $"{record.RequestId}.apimart.result.json");
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
                FailJob(jobPath, record, $"APIMart image generation request failed: {request.responseCode} {request.error} {Shorten(responseJson, 240)}");
                _isProcessing = false;
                yield break;
            }
        }

        if (!TryExtractTaskId(responseJson, out var taskId))
        {
            FailJob(jobPath, record, $"APIMart image generation response did not include task_id: {Shorten(responseJson, 240)}");
            _isProcessing = false;
            yield break;
        }

        record.State = GeneratedObjectJobState.BackendSubmitted;
        record.BackendAdapterName = nameof(ApimartImageBackendAdapter);
        record.BackendRequestPath = requestPath;
        record.BackendResultPath = resultPath;
        record.BackendTransformId = taskId;
        record.StatusNote = $"APIMart {model} task submitted; polling task {taskId}.";
        record.UpdatedAtIsoUtc = DateTime.UtcNow.ToString("O");
        File.WriteAllText(jobPath, JsonUtility.ToJson(record, true));

        yield return PollSubmittedJob(jobPath, record);
        _isProcessing = false;
    }

    private IEnumerator PollSubmittedJob(string jobPath, GeneratedAssetRecord record)
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
            var outputDirectory = GetOutputDirectory();
            Directory.CreateDirectory(outputDirectory);
            resultPath = Path.Combine(outputDirectory, $"{record.RequestId}.apimart.result.json");
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
                File.WriteAllText(resultPath, pollResponse ?? string.Empty);
                if (pollRequest.result != UnityWebRequest.Result.Success)
                {
                    FailJob(jobPath, record, $"APIMart task poll failed: {pollRequest.responseCode} {pollRequest.error} {Shorten(pollResponse, 240)}");
                    _isProcessing = false;
                    yield break;
                }
            }

            if (IsFailureStatus(pollResponse, out var failureStatus))
            {
                FailJob(jobPath, record, $"APIMart task failed with status '{failureStatus}'.");
                _isProcessing = false;
                yield break;
            }

            if (!IsSuccessStatus(pollResponse))
            {
                record.StatusNote = $"APIMart task {record.BackendTransformId} is still running.";
                record.UpdatedAtIsoUtc = DateTime.UtcNow.ToString("O");
                File.WriteAllText(jobPath, JsonUtility.ToJson(record, true));
                PublishSummary("polling");
                continue;
            }

            if (!TryExtractImageUrl(pollResponse, out var imageUrl))
            {
                FailJob(jobPath, record, $"APIMart task succeeded but no image URL was found: {Shorten(pollResponse, 240)}");
                _isProcessing = false;
                yield break;
            }

            yield return DownloadStylizedImage(jobPath, record, imageUrl, resultPath);
            _isProcessing = false;
            yield break;
        }

        record.StatusNote = $"APIMart polling paused after {timeoutSeconds:0}s. Re-run polling to resume task {record.BackendTransformId}.";
        record.UpdatedAtIsoUtc = DateTime.UtcNow.ToString("O");
        File.WriteAllText(jobPath, JsonUtility.ToJson(record, true));
        PublishSummary("poll-timeout-paused");
        _isProcessing = false;
    }

    private IEnumerator DownloadStylizedImage(string jobPath, GeneratedAssetRecord record, string imageUrl, string resultPath)
    {
        var outputDirectory = GetOutputDirectory();
        Directory.CreateDirectory(outputDirectory);
        var outputPath = Path.Combine(outputDirectory, $"{record.RequestId}.stylized.png");

        using (var downloadRequest = UnityWebRequest.Get(imageUrl))
        {
            downloadRequest.downloadHandler = new DownloadHandlerFile(outputPath);
            yield return downloadRequest.SendWebRequest();

            if (downloadRequest.result != UnityWebRequest.Result.Success)
            {
                FailJob(jobPath, record, $"APIMart generated image download failed: {downloadRequest.responseCode} {downloadRequest.error}");
                yield break;
            }
        }

        WriteBackendResult(resultPath, record, outputPath, imageUrl);

        record.State = GeneratedObjectJobState.StylizedImageReady;
        record.BackendAdapterName = nameof(ApimartImageBackendAdapter);
        record.StylizedImagePath = outputPath;
        record.StylizedImageUrl = string.Empty;
        record.PreviewImagePath = outputPath;
        record.StatusNote = "APIMart generated stylized PNG downloaded locally; hosted upload bridge can now publish a stable image_url for Seed3D.";
        record.FailureReason = string.Empty;
        record.UpdatedAtIsoUtc = DateTime.UtcNow.ToString("O");
        File.WriteAllText(jobPath, JsonUtility.ToJson(record, true));

        _lastProcessedRecord = record;
        PublishSummary("stylized-image-ready");
        Debug.Log($"[ApimartImageBackendAdapter] Stylized image ready for request {record.RequestId} -> {outputPath}", this);
    }

    private string BuildCreateTaskJson(GeneratedAssetRecord record)
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
        AppendJsonProperty(builder, "size", size);

        if (includeReferenceImage)
        {
            builder.Append(',');
            builder.Append("\"image_urls\":[");
            AppendJsonString(builder, BuildDataUri(record.SourceInputImagePath));
            builder.Append(']');
        }

        builder.Append('}');
        return builder.ToString();
    }

    private static string BuildDataUri(string imagePath)
    {
        var extension = Path.GetExtension(imagePath)?.TrimStart('.').ToLowerInvariant();
        var mime = extension == "jpg" || extension == "jpeg" ? "image/jpeg" : "image/png";
        var base64 = Convert.ToBase64String(File.ReadAllBytes(imagePath));
        return $"data:{mime};base64,{base64}";
    }

    private void FailJob(string jobPath, GeneratedAssetRecord record, string reason)
    {
        record.State = GeneratedObjectJobState.Failed;
        record.BackendAdapterName = nameof(ApimartImageBackendAdapter);
        record.FailureReason = reason;
        record.StatusNote = reason;
        record.UpdatedAtIsoUtc = DateTime.UtcNow.ToString("O");
        File.WriteAllText(jobPath, JsonUtility.ToJson(record, true));

        _lastProcessedRecord = record;
        PublishSummary("failed");
        Debug.LogWarning($"[ApimartImageBackendAdapter] Job failed for request {record.RequestId}: {reason}", this);
    }

    private bool TryLoadJob(string jobPath, out GeneratedAssetRecord record)
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

        record = JsonUtility.FromJson<GeneratedAssetRecord>(json);
        return record != null && !string.IsNullOrWhiteSpace(record.RequestId);
    }

    private static void WriteBackendResult(string resultPath, GeneratedAssetRecord record, string outputImagePath, string sourceImageUrl)
    {
        var result = new GeneratedImageBackendResult
        {
            RequestId = record.RequestId,
            ObjectId = record.ObjectId,
            ThemeId = record.ThemeId,
            PromptVersion = record.PromptVersion,
            PromptArtifactPath = record.PromptArtifactPath,
            SourceInputImagePath = record.SourceInputImagePath,
            SourceRequestPath = record.SourceRequestPath,
            OutputImagePath = outputImagePath,
            OutputImageUrl = string.Empty,
            BackendAdapterName = nameof(ApimartImageBackendAdapter),
            AppliedTransformId = $"apimart_gpt_image_2:{record.BackendTransformId}",
            PromptArtifactConsumed = true,
            OutputState = GeneratedObjectJobState.StylizedImageReady,
            StatusNote = $"APIMart generated image downloaded from transient backend URL: {sourceImageUrl}",
            CreatedAtIsoUtc = DateTime.UtcNow.ToString("O"),
        };

        File.WriteAllText(resultPath, JsonUtility.ToJson(result, true));
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
        builder.AppendLine("[ApimartImageBackendAdapter]");
        builder.AppendLine($"State: {state}");
        builder.AppendLine($"Auto Process: {autoProcessJobsInPlay}");
        builder.AppendLine($"Endpoint: {generationEndpoint}");
        builder.AppendLine($"Model: {model}");
        builder.AppendLine($"Reference Image: {includeReferenceImage}");

        if (!string.IsNullOrWhiteSpace(_lastProcessedRecord.RequestId))
        {
            builder.AppendLine($"Last Request: {_lastProcessedRecord.RequestId}");
            builder.AppendLine($"Last State: {_lastProcessedRecord.State}");
            builder.AppendLine($"Task Id: {_lastProcessedRecord.BackendTransformId}");
            builder.AppendLine($"Stylized Image: {_lastProcessedRecord.StylizedImagePath}");
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
