using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Meta.XR.MRUtilityKit;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[DisallowMultipleComponent]
public class BestViewCaptureService : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private RoomSemanticBootstrap roomSemanticBootstrap;
    [SerializeField] private ThemeIntentController themeIntentController;
    [SerializeField] private RuntimeStyleIntentController runtimeStyleIntentController;
    [SerializeField] private StylizationPlanner stylizationPlanner;
    [SerializeField] private Camera referenceCamera;
    [SerializeField] private Transform referenceCameraTransform;

    [Header("Capture Target")]
    [SerializeField] private BestViewSemanticCategory targetCategory = BestViewSemanticCategory.Table;
    [SerializeField, Min(0.5f)] private float maxCaptureDistance = 5f;
    [SerializeField, Range(0f, 0.45f)] private float viewportMargin = 0.08f;
    [SerializeField] private bool trackBestCandidateDuringPlay = true;

    [Header("Generated Asset Fit Constraints")]
    [SerializeField, Range(0.75f, 1.15f)] private float safetyFootprintScale = 1f;
    [SerializeField] private GeneratedObjectVerticalFitMode verticalFitMode = GeneratedObjectVerticalFitMode.PreserveScaffoldHeight;

    [Header("Crop Settings")]
    [SerializeField, Range(0f, 0.25f)] private float cropPaddingNormalized = 0.04f;
    [SerializeField, Range(0.05f, 0.5f)] private float minCropSizeNormalized = 0.16f;

    [Header("Raw Capture Cleanup")]
    [SerializeField] private bool suppressStylizedContentForCapture = true;
    [SerializeField] private bool suppressDebugVisualsForCapture = true;
    [SerializeField] private bool suppressInteractionVisualsForCapture = true;

    [Header("Capture Output")]
    [SerializeField] private string captureFolderName = "BestViewCaptures";
    [SerializeField] private BestViewCaptureSourceMode captureSourceMode = BestViewCaptureSourceMode.ExternalScreenshot;
    [SerializeField] private string externalScreenshotPath = string.Empty;
    [SerializeField] private bool copyExternalScreenshotIntoCaptureFolder = true;

    [Header("Play Mode Visibility Toggle")]
    [SerializeField] private bool enableVisibilityToggleInPlay = true;
    [SerializeField] private KeyCode toggleVisibilityKey = KeyCode.V;

    [SerializeField] private bool enableKeyboardCapture = true;
    [SerializeField] private KeyCode captureKey = KeyCode.C;

    public event Action SummaryChanged;

    public string LatestSummary => _latestSummary;
    public bool HasBestCandidate => _bestCandidate.IsValid;
    public BestViewCaptureRecord LastCapture => _lastCapture;
    public GeneratedObjectRequest LastGeneratedRequest => _lastGeneratedRequest;

    private BestViewCandidate _bestCandidate;
    private BestViewCaptureRecord _lastCapture = new();
    private GeneratedObjectRequest _lastGeneratedRequest = new();
    private List<SuppressedGameObjectState> _manualVisibilitySuppressionStates;
    private bool _virtualCaptureObjectsHidden;
    private string _latestSummary = "[BestViewCaptureService]\nState: waiting\nHint: enter Play and wait for a visible semantic target.";

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

    private void OnDisable()
    {
        RestoreManualVisibilitySuppression();
    }

    private void Update()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        if (trackBestCandidateDuringPlay)
        {
            RefreshBestCandidate();
        }

        if (enableVisibilityToggleInPlay && WasCapturePressed(toggleVisibilityKey))
        {
            ToggleVirtualCaptureObjectsVisibility();
            return;
        }

        if (!enableKeyboardCapture || !WasCapturePressed(captureKey))
        {
            return;
        }

        CaptureCurrentBestView();
    }

    [ContextMenu("Refresh Best View Candidate")]
    public void RefreshBestViewCandidate()
    {
        RefreshBestCandidate();
    }

    [ContextMenu("Capture Current Best View")]
    public void CaptureCurrentBestView()
    {
        if (!Application.isPlaying)
        {
            PublishWaitingState("enter-play-mode");
            return;
        }

        RefreshBestCandidate();
        if (!_bestCandidate.IsValid)
        {
            PublishWaitingState("no-visible-target");
            return;
        }

        StartCoroutine(CaptureCurrentBestViewAtEndOfFrame());
    }

    public string GetDebugSummary()
    {
        return _latestSummary;
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

        if (referenceCamera == null)
        {
            referenceCamera = Camera.main;
            if (referenceCamera == null)
            {
                referenceCamera = FindAnyObjectByType<Camera>();
            }
        }

        if (referenceCameraTransform == null && referenceCamera != null)
        {
            referenceCameraTransform = referenceCamera.transform;
        }
    }

    private void RefreshBestCandidate()
    {
        ResolveReferences();

        if (roomSemanticBootstrap == null || !roomSemanticBootstrap.HasReadyRoom || roomSemanticBootstrap.CurrentRoom == null)
        {
            _bestCandidate = default;
            PublishWaitingState("waiting-for-room");
            return;
        }

        if (referenceCamera == null || referenceCameraTransform == null)
        {
            _bestCandidate = default;
            PublishWaitingState("missing-camera");
            return;
        }

        var room = roomSemanticBootstrap.CurrentRoom;
        var bestCandidate = default(BestViewCandidate);
        var hasCandidate = false;

        for (var index = 0; index < room.Anchors.Count; index++)
        {
            var anchor = room.Anchors[index];
            if (!MatchesTargetCategory(anchor))
            {
                continue;
            }

            if (!TryBuildCandidate(anchor, index, out var candidate))
            {
                continue;
            }

            if (!hasCandidate || candidate.Score > bestCandidate.Score)
            {
                bestCandidate = candidate;
                hasCandidate = true;
            }
        }

        _bestCandidate = hasCandidate ? bestCandidate : default;
        PublishSummary(hasCandidate ? "tracking" : "no-visible-target");
    }

    private bool TryBuildCandidate(MRUKAnchor anchor, int anchorIndex, out BestViewCandidate candidate)
    {
        candidate = default;
        if (anchor == null || referenceCamera == null)
        {
            return false;
        }

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

        var worldCenter = anchor.transform.TransformPoint(localCenter);
        var viewportPoint = referenceCamera.WorldToViewportPoint(worldCenter);
        if (viewportPoint.z <= 0f || viewportPoint.z > maxCaptureDistance)
        {
            return false;
        }

        if (viewportPoint.x < viewportMargin || viewportPoint.x > 1f - viewportMargin ||
            viewportPoint.y < viewportMargin || viewportPoint.y > 1f - viewportMargin)
        {
            return false;
        }

        var horizontalForward = Vector3.ProjectOnPlane(referenceCameraTransform.forward, Vector3.up);
        var toTarget = Vector3.ProjectOnPlane(worldCenter - referenceCameraTransform.position, Vector3.up);
        if (horizontalForward.sqrMagnitude > 0.0001f && toTarget.sqrMagnitude > 0.0001f)
        {
            var facingDot = Vector3.Dot(horizontalForward.normalized, toTarget.normalized);
            if (facingDot < 0.2f)
            {
                return false;
            }
        }

        var centerDistance = Vector2.Distance(new Vector2(viewportPoint.x, viewportPoint.y), new Vector2(0.5f, 0.5f));
        var centerScore = Mathf.Clamp01(1f - centerDistance / 0.65f);
        var distanceScore = Mathf.Clamp01(1f - viewportPoint.z / maxCaptureDistance);
        var apparentSizeScore = Mathf.Clamp01(Mathf.Max(dimensions.x, dimensions.z) / 2f);

        candidate = new BestViewCandidate
        {
            Anchor = anchor,
            AnchorIndex = anchorIndex,
            WorldCenter = worldCenter,
            Dimensions = dimensions,
            ViewportCenter = new Vector2(viewportPoint.x, viewportPoint.y),
            Distance = viewportPoint.z,
            Score = centerScore * 0.55f + distanceScore * 0.25f + apparentSizeScore * 0.2f,
            CropRect = TryCalculateNormalizedCropRect(anchor, localCenter, dimensions, out var cropRect)
                ? cropRect
                : BuildFallbackCropRect(new Vector2(viewportPoint.x, viewportPoint.y), dimensions, viewportPoint.z),
        };

        return true;
    }

    private IEnumerator CaptureCurrentBestViewAtEndOfFrame()
    {
        yield return new WaitForEndOfFrame();

        var captureId = $"{targetCategory.ToString().ToLowerInvariant()}_{_bestCandidate.AnchorIndex:D2}_{DateTime.UtcNow:yyyyMMddHHmmss}";
        var outputDirectory = GetCaptureDirectory();
        Directory.CreateDirectory(outputDirectory);

        var capturedAtUtc = DateTime.UtcNow;
        var imagePath = Path.Combine(outputDirectory, $"{captureId}.png");
        var croppedImagePath = Path.Combine(outputDirectory, $"{captureId}.crop.png");
        var metadataPath = Path.Combine(outputDirectory, $"{captureId}.json");
        var requestPath = Path.Combine(outputDirectory, $"{captureId}.request.json");
        Texture2D capturedTexture = null;
        Texture2D croppedTexture = null;
        string sourceOriginalInputPath = string.Empty;
        string resolvedRawImagePath = imagePath;
        string resolvedCropImagePath = croppedImagePath;

        switch (captureSourceMode)
        {
            case BestViewCaptureSourceMode.ExternalScreenshot:
                sourceOriginalInputPath = externalScreenshotPath;
                if (!TryLoadExternalScreenshotTexture(externalScreenshotPath, out capturedTexture))
                {
                    PublishSummary("external-source-missing");
                    Debug.LogWarning("[BestViewCaptureService] External screenshot source is missing or unreadable.", this);
                    yield break;
                }

                if (copyExternalScreenshotIntoCaptureFolder)
                {
                    WriteTextureToPng(capturedTexture, imagePath);
                    resolvedCropImagePath = imagePath;
                }
                else
                {
                    resolvedRawImagePath = externalScreenshotPath;
                    resolvedCropImagePath = externalScreenshotPath;
                }

                break;

            case BestViewCaptureSourceMode.UnityFramebufferDebug:
            {
                var suppressionStates = BeginRawCaptureSuppression();
                yield return null;
                yield return new WaitForEndOfFrame();

                capturedTexture = CaptureCurrentFrameTexture();
                RestoreSuppressedGameObjects(suppressionStates);

                if (capturedTexture == null)
                {
                    PublishSummary("capture-failed");
                    Debug.LogWarning("[BestViewCaptureService] Failed to read the current frame for capture.", this);
                    yield break;
                }

                croppedTexture = CropTexture(capturedTexture, _bestCandidate.CropRect);
                WriteTextureToPng(capturedTexture, imagePath);
                if (croppedTexture != null)
                {
                    WriteTextureToPng(croppedTexture, croppedImagePath);
                }

                resolvedCropImagePath = croppedTexture != null ? croppedImagePath : string.Empty;

                break;
            }

            case BestViewCaptureSourceMode.DevicePassthroughReserved:
                PublishSummary("device-source-reserved");
                Debug.LogWarning("[BestViewCaptureService] Device passthrough capture is reserved for the future true-device path.", this);
                yield break;

            default:
                PublishSummary("capture-source-unsupported");
                Debug.LogWarning($"[BestViewCaptureService] Unsupported capture source mode: {captureSourceMode}", this);
                yield break;
        }

        _lastCapture = new BestViewCaptureRecord
        {
            CaptureId = captureId,
            RoomId = roomSemanticBootstrap != null && roomSemanticBootstrap.CurrentRoom != null
                ? roomSemanticBootstrap.CurrentRoom.name
                : "unknown_room",
            ThemeId = themeIntentController != null && themeIntentController.ActiveTheme != null
                ? themeIntentController.ActiveTheme.ThemeId
                : "no_theme",
            SemanticLabel = GetSemanticLabel(targetCategory),
            FunctionTag = GetFunctionTag(targetCategory),
            AnchorName = _bestCandidate.Anchor != null ? _bestCandidate.Anchor.name : "unknown_anchor",
            AnchorIndex = _bestCandidate.AnchorIndex,
            CreatedAtIsoUtc = capturedAtUtc.ToString("O"),
            CaptureSourceMode = captureSourceMode,
            SourceOriginalInputPath = sourceOriginalInputPath,
            ImagePath = resolvedRawImagePath,
            CroppedImagePath = resolvedCropImagePath,
            MetadataPath = metadataPath,
            GeneratedRequestPath = requestPath,
            WorldPosition = _bestCandidate.WorldCenter,
            Dimensions = _bestCandidate.Dimensions,
            CameraPosition = referenceCameraTransform != null ? referenceCameraTransform.position : Vector3.zero,
            CameraForward = referenceCameraTransform != null ? referenceCameraTransform.forward : Vector3.forward,
            ViewportCenter = _bestCandidate.ViewportCenter,
            NormalizedCropRect = SerializableRect.From(_bestCandidate.CropRect),
            Score = _bestCandidate.Score,
        };

        _lastGeneratedRequest = BuildGeneratedObjectRequest(
            captureId,
            sourceOriginalInputPath,
            resolvedRawImagePath,
            resolvedCropImagePath,
            metadataPath,
            requestPath,
            capturedAtUtc);
        File.WriteAllText(metadataPath, JsonUtility.ToJson(_lastCapture, true));
        File.WriteAllText(requestPath, JsonUtility.ToJson(_lastGeneratedRequest, true));
        SafeDestroy(capturedTexture);
        SafeDestroy(croppedTexture);
        PublishSummary("captured");
        Debug.Log($"[BestViewCaptureService] Captured best-view request -> {requestPath}", this);
    }

    private string GetCaptureDirectory()
    {
        var libraryRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Library"));
        return Path.Combine(libraryRoot, captureFolderName);
    }

    private bool MatchesTargetCategory(MRUKAnchor anchor)
    {
        if (anchor == null)
        {
            return false;
        }

        return targetCategory switch
        {
            BestViewSemanticCategory.Table => anchor.HasAnyLabel(MRUKAnchor.SceneLabels.TABLE),
            BestViewSemanticCategory.Screen => anchor.HasAnyLabel(MRUKAnchor.SceneLabels.SCREEN),
            BestViewSemanticCategory.Storage => anchor.HasAnyLabel(MRUKAnchor.SceneLabels.STORAGE),
            BestViewSemanticCategory.Seating => anchor.HasAnyLabel(MRUKAnchor.SceneLabels.COUCH),
            BestViewSemanticCategory.Other => anchor.HasAnyLabel(MRUKAnchor.SceneLabels.OTHER),
            _ => false,
        };
    }

    private void PublishWaitingState(string reason)
    {
        PublishSummary(reason);
    }

    private void PublishSummary(string state)
    {
        var builder = new StringBuilder(512);
        builder.AppendLine("[BestViewCaptureService]");
        builder.AppendLine($"State: {state}");
        builder.AppendLine($"Target: {targetCategory}");
        builder.AppendLine($"Source Mode: {captureSourceMode}");
        builder.AppendLine($"Virtual Objects: {(_virtualCaptureObjectsHidden ? "hidden" : "visible")}");

        if (_bestCandidate.IsValid)
        {
            builder.AppendLine($"Best Anchor: {_bestCandidate.AnchorIndex:D2} ({_bestCandidate.Anchor.name})");
            builder.AppendLine($"Score: {_bestCandidate.Score:F2}");
            builder.AppendLine($"Distance: {_bestCandidate.Distance:F2}m");
            builder.AppendLine($"Viewport: {_bestCandidate.ViewportCenter.x:F2}, {_bestCandidate.ViewportCenter.y:F2}");
            builder.AppendLine($"Dimensions: {_bestCandidate.Dimensions.x:F2}, {_bestCandidate.Dimensions.y:F2}, {_bestCandidate.Dimensions.z:F2}");
            builder.AppendLine(
                $"Crop: {_bestCandidate.CropRect.x:F2}, {_bestCandidate.CropRect.y:F2}, {_bestCandidate.CropRect.width:F2}, {_bestCandidate.CropRect.height:F2}");
        }
        else
        {
            builder.AppendLine("Best Anchor: none");
        }

        if (!string.IsNullOrWhiteSpace(_lastCapture.CaptureId))
        {
            builder.AppendLine($"Last Capture: {_lastCapture.CaptureId}");
            if (!string.IsNullOrWhiteSpace(_lastCapture.SourceOriginalInputPath))
            {
                builder.AppendLine($"Input Path: {_lastCapture.SourceOriginalInputPath}");
            }

            builder.AppendLine($"Raw Image: {_lastCapture.ImagePath}");
            builder.AppendLine($"Crop Image: {_lastCapture.CroppedImagePath}");
            builder.AppendLine($"Request Path: {_lastCapture.GeneratedRequestPath}");
        }
        else
        {
            builder.AppendLine("Last Capture: none");
        }

        if (enableVisibilityToggleInPlay)
        {
            builder.AppendLine($"Toggle Key: {toggleVisibilityKey} (Play mode)");
        }

        builder.AppendLine($"Key: {captureKey} (Play mode)");
        _latestSummary = builder.ToString().TrimEnd();
        SummaryChanged?.Invoke();
    }

    private static string GetSemanticLabel(BestViewSemanticCategory category)
    {
        return category switch
        {
            BestViewSemanticCategory.Table => "table",
            BestViewSemanticCategory.Screen => "screen",
            BestViewSemanticCategory.Storage => "storage",
            BestViewSemanticCategory.Seating => "seating",
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
            BestViewSemanticCategory.Other => "general",
            _ => "unknown",
        };
    }

    private static bool WasCapturePressed(KeyCode keyCode)
    {
#if ENABLE_INPUT_SYSTEM
        var keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return false;
        }

        if (!Enum.TryParse(keyCode.ToString(), true, out UnityEngine.InputSystem.Key inputKey))
        {
            return false;
        }

        var keyControl = keyboard[inputKey];
        return keyControl != null && keyControl.wasPressedThisFrame;
#else
        return Input.GetKeyDown(keyCode);
#endif
    }

    private void ToggleVirtualCaptureObjectsVisibility()
    {
        if (_virtualCaptureObjectsHidden)
        {
            RestoreManualVisibilitySuppression();
            PublishSummary("virtual-objects-visible");
            return;
        }

        _manualVisibilitySuppressionStates = BeginManualVisibilitySuppression();
        _virtualCaptureObjectsHidden = _manualVisibilitySuppressionStates != null && _manualVisibilitySuppressionStates.Count > 0;
        PublishSummary(_virtualCaptureObjectsHidden ? "virtual-objects-hidden" : "virtual-objects-unchanged");
    }

    private GeneratedObjectRequest BuildGeneratedObjectRequest(
        string captureId,
        string sourceOriginalInputPath,
        string imagePath,
        string croppedImagePath,
        string metadataPath,
        string requestPath,
        DateTime capturedAtUtc)
    {
        var semanticLabel = GetSemanticLabel(targetCategory);
        var functionTag = GetFunctionTag(targetCategory);
        var theme = themeIntentController != null ? themeIntentController.ActiveTheme : null;
        var plannedEntry = FindMatchingPlanEntry(_bestCandidate.Anchor, _bestCandidate.AnchorIndex);
        var worldRotation = _bestCandidate.Anchor != null ? _bestCandidate.Anchor.transform.rotation : Quaternion.identity;
        var targetSize = CalculateTargetPhysicalSize(_bestCandidate.Dimensions);

        var request = new GeneratedObjectRequest
        {
            RequestId = captureId,
            ObjectId = plannedEntry != null
                ? plannedEntry.ObjectId
                : $"{SanitizeToken(_bestCandidate.Anchor != null ? _bestCandidate.Anchor.name : semanticLabel)}_{_bestCandidate.AnchorIndex:D2}",
            RoomId = roomSemanticBootstrap != null && roomSemanticBootstrap.CurrentRoom != null
                ? roomSemanticBootstrap.CurrentRoom.name
                : "unknown_room",
            ThemeId = theme != null ? theme.ThemeId : "no_theme",
            ThemeDisplayName = theme != null ? theme.DisplayName : "No Theme",
            ThemeShortDescription = theme != null ? theme.ShortDescription : string.Empty,
            SemanticLabel = semanticLabel,
            FunctionTag = functionTag,
            SourceAnchorName = _bestCandidate.Anchor != null ? _bestCandidate.Anchor.name : "unknown_anchor",
            SourceAnchorIndex = _bestCandidate.AnchorIndex,
            WorldPose = SerializablePose.From(_bestCandidate.WorldCenter, worldRotation),
            WorldBounds = SerializableBounds.From(_bestCandidate.WorldCenter, _bestCandidate.Dimensions),
            Dimensions = _bestCandidate.Dimensions,
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
            CaptureSourceMode = captureSourceMode,
            SourceOriginalInputPath = sourceOriginalInputPath,
            SourceImagePath = !string.IsNullOrWhiteSpace(croppedImagePath) ? croppedImagePath : imagePath,
            SourceFullFrameImagePath = imagePath,
            SourceCroppedImagePath = croppedImagePath,
            SourceMetadataPath = metadataPath,
            SourceRequestPath = requestPath,
            NormalizedCropRect = SerializableRect.From(_bestCandidate.CropRect),
            BestViewCameraPose = SerializablePose.From(
                referenceCameraTransform != null ? referenceCameraTransform.position : Vector3.zero,
                referenceCameraTransform != null ? referenceCameraTransform.rotation : Quaternion.identity),
            BestViewYawDegrees = GetHorizontalYawDegrees(
                referenceCameraTransform != null ? referenceCameraTransform.forward : Vector3.forward),
            ScaffoldLongestAxis = GetDominantAxisVector(_bestCandidate.Dimensions),
            VisibilityScore = _bestCandidate.Score,
            PromptVersion = GeneratedObjectPromptBuilder.RoomifyImagePromptVersion,
            AppearancePrompt = BuildAppearancePrompt(theme, semanticLabel, functionTag, plannedEntry),
            CreatedAtIsoUtc = capturedAtUtc.ToString("O"),
        };

        RuntimeStyleIntentRequestUtility.ApplyToRequest(
            runtimeStyleIntentController != null ? runtimeStyleIntentController.CurrentIntent : null,
            request);
        request.ImageStylizationPrompt = GeneratedObjectPromptBuilder.BuildImageStylizationPrompt(request);
        return request;
    }

    private StylizationPlanEntry FindMatchingPlanEntry(MRUKAnchor anchor, int anchorIndex)
    {
        if (stylizationPlanner == null || stylizationPlanner.CurrentPlan == null || stylizationPlanner.CurrentPlan.Entries == null)
        {
            return null;
        }

        var anchorName = anchor != null ? anchor.name : string.Empty;
        for (var index = 0; index < stylizationPlanner.CurrentPlan.Entries.Count; index++)
        {
            var entry = stylizationPlanner.CurrentPlan.Entries[index];
            if (entry == null || entry.Parameters == null)
            {
                continue;
            }

            var matchesName = false;
            var matchesIndex = false;
            for (var parameterIndex = 0; parameterIndex < entry.Parameters.Count; parameterIndex++)
            {
                var parameter = entry.Parameters[parameterIndex];
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

    private static bool IsCollisionSensitive(string functionTag)
    {
        return string.Equals(functionTag, "support_surface", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(functionTag, "display_surface", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(functionTag, "storage", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(functionTag, "seating", StringComparison.OrdinalIgnoreCase);
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

    private static TargetPhysicalSize CalculateTargetPhysicalSize(Vector3 dimensions)
    {
        var x = Mathf.Max(0.01f, Mathf.Abs(dimensions.x));
        var y = Mathf.Max(0.01f, Mathf.Abs(dimensions.y));
        var z = Mathf.Max(0.01f, Mathf.Abs(dimensions.z));
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

    private static string BuildAppearancePrompt(
        ThemeProfile theme,
        string semanticLabel,
        string functionTag,
        StylizationPlanEntry plannedEntry)
    {
        var builder = new StringBuilder(256);
        builder.Append("Stylize the ");
        builder.Append(semanticLabel);
        builder.Append(" for the theme \"");
        builder.Append(theme != null ? theme.DisplayName : "Unassigned Theme");
        builder.Append("\" while preserving its ");
        builder.Append(functionTag);
        builder.Append(", approximate dimensions, dominant yaw, and walk-around footprint.");

        if (theme != null && !string.IsNullOrWhiteSpace(theme.ShortDescription))
        {
            builder.Append(" Theme intent: ");
            builder.Append(theme.ShortDescription.Trim());
            builder.Append('.');
        }

        if (plannedEntry != null && !string.IsNullOrWhiteSpace(plannedEntry.ReplacementDisplayName))
        {
            builder.Append(" Preferred replacement cue: ");
            builder.Append(plannedEntry.ReplacementDisplayName);
            builder.Append('.');
        }

        if (plannedEntry != null && !string.IsNullOrWhiteSpace(plannedEntry.ReplicaName))
        {
            builder.Append(" Roomify replica: ");
            builder.Append(plannedEntry.ReplicaName.Trim());
            if (!string.IsNullOrWhiteSpace(plannedEntry.ReplicaFunction))
            {
                builder.Append(" preserving ");
                builder.Append(plannedEntry.ReplicaFunction.Trim());
            }

            builder.Append('.');
        }

        if (plannedEntry != null && !string.IsNullOrWhiteSpace(plannedEntry.AppearancePrompt))
        {
            builder.Append(" Appearance details: ");
            builder.Append(plannedEntry.AppearancePrompt.Trim());
        }

        return builder.ToString();
    }

    private static bool TryLoadExternalScreenshotTexture(string filePath, out Texture2D texture)
    {
        texture = null;
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return false;
        }

        var bytes = File.ReadAllBytes(filePath);
        if (bytes == null || bytes.Length == 0)
        {
            return false;
        }

        texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
        if (!texture.LoadImage(bytes, false))
        {
            SafeDestroy(texture);
            texture = null;
            return false;
        }

        texture.Apply(false, false);
        return true;
    }

    private static void WriteTextureToPng(Texture2D texture, string outputPath)
    {
        if (texture == null || string.IsNullOrWhiteSpace(outputPath))
        {
            return;
        }

        File.WriteAllBytes(outputPath, texture.EncodeToPNG());
    }

    private List<SuppressedGameObjectState> BeginRawCaptureSuppression()
    {
        var states = new List<SuppressedGameObjectState>(8);
        if (!suppressStylizedContentForCapture &&
            !suppressDebugVisualsForCapture &&
            !suppressInteractionVisualsForCapture)
        {
            return states;
        }

        var allTransforms = FindObjectsByType<Transform>(FindObjectsInactive.Include);
        var seenObjects = new HashSet<GameObject>();

        for (var index = 0; index < allTransforms.Length; index++)
        {
            var current = allTransforms[index];
            if (current == null)
            {
                continue;
            }

            if (!ShouldSuppressForRawCapture(current))
            {
                continue;
            }

            var targetObject = current.gameObject;
            if (!seenObjects.Add(targetObject))
            {
                continue;
            }

            states.Add(new SuppressedGameObjectState(targetObject, targetObject.activeSelf));
            targetObject.SetActive(false);
        }

        return states;
    }

    private List<SuppressedGameObjectState> BeginManualVisibilitySuppression()
    {
        var states = new List<SuppressedGameObjectState>(16);
        var allTransforms = FindObjectsByType<Transform>(FindObjectsInactive.Include);
        var seenObjects = new HashSet<GameObject>();

        for (var index = 0; index < allTransforms.Length; index++)
        {
            var current = allTransforms[index];
            if (current == null)
            {
                continue;
            }

            if (!ShouldSuppressForManualVisibilityToggle(current))
            {
                continue;
            }

            var targetObject = current.gameObject;
            if (!seenObjects.Add(targetObject))
            {
                continue;
            }

            states.Add(new SuppressedGameObjectState(targetObject, targetObject.activeSelf));
            targetObject.SetActive(false);
        }

        return states;
    }

    private bool ShouldSuppressForRawCapture(Transform current)
    {
        var name = current.name;
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        if (suppressStylizedContentForCapture && string.Equals(name, "StylizedContentRoot", StringComparison.Ordinal))
        {
            return true;
        }

        if (suppressDebugVisualsForCapture &&
            (string.Equals(name, "SemanticDebugCanvas", StringComparison.Ordinal) ||
             string.Equals(name, "RoomModel", StringComparison.Ordinal)))
        {
            return true;
        }

        if (suppressInteractionVisualsForCapture &&
            (string.Equals(name, "OVRInteractionComprehensive", StringComparison.Ordinal) ||
             name.Contains("ControllerVisual", StringComparison.Ordinal) ||
             name.Contains("HandVisual", StringComparison.Ordinal)))
        {
            return true;
        }

        return false;
    }

    private bool ShouldSuppressForManualVisibilityToggle(Transform current)
    {
        var name = current.name;
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        if (suppressStylizedContentForCapture &&
            (string.Equals(name, "StylizedContentRoot", StringComparison.Ordinal) ||
             IsRuntimeSemanticVisualRoot(name)))
        {
            return true;
        }

        if (suppressDebugVisualsForCapture &&
            (string.Equals(name, "RoomModel", StringComparison.Ordinal) ||
             IsRuntimeSemanticVisualRoot(name)))
        {
            return true;
        }

        if (suppressInteractionVisualsForCapture &&
            (string.Equals(name, "OVRInteractionComprehensive", StringComparison.Ordinal) ||
             name.Contains("ControllerVisual", StringComparison.Ordinal) ||
             name.Contains("HandVisual", StringComparison.Ordinal)))
        {
            return true;
        }

        return false;
    }

    private static bool IsRuntimeSemanticVisualRoot(string name)
    {
        return name.Contains("(PrefabSpawner Clone)", StringComparison.Ordinal) ||
               name.StartsWith("Volume(", StringComparison.Ordinal) ||
               name.StartsWith("PlaneMesh(", StringComparison.Ordinal);
    }

    private static void RestoreSuppressedGameObjects(List<SuppressedGameObjectState> states)
    {
        if (states == null)
        {
            return;
        }

        for (var index = states.Count - 1; index >= 0; index--)
        {
            var state = states[index];
            if (state.Target == null)
            {
                continue;
            }

            state.Target.SetActive(state.WasActive);
        }
    }

    private void RestoreManualVisibilitySuppression()
    {
        RestoreSuppressedGameObjects(_manualVisibilitySuppressionStates);
        _manualVisibilitySuppressionStates = null;
        _virtualCaptureObjectsHidden = false;
    }

    private static Texture2D CaptureCurrentFrameTexture()
    {
        if (Screen.width <= 0 || Screen.height <= 0)
        {
            return null;
        }

        var texture = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
        texture.ReadPixels(new Rect(0f, 0f, Screen.width, Screen.height), 0, 0);
        texture.Apply(false, false);
        return texture;
    }

    private static Texture2D CropTexture(Texture2D sourceTexture, Rect normalizedCropRect)
    {
        if (sourceTexture == null)
        {
            return null;
        }

        var pixelRect = ToPixelRect(normalizedCropRect, sourceTexture.width, sourceTexture.height);
        if (pixelRect.width <= 0 || pixelRect.height <= 0)
        {
            return null;
        }

        var pixels = sourceTexture.GetPixels(pixelRect.x, pixelRect.y, pixelRect.width, pixelRect.height);
        var croppedTexture = new Texture2D(pixelRect.width, pixelRect.height, TextureFormat.RGB24, false);
        croppedTexture.SetPixels(pixels);
        croppedTexture.Apply(false, false);
        return croppedTexture;
    }

    private static RectInt ToPixelRect(Rect normalizedCropRect, int textureWidth, int textureHeight)
    {
        var x = Mathf.Clamp(Mathf.FloorToInt(normalizedCropRect.x * textureWidth), 0, Mathf.Max(0, textureWidth - 1));
        var y = Mathf.Clamp(Mathf.FloorToInt(normalizedCropRect.y * textureHeight), 0, Mathf.Max(0, textureHeight - 1));
        var width = Mathf.Clamp(Mathf.CeilToInt(normalizedCropRect.width * textureWidth), 1, textureWidth - x);
        var height = Mathf.Clamp(Mathf.CeilToInt(normalizedCropRect.height * textureHeight), 1, textureHeight - y);
        return new RectInt(x, y, width, height);
    }

    private static void SafeDestroy(UnityEngine.Object target)
    {
        if (target == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(target);
        }
        else
        {
            DestroyImmediate(target);
        }
    }

    private bool TryCalculateNormalizedCropRect(
        MRUKAnchor anchor,
        Vector3 localCenter,
        Vector3 dimensions,
        out Rect cropRect)
    {
        cropRect = default;
        if (anchor == null || referenceCamera == null)
        {
            return false;
        }

        var worldPoints = GetAnchorCropSamplePoints(anchor, localCenter, dimensions);
        var hasVisiblePoint = false;
        var minX = float.MaxValue;
        var minY = float.MaxValue;
        var maxX = float.MinValue;
        var maxY = float.MinValue;

        for (var index = 0; index < worldPoints.Length; index++)
        {
            var viewportPoint = referenceCamera.WorldToViewportPoint(worldPoints[index]);
            if (viewportPoint.z <= 0f)
            {
                continue;
            }

            hasVisiblePoint = true;
            minX = Mathf.Min(minX, viewportPoint.x);
            minY = Mathf.Min(minY, viewportPoint.y);
            maxX = Mathf.Max(maxX, viewportPoint.x);
            maxY = Mathf.Max(maxY, viewportPoint.y);
        }

        if (!hasVisiblePoint)
        {
            return false;
        }

        cropRect = BuildCropRect(minX, minY, maxX, maxY);
        return cropRect.width > 0f && cropRect.height > 0f;
    }

    private Vector3[] GetAnchorCropSamplePoints(MRUKAnchor anchor, Vector3 localCenter, Vector3 dimensions)
    {
        var halfExtents = dimensions * 0.5f;
        halfExtents.x = Mathf.Max(halfExtents.x, 0.03f);
        halfExtents.y = Mathf.Max(halfExtents.y, 0.03f);
        halfExtents.z = Mathf.Max(halfExtents.z, 0.03f);

        var points = new Vector3[9];
        var pointIndex = 0;
        for (var x = -1; x <= 1; x += 2)
        {
            for (var y = -1; y <= 1; y += 2)
            {
                for (var z = -1; z <= 1; z += 2)
                {
                    var localOffset = new Vector3(halfExtents.x * x, halfExtents.y * y, halfExtents.z * z);
                    points[pointIndex++] = anchor.transform.TransformPoint(localCenter + localOffset);
                }
            }
        }

        points[pointIndex] = anchor.transform.TransformPoint(localCenter);
        return points;
    }

    private Rect BuildFallbackCropRect(Vector2 viewportCenter, Vector3 dimensions, float distance)
    {
        var dimensionScore = Mathf.Clamp01(Mathf.Max(dimensions.x, dimensions.z) / 2f);
        var distanceScore = Mathf.Clamp01(1f - distance / maxCaptureDistance);
        var estimatedSize = Mathf.Lerp(minCropSizeNormalized, 0.34f, dimensionScore * 0.65f + distanceScore * 0.35f);
        return BuildCenteredCropRect(viewportCenter, estimatedSize, estimatedSize);
    }

    private Rect BuildCropRect(float minX, float minY, float maxX, float maxY)
    {
        minX -= cropPaddingNormalized;
        minY -= cropPaddingNormalized;
        maxX += cropPaddingNormalized;
        maxY += cropPaddingNormalized;

        var width = Mathf.Max(minCropSizeNormalized, maxX - minX);
        var height = Mathf.Max(minCropSizeNormalized, maxY - minY);
        var center = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
        return BuildCenteredCropRect(center, width, height);
    }

    private static Rect BuildCenteredCropRect(Vector2 center, float width, float height)
    {
        width = Mathf.Clamp(width, 0.01f, 1f);
        height = Mathf.Clamp(height, 0.01f, 1f);

        var x = Mathf.Clamp(center.x - width * 0.5f, 0f, 1f - width);
        var y = Mathf.Clamp(center.y - height * 0.5f, 0f, 1f - height);
        return new Rect(x, y, width, height);
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

        return builder.ToString().Trim('_');
    }

    private struct BestViewCandidate
    {
        public MRUKAnchor Anchor;
        public int AnchorIndex;
        public Vector3 WorldCenter;
        public Vector3 Dimensions;
        public Vector2 ViewportCenter;
        public float Distance;
        public float Score;
        public Rect CropRect;

        public bool IsValid => Anchor != null;
    }

    private readonly struct SuppressedGameObjectState
    {
        public readonly GameObject Target;
        public readonly bool WasActive;

        public SuppressedGameObjectState(GameObject target, bool wasActive)
        {
            Target = target;
            WasActive = wasActive;
        }
    }

    private struct TargetPhysicalSize
    {
        public float LengthMeters;
        public float WidthMeters;
        public float HeightMeters;
        public float AspectRatio;
    }
}

[Serializable]
public class BestViewCaptureRecord
{
    public string CaptureId;
    public string RoomId;
    public string ThemeId;
    public string SemanticLabel;
    public string FunctionTag;
    public string AnchorName;
    public int AnchorIndex;
    public string CreatedAtIsoUtc;
    public BestViewCaptureSourceMode CaptureSourceMode;
    public string SourceOriginalInputPath;
    public string ImagePath;
    public string CroppedImagePath;
    public string MetadataPath;
    public string GeneratedRequestPath;
    public Vector3 WorldPosition;
    public Vector3 Dimensions;
    public Vector3 CameraPosition;
    public Vector3 CameraForward;
    public Vector2 ViewportCenter;
    public SerializableRect NormalizedCropRect;
    public float Score;
}

public enum BestViewSemanticCategory
{
    Table,
    Screen,
    Storage,
    Seating,
    Other,
}
