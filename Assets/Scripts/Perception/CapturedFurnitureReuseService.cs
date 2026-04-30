using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

[DisallowMultipleComponent]
public class CapturedFurnitureReuseService : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private RoomSemanticBootstrap roomSemanticBootstrap;
    [SerializeField] private ThemeIntentController themeIntentController;
    [SerializeField] private RuntimeStyleIntentController runtimeStyleIntentController;
    [SerializeField] private GenerationQueueStatusService generationQueueStatusService;
    [SerializeField] private RoomStyleCacheService roomStyleCacheService;

    [Header("Folders")]
    [SerializeField] private string captureFolderName = "BestViewCaptures";
    [SerializeField] private string jobFolderName = "GeneratedObjectJobs";

    [Header("Reuse Policy")]
    [SerializeField] private bool filterByCurrentRoomId = true;
    [SerializeField] private bool skipExistingJobsForCurrentTheme = true;
    [SerializeField] private bool chooseLatestCapturePerObject = true;

    [Header("Runtime State")]
    [SerializeField, TextArea(3, 6)] private string latestSummary = "[CapturedFurnitureReuse]\nState: waiting";

    public string LatestSummary => latestSummary;
    public int LastQueuedCount { get; private set; }
    public int LastSkippedCount { get; private set; }
    public int LastFailedCount { get; private set; }

    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private void Reset()
    {
        ResolveReferences();
    }

    private void Awake()
    {
        ResolveReferences();
    }

    [ContextMenu("Regenerate Current Room Furniture From Captures")]
    public void RegenerateCurrentRoomForCurrentTheme()
    {
        ResolveReferences();
        LastQueuedCount = 0;
        LastSkippedCount = 0;
        LastFailedCount = 0;

        var theme = themeIntentController != null ? themeIntentController.ActiveTheme : null;
        if (theme == null)
        {
            PublishSummary("failed", "No active ThemeProfile.");
            return;
        }

        var currentRoomId = ResolveCurrentRoomId();
        var runtimeIntent = runtimeStyleIntentController != null ? runtimeStyleIntentController.CurrentIntent : null;
        var styleVariantId = SurfaceTexturePromptBuilder.BuildStyleVariantId(runtimeIntent);
        var effectiveThemeId = RuntimeStyleIntentRequestUtility.BuildEffectiveThemeId(theme, runtimeIntent);
        var effectiveThemeDisplayName = RuntimeStyleIntentRequestUtility.BuildEffectiveThemeDisplayName(theme, runtimeIntent);
        var sourceRequests = CollectReusableRequests(currentRoomId);
        if (sourceRequests.Count == 0)
        {
            PublishSummary("no-captures", $"No reusable capture request matched room '{currentRoomId}'.");
            return;
        }

        var jobDirectory = ResolveLibraryDirectory(jobFolderName);
        Directory.CreateDirectory(jobDirectory);

        foreach (var source in sourceRequests)
        {
            try
            {
                if (!HasUsableSourceImage(source.Request))
                {
                    LastSkippedCount++;
                    continue;
                }

                if (skipExistingJobsForCurrentTheme &&
                    HasExistingJobForCurrentTheme(source.Request, effectiveThemeId, styleVariantId, jobDirectory))
                {
                    LastSkippedCount++;
                    continue;
                }

                QueueDerivedJob(source.Request, theme, runtimeIntent, styleVariantId, jobDirectory);
                LastQueuedCount++;
            }
            catch (Exception exception)
            {
                LastFailedCount++;
                Debug.LogWarning($"[CapturedFurnitureReuse] Failed to reuse capture {source.Path}: {exception.Message}", this);
            }
        }

        generationQueueStatusService?.Refresh();
        roomStyleCacheService?.Refresh();
        PublishSummary(
            "queued",
            $"Theme={effectiveThemeDisplayName}, style={styleVariantId}, scanned={sourceRequests.Count}, queued={LastQueuedCount}, skipped={LastSkippedCount}, failed={LastFailedCount}.");
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

        if (generationQueueStatusService == null)
        {
            generationQueueStatusService = FindAnyObjectByType<GenerationQueueStatusService>();
        }

        if (roomStyleCacheService == null)
        {
            roomStyleCacheService = FindAnyObjectByType<RoomStyleCacheService>();
        }
    }

    private List<ReusableRequest> CollectReusableRequests(string currentRoomId)
    {
        var byObject = new Dictionary<string, ReusableRequest>(StringComparer.OrdinalIgnoreCase);
        var output = new List<ReusableRequest>();
        foreach (var requestPath in EnumerateRequestPaths())
        {
            var request = TryReadJson<GeneratedObjectRequest>(requestPath);
            if (request == null || string.IsNullOrWhiteSpace(request.RequestId))
            {
                continue;
            }

            if (filterByCurrentRoomId && !string.IsNullOrWhiteSpace(currentRoomId) &&
                !string.Equals(request.RoomId, currentRoomId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var reusable = new ReusableRequest
            {
                Path = requestPath,
                Request = request,
                TimestampUtc = ResolveRequestTimestampUtc(request, requestPath),
            };

            if (!chooseLatestCapturePerObject)
            {
                output.Add(reusable);
                continue;
            }

            var key = BuildObjectKey(request);
            if (!byObject.TryGetValue(key, out var existing) || reusable.TimestampUtc > existing.TimestampUtc)
            {
                byObject[key] = reusable;
            }
        }

        if (chooseLatestCapturePerObject)
        {
            output.AddRange(byObject.Values);
        }

        output.Sort((left, right) => string.Compare(BuildObjectKey(left.Request), BuildObjectKey(right.Request), StringComparison.OrdinalIgnoreCase));
        return output;
    }

    private IEnumerable<string> EnumerateRequestPaths()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var captureRoot = ResolveLibraryDirectory(captureFolderName);
        if (Directory.Exists(captureRoot))
        {
            foreach (var path in Directory.GetFiles(captureRoot, "*.request.json", SearchOption.AllDirectories))
            {
                if (seen.Add(Path.GetFullPath(path)))
                {
                    yield return path;
                }
            }
        }

        var jobDirectory = ResolveLibraryDirectory(jobFolderName);
        if (!Directory.Exists(jobDirectory))
        {
            yield break;
        }

        foreach (var jobPath in Directory.GetFiles(jobDirectory, "*.job.json", SearchOption.TopDirectoryOnly))
        {
            var record = TryReadJson<GeneratedAssetRecord>(jobPath);
            if (record == null || string.IsNullOrWhiteSpace(record.SourceRequestPath) || !File.Exists(record.SourceRequestPath))
            {
                continue;
            }

            if (seen.Add(Path.GetFullPath(record.SourceRequestPath)))
            {
                yield return record.SourceRequestPath;
            }
        }
    }

    private void QueueDerivedJob(
        GeneratedObjectRequest source,
        ThemeProfile theme,
        RuntimeStyleIntent runtimeIntent,
        string styleVariantId,
        string jobDirectory)
    {
        var request = CloneRequest(source);
        var effectiveThemeId = RuntimeStyleIntentRequestUtility.BuildEffectiveThemeId(theme, runtimeIntent);
        var effectiveThemeDisplayName = RuntimeStyleIntentRequestUtility.BuildEffectiveThemeDisplayName(theme, runtimeIntent);
        var effectiveThemeDescription = RuntimeStyleIntentRequestUtility.BuildEffectiveThemeDescription(theme, runtimeIntent);
        var requestId = BuildDerivedRequestId(source, effectiveThemeId, styleVariantId, jobDirectory);
        var requestPath = Path.Combine(jobDirectory, $"{requestId}.request.json");
        var promptPath = Path.Combine(jobDirectory, $"{requestId}.prompt.txt");
        var jobPath = Path.Combine(jobDirectory, $"{requestId}.job.json");
        var timestamp = DateTime.UtcNow;

        request.RequestId = requestId;
        request.ThemeId = effectiveThemeId;
        request.ThemeDisplayName = effectiveThemeDisplayName;
        request.ThemeShortDescription = effectiveThemeDescription;
        request.StyleVariantId = styleVariantId;
        request.SourceRequestPath = requestPath;
        request.PromptVersion = GeneratedObjectPromptBuilder.RoomifyImagePromptVersion;
        request.AppearancePrompt = BuildAppearancePrompt(theme, runtimeIntent, request);
        request.PlannedReplacementDisplayName = BuildReplacementDisplayName(theme, runtimeIntent, request);
        request.PlannedReplicaName = string.IsNullOrWhiteSpace(request.PlannedReplicaName)
            ? request.PlannedReplacementDisplayName
            : request.PlannedReplicaName;
        request.PlannedReplicaFunction = string.IsNullOrWhiteSpace(request.PlannedReplicaFunction)
            ? request.FunctionTag
            : request.PlannedReplicaFunction;
        request.CreatedAtIsoUtc = timestamp.ToString("O");
        NormalizePhysicalSizeForReuse(request);

        RuntimeStyleIntentRequestUtility.ApplyThemeIdentityToRequest(theme, runtimeIntent, request);
        request.ImageStylizationPrompt = GeneratedObjectPromptBuilder.BuildImageStylizationPrompt(request);

        File.WriteAllText(requestPath, JsonUtility.ToJson(request, true), Utf8NoBom);
        File.WriteAllText(promptPath, request.ImageStylizationPrompt ?? string.Empty, Utf8NoBom);

        var record = new GeneratedAssetRecord
        {
            RequestId = request.RequestId,
            ObjectId = request.ObjectId,
            ThemeId = request.ThemeId,
            StyleVariantId = request.StyleVariantId,
            CaptureSourceMode = request.CaptureSourceMode,
            State = GeneratedObjectJobState.CaptureReady,
            SourceInputImagePath = request.SourceImagePath,
            SourceRequestPath = request.SourceRequestPath,
            CoordinatorJobPath = jobPath,
            StatusNote = "Queued by reusing an existing source capture for the current theme/style.",
            PreviewImagePath = request.SourceImagePath,
            SourceYawDegrees = request.BestViewYawDegrees,
            PromptVersion = request.PromptVersion,
            PromptArtifactPath = promptPath,
            TargetLengthMeters = request.TargetLengthMeters,
            TargetWidthMeters = request.TargetWidthMeters,
            TargetHeightMeters = request.TargetHeightMeters,
            TargetAspectRatio = request.TargetAspectRatio,
            SafetyFootprintScale = request.SafetyFootprintScale,
            VerticalFitMode = request.VerticalFitMode,
            UpdatedAtIsoUtc = timestamp.ToString("O"),
        };

        File.WriteAllText(jobPath, JsonUtility.ToJson(record, true), Utf8NoBom);
        Debug.Log($"[CapturedFurnitureReuse] Queued reused-capture job -> {jobPath}", this);
    }

    private bool HasExistingJobForCurrentTheme(
        GeneratedObjectRequest source,
        string themeId,
        string styleVariantId,
        string jobDirectory)
    {
        if (!Directory.Exists(jobDirectory))
        {
            return false;
        }

        var sourceKey = BuildObjectKey(source);
        foreach (var jobPath in Directory.GetFiles(jobDirectory, "*.job.json", SearchOption.TopDirectoryOnly))
        {
            var record = TryReadJson<GeneratedAssetRecord>(jobPath);
            if (record == null || record.State == GeneratedObjectJobState.Failed)
            {
                continue;
            }

            if (!string.Equals(record.ThemeId, themeId, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(NormalizeStyleVariant(record.StyleVariantId), NormalizeStyleVariant(styleVariantId), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.Equals(record.ObjectId, source.ObjectId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(BuildRecordFallbackKey(record), sourceKey, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static GeneratedObjectRequest CloneRequest(GeneratedObjectRequest source)
    {
        return JsonUtility.FromJson<GeneratedObjectRequest>(JsonUtility.ToJson(source));
    }

    private static void NormalizePhysicalSizeForReuse(GeneratedObjectRequest request)
    {
        if (request == null || !IsLowProfileFurniture(request.SemanticLabel))
        {
            return;
        }

        var x = Mathf.Max(0.01f, Mathf.Abs(request.Dimensions.x));
        var y = Mathf.Max(0.01f, Mathf.Abs(request.Dimensions.y));
        var z = Mathf.Max(0.01f, Mathf.Abs(request.Dimensions.z));
        var axes = new[] { x, y, z };
        Array.Sort(axes);
        request.TargetHeightMeters = axes[0];
        request.TargetWidthMeters = axes[1];
        request.TargetLengthMeters = axes[2];
        request.TargetAspectRatio = request.TargetWidthMeters > 0.0001f
            ? request.TargetLengthMeters / request.TargetWidthMeters
            : 1f;
    }

    private static bool IsLowProfileFurniture(string semanticLabel)
    {
        return string.Equals(semanticLabel, "table", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(semanticLabel, "bed", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasUsableSourceImage(GeneratedObjectRequest request)
    {
        return request != null &&
            !string.IsNullOrWhiteSpace(request.SourceImagePath) &&
            File.Exists(request.SourceImagePath);
    }

    private static string BuildObjectKey(GeneratedObjectRequest request)
    {
        if (request == null)
        {
            return "unknown";
        }

        if (!string.IsNullOrWhiteSpace(request.ObjectId))
        {
            return request.ObjectId.Trim();
        }

        return $"{request.SemanticLabel}_{SanitizeToken(request.SourceAnchorName)}_{request.SourceAnchorIndex:D2}";
    }

    private static string BuildRecordFallbackKey(GeneratedAssetRecord record)
    {
        return record == null || string.IsNullOrWhiteSpace(record.ObjectId) ? "unknown" : record.ObjectId.Trim();
    }

    private static string BuildDerivedRequestId(
        GeneratedObjectRequest source,
        string themeId,
        string styleVariantId,
        string jobDirectory)
    {
        var semantic = SanitizeToken(string.IsNullOrWhiteSpace(source.SemanticLabel) ? "object" : source.SemanticLabel);
        var objectToken = SanitizeToken(BuildObjectKey(source));
        if (objectToken.Length > 34)
        {
            objectToken = objectToken.Substring(0, 34).Trim('_');
        }

        var themeToken = SanitizeToken(themeId);
        var styleToken = SanitizeToken(NormalizeStyleVariant(styleVariantId));
        if (styleToken.Length > 32)
        {
            styleToken = styleToken.Substring(0, 32).Trim('_');
        }

        var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var baseId = $"{semantic}_{objectToken}_{themeToken}_{styleToken}_{timestamp}";
        var requestId = baseId;
        var suffix = 1;
        while (File.Exists(Path.Combine(jobDirectory, $"{requestId}.job.json")) ||
               File.Exists(Path.Combine(jobDirectory, $"{requestId}.request.json")))
        {
            requestId = $"{baseId}_{suffix++}";
        }

        return requestId;
    }

    private static string BuildAppearancePrompt(ThemeProfile theme, RuntimeStyleIntent runtimeIntent, GeneratedObjectRequest request)
    {
        var semantic = string.IsNullOrWhiteSpace(request.SemanticLabel) ? "object" : request.SemanticLabel.Trim();
        var functionTag = string.IsNullOrWhiteSpace(request.FunctionTag) ? "recognizable function" : request.FunctionTag.Trim();
        var themeDisplayName = RuntimeStyleIntentRequestUtility.BuildEffectiveThemeDisplayName(theme, runtimeIntent);
        var themeDescription = RuntimeStyleIntentRequestUtility.BuildEffectiveThemeDescription(theme, runtimeIntent);
        var builder = new StringBuilder(384);

        if (string.Equals(semantic, "other", StringComparison.OrdinalIgnoreCase))
        {
            builder.Append("Reuse the existing source capture to infer the object's visible category, then restyle that same physical object for the theme \"");
            builder.Append(themeDisplayName);
            builder.Append("\" while preserving footprint, dimensions, dominant yaw, contact surface, and recognizable function.");
        }
        else
        {
            builder.Append("Reuse the existing source capture to restyle this ");
            builder.Append(semantic);
            builder.Append(" for the theme \"");
            builder.Append(themeDisplayName);
            builder.Append("\" while preserving ");
            builder.Append(functionTag);
            builder.Append(", footprint, dimensions, dominant yaw, contact surfaces, and walk-around clearance.");
        }

        if (!string.IsNullOrWhiteSpace(themeDescription))
        {
            builder.Append(" Theme intent: ");
            builder.Append(themeDescription.Trim());
            builder.Append('.');
        }

        return builder.ToString();
    }

    private static string BuildReplacementDisplayName(ThemeProfile theme, RuntimeStyleIntent runtimeIntent, GeneratedObjectRequest request)
    {
        var semantic = string.IsNullOrWhiteSpace(request.SemanticLabel) ? "object" : request.SemanticLabel.Trim();
        return $"{RuntimeStyleIntentRequestUtility.BuildEffectiveThemeDisplayName(theme, runtimeIntent)} generated {semantic}";
    }

    private string ResolveCurrentRoomId()
    {
        if (roomSemanticBootstrap != null && roomSemanticBootstrap.CurrentRoom != null)
        {
            return roomSemanticBootstrap.CurrentRoom.name;
        }

        return "unknown_room";
    }

    private static DateTime ResolveRequestTimestampUtc(GeneratedObjectRequest request, string path)
    {
        if (request != null &&
            !string.IsNullOrWhiteSpace(request.CreatedAtIsoUtc) &&
            DateTime.TryParse(request.CreatedAtIsoUtc, out var parsed))
        {
            return parsed.ToUniversalTime();
        }

        try
        {
            return File.GetLastWriteTimeUtc(path);
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    private static string NormalizeStyleVariant(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? SurfaceTexturePromptBuilder.PresetStyleVariantId : value.Trim();
    }

    private static T TryReadJson<T>(string path) where T : class
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return null;
            }

            var json = File.ReadAllText(path, Utf8NoBom);
            return string.IsNullOrWhiteSpace(json) ? null : JsonUtility.FromJson<T>(json);
        }
        catch
        {
            return null;
        }
    }

    private string ResolveLibraryDirectory(string folderName)
    {
#if UNITY_EDITOR
        var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
        return Path.Combine(projectRoot, "Library", NormalizeFolderName(folderName));
#else
        return Path.Combine(Application.persistentDataPath, NormalizeFolderName(folderName));
#endif
    }

    private static string NormalizeFolderName(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "GeneratedObjectJobs" : value.Replace('\\', '/').Trim('/');
    }

    private static string SanitizeToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "object";
        }

        var builder = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            builder.Append(char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '_');
        }

        var token = builder.ToString().Trim('_');
        return string.IsNullOrWhiteSpace(token) ? "object" : token;
    }

    private void PublishSummary(string state, string detail)
    {
        latestSummary = $"[CapturedFurnitureReuse]\nState: {state}\n{detail}";
        Debug.Log(latestSummary, this);
    }

    private struct ReusableRequest
    {
        public string Path;
        public GeneratedObjectRequest Request;
        public DateTime TimestampUtc;
    }
}
