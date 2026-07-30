using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

[DisallowMultipleComponent]
public class HostedImageUploadBridge : MonoBehaviour
{
    [Header("Upload Service")]
    [SerializeField] private string uploadEndpoint = string.Empty;
    [SerializeField] private string authHeaderName = "Authorization";
    [SerializeField] private string authTokenEnvironmentVariable = string.Empty;
    [SerializeField] private string authHeaderPrefix = "Bearer ";
    [SerializeField] private bool uploadRawPngBody = true;
    [SerializeField] private string formFileFieldName = "file";
    [SerializeField, Min(1)] private int requestTimeoutSeconds = 120;
    [SerializeField] private Seed3DBackendAdapter seed3DBackendAdapter;

    [Header("Processing")]
    [SerializeField, Tooltip("Opt in only for trusted Editor/Link workflows. Standalone Quest builds should use the secure HTTPS backend.")]
    private bool autoProcessJobsInPlay;
    [SerializeField] private bool skipUploadWhenSeed3DCanUseLocalBase64 = true;
    [SerializeField, Min(1)] private int maxConcurrentUploadJobs = 3;
    [SerializeField, Min(1f)] private float pollIntervalSeconds = 3f;
    [SerializeField] private string jobFolderName = "GeneratedObjectJobs";

    public event Action SummaryChanged;

    public string LatestSummary => _latestSummary;
    public GeneratedAssetRecord LastProcessedRecord => _lastProcessedRecord;

    private GeneratedAssetRecord _lastProcessedRecord = new();
    private readonly HashSet<string> _activeJobPaths = new(StringComparer.OrdinalIgnoreCase);
    private bool _isProcessing;
    private float _nextPollTime;
    private string _latestSummary =
        "[HostedImageUploadBridge]\nState: waiting\nHint: configure uploadEndpoint to host local stylized PNGs for Seed3D image_url.";

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

        ResolveReferences();
        _nextPollTime = Time.unscaledTime + pollIntervalSeconds;
        ProcessNextLocalStylizedImage();
    }

    [ContextMenu("Process Next Local Stylized Image")]
    public void ProcessNextLocalStylizedImage()
    {
        ResolveReferences();
        if (!HasUploadCapacity())
        {
            PublishSummary("at-upload-capacity");
            return;
        }

        if (!IsHttpUrl(uploadEndpoint))
        {
            PublishSummary("waiting-for-upload-endpoint");
            return;
        }

        var authToken = GetAuthToken();
        if (!string.IsNullOrWhiteSpace(authTokenEnvironmentVariable) && string.IsNullOrWhiteSpace(authToken))
        {
            PublishSummary("waiting-for-auth-token");
            return;
        }

        var jobDirectory = GetJobDirectory();
        if (!Directory.Exists(jobDirectory))
        {
            PublishSummary("waiting-for-job-folder");
            return;
        }

        var startedCount = 0;
        var jobPaths = Directory.GetFiles(jobDirectory, "*.job.json", SearchOption.TopDirectoryOnly);
        for (var index = 0; index < jobPaths.Length; index++)
        {
            var jobPath = jobPaths[index];
            if (IsJobActive(jobPath))
            {
                continue;
            }

            if (!TryLoadJob(jobPath, out var record) || !NeedsHostedImage(record))
            {
                continue;
            }

            StartCoroutine(RunTrackedUpload(jobPath, record, authToken));
            startedCount++;
            if (!HasUploadCapacity())
            {
                break;
            }
        }

        PublishSummary(startedCount > 0 ? "uploading-batch" : "waiting-for-local-stylized-image");
    }

    public string GetDebugSummary()
    {
        return _latestSummary;
    }

    private void ResolveReferences()
    {
        if (seed3DBackendAdapter == null)
        {
            seed3DBackendAdapter = FindAnyObjectByType<Seed3DBackendAdapter>();
        }
    }

    private IEnumerator RunTrackedUpload(string jobPath, GeneratedAssetRecord record, string authToken)
    {
        var key = NormalizeJobPath(jobPath);
        _activeJobPaths.Add(key);
        _isProcessing = true;

        yield return UploadStylizedImage(jobPath, record, authToken);

        _activeJobPaths.Remove(key);
        _isProcessing = _activeJobPaths.Count > 0;
        PublishSummary(_isProcessing ? "uploading-batch" : "idle");
    }

    private bool HasUploadCapacity()
    {
        return _activeJobPaths.Count < Mathf.Max(1, maxConcurrentUploadJobs);
    }

    private bool IsJobActive(string jobPath)
    {
        return _activeJobPaths.Contains(NormalizeJobPath(jobPath));
    }

    private static string NormalizeJobPath(string jobPath)
    {
        return string.IsNullOrWhiteSpace(jobPath) ? string.Empty : Path.GetFullPath(jobPath);
    }

    private IEnumerator UploadStylizedImage(string jobPath, GeneratedAssetRecord record, string authToken)
    {
        _isProcessing = true;
        _lastProcessedRecord = record;
        PublishSummary("uploading");

        var imageBytes = File.ReadAllBytes(record.StylizedImagePath);

        using (var request = CreateUploadRequest(imageBytes, record.StylizedImagePath))
        {
            request.timeout = Mathf.Max(1, requestTimeoutSeconds);
            if (!string.IsNullOrWhiteSpace(authToken) && !string.IsNullOrWhiteSpace(authHeaderName))
            {
                request.SetRequestHeader(authHeaderName, $"{authHeaderPrefix}{authToken}");
            }

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                record.StatusNote = $"Hosted image upload failed: {request.responseCode} {request.error}";
                record.UpdatedAtIsoUtc = DateTime.UtcNow.ToString("O");
                File.WriteAllText(jobPath, JsonUtility.ToJson(record, true));
                _lastProcessedRecord = record;
                _isProcessing = false;
                PublishSummary("upload-failed");
                yield break;
            }

            var responseJson = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
            if (!TryExtractHostedUrl(responseJson, out var hostedUrl))
            {
                record.StatusNote = "Hosted image upload succeeded but response did not include url, image_url, public_url, or download_url.";
                record.UpdatedAtIsoUtc = DateTime.UtcNow.ToString("O");
                File.WriteAllText(jobPath, JsonUtility.ToJson(record, true));
                _lastProcessedRecord = record;
                _isProcessing = false;
                PublishSummary("missing-hosted-url");
                yield break;
            }

            record.StylizedImageUrl = hostedUrl;
            record.PreviewImagePath = hostedUrl;
            record.StatusNote = "Hosted image upload bridge wrote StylizedImageUrl for Seed3D image_url input.";
            record.UpdatedAtIsoUtc = DateTime.UtcNow.ToString("O");
            File.WriteAllText(jobPath, JsonUtility.ToJson(record, true));
        }

        _lastProcessedRecord = record;
        _isProcessing = false;
        PublishSummary("hosted-url-ready");
        Debug.Log($"[HostedImageUploadBridge] Hosted stylized image URL ready for request {record.RequestId}.", this);
    }

    private UnityWebRequest CreateUploadRequest(byte[] imageBytes, string imagePath)
    {
        if (uploadRawPngBody)
        {
            var request = new UnityWebRequest(uploadEndpoint, UnityWebRequest.kHttpVerbPOST);
            request.uploadHandler = new UploadHandlerRaw(imageBytes);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "image/png");
            return request;
        }

        var form = new WWWForm();
        form.AddBinaryData(formFileFieldName, imageBytes, Path.GetFileName(imagePath), "image/png");
        return UnityWebRequest.Post(uploadEndpoint, form);
    }

    private string GetAuthToken()
    {
        return string.IsNullOrWhiteSpace(authTokenEnvironmentVariable)
            ? string.Empty
            : Environment.GetEnvironmentVariable(authTokenEnvironmentVariable);
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

    private bool NeedsHostedImage(GeneratedAssetRecord record)
    {
        if (record == null ||
               record.State == GeneratedObjectJobState.StylizedImageReady &&
               string.IsNullOrWhiteSpace(record.StylizedImageUrl) &&
               !IsHttpUrl(record.StylizedImagePath) &&
               !string.IsNullOrWhiteSpace(record.StylizedImagePath) &&
               File.Exists(record.StylizedImagePath) == false)
        {
            return false;
        }

        if (skipUploadWhenSeed3DCanUseLocalBase64 &&
            seed3DBackendAdapter != null &&
            seed3DBackendAdapter.ShouldPreferLocalBase64ImageInput(record.StylizedImagePath))
        {
            return false;
        }

        return record.State == GeneratedObjectJobState.StylizedImageReady &&
               string.IsNullOrWhiteSpace(record.StylizedImageUrl) &&
               !IsHttpUrl(record.StylizedImagePath) &&
               !string.IsNullOrWhiteSpace(record.StylizedImagePath) &&
               File.Exists(record.StylizedImagePath);
    }

    private static bool TryExtractHostedUrl(string json, out string hostedUrl)
    {
        return TryExtractString(json, "url", out hostedUrl) ||
               TryExtractString(json, "image_url", out hostedUrl) ||
               TryExtractString(json, "public_url", out hostedUrl) ||
               TryExtractString(json, "download_url", out hostedUrl);
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
        return IsHttpUrl(value);
    }

    private string GetJobDirectory()
    {
        var libraryRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Library"));
        return Path.Combine(libraryRoot, jobFolderName);
    }

    private static bool IsHttpUrl(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private void PublishSummary(string state)
    {
        var builder = new System.Text.StringBuilder(384);
        builder.AppendLine("[HostedImageUploadBridge]");
        builder.AppendLine($"State: {state}");
        builder.AppendLine($"Auto Process: {autoProcessJobsInPlay}");
        builder.AppendLine($"Active Jobs: {_activeJobPaths.Count}/{Mathf.Max(1, maxConcurrentUploadJobs)}");
        builder.AppendLine($"Endpoint Configured: {IsHttpUrl(uploadEndpoint)}");
        builder.AppendLine($"Auth Env: {(string.IsNullOrWhiteSpace(authTokenEnvironmentVariable) ? "none" : authTokenEnvironmentVariable)}");
        builder.AppendLine($"Upload Body: {(uploadRawPngBody ? "raw image/png" : $"multipart field '{formFileFieldName}'")}");
        builder.AppendLine($"Skip For Seed3D Base64: {skipUploadWhenSeed3DCanUseLocalBase64}");

        if (!string.IsNullOrWhiteSpace(_lastProcessedRecord.RequestId))
        {
            builder.AppendLine($"Last Request: {_lastProcessedRecord.RequestId}");
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
}
