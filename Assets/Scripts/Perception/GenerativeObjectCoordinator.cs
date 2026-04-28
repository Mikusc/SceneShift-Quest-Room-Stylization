using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

[DisallowMultipleComponent]
public class GenerativeObjectCoordinator : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private BestViewCaptureService bestViewCaptureService;

    [Header("Coordinator")]
    [SerializeField] private bool autoQueueLatestCaptureInPlay = true;
    [SerializeField] private string jobFolderName = "GeneratedObjectJobs";

    public event Action SummaryChanged;

    public string LatestSummary => _latestSummary;
    public GeneratedAssetRecord LastQueuedRecord => _lastQueuedRecord;
    public string LastQueuedJobPath => _lastQueuedJobPath;

    private readonly List<GeneratedAssetRecord> _queuedRecords = new();
    private GeneratedAssetRecord _lastQueuedRecord = new();
    private string _lastQueuedRequestId = string.Empty;
    private string _lastQueuedJobPath = string.Empty;
    private string _latestSummary =
        "[GenerativeObjectCoordinator]\nState: waiting\nHint: capture one request first, then this coordinator will queue a local job shell.";

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
        if (!Application.isPlaying || !autoQueueLatestCaptureInPlay)
        {
            return;
        }

        TryQueueLatestCapture();
    }

    [ContextMenu("Queue Latest Capture")]
    public void QueueLatestCapture()
    {
        TryQueueLatestCapture();
    }

    public string GetDebugSummary()
    {
        return _latestSummary;
    }

    private void ResolveReferences()
    {
        if (bestViewCaptureService == null)
        {
            bestViewCaptureService = FindAnyObjectByType<BestViewCaptureService>();
        }
    }

    private void TryQueueLatestCapture()
    {
        ResolveReferences();
        if (bestViewCaptureService == null)
        {
            PublishSummary("missing-capture-service");
            return;
        }

        var request = bestViewCaptureService.LastGeneratedRequest;
        if (request == null || string.IsNullOrWhiteSpace(request.RequestId))
        {
            PublishSummary("waiting-for-request");
            return;
        }

        if (string.Equals(request.RequestId, _lastQueuedRequestId, StringComparison.Ordinal))
        {
            PublishSummary("capture-already-queued");
            return;
        }

        var jobDirectory = GetJobDirectory();
        Directory.CreateDirectory(jobDirectory);
        var jobPath = Path.Combine(jobDirectory, $"{request.RequestId}.job.json");
        var promptPath = Path.Combine(jobDirectory, $"{request.RequestId}.prompt.txt");

        var record = new GeneratedAssetRecord
        {
            RequestId = request.RequestId,
            ObjectId = request.ObjectId,
            ThemeId = request.ThemeId,
            StyleVariantId = string.IsNullOrWhiteSpace(request.StyleVariantId)
                ? SurfaceTexturePromptBuilder.PresetStyleVariantId
                : request.StyleVariantId,
            CaptureSourceMode = request.CaptureSourceMode,
            State = GeneratedObjectJobState.CaptureReady,
            SourceInputImagePath = request.SourceImagePath,
            SourceRequestPath = request.SourceRequestPath,
            CoordinatorJobPath = jobPath,
            StatusNote = "Local coordinator shell queued from captured request and wrote a Roomify-style prompt artifact.",
            PreviewImagePath = request.SourceImagePath,
            SourceYawDegrees = request.BestViewYawDegrees,
            PromptVersion = request.PromptVersion,
            PromptArtifactPath = promptPath,
            UpdatedAtIsoUtc = DateTime.UtcNow.ToString("O"),
        };

        if (!string.IsNullOrWhiteSpace(request.ImageStylizationPrompt))
        {
            File.WriteAllText(promptPath, request.ImageStylizationPrompt);
        }

        File.WriteAllText(jobPath, JsonUtility.ToJson(record, true));

        _queuedRecords.Add(record);
        _lastQueuedRecord = record;
        _lastQueuedRequestId = record.RequestId;
        _lastQueuedJobPath = jobPath;

        PublishSummary("queued");
        Debug.Log($"[GenerativeObjectCoordinator] Queued local generated-object job -> {jobPath}", this);
    }

    private string GetJobDirectory()
    {
        var libraryRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Library"));
        return Path.Combine(libraryRoot, jobFolderName);
    }

    private void PublishSummary(string state)
    {
        var builder = new StringBuilder(384);
        builder.AppendLine("[GenerativeObjectCoordinator]");
        builder.AppendLine($"State: {state}");
        builder.AppendLine($"Auto Queue: {autoQueueLatestCaptureInPlay}");
        builder.AppendLine($"Queued Jobs: {_queuedRecords.Count}");

        if (!string.IsNullOrWhiteSpace(_lastQueuedRequestId))
        {
            builder.AppendLine($"Last Request: {_lastQueuedRequestId}");
            builder.AppendLine($"Last Job Path: {_lastQueuedJobPath}");
            builder.AppendLine($"Last Input: {_lastQueuedRecord.SourceInputImagePath}");
            builder.AppendLine($"Last State: {_lastQueuedRecord.State}");
            builder.AppendLine($"Prompt Version: {_lastQueuedRecord.PromptVersion}");
            builder.AppendLine($"Prompt Path: {_lastQueuedRecord.PromptArtifactPath}");
        }
        else
        {
            builder.AppendLine("Last Request: none");
        }

        _latestSummary = builder.ToString().TrimEnd();
        SummaryChanged?.Invoke();
    }
}
