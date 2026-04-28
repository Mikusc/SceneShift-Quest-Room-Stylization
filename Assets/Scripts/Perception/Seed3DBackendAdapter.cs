using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

[DisallowMultipleComponent]
public class Seed3DBackendAdapter : MonoBehaviour
{
    private const string LongRunningStatusMarker = "background polling";

    [Header("Ark Authentication")]
    [SerializeField] private string apiKeyEnvironmentVariable = "ARK_API_KEY";

    [Header("Seed3D Request")]
    [SerializeField] private string taskEndpoint = "https://ark.cn-beijing.volces.com/api/v3/contents/generations/tasks";
    [SerializeField] private string model = "doubao-seed3d-2-0-260328";
    [SerializeField] private string subdivisionLevel = "medium";
    [SerializeField] private string fileFormat = "glb";
    [SerializeField, TextArea(2, 5)] private string extraTextInstruction =
        "Generate one clean 3D model from the isolated stylized object image. Preserve proportions, footprint, and support/contact surfaces.";

    [Header("Polling")]
    [SerializeField] private bool autoProcessJobsInPlay = true;
    [SerializeField, Min(1)] private int maxConcurrentSeed3DJobs = 2;
    [SerializeField, Min(1f)] private float pollIntervalSeconds = 5f;
    [SerializeField, Min(15f)] private float timeoutSeconds = 900f;
    [SerializeField, Tooltip("After the normal timeout, keep checking long-running Seed3D tasks with one lightweight poll per interval instead of blocking a slot indefinitely.")]
    private bool useBackgroundPollingAfterTimeout = true;
    [SerializeField, Min(10f)] private float backgroundPollIntervalSeconds = 60f;

    [Header("Folders")]
    [SerializeField] private string jobFolderName = "GeneratedObjectJobs";
    [SerializeField] private string generatedAssetFolder = "Assets/Generated/ThemeAssets";
    [SerializeField] private string backendModelFolderName = "GeneratedObjectModels";

    public event Action SummaryChanged;

    public string LatestSummary => _latestSummary;
    public GeneratedAssetRecord LastProcessedRecord => _lastProcessedRecord;

    private GeneratedAssetRecord _lastProcessedRecord = new();
    private readonly HashSet<string> _activeJobPaths = new(StringComparer.OrdinalIgnoreCase);
    private bool _isProcessing;
    private float _nextProcessTime;
    private string _latestSummary =
        "[Seed3DBackendAdapter]\nState: waiting\nHint: provide ARK_API_KEY in the environment and a StylizedImageReady job with a public image_url.";

    private void OnEnable()
    {
        PublishSummary("enabled");
    }

    private void Update()
    {
        if (!Application.isPlaying || !autoProcessJobsInPlay)
        {
            return;
        }

        if (Time.unscaledTime < _nextProcessTime)
        {
            return;
        }

        _nextProcessTime = Time.unscaledTime + pollIntervalSeconds;
        ProcessNextReadyJob();
    }

    [ContextMenu("Process Next Seed3D Job")]
    public void ProcessNextReadyJob()
    {
        if (!HasSeed3DCapacity())
        {
            PublishSummary("at-seed3d-capacity");
            return;
        }

        var jobDirectory = GetLibraryDirectory(jobFolderName);
        if (!Directory.Exists(jobDirectory))
        {
            PublishSummary("waiting-for-job-folder");
            return;
        }

        var startedCount = 0;
        var deferredLongRunningCount = 0;
        var jobPaths = Directory.GetFiles(jobDirectory, "*.job.json", SearchOption.TopDirectoryOnly);
        for (var index = 0; index < jobPaths.Length; index++)
        {
            var jobPath = jobPaths[index];
            if (IsJobActive(jobPath))
            {
                continue;
            }

            if (!TryLoadJob(jobPath, out var record) || !IsSeed3DProcessableState(record.State))
            {
                continue;
            }

            if (useBackgroundPollingAfterTimeout &&
                IsBackgroundPollingRecord(record) &&
                !IsBackgroundPollDue(record))
            {
                deferredLongRunningCount++;
                continue;
            }

            StartCoroutine(RunTrackedJob(jobPath, record));
            startedCount++;
            if (!HasSeed3DCapacity())
            {
                break;
            }
        }

        PublishSummary(startedCount > 0
            ? "processing-batch"
            : deferredLongRunningCount > 0 ? "waiting-background-seed3d-poll" : "waiting-for-seed3d-job");
    }

    public string GetDebugSummary()
    {
        return _latestSummary;
    }

    private IEnumerator RunTrackedJob(string jobPath, GeneratedAssetRecord record)
    {
        var key = NormalizeJobPath(jobPath);
        _activeJobPaths.Add(key);
        _isProcessing = true;

        yield return ProcessJob(jobPath, record);

        _activeJobPaths.Remove(key);
        _isProcessing = _activeJobPaths.Count > 0;
        PublishSummary(_isProcessing ? "processing-batch" : "idle");
    }

    private bool HasSeed3DCapacity()
    {
        return _activeJobPaths.Count < Mathf.Max(1, maxConcurrentSeed3DJobs);
    }

    private bool IsJobActive(string jobPath)
    {
        return _activeJobPaths.Contains(NormalizeJobPath(jobPath));
    }

    private static string NormalizeJobPath(string jobPath)
    {
        return string.IsNullOrWhiteSpace(jobPath) ? string.Empty : Path.GetFullPath(jobPath);
    }

    private IEnumerator ProcessJob(string jobPath, GeneratedAssetRecord record)
    {
        _isProcessing = true;
        _lastProcessedRecord = record;
        PublishSummary("processing");

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

        if (record.State == GeneratedObjectJobState.ModelGenerationSubmitted)
        {
            if (string.IsNullOrWhiteSpace(record.ModelGenerationTaskId))
            {
                FailJob(jobPath, record, "Seed3D job was ModelGenerationSubmitted but had no task id.");
                _isProcessing = false;
                yield break;
            }

            if (useBackgroundPollingAfterTimeout && IsBackgroundPollingRecord(record))
            {
                yield return PollSubmittedTaskOnce(jobPath, record, apiKey, "background-poll");
            }
            else
            {
                yield return PollSubmittedTask(jobPath, record, apiKey);
            }

            _isProcessing = false;
            yield break;
        }

        var imageUrl = ResolveHostedStylizedImageUrl(record);
        if (!IsHttpUrl(imageUrl))
        {
            record.StatusNote = "Waiting for StylizedImageUrl, or an http(s) StylizedImagePath, before submitting Seed3D. Local files are not uploaded by this adapter.";
            record.UpdatedAtIsoUtc = DateTime.UtcNow.ToString("O");
            File.WriteAllText(jobPath, JsonUtility.ToJson(record, true));
            PublishSummary("waiting-for-hosted-image-url");
            _isProcessing = false;
            yield break;
        }

        var metadataDirectory = Path.Combine(GetLibraryDirectory(backendModelFolderName), record.RequestId);
        Directory.CreateDirectory(metadataDirectory);
        var requestPath = Path.Combine(metadataDirectory, $"{record.RequestId}.seed3d.request.json");
        var resultPath = Path.Combine(metadataDirectory, $"{record.RequestId}.seed3d.result.json");
        EnrichRecordFromSourceRequest(record);
        var requestJson = BuildCreateTaskJson(record, imageUrl);
        File.WriteAllText(requestPath, requestJson);

        string createResponse;
        using (var createRequest = new UnityWebRequest(taskEndpoint, "POST"))
        {
            createRequest.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(requestJson));
            createRequest.downloadHandler = new DownloadHandlerBuffer();
            createRequest.SetRequestHeader("Content-Type", "application/json");
            createRequest.SetRequestHeader("Authorization", $"Bearer {apiKey}");

            yield return createRequest.SendWebRequest();

            createResponse = createRequest.downloadHandler != null ? createRequest.downloadHandler.text : string.Empty;
            File.WriteAllText(resultPath, createResponse ?? string.Empty);
            if (createRequest.result != UnityWebRequest.Result.Success)
            {
                FailJob(jobPath, record, $"Seed3D create task request failed: {createRequest.responseCode} {createRequest.error}");
                _isProcessing = false;
                yield break;
            }
        }

        if (!TryExtractTaskId(createResponse, out var taskId))
        {
            FailJob(jobPath, record, "Seed3D create task response did not include a task id.");
            _isProcessing = false;
            yield break;
        }

        record.BackendAdapterName = nameof(Seed3DBackendAdapter);
        record.State = GeneratedObjectJobState.ModelGenerationSubmitted;
        record.ModelGenerationTaskId = taskId;
        record.ModelGenerationRequestPath = requestPath;
        record.ModelGenerationResultPath = resultPath;
        record.StatusNote = "Seed3D task submitted; polling for model output.";
        record.UpdatedAtIsoUtc = DateTime.UtcNow.ToString("O");
        File.WriteAllText(jobPath, JsonUtility.ToJson(record, true));

        yield return PollSubmittedTask(jobPath, record, apiKey);
        _isProcessing = false;
    }

    private IEnumerator PollSubmittedTask(string jobPath, GeneratedAssetRecord record, string apiKey)
    {
        if (record == null || string.IsNullOrWhiteSpace(record.ModelGenerationTaskId))
        {
            yield break;
        }

        var resultPath = EnsureModelResultPath(record);

        var startedAt = Time.realtimeSinceStartup;
        var queryUrl = $"{taskEndpoint.TrimEnd('/')}/{UnityWebRequest.EscapeURL(record.ModelGenerationTaskId)}";
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
                    FailJob(jobPath, record, $"Seed3D poll request failed: {pollRequest.responseCode} {pollRequest.error}");
                    _isProcessing = false;
                    yield break;
                }
            }

            if (IsFailureStatus(pollResponse, out var failedStatus))
            {
                FailJob(jobPath, record, $"Seed3D task failed with status '{failedStatus}'.");
                _isProcessing = false;
                yield break;
            }

            if (!IsSuccessStatus(pollResponse))
            {
                UpdateRunningPollStatus(jobPath, record, Time.realtimeSinceStartup - startedAt);
                PublishSummary("polling");
                continue;
            }

            if (!TryExtractModelUrl(pollResponse, out var modelUrl))
            {
                FailJob(jobPath, record, "Seed3D task succeeded but no downloadable model URL was found.");
                _isProcessing = false;
                yield break;
            }

            yield return DownloadModel(jobPath, record, modelUrl, resultPath);
            yield break;
        }

        record.State = GeneratedObjectJobState.ModelGenerationSubmitted;
        record.StatusNote = useBackgroundPollingAfterTimeout
            ? $"Seed3D still running after {timeoutSeconds:0}s; {LongRunningStatusMarker} will resume every {backgroundPollIntervalSeconds:0}s for task {record.ModelGenerationTaskId}."
            : $"Seed3D polling paused after {timeoutSeconds:0}s without a terminal result. Re-run polling to resume task {record.ModelGenerationTaskId}.";
        record.UpdatedAtIsoUtc = DateTime.UtcNow.ToString("O");
        File.WriteAllText(jobPath, JsonUtility.ToJson(record, true));
        _lastProcessedRecord = record;
        PublishSummary(useBackgroundPollingAfterTimeout ? "poll-timeout-background" : "poll-timeout-paused");
    }

    private IEnumerator PollSubmittedTaskOnce(string jobPath, GeneratedAssetRecord record, string apiKey, string summaryState)
    {
        if (record == null || string.IsNullOrWhiteSpace(record.ModelGenerationTaskId))
        {
            yield break;
        }

        var resultPath = EnsureModelResultPath(record);
        var queryUrl = $"{taskEndpoint.TrimEnd('/')}/{UnityWebRequest.EscapeURL(record.ModelGenerationTaskId)}";

        string pollResponse;
        using (var pollRequest = UnityWebRequest.Get(queryUrl))
        {
            pollRequest.SetRequestHeader("Authorization", $"Bearer {apiKey}");
            yield return pollRequest.SendWebRequest();

            pollResponse = pollRequest.downloadHandler != null ? pollRequest.downloadHandler.text : string.Empty;
            File.WriteAllText(resultPath, pollResponse ?? string.Empty);
            if (pollRequest.result != UnityWebRequest.Result.Success)
            {
                FailJob(jobPath, record, $"Seed3D poll request failed: {pollRequest.responseCode} {pollRequest.error}");
                _isProcessing = false;
                yield break;
            }
        }

        if (IsFailureStatus(pollResponse, out var failedStatus))
        {
            FailJob(jobPath, record, $"Seed3D task failed with status '{failedStatus}'.");
            _isProcessing = false;
            yield break;
        }

        if (!IsSuccessStatus(pollResponse))
        {
            record.State = GeneratedObjectJobState.ModelGenerationSubmitted;
            record.StatusNote = $"Seed3D still running; {LongRunningStatusMarker} checked task {record.ModelGenerationTaskId}. Next check after about {backgroundPollIntervalSeconds:0}s.";
            record.UpdatedAtIsoUtc = DateTime.UtcNow.ToString("O");
            File.WriteAllText(jobPath, JsonUtility.ToJson(record, true));
            _lastProcessedRecord = record;
            PublishSummary(summaryState);
            yield break;
        }

        if (!TryExtractModelUrl(pollResponse, out var modelUrl))
        {
            FailJob(jobPath, record, "Seed3D task succeeded but no downloadable model URL was found.");
            _isProcessing = false;
            yield break;
        }

        yield return DownloadModel(jobPath, record, modelUrl, resultPath);
    }

    private IEnumerator DownloadModel(string jobPath, GeneratedAssetRecord record, string modelUrl, string resultPath)
    {
        var outputDirectory = Path.GetFullPath(Path.Combine(GetProjectRoot(), generatedAssetFolder, record.RequestId));
        Directory.CreateDirectory(outputDirectory);

        var extension = NormalizeFileFormat(fileFormat);
        var outputPath = Path.Combine(outputDirectory, $"{record.RequestId}.seed3d.generated.{extension}");
        var metadataDirectory = string.IsNullOrWhiteSpace(resultPath)
            ? Path.Combine(GetLibraryDirectory(backendModelFolderName), record.RequestId)
            : Path.GetDirectoryName(resultPath);
        if (string.IsNullOrWhiteSpace(metadataDirectory))
        {
            metadataDirectory = Path.Combine(GetLibraryDirectory(backendModelFolderName), record.RequestId);
        }

        Directory.CreateDirectory(metadataDirectory);

        var downloadExtension = ResolveDownloadExtension(modelUrl, extension);
        var downloadPath = Path.Combine(metadataDirectory, $"{record.RequestId}.seed3d.downloaded.{downloadExtension}");

        using (var downloadRequest = UnityWebRequest.Get(modelUrl))
        {
            downloadRequest.downloadHandler = new DownloadHandlerFile(downloadPath);
            yield return downloadRequest.SendWebRequest();

            if (downloadRequest.result != UnityWebRequest.Result.Success)
            {
                FailJob(jobPath, record, $"Seed3D model download failed: {downloadRequest.responseCode} {downloadRequest.error}");
                yield break;
            }
        }

        if (!TryPrepareDownloadedModel(record.RequestId, downloadPath, outputDirectory, extension, outputPath, out var modelAssetPath, out var prepareError))
        {
            FailJob(jobPath, record, prepareError);
            yield break;
        }

        record.State = GeneratedObjectJobState.ModelReady;
        record.BackendAdapterName = nameof(Seed3DBackendAdapter);
        record.BackendTransformId = $"seed3d_2_0_260328_{subdivisionLevel}_{extension}";
        record.ModelGenerationResultPath = resultPath;
        record.GeneratedModelPath = modelAssetPath;
        record.PreviewImagePath = !string.IsNullOrWhiteSpace(record.StylizedImagePath)
            ? record.StylizedImagePath
            : record.StylizedImageUrl;
        record.StatusNote = IsZipFile(downloadPath)
            ? "Seed3D returned a zip package; extracted the real model asset and advanced the job to ModelReady."
            : "Seed3D model downloaded and job advanced to ModelReady.";
        record.FailureReason = string.Empty;
        record.UpdatedAtIsoUtc = DateTime.UtcNow.ToString("O");
        File.WriteAllText(jobPath, JsonUtility.ToJson(record, true));

        _lastProcessedRecord = record;
        PublishSummary("model-ready");
        Debug.Log($"[Seed3DBackendAdapter] Seed3D model ready for request {record.RequestId} -> {modelAssetPath}", this);
    }

    private bool TryPrepareDownloadedModel(
        string requestId,
        string downloadPath,
        string outputDirectory,
        string extension,
        string directOutputPath,
        out string modelAssetPath,
        out string error)
    {
        modelAssetPath = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(downloadPath) || !File.Exists(downloadPath))
        {
            error = "Seed3D download completed but the downloaded file is missing.";
            return false;
        }

        if (extension == "glb" && IsGlbFile(downloadPath))
        {
            File.Copy(downloadPath, directOutputPath, true);
            modelAssetPath = directOutputPath;
            return true;
        }

        if (!IsZipFile(downloadPath))
        {
            error = $"Seed3D downloaded file was neither a {extension} model nor a zip package: {downloadPath}";
            return false;
        }

        var packageDirectory = Path.Combine(Path.GetDirectoryName(downloadPath), "downloaded_package");
        if (Directory.Exists(packageDirectory))
        {
            Directory.Delete(packageDirectory, true);
        }

        Directory.CreateDirectory(packageDirectory);
        ZipFile.ExtractToDirectory(downloadPath, packageDirectory);

        var candidates = Directory.GetFiles(packageDirectory, $"*.{extension}", SearchOption.AllDirectories);
        if (candidates == null || candidates.Length == 0)
        {
            error = $"Seed3D zip package did not contain a .{extension} model.";
            return false;
        }

        var selectedModelPath = SelectLargestFile(candidates);
        if (string.IsNullOrWhiteSpace(selectedModelPath))
        {
            error = $"Seed3D zip package contained .{extension} files, but no readable model file was selected.";
            return false;
        }

        var extractedOutputPath = Path.Combine(outputDirectory, $"{requestId}.seed3d.pbr.{extension}");
        File.Copy(selectedModelPath, extractedOutputPath, true);
        modelAssetPath = extractedOutputPath;
        return true;
    }

    private string BuildCreateTaskJson(GeneratedAssetRecord record, string imageUrl)
    {
        var promptText = BuildSeed3DTextPrompt(record);
        return "{\n" +
               $"  \"model\": \"{EscapeJson(model)}\",\n" +
               "  \"content\": [\n" +
               $"    {{ \"type\": \"text\", \"text\": \"{EscapeJson(promptText)}\" }},\n" +
               $"    {{ \"type\": \"image_url\", \"image_url\": {{ \"url\": \"{EscapeJson(imageUrl)}\" }} }}\n" +
               "  ]\n" +
               "}";
    }

    private string BuildSeed3DTextPrompt(GeneratedAssetRecord record)
    {
        var builder = new StringBuilder(512);
        builder.Append(extraTextInstruction.Trim());
        builder.Append(' ');
        builder.Append("--subdivisionlevel ");
        builder.Append(SanitizeCommandToken(subdivisionLevel));
        builder.Append(" --fileformat ");
        builder.Append(SanitizeCommandToken(fileFormat));
        builder.Append(". Target physical size: length=");
        builder.Append(FormatFloat(record.TargetLengthMeters));
        builder.Append("m, width=");
        builder.Append(FormatFloat(record.TargetWidthMeters));
        builder.Append("m, height=");
        builder.Append(FormatFloat(record.TargetHeightMeters));
        builder.Append("m, length/width aspect ratio=");
        builder.Append(FormatFloat(record.TargetAspectRatio));
        builder.Append(", safety footprint scale=");
        builder.Append(FormatFloat(record.SafetyFootprintScale));
        builder.Append(", vertical fit mode=");
        builder.Append(record.VerticalFitMode);
        builder.Append(". Preserve bottom support, tabletop/contact surfaces, and dominant yaw for MR scaffold registration.");
        return builder.ToString();
    }

    private void FailJob(string jobPath, GeneratedAssetRecord record, string reason)
    {
        record.State = GeneratedObjectJobState.Failed;
        record.BackendAdapterName = nameof(Seed3DBackendAdapter);
        record.FailureReason = reason;
        record.StatusNote = reason;
        record.UpdatedAtIsoUtc = DateTime.UtcNow.ToString("O");
        File.WriteAllText(jobPath, JsonUtility.ToJson(record, true));

        _lastProcessedRecord = record;
        PublishSummary("failed");
        Debug.LogWarning($"[Seed3DBackendAdapter] Job failed for request {record.RequestId}: {reason}", this);
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

    private static bool IsSeed3DProcessableState(GeneratedObjectJobState state)
    {
        return state == GeneratedObjectJobState.StylizedImageReady ||
               state == GeneratedObjectJobState.ModelGenerationSubmitted;
    }

    private string EnsureModelResultPath(GeneratedAssetRecord record)
    {
        var resultPath = record.ModelGenerationResultPath;
        if (!string.IsNullOrWhiteSpace(resultPath))
        {
            var existingDirectory = Path.GetDirectoryName(resultPath);
            if (!string.IsNullOrWhiteSpace(existingDirectory))
            {
                Directory.CreateDirectory(existingDirectory);
            }

            return resultPath;
        }

        var metadataDirectory = Path.Combine(GetLibraryDirectory(backendModelFolderName), record.RequestId);
        Directory.CreateDirectory(metadataDirectory);
        resultPath = Path.Combine(metadataDirectory, $"{record.RequestId}.seed3d.result.json");
        record.ModelGenerationResultPath = resultPath;
        return resultPath;
    }

    private void UpdateRunningPollStatus(string jobPath, GeneratedAssetRecord record, float elapsedSeconds)
    {
        record.State = GeneratedObjectJobState.ModelGenerationSubmitted;
        record.StatusNote = $"Seed3D still running; elapsed {elapsedSeconds:0}s / {timeoutSeconds:0}s before timeout. Task {record.ModelGenerationTaskId}.";
        record.UpdatedAtIsoUtc = DateTime.UtcNow.ToString("O");
        File.WriteAllText(jobPath, JsonUtility.ToJson(record, true));
        _lastProcessedRecord = record;
    }

    private static bool IsBackgroundPollingRecord(GeneratedAssetRecord record)
    {
        if (record == null || record.State != GeneratedObjectJobState.ModelGenerationSubmitted)
        {
            return false;
        }

        var statusNote = record.StatusNote ?? string.Empty;
        return statusNote.IndexOf(LongRunningStatusMarker, StringComparison.OrdinalIgnoreCase) >= 0 ||
               statusNote.IndexOf("polling paused after", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private bool IsBackgroundPollDue(GeneratedAssetRecord record)
    {
        if (!IsBackgroundPollingRecord(record))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(record.UpdatedAtIsoUtc) ||
            !DateTime.TryParse(
                record.UpdatedAtIsoUtc,
                null,
                System.Globalization.DateTimeStyles.RoundtripKind,
                out var updatedAt))
        {
            return true;
        }

        return DateTime.UtcNow - updatedAt.ToUniversalTime() >= TimeSpan.FromSeconds(Mathf.Max(10f, backgroundPollIntervalSeconds));
    }

    private static void EnrichRecordFromSourceRequest(GeneratedAssetRecord record)
    {
        if (record == null ||
            string.IsNullOrWhiteSpace(record.SourceRequestPath) ||
            !File.Exists(record.SourceRequestPath))
        {
            return;
        }

        var requestJson = File.ReadAllText(record.SourceRequestPath);
        if (string.IsNullOrWhiteSpace(requestJson))
        {
            return;
        }

        var request = JsonUtility.FromJson<GeneratedObjectRequest>(requestJson);
        if (request == null || string.IsNullOrWhiteSpace(request.RequestId))
        {
            return;
        }

        if (record.TargetLengthMeters <= 0f)
        {
            record.TargetLengthMeters = request.TargetLengthMeters;
        }

        if (record.TargetWidthMeters <= 0f)
        {
            record.TargetWidthMeters = request.TargetWidthMeters;
        }

        if (record.TargetHeightMeters <= 0f)
        {
            record.TargetHeightMeters = request.TargetHeightMeters;
        }

        if (record.TargetAspectRatio <= 0f)
        {
            record.TargetAspectRatio = request.TargetAspectRatio;
        }

        if (record.SafetyFootprintScale <= 0f || Mathf.Approximately(record.SafetyFootprintScale, 1f))
        {
            record.SafetyFootprintScale = request.SafetyFootprintScale;
        }

        record.VerticalFitMode = request.VerticalFitMode;
    }

    private string GetLibraryDirectory(string folderName)
    {
        var libraryRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Library"));
        return Path.Combine(libraryRoot, folderName);
    }

    private static string GetProjectRoot()
    {
        return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    }

    private static bool IsHttpUrl(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private static string ResolveHostedStylizedImageUrl(GeneratedAssetRecord record)
    {
        if (record == null)
        {
            return string.Empty;
        }

        if (IsHttpUrl(record.StylizedImageUrl))
        {
            return record.StylizedImageUrl;
        }

        return IsHttpUrl(record.StylizedImagePath) ? record.StylizedImagePath : string.Empty;
    }

    private static bool TryExtractTaskId(string json, out string taskId)
    {
        return TryExtractString(json, "task_id", out taskId) ||
               TryExtractString(json, "id", out taskId);
    }

    private static bool TryExtractModelUrl(string json, out string modelUrl)
    {
        return TryExtractString(json, "file_url", out modelUrl) ||
               TryExtractString(json, "model_url", out modelUrl) ||
               TryExtractString(json, "download_url", out modelUrl) ||
               TryExtractFirstUrl(json, out modelUrl);
    }

    private static bool IsSuccessStatus(string json)
    {
        return TryExtractString(json, "status", out var status) &&
               (status.Equals("succeeded", StringComparison.OrdinalIgnoreCase) ||
                status.Equals("success", StringComparison.OrdinalIgnoreCase) ||
                status.Equals("completed", StringComparison.OrdinalIgnoreCase));
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

    private static bool TryExtractFirstUrl(string json, out string url)
    {
        url = string.Empty;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        var match = Regex.Match(json, "\"(?<url>https?://[^\"]+)\"", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return false;
        }

        url = Regex.Unescape(match.Groups["url"].Value);
        return true;
    }

    private static string NormalizeFileFormat(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "glb";
        }

        var trimmed = value.Trim().TrimStart('.').ToLowerInvariant();
        return Regex.Replace(trimmed, "[^a-z0-9]", string.Empty);
    }

    private static string ResolveDownloadExtension(string url, string fallbackExtension)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var extension = Path.GetExtension(uri.AbsolutePath);
            if (!string.IsNullOrWhiteSpace(extension))
            {
                return NormalizeFileFormat(extension);
            }
        }

        return string.IsNullOrWhiteSpace(fallbackExtension) ? "bin" : fallbackExtension;
    }

    private static bool IsGlbFile(string path)
    {
        return FileStartsWith(path, new byte[] { 0x67, 0x6c, 0x54, 0x46 });
    }

    private static bool IsZipFile(string path)
    {
        return FileStartsWith(path, new byte[] { 0x50, 0x4b, 0x03, 0x04 });
    }

    private static bool FileStartsWith(string path, byte[] expectedBytes)
    {
        if (string.IsNullOrWhiteSpace(path) || expectedBytes == null || expectedBytes.Length == 0 || !File.Exists(path))
        {
            return false;
        }

        var buffer = new byte[expectedBytes.Length];
        using (var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            if (stream.Read(buffer, 0, buffer.Length) != buffer.Length)
            {
                return false;
            }
        }

        for (var index = 0; index < expectedBytes.Length; index++)
        {
            if (buffer[index] != expectedBytes[index])
            {
                return false;
            }
        }

        return true;
    }

    private static string SelectLargestFile(string[] paths)
    {
        if (paths == null || paths.Length == 0)
        {
            return string.Empty;
        }

        string selectedPath = null;
        var selectedLength = -1L;
        for (var index = 0; index < paths.Length; index++)
        {
            var path = paths[index];
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                continue;
            }

            var length = new FileInfo(path).Length;
            if (length <= selectedLength)
            {
                continue;
            }

            selectedPath = path;
            selectedLength = length;
        }

        return selectedPath ?? string.Empty;
    }

    private static string SanitizeCommandToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return Regex.Replace(value.Trim().ToLowerInvariant(), "[^a-z0-9_-]", string.Empty);
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

    private static string FormatFloat(float value)
    {
        return value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
    }

    private void PublishSummary(string state)
    {
        var builder = new StringBuilder(512);
        builder.AppendLine("[Seed3DBackendAdapter]");
        builder.AppendLine($"State: {state}");
        builder.AppendLine($"Auto Process: {autoProcessJobsInPlay}");
        builder.AppendLine($"Active Jobs: {_activeJobPaths.Count}/{Mathf.Max(1, maxConcurrentSeed3DJobs)}");
        builder.AppendLine($"Endpoint: {taskEndpoint}");
        builder.AppendLine($"Model: {model}");
        builder.AppendLine($"Subdivision: {subdivisionLevel}");
        builder.AppendLine($"File Format: {fileFormat}");

        if (!string.IsNullOrWhiteSpace(_lastProcessedRecord.RequestId))
        {
            builder.AppendLine($"Last Request: {_lastProcessedRecord.RequestId}");
            builder.AppendLine($"Last State: {_lastProcessedRecord.State}");
            builder.AppendLine($"Task Id: {_lastProcessedRecord.ModelGenerationTaskId}");
            builder.AppendLine($"Generated Model: {_lastProcessedRecord.GeneratedModelPath}");
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
