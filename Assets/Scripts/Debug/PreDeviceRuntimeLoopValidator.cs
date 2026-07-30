using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Meta.XR.MRUtilityKit;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PreDeviceRuntimeLoopValidator : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private RoomSemanticBootstrap roomSemanticBootstrap;
    [SerializeField] private ThemeIntentController themeIntentController;
    [SerializeField] private RuntimeStyleIntentController runtimeStyleIntentController;
    [SerializeField] private StylizationPlanner stylizationPlanner;
    [SerializeField] private QuestRuntimeGenerationClient runtimeGenerationClient;
    [SerializeField] private GenerationQueueStatusService generationQueueStatusService;

    [Header("Target")]
    [SerializeField] private bool preferTableTarget = true;
    [SerializeField] private bool allowAnySupportedFurnitureFallback = true;
    [SerializeField, Min(0.01f)] private float safetyFootprintScale = 1f;
    [SerializeField] private GeneratedObjectVerticalFitMode verticalFitMode = GeneratedObjectVerticalFitMode.PreserveScaffoldHeight;

    [Header("Artifacts")]
    [SerializeField] private string jobFolderName = "GeneratedObjectJobs";
    [SerializeField] private string requestPrefix = "predevice_room_loop";

    public string LatestSummary => latestSummary;
    public string LastQueuedJobPath => lastQueuedJobPath;
    public GeneratedObjectRequest LastRequest => lastRequest;

    private string latestSummary = "[PreDeviceRuntimeLoopValidator]\nState: waiting\nHint: enter Play after MRUK room load, then queue and submit a room-context runtime test.";
    private string lastQueuedJobPath = string.Empty;
    private GeneratedObjectRequest lastRequest;

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

    [ContextMenu("Queue Room Target Request")]
    public void QueueRoomTargetRequest()
    {
        QueueRoomTargetRequestInternal();
    }

    [ContextMenu("Queue And Submit Local Test")]
    public async void QueueAndSubmitLocalTest()
    {
        await QueueAndSubmitLocalTestAsync();
    }

    public async Task<GeneratedAssetRecord> QueueAndSubmitLocalTestAsync()
    {
        if (!QueueRoomTargetRequestInternal())
        {
            return null;
        }

        if (runtimeGenerationClient == null)
        {
            PublishSummary("blocked", "QuestRuntimeGenerationClient is missing.");
            return null;
        }

        PublishSummary("submitted", $"Queued {ShortId(lastRequest.RequestId)} and submitted to the local runtime test backend.");
        var record = await runtimeGenerationClient.SubmitLatestRequestAsync(true);
        generationQueueStatusService?.Refresh();
        PublishSummary(
            record != null ? record.State.ToString() : "no-record",
            record != null
                ? $"request={ShortId(record.RequestId)} object={record.ObjectId} job={lastQueuedJobPath}"
                : "Runtime client did not return a record.");
        return record;
    }

    public bool QueueRoomTargetRequestInternal()
    {
        ResolveReferences();
        if (!Application.isPlaying)
        {
            PublishSummary("blocked", "Enter Play Mode first; runtime GLB loading must be validated in Play or on Quest.");
            return false;
        }

        if (roomSemanticBootstrap == null || !roomSemanticBootstrap.HasReadyRoom || roomSemanticBootstrap.CurrentRoom == null)
        {
            PublishSummary("blocked", "MRUK room is not ready.");
            return false;
        }

        if (!TrySelectTargetAnchor(roomSemanticBootstrap.CurrentRoom, out var anchor, out var anchorIndex, out var category))
        {
            PublishSummary("blocked", "No supported MRUK furniture target found for pre-device runtime validation.");
            return false;
        }

        var request = BuildRequest(roomSemanticBootstrap.CurrentRoom, anchor, anchorIndex, category);
        if (request == null)
        {
            PublishSummary("blocked", "Failed to build generated-object request.");
            return false;
        }

        var jobPath = WriteJobArtifacts(request);
        if (string.IsNullOrWhiteSpace(jobPath))
        {
            PublishSummary("blocked", "Failed to write generated-object job artifacts.");
            return false;
        }

        lastRequest = request;
        lastQueuedJobPath = jobPath;
        generationQueueStatusService?.Refresh();
        PublishSummary(
            "queued",
            $"request={ShortId(request.RequestId)} room={request.RoomId} object={request.ObjectId} semantic={request.SemanticLabel} job={jobPath}");
        return true;
    }

    private bool TrySelectTargetAnchor(
        MRUKRoom room,
        out MRUKAnchor anchor,
        out int anchorIndex,
        out BestViewSemanticCategory category)
    {
        anchor = null;
        anchorIndex = -1;
        category = BestViewSemanticCategory.Other;

        if (room == null || room.Anchors == null)
        {
            return false;
        }

        if (preferTableTarget)
        {
            for (var index = 0; index < room.Anchors.Count; index++)
            {
                var candidate = room.Anchors[index];
                if (candidate != null && candidate.HasAnyLabel(MRUKAnchor.SceneLabels.TABLE))
                {
                    anchor = candidate;
                    anchorIndex = index;
                    category = BestViewSemanticCategory.Table;
                    return true;
                }
            }
        }

        if (!allowAnySupportedFurnitureFallback)
        {
            return false;
        }

        for (var index = 0; index < room.Anchors.Count; index++)
        {
            var candidate = room.Anchors[index];
            if (TryResolveCategory(candidate, out var resolvedCategory))
            {
                anchor = candidate;
                anchorIndex = index;
                category = resolvedCategory;
                return true;
            }
        }

        return false;
    }

    private GeneratedObjectRequest BuildRequest(
        MRUKRoom room,
        MRUKAnchor anchor,
        int anchorIndex,
        BestViewSemanticCategory category)
    {
        if (room == null || anchor == null)
        {
            return null;
        }

        var requestId = $"{SanitizeToken(requestPrefix)}_{category.ToString().ToLowerInvariant()}_{anchorIndex:D2}_{DateTime.UtcNow:yyyyMMddHHmmss}";
        var semanticLabel = GetSemanticLabel(category);
        var functionTag = GetFunctionTag(category);
        var plannedEntry = FindMatchingPlanEntry(anchor, anchorIndex);
        var anchorBounds = ResolveAnchorBounds(anchor);
        var targetSize = CalculateTargetPhysicalSize(anchorBounds.Size, semanticLabel);
        var theme = themeIntentController != null ? themeIntentController.ActiveTheme : null;
        var runtimeIntent = runtimeStyleIntentController != null ? runtimeStyleIntentController.CurrentIntent : null;
        var requestDirectory = GetJobDirectory();
        var requestPath = Path.Combine(requestDirectory, $"{requestId}.request.json");

        var request = new GeneratedObjectRequest
        {
            RequestId = requestId,
            ObjectId = plannedEntry != null
                ? plannedEntry.ObjectId
                : $"{SanitizeToken(anchor.name)}_{anchorIndex:D2}",
            RoomId = room.name,
            ThemeId = RuntimeStyleIntentRequestUtility.BuildEffectiveThemeId(theme, runtimeIntent),
            ThemeDisplayName = RuntimeStyleIntentRequestUtility.BuildEffectiveThemeDisplayName(theme, runtimeIntent),
            ThemeShortDescription = RuntimeStyleIntentRequestUtility.BuildEffectiveThemeDescription(theme, runtimeIntent),
            SemanticLabel = semanticLabel,
            FunctionTag = functionTag,
            SourceAnchorName = anchor.name,
            SourceAnchorIndex = anchorIndex,
            WorldPose = SerializablePose.From(anchorBounds.Center, anchor.transform.rotation),
            WorldBounds = SerializableBounds.From(anchorBounds.Center, anchorBounds.Size),
            Dimensions = anchorBounds.Size,
            TargetLengthMeters = targetSize.LengthMeters,
            TargetWidthMeters = targetSize.WidthMeters,
            TargetHeightMeters = targetSize.HeightMeters,
            TargetAspectRatio = targetSize.AspectRatio,
            SafetyFootprintScale = safetyFootprintScale,
            VerticalFitMode = verticalFitMode,
            CollisionSensitive = IsCollisionSensitive(functionTag),
            PlannedReplacementMode = plannedEntry != null ? plannedEntry.ReplacementMode : ReplacementMode.ProxyPrefab,
            PlannedReplacementId = plannedEntry != null ? plannedEntry.ReplacementId : string.Empty,
            PlannedReplacementDisplayName = plannedEntry != null ? plannedEntry.ReplacementDisplayName : string.Empty,
            PlannedReplicaName = plannedEntry != null ? plannedEntry.ReplicaName : string.Empty,
            PlannedReplicaFunction = plannedEntry != null ? plannedEntry.ReplicaFunction : string.Empty,
            PreserveFootprint = plannedEntry == null || plannedEntry.PreserveFootprint,
            PreserveYawOrientation = plannedEntry == null || plannedEntry.PreserveYawOrientation,
            CaptureSourceMode = BestViewCaptureSourceMode.UnityFramebufferDebug,
            SourceOriginalInputPath = string.Empty,
            SourceImagePath = string.Empty,
            SourceFullFrameImagePath = string.Empty,
            SourceCroppedImagePath = string.Empty,
            SourceMetadataPath = string.Empty,
            SourceRequestPath = requestPath,
            NormalizedCropRect = SerializableRect.FullFrame,
            BestViewCameraPose = SerializablePose.From(Camera.main != null ? Camera.main.transform.position : Vector3.zero, Camera.main != null ? Camera.main.transform.rotation : Quaternion.identity),
            BestViewYawDegrees = GetHorizontalYawDegrees(anchor.transform.forward),
            ScaffoldLongestAxis = GetDominantAxisVector(anchorBounds.Size),
            VisibilityScore = 1f,
            PromptVersion = GeneratedObjectPromptBuilder.RoomifyImagePromptVersion,
            AppearancePrompt = BuildAppearancePrompt(theme, runtimeIntent, semanticLabel, functionTag, plannedEntry),
            CreatedAtIsoUtc = DateTime.UtcNow.ToString("O"),
        };

        RuntimeStyleIntentRequestUtility.ApplyThemeIdentityToRequest(theme, runtimeIntent, request);
        request.ImageStylizationPrompt = GeneratedObjectPromptBuilder.BuildImageStylizationPrompt(request);
        return request;
    }

    private string WriteJobArtifacts(GeneratedObjectRequest request)
    {
        try
        {
            var jobDirectory = GetJobDirectory();
            Directory.CreateDirectory(jobDirectory);

            var requestPath = Path.Combine(jobDirectory, $"{request.RequestId}.request.json");
            request.SourceRequestPath = requestPath;
            File.WriteAllText(requestPath, JsonUtility.ToJson(request, true));

            var promptPath = Path.Combine(jobDirectory, $"{request.RequestId}.prompt.txt");
            File.WriteAllText(promptPath, request.ImageStylizationPrompt ?? string.Empty);

            var jobPath = Path.Combine(jobDirectory, $"{request.RequestId}.job.json");
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
                StatusNote = "Queued from pre-device MRUK room-context validator for local runtime GLB loading.",
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
                UpdatedAtIsoUtc = DateTime.UtcNow.ToString("O"),
            };

            File.WriteAllText(jobPath, JsonUtility.ToJson(record, true));
            Debug.Log($"[PreDeviceRuntimeLoopValidator] Queued room-context runtime job -> {jobPath}", this);
            return jobPath;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[PreDeviceRuntimeLoopValidator] Failed to write job artifacts: {exception.Message}", this);
            return string.Empty;
        }
    }

    private AnchorBounds ResolveAnchorBounds(MRUKAnchor anchor)
    {
        var localCenter = Vector3.zero;
        var dimensions = Vector3.one * 0.1f;

        if (anchor.VolumeBounds.HasValue)
        {
            var volumeBounds = anchor.VolumeBounds.Value;
            localCenter = volumeBounds.center;
            dimensions = volumeBounds.size;
        }
        else if (anchor.PlaneRect.HasValue)
        {
            var planeRect = anchor.PlaneRect.Value;
            dimensions = new Vector3(Mathf.Max(planeRect.width, 0.05f), 0.05f, Mathf.Max(planeRect.height, 0.05f));
        }

        return new AnchorBounds(anchor.transform.TransformPoint(localCenter), Abs(dimensions));
    }

    private StylizationPlanEntry FindMatchingPlanEntry(MRUKAnchor anchor, int anchorIndex)
    {
        if (stylizationPlanner == null || stylizationPlanner.CurrentPlan == null || stylizationPlanner.CurrentPlan.Entries == null)
        {
            return null;
        }

        var anchorName = anchor != null ? anchor.name : string.Empty;
        foreach (var entry in stylizationPlanner.CurrentPlan.Entries)
        {
            if (entry == null || entry.Parameters == null)
            {
                continue;
            }

            var matchesName = false;
            var matchesIndex = false;
            foreach (var parameter in entry.Parameters)
            {
                if (parameter == null)
                {
                    continue;
                }

                if (string.Equals(parameter.Key, "anchor_name", StringComparison.OrdinalIgnoreCase))
                {
                    matchesName = string.Equals(parameter.Value, anchorName, StringComparison.Ordinal);
                }
                else if (string.Equals(parameter.Key, "anchor_index", StringComparison.OrdinalIgnoreCase))
                {
                    matchesIndex = string.Equals(parameter.Value, anchorIndex.ToString(), StringComparison.Ordinal);
                }
            }

            if (matchesName && matchesIndex)
            {
                return entry;
            }
        }

        return null;
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

        if (stylizationPlanner == null)
        {
            stylizationPlanner = FindAnyObjectByType<StylizationPlanner>();
        }

        if (runtimeGenerationClient == null)
        {
            runtimeGenerationClient = FindAnyObjectByType<QuestRuntimeGenerationClient>();
        }

        if (generationQueueStatusService == null)
        {
            generationQueueStatusService = FindAnyObjectByType<GenerationQueueStatusService>();
        }
    }

    private string GetJobDirectory()
    {
#if UNITY_EDITOR
        var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
        return Path.Combine(projectRoot, "Library", string.IsNullOrWhiteSpace(jobFolderName) ? "GeneratedObjectJobs" : jobFolderName);
#else
        return Path.Combine(Application.persistentDataPath, string.IsNullOrWhiteSpace(jobFolderName) ? "GeneratedObjectJobs" : jobFolderName);
#endif
    }

    private void PublishSummary(string state, string detail)
    {
        latestSummary =
            "[PreDeviceRuntimeLoopValidator]\n" +
            $"State: {state}\n" +
            $"Detail: {detail}";
        Debug.Log(latestSummary, this);
    }

    private static bool TryResolveCategory(MRUKAnchor anchor, out BestViewSemanticCategory category)
    {
        category = BestViewSemanticCategory.Other;
        if (anchor == null)
        {
            return false;
        }

        if (anchor.HasAnyLabel(MRUKAnchor.SceneLabels.TABLE))
        {
            category = BestViewSemanticCategory.Table;
            return true;
        }

        if (anchor.HasAnyLabel(MRUKAnchor.SceneLabels.SCREEN))
        {
            category = BestViewSemanticCategory.Screen;
            return true;
        }

        if (anchor.HasAnyLabel(MRUKAnchor.SceneLabels.STORAGE))
        {
            category = BestViewSemanticCategory.Storage;
            return true;
        }

        if (anchor.HasAnyLabel(MRUKAnchor.SceneLabels.COUCH))
        {
            category = BestViewSemanticCategory.Seating;
            return true;
        }

        if (anchor.HasAnyLabel(MRUKAnchor.SceneLabels.BED))
        {
            category = BestViewSemanticCategory.Bed;
            return true;
        }

        if (anchor.HasAnyLabel(MRUKAnchor.SceneLabels.LAMP))
        {
            category = BestViewSemanticCategory.Lamp;
            return true;
        }

        if (anchor.HasAnyLabel(MRUKAnchor.SceneLabels.PLANT))
        {
            category = BestViewSemanticCategory.Plant;
            return true;
        }

        if (anchor.HasAnyLabel(MRUKAnchor.SceneLabels.OTHER))
        {
            category = BestViewSemanticCategory.Other;
            return true;
        }

        return false;
    }

    private static string GetSemanticLabel(BestViewSemanticCategory category)
    {
        return category switch
        {
            BestViewSemanticCategory.Table => "table",
            BestViewSemanticCategory.Screen => "screen",
            BestViewSemanticCategory.Storage => "storage",
            BestViewSemanticCategory.Seating => "seating",
            BestViewSemanticCategory.Bed => "bed",
            BestViewSemanticCategory.Lamp => "lamp",
            BestViewSemanticCategory.Plant => "plant",
            BestViewSemanticCategory.Other => "other",
            _ => "unknown",
        };
    }

    private static string GetFunctionTag(BestViewSemanticCategory category)
    {
        return category switch
        {
            BestViewSemanticCategory.Table => "support_surface",
            BestViewSemanticCategory.Screen => "display_surface",
            BestViewSemanticCategory.Storage => "storage",
            BestViewSemanticCategory.Seating => "seating",
            BestViewSemanticCategory.Bed => "sleeping_surface",
            BestViewSemanticCategory.Lamp => "lighting",
            BestViewSemanticCategory.Plant => "decorative_plant",
            BestViewSemanticCategory.Other => "model_inferred_object",
            _ => "unknown",
        };
    }

    private static bool IsCollisionSensitive(string functionTag)
    {
        return string.Equals(functionTag, "support_surface", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(functionTag, "display_surface", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(functionTag, "storage", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(functionTag, "seating", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(functionTag, "sleeping_surface", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(functionTag, "lighting", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(functionTag, "decorative_plant", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(functionTag, "model_inferred_object", StringComparison.OrdinalIgnoreCase);
    }

    private static TargetPhysicalSize CalculateTargetPhysicalSize(Vector3 dimensions, string semanticLabel)
    {
        var x = Mathf.Max(0.01f, Mathf.Abs(dimensions.x));
        var y = Mathf.Max(0.01f, Mathf.Abs(dimensions.y));
        var z = Mathf.Max(0.01f, Mathf.Abs(dimensions.z));
        var axes = new[] { x, y, z };
        Array.Sort(axes);

        if (string.Equals(semanticLabel, "table", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(semanticLabel, "bed", StringComparison.OrdinalIgnoreCase))
        {
            return new TargetPhysicalSize
            {
                LengthMeters = axes[2],
                WidthMeters = axes[1],
                HeightMeters = axes[0],
                AspectRatio = axes[1] > 0.0001f ? axes[2] / axes[1] : 1f,
            };
        }

        var length = Mathf.Max(x, z);
        var width = Mathf.Min(x, z);
        return new TargetPhysicalSize
        {
            LengthMeters = length,
            WidthMeters = width,
            HeightMeters = y,
            AspectRatio = width > 0.0001f ? length / width : 1f,
        };
    }

    private static float GetHorizontalYawDegrees(Vector3 forward)
    {
        var horizontalForward = Vector3.ProjectOnPlane(forward, Vector3.up);
        if (horizontalForward.sqrMagnitude < 0.0001f)
        {
            return 0f;
        }

        return Mathf.Atan2(horizontalForward.x, horizontalForward.z) * Mathf.Rad2Deg;
    }

    private static Vector3 GetDominantAxisVector(Vector3 dimensions)
    {
        if (dimensions.x >= dimensions.y && dimensions.x >= dimensions.z)
        {
            return Vector3.right * dimensions.x;
        }

        if (dimensions.y >= dimensions.x && dimensions.y >= dimensions.z)
        {
            return Vector3.up * dimensions.y;
        }

        return Vector3.forward * dimensions.z;
    }

    private static Vector3 Abs(Vector3 value)
    {
        return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
    }

    private static string BuildAppearancePrompt(
        ThemeProfile theme,
        RuntimeStyleIntent runtimeIntent,
        string semanticLabel,
        string functionTag,
        StylizationPlanEntry plannedEntry)
    {
        var builder = new StringBuilder(256);
        var themeDisplayName = RuntimeStyleIntentRequestUtility.BuildEffectiveThemeDisplayName(theme, runtimeIntent);
        var themeDescription = RuntimeStyleIntentRequestUtility.BuildEffectiveThemeDescription(theme, runtimeIntent);
        builder.Append("Pre-device local test: stylize the ");
        builder.Append(semanticLabel);
        builder.Append(" for the theme \"");
        builder.Append(themeDisplayName);
        builder.Append("\" while preserving ");
        builder.Append(functionTag);
        builder.Append(", approximate dimensions, dominant yaw, and walk-around footprint.");

        if (plannedEntry != null && !string.IsNullOrWhiteSpace(plannedEntry.AppearancePrompt))
        {
            builder.Append(' ');
            builder.Append(plannedEntry.AppearancePrompt.Trim());
        }
        else if (!string.IsNullOrWhiteSpace(themeDescription))
        {
            builder.Append(" Theme intent: ");
            builder.Append(themeDescription.Trim());
        }

        return builder.ToString();
    }

    private static string SanitizeToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "target";
        }

        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Trim().ToLowerInvariant())
        {
            builder.Append(char.IsLetterOrDigit(character) ? character : '_');
        }

        var result = builder.ToString().Trim('_');
        return string.IsNullOrWhiteSpace(result) ? "target" : result;
    }

    private static string ShortId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "none";
        }

        return value.Length <= 24 ? value : value[..24];
    }

    private readonly struct AnchorBounds
    {
        public AnchorBounds(Vector3 center, Vector3 size)
        {
            Center = center;
            Size = size;
        }

        public Vector3 Center { get; }
        public Vector3 Size { get; }
    }

    private struct TargetPhysicalSize
    {
        public float LengthMeters { get; set; }
        public float WidthMeters { get; set; }
        public float HeightMeters { get; set; }
        public float AspectRatio { get; set; }
    }
}
