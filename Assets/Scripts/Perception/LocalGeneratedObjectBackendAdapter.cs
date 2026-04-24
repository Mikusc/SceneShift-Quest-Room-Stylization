using System;
using System.IO;
using System.Text;
using UnityEngine;

[DisallowMultipleComponent]
public class LocalGeneratedObjectBackendAdapter : MonoBehaviour
{
    private enum BackendProcessingMode
    {
        LocalMockStylization,
        ExternalFileProtocol,
    }

    [Header("References")]
    [SerializeField] private GenerativeObjectCoordinator generativeObjectCoordinator;

    [Header("Local Simulation")]
    [SerializeField] private bool autoConsumeJobsInPlay = true;
    [SerializeField] private BackendProcessingMode processingMode = BackendProcessingMode.LocalMockStylization;
    [SerializeField] private string jobFolderName = "GeneratedObjectJobs";
    [SerializeField] private string outputFolderName = "GeneratedObjectOutputs";
    [SerializeField] private string backendInboxFolderName = "GeneratedObjectBackendInbox";

    public event Action SummaryChanged;

    public string LatestSummary => _latestSummary;
    public GeneratedAssetRecord LastProcessedRecord => _lastProcessedRecord;

    private GeneratedAssetRecord _lastProcessedRecord = new();
    private string _latestSummary =
        "[LocalGeneratedObjectBackendAdapter]\nState: waiting\nHint: queue a generated-object job first, then this adapter will simulate a stylized-image result locally.";

    private void Reset()
    {
        ResolveReferences();
    }

    private void Awake()
    {
        ResolveReferences();
        PublishSummary("awake");
    }

    private void OnEnable()
    {
        ResolveReferences();
        PublishSummary("enabled");
    }

    private void Update()
    {
        if (!Application.isPlaying || !autoConsumeJobsInPlay)
        {
            return;
        }

        ConsumePendingJobs();
    }

    [ContextMenu("Consume Pending Jobs")]
    public void ConsumePendingJobs()
    {
        ResolveReferences();

        var jobDirectory = GetJobDirectory();
        if (!Directory.Exists(jobDirectory))
        {
            PublishSummary("waiting-for-job-folder");
            return;
        }

        var jobPaths = Directory.GetFiles(jobDirectory, "*.job.json", SearchOption.TopDirectoryOnly);
        if (jobPaths == null || jobPaths.Length == 0)
        {
            PublishSummary("waiting-for-jobs");
            return;
        }

        var processedCount = 0;
        for (var index = 0; index < jobPaths.Length; index++)
        {
            var jobPath = jobPaths[index];
            if (!TryLoadJob(jobPath, out var record))
            {
                continue;
            }

            if (processingMode == BackendProcessingMode.LocalMockStylization)
            {
                if (record.State != GeneratedObjectJobState.CaptureReady)
                {
                    continue;
                }

                ProcessCaptureReadyJob(jobPath, record);
                processedCount++;
                continue;
            }

            if (record.State == GeneratedObjectJobState.CaptureReady)
            {
                SubmitCaptureReadyJobToExternalProtocol(jobPath, record);
                processedCount++;
                continue;
            }

            if (record.State == GeneratedObjectJobState.BackendSubmitted &&
                TryConsumeExternalProtocolResult(jobPath, record))
            {
                processedCount++;
            }
        }

        if (processedCount == 0)
        {
            PublishSummary("no-pending-capture-ready-jobs");
            return;
        }

        PublishSummary("processed");
    }

    public string GetDebugSummary()
    {
        return _latestSummary;
    }

    private void ResolveReferences()
    {
        if (generativeObjectCoordinator == null)
        {
            generativeObjectCoordinator = FindAnyObjectByType<GenerativeObjectCoordinator>();
        }
    }

    private void ProcessCaptureReadyJob(string jobPath, GeneratedAssetRecord record)
    {
        var outputDirectory = GetOutputDirectory();
        Directory.CreateDirectory(outputDirectory);
        var resultPath = Path.Combine(outputDirectory, $"{record.RequestId}.result.json");

        if (string.IsNullOrWhiteSpace(record.SourceInputImagePath) || !File.Exists(record.SourceInputImagePath))
        {
            record.State = GeneratedObjectJobState.Failed;
            record.FailureReason = "Source input image is missing for local stylized-image simulation.";
            record.StatusNote = "Local backend adapter could not find the source input image and wrote a failed result artifact.";
            record.BackendAdapterName = nameof(LocalGeneratedObjectBackendAdapter);
            record.BackendResultPath = resultPath;
            record.BackendTransformId = string.Empty;
            record.UpdatedAtIsoUtc = DateTime.UtcNow.ToString("O");

            WriteBackendResult(resultPath, record, string.Empty, GeneratedObjectJobState.Failed, record.StatusNote, string.Empty, false);
            File.WriteAllText(jobPath, JsonUtility.ToJson(record, true));
            _lastProcessedRecord = record;
            Debug.LogWarning($"[LocalGeneratedObjectBackendAdapter] Missing source input image for job -> {jobPath}", this);
            return;
        }

        var outputPath = Path.Combine(outputDirectory, $"{record.RequestId}.stylized.png");
        if (!TryCreateLocalStylizedImage(record, outputPath, out var transformId, out var transformError))
        {
            record.State = GeneratedObjectJobState.Failed;
            record.FailureReason = transformError;
            record.StatusNote = $"Local backend adapter failed to create a mock stylized image and wrote a failed result artifact. Reason: {transformError}";
            record.BackendAdapterName = nameof(LocalGeneratedObjectBackendAdapter);
            record.BackendResultPath = resultPath;
            record.BackendTransformId = string.Empty;
            record.UpdatedAtIsoUtc = DateTime.UtcNow.ToString("O");

            WriteBackendResult(resultPath, record, string.Empty, GeneratedObjectJobState.Failed, record.StatusNote, string.Empty, File.Exists(record.PromptArtifactPath));
            File.WriteAllText(jobPath, JsonUtility.ToJson(record, true));
            _lastProcessedRecord = record;
            Debug.LogWarning($"[LocalGeneratedObjectBackendAdapter] Failed to simulate stylized-image output for job -> {jobPath}. Reason: {transformError}", this);
            return;
        }

        record.State = GeneratedObjectJobState.StylizedImageReady;
        record.StylizedImagePath = outputPath;
        record.StylizedImageUrl = string.Empty;
        record.PreviewImagePath = outputPath;
        record.StatusNote = $"Local backend adapter simulated stylized-image output by applying mock transform '{transformId}', consuming the queued prompt artifact, and writing a backend result artifact.";
        record.BackendAdapterName = nameof(LocalGeneratedObjectBackendAdapter);
        record.BackendResultPath = resultPath;
        record.BackendTransformId = transformId;
        record.FailureReason = string.Empty;
        record.UpdatedAtIsoUtc = DateTime.UtcNow.ToString("O");

        WriteBackendResult(resultPath, record, outputPath, GeneratedObjectJobState.StylizedImageReady, record.StatusNote, transformId, File.Exists(record.PromptArtifactPath));
        File.WriteAllText(jobPath, JsonUtility.ToJson(record, true));
        _lastProcessedRecord = record;
        Debug.Log($"[LocalGeneratedObjectBackendAdapter] Simulated stylized-image output -> {outputPath}", this);
    }

    private void SubmitCaptureReadyJobToExternalProtocol(string jobPath, GeneratedAssetRecord record)
    {
        var outputDirectory = GetOutputDirectory();
        Directory.CreateDirectory(outputDirectory);

        var inboxDirectory = GetBackendInboxDirectory();
        Directory.CreateDirectory(inboxDirectory);

        var submissionPath = Path.Combine(inboxDirectory, $"{record.RequestId}.submission.json");
        var templatePath = Path.Combine(inboxDirectory, $"{record.RequestId}.result.template.json");
        var expectedOutputImagePath = Path.Combine(outputDirectory, $"{record.RequestId}.stylized.png");
        var expectedResultPath = Path.Combine(outputDirectory, $"{record.RequestId}.result.json");

        var submission = new GeneratedImageBackendSubmission
        {
            RequestId = record.RequestId,
            ObjectId = record.ObjectId,
            ThemeId = record.ThemeId,
            PromptVersion = record.PromptVersion,
            PromptArtifactPath = record.PromptArtifactPath,
            SourceInputImagePath = record.SourceInputImagePath,
            SourceRequestPath = record.SourceRequestPath,
            RequestedOutputImagePath = expectedOutputImagePath,
            RequestedResultPath = expectedResultPath,
            ResultTemplatePath = templatePath,
            BackendAdapterName = nameof(LocalGeneratedObjectBackendAdapter),
            SubmissionNote = "External file-protocol backend should consume the prompt and source image, then write the stylized image and result artifact to the requested output paths. A prefilled result template is provided for manual workers.",
            CreatedAtIsoUtc = DateTime.UtcNow.ToString("O"),
        };

        File.WriteAllText(submissionPath, JsonUtility.ToJson(submission, true));
        WriteExternalResultTemplate(templatePath, submission);

        record.State = GeneratedObjectJobState.BackendSubmitted;
        record.BackendAdapterName = nameof(LocalGeneratedObjectBackendAdapter);
        record.BackendRequestPath = submissionPath;
        record.BackendResultPath = expectedResultPath;
        record.BackendResultTemplatePath = templatePath;
        record.BackendTransformId = string.Empty;
        record.StatusNote = "Local backend adapter submitted the job to the external file protocol, wrote a prefilled result template, and is waiting for a dropped result artifact.";
        record.UpdatedAtIsoUtc = DateTime.UtcNow.ToString("O");

        File.WriteAllText(jobPath, JsonUtility.ToJson(record, true));
        _lastProcessedRecord = record;
        Debug.Log($"[LocalGeneratedObjectBackendAdapter] Submitted generated-object job to external file protocol -> {submissionPath}", this);
    }

    private bool TryConsumeExternalProtocolResult(string jobPath, GeneratedAssetRecord record)
    {
        if (string.IsNullOrWhiteSpace(record.BackendResultPath) || !File.Exists(record.BackendResultPath))
        {
            return false;
        }

        var resultJson = File.ReadAllText(record.BackendResultPath);
        if (string.IsNullOrWhiteSpace(resultJson))
        {
            return false;
        }

        var result = JsonUtility.FromJson<GeneratedImageBackendResult>(resultJson);
        if (result == null || string.IsNullOrWhiteSpace(result.RequestId))
        {
            return false;
        }

        record.BackendAdapterName = string.IsNullOrWhiteSpace(result.BackendAdapterName)
            ? nameof(LocalGeneratedObjectBackendAdapter)
            : result.BackendAdapterName;
        record.BackendTransformId = result.AppliedTransformId;
        record.UpdatedAtIsoUtc = DateTime.UtcNow.ToString("O");
        record.StatusNote = result.StatusNote;

        var hasLocalOutput = !string.IsNullOrWhiteSpace(result.OutputImagePath) && File.Exists(result.OutputImagePath);
        var hasHostedOutput = IsHttpUrl(result.OutputImageUrl);
        if (result.OutputState == GeneratedObjectJobState.StylizedImageReady && (hasLocalOutput || hasHostedOutput))
        {
            record.State = GeneratedObjectJobState.StylizedImageReady;
            record.StylizedImagePath = hasLocalOutput ? result.OutputImagePath : string.Empty;
            record.StylizedImageUrl = hasHostedOutput ? result.OutputImageUrl : string.Empty;
            record.PreviewImagePath = hasLocalOutput ? result.OutputImagePath : result.OutputImageUrl;
            record.FailureReason = string.Empty;
            File.WriteAllText(jobPath, JsonUtility.ToJson(record, true));
            _lastProcessedRecord = record;
            Debug.Log($"[LocalGeneratedObjectBackendAdapter] Consumed external backend result -> {record.PreviewImagePath}", this);
            return true;
        }

        if (result.OutputState == GeneratedObjectJobState.Failed)
        {
            record.State = GeneratedObjectJobState.Failed;
            record.FailureReason = string.IsNullOrWhiteSpace(result.StatusNote)
                ? "External file-protocol backend reported failure."
                : result.StatusNote;
            File.WriteAllText(jobPath, JsonUtility.ToJson(record, true));
            _lastProcessedRecord = record;
            Debug.LogWarning($"[LocalGeneratedObjectBackendAdapter] External backend reported failure for job -> {jobPath}", this);
            return true;
        }

        return false;
    }

    private static void WriteExternalResultTemplate(string templatePath, GeneratedImageBackendSubmission submission)
    {
        var template = new GeneratedImageBackendResult
        {
            RequestId = submission.RequestId,
            ObjectId = submission.ObjectId,
            ThemeId = submission.ThemeId,
            PromptVersion = submission.PromptVersion,
            PromptArtifactPath = submission.PromptArtifactPath,
            SourceInputImagePath = submission.SourceInputImagePath,
            SourceRequestPath = submission.SourceRequestPath,
            OutputImagePath = submission.RequestedOutputImagePath,
            OutputImageUrl = string.Empty,
            BackendAdapterName = "ManualGPTImageWorker",
            AppliedTransformId = "manual_gpt_image_v1",
            PromptArtifactConsumed = true,
            OutputState = GeneratedObjectJobState.StylizedImageReady,
            StatusNote = "Copy this template to RequestedResultPath after saving the generated image to RequestedOutputImagePath.",
            CreatedAtIsoUtc = DateTime.UtcNow.ToString("O"),
        };

        File.WriteAllText(templatePath, JsonUtility.ToJson(template, true));
    }

    private static void WriteBackendResult(
        string resultPath,
        GeneratedAssetRecord record,
        string outputImagePath,
        GeneratedObjectJobState outputState,
        string statusNote,
        string appliedTransformId,
        bool promptArtifactConsumed)
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
            BackendAdapterName = nameof(LocalGeneratedObjectBackendAdapter),
            AppliedTransformId = appliedTransformId,
            PromptArtifactConsumed = promptArtifactConsumed,
            OutputState = outputState,
            StatusNote = statusNote,
            CreatedAtIsoUtc = DateTime.UtcNow.ToString("O"),
        };

        File.WriteAllText(resultPath, JsonUtility.ToJson(result, true));
    }

    private bool TryCreateLocalStylizedImage(
        GeneratedAssetRecord record,
        string outputPath,
        out string transformId,
        out string failureReason)
    {
        transformId = string.Empty;
        failureReason = string.Empty;

        var imageBytes = File.ReadAllBytes(record.SourceInputImagePath);
        if (imageBytes == null || imageBytes.Length == 0)
        {
            failureReason = "Source input image bytes were empty.";
            return false;
        }

        var sourceTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!ImageConversion.LoadImage(sourceTexture, imageBytes, false))
        {
            Destroy(sourceTexture);
            failureReason = "Unity could not decode the source input image.";
            return false;
        }

        var recipe = ResolveRecipe(record.ThemeId);
        var pixels = sourceTexture.GetPixels32();
        for (var index = 0; index < pixels.Length; index++)
        {
            pixels[index] = ApplyRecipe(pixels[index], recipe);
        }

        sourceTexture.SetPixels32(pixels);
        sourceTexture.Apply(false, false);

        var outputBytes = sourceTexture.EncodeToPNG();
        Destroy(sourceTexture);

        if (outputBytes == null || outputBytes.Length == 0)
        {
            failureReason = "Unity failed to encode the mock stylized image.";
            return false;
        }

        File.WriteAllBytes(outputPath, outputBytes);
        transformId = recipe.Id;
        return true;
    }

    private static LocalMockStylizationRecipe ResolveRecipe(string themeId)
    {
        switch (themeId?.Trim().ToLowerInvariant())
        {
            case "future_research_lab":
                return new LocalMockStylizationRecipe(
                    "future_research_lab_tint_v1",
                    new Color(0.2f, 0.86f, 0.92f, 1f),
                    0.2f,
                    0.72f,
                    1.05f,
                    1.06f);
            case "arcane_knowledge_chamber":
                return new LocalMockStylizationRecipe(
                    "arcane_knowledge_chamber_tint_v1",
                    new Color(0.72f, 0.42f, 0.9f, 1f),
                    0.18f,
                    0.82f,
                    0.96f,
                    1.04f);
            default:
                return new LocalMockStylizationRecipe(
                    "generic_scene_shift_tint_v1",
                    new Color(0.34f, 0.78f, 0.88f, 1f),
                    0.16f,
                    0.8f,
                    1.02f,
                    1.03f);
        }
    }

    private static Color32 ApplyRecipe(Color32 input, LocalMockStylizationRecipe recipe)
    {
        var source = new Color(
            input.r / 255f,
            input.g / 255f,
            input.b / 255f,
            input.a / 255f);

        var luma = source.r * 0.2126f + source.g * 0.7152f + source.b * 0.0722f;
        var grayscale = new Color(luma, luma, luma, source.a);

        var saturated = Color.Lerp(grayscale, source, recipe.Saturation);
        var contrasted = ApplyContrast(saturated, recipe.Contrast);
        var tinted = Color.Lerp(contrasted, recipe.OverlayColor, recipe.OverlayStrength);
        tinted.r = Mathf.Clamp01(tinted.r * recipe.Brightness);
        tinted.g = Mathf.Clamp01(tinted.g * recipe.Brightness);
        tinted.b = Mathf.Clamp01(tinted.b * recipe.Brightness);
        tinted.a = source.a;

        return tinted;
    }

    private static Color ApplyContrast(Color color, float contrast)
    {
        var red = Mathf.Clamp01(((color.r - 0.5f) * contrast) + 0.5f);
        var green = Mathf.Clamp01(((color.g - 0.5f) * contrast) + 0.5f);
        var blue = Mathf.Clamp01(((color.b - 0.5f) * contrast) + 0.5f);
        return new Color(red, green, blue, color.a);
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

    private string GetBackendInboxDirectory()
    {
        var libraryRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Library"));
        return Path.Combine(libraryRoot, backendInboxFolderName);
    }

    private static bool IsHttpUrl(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private void PublishSummary(string state)
    {
        var builder = new StringBuilder(384);
        builder.AppendLine("[LocalGeneratedObjectBackendAdapter]");
        builder.AppendLine($"State: {state}");
        builder.AppendLine($"Auto Consume: {autoConsumeJobsInPlay}");
        builder.AppendLine($"Mode: {processingMode}");

        if (!string.IsNullOrWhiteSpace(_lastProcessedRecord.RequestId))
        {
            builder.AppendLine($"Last Request: {_lastProcessedRecord.RequestId}");
            builder.AppendLine($"Last Job State: {_lastProcessedRecord.State}");
            builder.AppendLine($"Request Path: {_lastProcessedRecord.BackendRequestPath}");
            builder.AppendLine($"Prompt Version: {_lastProcessedRecord.PromptVersion}");
            builder.AppendLine($"Prompt Path: {_lastProcessedRecord.PromptArtifactPath}");
            builder.AppendLine($"Result Path: {_lastProcessedRecord.BackendResultPath}");
            builder.AppendLine($"Template Path: {_lastProcessedRecord.BackendResultTemplatePath}");
            builder.AppendLine($"Transform Id: {_lastProcessedRecord.BackendTransformId}");
            builder.AppendLine($"Stylized Image: {_lastProcessedRecord.StylizedImagePath}");
            builder.AppendLine($"Stylized Image URL: {_lastProcessedRecord.StylizedImageUrl}");
            builder.AppendLine($"Status Note: {_lastProcessedRecord.StatusNote}");
        }
        else
        {
            builder.AppendLine("Last Request: none");
        }

        _latestSummary = builder.ToString().TrimEnd();
        SummaryChanged?.Invoke();
    }

    private readonly struct LocalMockStylizationRecipe
    {
        public readonly string Id;
        public readonly Color OverlayColor;
        public readonly float OverlayStrength;
        public readonly float Saturation;
        public readonly float Brightness;
        public readonly float Contrast;

        public LocalMockStylizationRecipe(
            string id,
            Color overlayColor,
            float overlayStrength,
            float saturation,
            float brightness,
            float contrast)
        {
            Id = id;
            OverlayColor = overlayColor;
            OverlayStrength = overlayStrength;
            Saturation = saturation;
            Brightness = brightness;
            Contrast = contrast;
        }
    }
}
