using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

[DisallowMultipleComponent]
public sealed class QuestRuntimeGenerationClient : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private DevicePassthroughCaptureService captureService;
    [SerializeField] private RuntimeGeneratedModelLoader runtimeGeneratedModelLoader;
    [SerializeField] private GenerationQueueStatusService generationQueueStatusService;

    [Header("Folders")]
    [SerializeField] private string generatedObjectJobFolderName = "GeneratedObjectJobs";
#if UNITY_EDITOR
    [SerializeField] private bool includeEditorLibraryJobs = true;
#endif

    [Header("Backend")]
    [SerializeField] private RuntimeGenerationClientMode clientMode = RuntimeGenerationClientMode.LocalTestModelUrl;
    [SerializeField] private string backendSubmitUrl;
    [SerializeField] private string localTestModelUrl = string.Empty;
    [SerializeField, Min(1f)] private float requestTimeoutSeconds = 30f;
    [SerializeField] private bool sendCapturedImageWithBackendRequest = true;
    [SerializeField] private bool preferCroppedSourceImage = true;

    [Header("Backend Polling")]
    [SerializeField, Min(1f)] private float backendPollIntervalSeconds = 5f;
    [SerializeField, Min(5f)] private float backendMaxPollSeconds = 900f;
    [SerializeField, Min(1)] private int backendMaxPollAttempts = 180;

    [Header("Runtime Behavior")]
    [SerializeField] private bool autoLoadReadyModel = true;
    [SerializeField] private bool writeBackendArtifacts = true;

    public event Action SummaryChanged;

    public string LatestSummary => latestSummary;
    public GeneratedAssetRecord LastSubmittedRecord => lastSubmittedRecord;
    public string LastSubmittedJobPath => lastSubmittedJobPath;

    private GeneratedAssetRecord lastSubmittedRecord;
    private string lastSubmittedJobPath = string.Empty;
    private string latestSummary = "[QuestRuntimeGenerationClient]\nState: waiting\nHint: capture a generated-object request, then submit it to a runtime backend.";

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
    }

    [ContextMenu("Submit Latest Request")]
    public async void SubmitLatestRequest()
    {
        await SubmitLatestRequestAsync(false);
    }

    [ContextMenu("Submit Latest Request And Load")]
    public async void SubmitLatestRequestAndLoad()
    {
        await SubmitLatestRequestAsync(true);
    }

    public async Task<GeneratedAssetRecord> SubmitLatestRequestAsync(bool loadWhenReady)
    {
        ResolveReferences();
        if (!TryResolveLatestJob(out var record, out var jobPath))
        {
            PublishSummary("submit", "No generated-object job was found. Capture or queue one target first.");
            return null;
        }

        var sourceRequest = TryReadJson<GeneratedObjectRequest>(record.SourceRequestPath);
        MarkBackendSubmitted(record, jobPath);
        var submission = BuildSubmission(record, sourceRequest);
        if (writeBackendArtifacts)
        {
            record.RuntimeBackendSubmissionPath = WriteRuntimeBackendArtifact(
                jobPath,
                record.RequestId,
                "runtime-submission",
                JsonUtility.ToJson(submission, true));
            WriteRecord(jobPath, record);
        }

        RuntimeGenerationBackendResult backendResult;
        if (clientMode == RuntimeGenerationClientMode.LocalTestModelUrl)
        {
            backendResult = BuildLocalTestResult(record, sourceRequest);
        }
        else
        {
            backendResult = await SubmitToHttpBackendAsync(record, jobPath, submission);
        }

        if (backendResult == null)
        {
            FailRecord(record, jobPath, "Runtime backend did not return a result.");
            return record;
        }

        if (writeBackendArtifacts)
        {
            record.RuntimeBackendResultPath = WriteRuntimeBackendArtifact(
                jobPath,
                record.RequestId,
                "runtime-result",
                JsonUtility.ToJson(backendResult, true));
        }

        ApplyBackendResult(record, jobPath, backendResult);
        lastSubmittedRecord = record;
        lastSubmittedJobPath = jobPath;
        generationQueueStatusService?.Refresh();

        if (loadWhenReady || autoLoadReadyModel)
        {
            await TryLoadReadyModelAsync(record, jobPath);
        }

        return record;
    }

    private void MarkBackendSubmitted(GeneratedAssetRecord record, string jobPath)
    {
        record.State = GeneratedObjectJobState.RuntimeBackendSubmitted;
        record.StatusNote = clientMode == RuntimeGenerationClientMode.LocalTestModelUrl
            ? "Runtime generation submitted to local test backend."
            : "Runtime generation submitted to configured backend endpoint.";
        record.FailureReason = string.Empty;
        record.RuntimeBackendSubmissionPath = string.Empty;
        record.RuntimeBackendResultPath = string.Empty;
        record.UpdatedAtIsoUtc = DateTime.UtcNow.ToString("O");
        WriteRecord(jobPath, record);
        PublishSummary("submitted", ShortId(record.RequestId));
    }

    private RuntimeGenerationBackendResult BuildLocalTestResult(GeneratedAssetRecord record, GeneratedObjectRequest request)
    {
        if (string.IsNullOrWhiteSpace(localTestModelUrl))
        {
            return new RuntimeGenerationBackendResult
            {
                RequestId = record.RequestId,
                ObjectId = record.ObjectId,
                ThemeId = record.ThemeId,
                StyleVariantId = NormalizeStyleVariantId(record.StyleVariantId),
                OutputState = GeneratedObjectJobState.Failed,
                FailureReason = "Local test model URL is empty.",
                StatusNote = "Local runtime generation failed.",
                CreatedAtIsoUtc = DateTime.UtcNow.ToString("O"),
            };
        }

        return new RuntimeGenerationBackendResult
        {
            RequestId = record.RequestId,
            ObjectId = record.ObjectId,
            ThemeId = record.ThemeId,
            StyleVariantId = NormalizeStyleVariantId(record.StyleVariantId),
            RuntimeBackendJobId = $"local-test-{ShortId(record.RequestId)}",
            RuntimeModelUrl = localTestModelUrl,
            OutputState = GeneratedObjectJobState.RuntimeModelReady,
            StatusNote = $"Local test backend returned a fixed model for {request?.SemanticLabel ?? record.ObjectId ?? "object"}.",
            CreatedAtIsoUtc = DateTime.UtcNow.ToString("O"),
        };
    }

    private async Task<RuntimeGenerationBackendResult> SubmitToHttpBackendAsync(
        GeneratedAssetRecord record,
        string jobPath,
        RuntimeGenerationBackendSubmission submission)
    {
        if (string.IsNullOrWhiteSpace(backendSubmitUrl))
        {
            return new RuntimeGenerationBackendResult
            {
                RequestId = record.RequestId,
                ObjectId = record.ObjectId,
                ThemeId = record.ThemeId,
                StyleVariantId = NormalizeStyleVariantId(record.StyleVariantId),
                OutputState = GeneratedObjectJobState.Failed,
                FailureReason = "Runtime backend submit URL is empty.",
                StatusNote = "Runtime backend submission failed.",
                CreatedAtIsoUtc = DateTime.UtcNow.ToString("O"),
            };
        }

        if (!IsPermittedBackendSubmitUrl(backendSubmitUrl, out var backendUrlError))
        {
            return new RuntimeGenerationBackendResult
            {
                RequestId = record.RequestId,
                ObjectId = record.ObjectId,
                ThemeId = record.ThemeId,
                StyleVariantId = NormalizeStyleVariantId(record.StyleVariantId),
                OutputState = GeneratedObjectJobState.Failed,
                FailureReason = backendUrlError,
                StatusNote = "Runtime backend submission failed.",
                CreatedAtIsoUtc = DateTime.UtcNow.ToString("O"),
            };
        }

        var initialResult = await PostRuntimeBackendSubmissionAsync(record, submission);
        if (initialResult == null)
        {
            return null;
        }

        if (!ShouldPollBackendResult(initialResult))
        {
            return initialResult;
        }

        ApplyBackendResult(record, jobPath, initialResult);
        generationQueueStatusService?.Refresh();
        return await PollRuntimeBackendUntilTerminalAsync(record, jobPath, initialResult);
    }

    private async Task<RuntimeGenerationBackendResult> PostRuntimeBackendSubmissionAsync(
        GeneratedAssetRecord record,
        RuntimeGenerationBackendSubmission submission)
    {
        var submissionJson = JsonUtility.ToJson(submission, true);
        UnityWebRequest requestMessage;
        if (sendCapturedImageWithBackendRequest)
        {
            if (!TryReadBackendImageBytes(submission, out var imageBytes, out var imageError))
            {
                return BuildHttpFailureResult(record, $"Captured source image is not uploadable: {imageError}");
            }

            var form = new WWWForm();
            form.AddField("metadata", submissionJson, Encoding.UTF8);
            if (!string.IsNullOrWhiteSpace(submission.SourceRequestJson))
            {
                form.AddField("request_json", submission.SourceRequestJson, Encoding.UTF8);
            }

            if (!string.IsNullOrWhiteSpace(submission.PromptText))
            {
                form.AddField("prompt_text", submission.PromptText, Encoding.UTF8);
            }

            form.AddBinaryData(
                "image",
                imageBytes,
                string.IsNullOrWhiteSpace(submission.SourceImageFileName) ? $"{record.RequestId}.png" : submission.SourceImageFileName,
                string.IsNullOrWhiteSpace(submission.SourceImageMimeType) ? "image/png" : submission.SourceImageMimeType);
            requestMessage = UnityWebRequest.Post(backendSubmitUrl, form);
        }
        else
        {
            requestMessage = new UnityWebRequest(backendSubmitUrl, UnityWebRequest.kHttpVerbPOST);
            var body = Encoding.UTF8.GetBytes(submissionJson);
            requestMessage.uploadHandler = new UploadHandlerRaw(body);
            requestMessage.downloadHandler = new DownloadHandlerBuffer();
            requestMessage.SetRequestHeader("Content-Type", "application/json");
        }

        using (requestMessage)
        {
            requestMessage.downloadHandler ??= new DownloadHandlerBuffer();
            requestMessage.SetRequestHeader("Accept", "application/json");
            requestMessage.timeout = Mathf.Max(1, Mathf.CeilToInt(requestTimeoutSeconds));

            var result = await SendRequestAsync(requestMessage);
            if (result != UnityWebRequest.Result.Success)
            {
                return BuildHttpFailureResult(
                    record,
                    $"{requestMessage.responseCode} {requestMessage.error}",
                    requestMessage.downloadHandler != null ? requestMessage.downloadHandler.text : string.Empty);
            }

            return ParseBackendResult(record, requestMessage.downloadHandler.text);
        }
    }

    private async Task<RuntimeGenerationBackendResult> PollRuntimeBackendUntilTerminalAsync(
        GeneratedAssetRecord record,
        string jobPath,
        RuntimeGenerationBackendResult initialResult)
    {
        var current = initialResult;
        var startedAt = DateTime.UtcNow;
        var attempts = 0;
        while (ShouldPollBackendResult(current))
        {
            if (attempts >= Mathf.Max(1, backendMaxPollAttempts) ||
                DateTime.UtcNow - startedAt > TimeSpan.FromSeconds(Mathf.Max(5f, backendMaxPollSeconds)))
            {
                return BuildHttpFailureResult(
                    record,
                    $"Runtime backend polling timed out after {attempts} attempts / {backendMaxPollSeconds:0}s.");
            }

            var statusUrl = ResolveRuntimeBackendStatusUrl(current);
            if (string.IsNullOrWhiteSpace(statusUrl))
            {
                return current;
            }

            await DelaySecondsAsync(backendPollIntervalSeconds);
            attempts++;

            using var pollRequest = UnityWebRequest.Get(statusUrl);
            pollRequest.SetRequestHeader("Accept", "application/json");
            pollRequest.timeout = Mathf.Max(1, Mathf.CeilToInt(requestTimeoutSeconds));

            var pollResult = await SendRequestAsync(pollRequest);
            if (pollResult != UnityWebRequest.Result.Success)
            {
                return BuildHttpFailureResult(
                    record,
                    $"Runtime backend poll failed: {pollRequest.responseCode} {pollRequest.error}",
                    pollRequest.downloadHandler != null ? pollRequest.downloadHandler.text : string.Empty);
            }

            current = ParseBackendResult(record, pollRequest.downloadHandler.text);
            if (current == null)
            {
                return BuildHttpFailureResult(record, "Runtime backend poll returned an empty result.");
            }

            ApplyBackendResult(record, jobPath, current);
            generationQueueStatusService?.Refresh();
        }

        return current;
    }

    private RuntimeGenerationBackendResult BuildHttpFailureResult(
        GeneratedAssetRecord record,
        string reason,
        string responseBody = "")
    {
        var detail = string.IsNullOrWhiteSpace(responseBody)
            ? reason
            : $"{reason}; body={TruncateForStatus(responseBody, 240)}";
        return new RuntimeGenerationBackendResult
        {
            RequestId = record.RequestId,
            ObjectId = record.ObjectId,
            ThemeId = record.ThemeId,
            StyleVariantId = NormalizeStyleVariantId(record.StyleVariantId),
            OutputState = GeneratedObjectJobState.Failed,
            FailureReason = detail,
            StatusNote = "Runtime backend submission failed.",
            CreatedAtIsoUtc = DateTime.UtcNow.ToString("O"),
        };
    }

    private RuntimeGenerationBackendResult ParseBackendResult(GeneratedAssetRecord record, string responseJson)
    {
        if (string.IsNullOrWhiteSpace(responseJson))
        {
            return null;
        }

        var parsed = JsonUtility.FromJson<RuntimeGenerationBackendResult>(responseJson);
        if (parsed == null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(parsed.RequestId))
        {
            parsed.RequestId = record.RequestId;
        }

        if (string.IsNullOrWhiteSpace(parsed.ObjectId))
        {
            parsed.ObjectId = record.ObjectId;
        }

        if (string.IsNullOrWhiteSpace(parsed.ThemeId))
        {
            parsed.ThemeId = record.ThemeId;
        }

        if (string.IsNullOrWhiteSpace(parsed.StyleVariantId))
        {
            parsed.StyleVariantId = NormalizeStyleVariantId(record.StyleVariantId);
        }

        return parsed;
    }

    private bool ShouldPollBackendResult(RuntimeGenerationBackendResult result)
    {
        if (result == null || !string.IsNullOrWhiteSpace(result.FailureReason))
        {
            return false;
        }

        if (result.OutputState == GeneratedObjectJobState.RuntimeModelReady &&
            !string.IsNullOrWhiteSpace(result.RuntimeModelUrl))
        {
            return false;
        }

        if (result.OutputState == GeneratedObjectJobState.Failed)
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(ResolveRuntimeBackendStatusUrl(result));
    }

    private string ResolveRuntimeBackendStatusUrl(RuntimeGenerationBackendResult result)
    {
        if (result == null)
        {
            return string.Empty;
        }

        if (Uri.TryCreate(result.RuntimeBackendStatusUrl, UriKind.Absolute, out var absoluteStatusUri))
        {
            return absoluteStatusUri.ToString();
        }

        if (!string.IsNullOrWhiteSpace(result.RuntimeBackendStatusUrl) &&
            Uri.TryCreate(backendSubmitUrl, UriKind.Absolute, out var submitUri))
        {
            if (result.RuntimeBackendStatusUrl.StartsWith("/", StringComparison.Ordinal))
            {
                return new Uri(submitUri, result.RuntimeBackendStatusUrl).ToString();
            }

            var submitBase = backendSubmitUrl.EndsWith("/", StringComparison.Ordinal)
                ? backendSubmitUrl
                : $"{backendSubmitUrl}/";
            return new Uri(new Uri(submitBase), result.RuntimeBackendStatusUrl).ToString();
        }

        if (string.IsNullOrWhiteSpace(result.RuntimeBackendJobId) ||
            !Uri.TryCreate(backendSubmitUrl, UriKind.Absolute, out _))
        {
            return string.Empty;
        }

        var baseText = backendSubmitUrl.EndsWith("/", StringComparison.Ordinal)
            ? backendSubmitUrl
            : $"{backendSubmitUrl}/";
        return new Uri(new Uri(baseText), UnityWebRequest.EscapeURL(result.RuntimeBackendJobId)).ToString();
    }

    private static async Task DelaySecondsAsync(float seconds)
    {
        var endAt = DateTime.UtcNow + TimeSpan.FromSeconds(Mathf.Max(0.1f, seconds));
        while (DateTime.UtcNow < endAt)
        {
            await Task.Delay(100);
        }
    }

    private static bool TryReadBackendImageBytes(
        RuntimeGenerationBackendSubmission submission,
        out byte[] imageBytes,
        out string error)
    {
        imageBytes = null;
        error = string.Empty;
        if (submission == null || string.IsNullOrWhiteSpace(submission.SourceInputImagePath))
        {
            error = "SourceInputImagePath is empty.";
            return false;
        }

        if (!File.Exists(submission.SourceInputImagePath))
        {
            error = submission.SourceInputImagePath;
            return false;
        }

        try
        {
            imageBytes = File.ReadAllBytes(submission.SourceInputImagePath);
            return imageBytes.Length > 0;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
    }

    private RuntimeGenerationBackendSubmission BuildSubmission(GeneratedAssetRecord record, GeneratedObjectRequest request)
    {
        var sourceImagePath = ResolveBackendSourceImagePath(record, request);
        var sourceRequestJson = ReadTextFileIfExists(record.SourceRequestPath);
        var promptText = ResolvePromptText(record, request);
        var imageFileInfo = !string.IsNullOrWhiteSpace(sourceImagePath) && File.Exists(sourceImagePath)
            ? new FileInfo(sourceImagePath)
            : null;

        return new RuntimeGenerationBackendSubmission
        {
            RequestId = record.RequestId,
            ObjectId = record.ObjectId,
            RoomId = request?.RoomId,
            ThemeId = record.ThemeId,
            StyleVariantId = NormalizeStyleVariantId(record.StyleVariantId),
            SourceRequestPath = record.SourceRequestPath,
            SourceRequestJson = sourceRequestJson,
            SourceInputImagePath = sourceImagePath,
            SourceImageFileName = imageFileInfo?.Name,
            SourceImageMimeType = imageFileInfo != null ? ResolveMimeType(sourceImagePath) : string.Empty,
            SourceImageSha256 = imageFileInfo != null ? ComputeSha256(sourceImagePath) : string.Empty,
            SourceImageByteLength = imageFileInfo?.Length ?? 0L,
            PromptArtifactPath = record.PromptArtifactPath,
            PromptText = promptText,
            UserStyleIntent = request?.UserStyleIntent,
            ThemeDisplayName = request?.ThemeDisplayName,
            SemanticLabel = request?.SemanticLabel,
            FunctionTag = request?.FunctionTag,
            WorldBounds = request != null ? request.WorldBounds : default,
            WorldPose = request != null ? request.WorldPose : default,
            TargetLengthMeters = record.TargetLengthMeters,
            TargetWidthMeters = record.TargetWidthMeters,
            TargetHeightMeters = record.TargetHeightMeters,
            TargetAspectRatio = record.TargetAspectRatio,
            SafetyFootprintScale = record.SafetyFootprintScale,
            VerticalFitMode = record.VerticalFitMode,
            SubmissionNote = "Runtime generation request from Quest client. This payload must not include service API keys.",
            CreatedAtIsoUtc = DateTime.UtcNow.ToString("O"),
        };
    }

    private void ApplyBackendResult(GeneratedAssetRecord record, string jobPath, RuntimeGenerationBackendResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.RequestId) &&
            !string.Equals(result.RequestId, record.RequestId, StringComparison.Ordinal))
        {
            FailRecord(record, jobPath, $"Runtime backend returned mismatched request id {result.RequestId}.");
            return;
        }

        record.RuntimeBackendJobId = result.RuntimeBackendJobId;
        record.RuntimeBackendStatusUrl = ResolveRuntimeBackendStatusUrl(result);
        if (!string.IsNullOrWhiteSpace(result.RuntimeModelUrl))
        {
            record.RuntimeModelUrl = result.RuntimeModelUrl;
        }

        if (!string.IsNullOrWhiteSpace(result.RuntimeModelMimeType))
        {
            record.RuntimeModelMimeType = result.RuntimeModelMimeType;
        }

        if (!string.IsNullOrWhiteSpace(result.RuntimeModelHash))
        {
            record.RuntimeModelHash = result.RuntimeModelHash;
        }

        record.State = !string.IsNullOrWhiteSpace(result.FailureReason)
            ? GeneratedObjectJobState.Failed
            : result.OutputState;
        if (record.State == GeneratedObjectJobState.Pending || record.State == GeneratedObjectJobState.CaptureReady)
        {
            record.State = string.IsNullOrWhiteSpace(record.RuntimeModelUrl)
                ? GeneratedObjectJobState.RuntimeBackendSubmitted
                : GeneratedObjectJobState.RuntimeModelReady;
        }

        if (record.State == GeneratedObjectJobState.RuntimeModelReady &&
            string.IsNullOrWhiteSpace(record.RuntimeModelUrl))
        {
            if (string.IsNullOrWhiteSpace(record.RuntimeBackendStatusUrl))
            {
                record.State = GeneratedObjectJobState.Failed;
                result.FailureReason = "Runtime backend reported a ready model without RuntimeModelUrl.";
            }
            else
            {
                record.State = GeneratedObjectJobState.RuntimeBackendSubmitted;
            }
        }

        record.FailureReason = result.FailureReason ?? string.Empty;
        record.StatusNote = string.IsNullOrWhiteSpace(result.StatusNote)
            ? $"Runtime backend result: {record.State}"
            : result.StatusNote;
        record.UpdatedAtIsoUtc = DateTime.UtcNow.ToString("O");
        WriteRecord(jobPath, record);
        PublishSummary(record.State.ToString(), record.StatusNote);
    }

    private static string WriteRuntimeBackendArtifact(
        string jobPath,
        string requestId,
        string suffix,
        string json)
    {
        if (string.IsNullOrWhiteSpace(requestId) || string.IsNullOrWhiteSpace(suffix))
        {
            return string.Empty;
        }

        try
        {
            var directory = Path.GetDirectoryName(jobPath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                directory = Path.Combine(Application.persistentDataPath, "GeneratedObjectJobs");
            }

            Directory.CreateDirectory(directory);
            var artifactPath = Path.Combine(directory, $"{requestId}.{suffix}.json");
            File.WriteAllText(artifactPath, json ?? string.Empty);
            return artifactPath;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[QuestRuntimeGenerationClient] Failed to write runtime backend artifact: {exception.Message}");
            return string.Empty;
        }
    }

    private async Task TryLoadReadyModelAsync(GeneratedAssetRecord record, string jobPath)
    {
        if (runtimeGeneratedModelLoader == null)
        {
            PublishSummary("ready", "Runtime model is ready, but RuntimeGeneratedModelLoader is missing.");
            return;
        }

        if (record.State != GeneratedObjectJobState.RuntimeModelReady ||
            string.IsNullOrWhiteSpace(record.RuntimeModelUrl))
        {
            return;
        }

        await runtimeGeneratedModelLoader.LoadFromRecordAsync(record, jobPath);
        generationQueueStatusService?.Refresh();
    }

    private void FailRecord(GeneratedAssetRecord record, string jobPath, string reason)
    {
        record.State = GeneratedObjectJobState.Failed;
        record.FailureReason = reason;
        record.StatusNote = "Runtime backend failed.";
        record.UpdatedAtIsoUtc = DateTime.UtcNow.ToString("O");
        WriteRecord(jobPath, record);
        PublishSummary("failed", reason);
    }

    private bool TryResolveLatestJob(out GeneratedAssetRecord record, out string jobPath)
    {
        record = null;
        jobPath = string.Empty;

        if (captureService != null &&
            !string.IsNullOrWhiteSpace(captureService.LastQueuedJobPath) &&
            File.Exists(captureService.LastQueuedJobPath) &&
            TryReadRecord(captureService.LastQueuedJobPath, out record))
        {
            jobPath = captureService.LastQueuedJobPath;
            return true;
        }

        var activeRequest = captureService != null ? captureService.LastGeneratedRequest : null;
        if (activeRequest == null ||
            string.IsNullOrWhiteSpace(activeRequest.RequestId) ||
            string.IsNullOrWhiteSpace(activeRequest.ObjectId))
        {
            return false;
        }

        foreach (var directory in GetJobDirectories())
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var candidatePath in Directory.GetFiles(directory, "*.job.json", SearchOption.TopDirectoryOnly))
            {
                if (!TryReadRecord(candidatePath, out var candidate))
                {
                    continue;
                }

                if (!string.Equals(candidate.RequestId, activeRequest.RequestId, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(candidate.ObjectId, activeRequest.ObjectId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                record = candidate;
                jobPath = candidatePath;
                return true;
            }
        }

        return false;
    }

    private IEnumerable<string> GetJobDirectories()
    {
        yield return Path.Combine(Application.persistentDataPath, generatedObjectJobFolderName);
#if UNITY_EDITOR
        if (includeEditorLibraryJobs)
        {
            yield return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Library", generatedObjectJobFolderName));
        }
#endif
    }

    private bool TryReadRecord(string path, out GeneratedAssetRecord record)
    {
        record = TryReadJson<GeneratedAssetRecord>(path);
        return record != null && !string.IsNullOrWhiteSpace(record.RequestId);
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
            Debug.LogWarning($"[QuestRuntimeGenerationClient] Failed to read JSON {path}: {exception.Message}");
            return null;
        }
    }

    private static async Task<UnityWebRequest.Result> SendRequestAsync(UnityWebRequest request)
    {
        var operation = request.SendWebRequest();
        while (!operation.isDone)
        {
            await Task.Yield();
        }

        return request.result;
    }

    private void WriteRecord(string jobPath, GeneratedAssetRecord record)
    {
        if (string.IsNullOrWhiteSpace(jobPath) || record == null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(jobPath) ?? Application.persistentDataPath);
            File.WriteAllText(jobPath, JsonUtility.ToJson(record, true));
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[QuestRuntimeGenerationClient] Failed to write job record {jobPath}: {exception.Message}");
        }
    }

    private void ResolveReferences()
    {
        if (captureService == null)
        {
            captureService = FindAnyObjectByType<DevicePassthroughCaptureService>();
        }

        if (runtimeGeneratedModelLoader == null)
        {
            runtimeGeneratedModelLoader = FindAnyObjectByType<RuntimeGeneratedModelLoader>();
        }

        if (generationQueueStatusService == null)
        {
            generationQueueStatusService = FindAnyObjectByType<GenerationQueueStatusService>();
        }
    }

    private void PublishSummary(string state, string detail)
    {
        latestSummary =
            "[QuestRuntimeGenerationClient]\n" +
            $"State: {state}\n" +
            $"Mode: {clientMode}\n" +
            $"Detail: {detail}";
        SummaryChanged?.Invoke();
    }

    private string ResolveBackendSourceImagePath(GeneratedAssetRecord record, GeneratedObjectRequest request)
    {
        if (preferCroppedSourceImage)
        {
            var croppedPath = FirstExistingPath(request?.SourceCroppedImagePath, request?.SourceImagePath, record.SourceInputImagePath);
            if (!string.IsNullOrWhiteSpace(croppedPath))
            {
                return croppedPath;
            }
        }

        return FirstExistingPath(
            record.SourceInputImagePath,
            request?.SourceImagePath,
            request?.SourceCroppedImagePath,
            request?.SourceFullFrameImagePath,
            request?.SourceOriginalInputPath);
    }

    private static string FirstExistingPath(params string[] paths)
    {
        if (paths == null)
        {
            return string.Empty;
        }

        for (var index = 0; index < paths.Length; index++)
        {
            var path = paths[index];
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                return path;
            }
        }

        return string.Empty;
    }

    private static string ResolvePromptText(GeneratedAssetRecord record, GeneratedObjectRequest request)
    {
        var promptArtifact = ReadTextFileIfExists(record.PromptArtifactPath);
        if (!string.IsNullOrWhiteSpace(promptArtifact))
        {
            return promptArtifact;
        }

        if (!string.IsNullOrWhiteSpace(request?.ImageStylizationPrompt))
        {
            return request.ImageStylizationPrompt;
        }

        if (!string.IsNullOrWhiteSpace(request?.AppearancePrompt))
        {
            return request.AppearancePrompt;
        }

        return request?.ObjectStyleDirective ?? string.Empty;
    }

    private static string ReadTextFileIfExists(string path)
    {
        try
        {
            return string.IsNullOrWhiteSpace(path) || !File.Exists(path)
                ? string.Empty
                : File.ReadAllText(path);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[QuestRuntimeGenerationClient] Failed to read text file {path}: {exception.Message}");
            return string.Empty;
        }
    }

    private static string ResolveMimeType(string path)
    {
        var extension = Path.GetExtension(path)?.ToLowerInvariant();
        return extension switch
        {
            ".jpg" => "image/jpeg",
            ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            _ => "image/png",
        };
    }

    private static bool IsPermittedBackendSubmitUrl(string url, out string error)
    {
        error = string.Empty;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            error = "Runtime backend submit URL is not a valid absolute URL.";
            return false;
        }

        if (uri.Scheme == Uri.UriSchemeHttps)
        {
            return true;
        }

#if UNITY_EDITOR
        if (uri.Scheme == Uri.UriSchemeHttp &&
            (uri.IsLoopback ||
             string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }
#endif

        error = "Runtime backend submit URL must be HTTPS for Quest builds.";
        return false;
    }

    private static string ComputeSha256(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(stream);
            var builder = new StringBuilder(hash.Length * 2);
            for (var index = 0; index < hash.Length; index++)
            {
                builder.Append(hash[index].ToString("x2"));
            }

            return builder.ToString();
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[QuestRuntimeGenerationClient] Failed to hash source image {path}: {exception.Message}");
            return string.Empty;
        }
    }

    private static string TruncateForStatus(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length <= maxLength)
        {
            return value ?? string.Empty;
        }

        return value.Substring(0, Mathf.Max(0, maxLength)) + "...";
    }

    private static string NormalizeStyleVariantId(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "preset" : value.Trim();
    }

    private static string ShortId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "none";
        }

        return value.Length <= 18 ? value : value[..18];
    }
}

public enum RuntimeGenerationClientMode
{
    LocalTestModelUrl,
    HttpBackend,
}
