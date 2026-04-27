using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Meta.XR;
using Meta.XR.MRUtilityKit;
using UnityEngine;
using UnityEngine.Android;
using UnityEngine.XR;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[DisallowMultipleComponent]
public class DevicePassthroughCaptureService : MonoBehaviour
{
    private const string CameraPermission = "horizonos.permission.HEADSET_CAMERA";

    [Header("References")]
    [SerializeField] private PassthroughCameraAccess passthroughCameraAccess;
    [SerializeField] private RoomSemanticBootstrap roomSemanticBootstrap;
    [SerializeField] private ThemeIntentController themeIntentController;
    [SerializeField] private RuntimeStyleIntentController runtimeStyleIntentController;
    [SerializeField] private StylizationPlanner stylizationPlanner;
    [SerializeField, Tooltip("Used to keep best-angle scoring live when the Quest Link PCA provider is not playing.")]
    private Camera referenceCamera;
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

    [Header("Output")]
    [SerializeField] private string captureFolderName = "BestViewCaptures/DevicePassthrough";
    [SerializeField] private string jobFolderName = "GeneratedObjectJobs";
    [SerializeField] private bool queueGeneratedObjectJob = true;

    [Header("Input")]
    [SerializeField] private bool requestPermissionOnCapture = true;
    [SerializeField] private bool enableKeyboardCapture = true;
    [SerializeField] private KeyCode captureKey = KeyCode.P;
    [SerializeField] private bool enableXrControllerCapture = true;
    [SerializeField] private XRNode xrCaptureController = XRNode.RightHand;
    [SerializeField] private XrControllerCaptureButton xrCaptureButton = XrControllerCaptureButton.PrimaryButton;

    [Header("PCA Provider Failure Handling")]
    [SerializeField, Min(0.1f), Tooltip("Delay before treating a started PCA provider as failed when it does not enter IsPlaying.")]
    private float pcaStartGraceSeconds = 2f;
    [SerializeField, Min(1f), Tooltip("Cooldown after a PCA provider startup failure to avoid repeated Link/runtime error spam.")]
    private float pcaFailureRetryCooldownSeconds = 20f;
    [SerializeField, Tooltip("Disable PassthroughCameraAccess after startup failure so it can be retried deliberately later.")]
    private bool disablePcaComponentAfterStartFailure = true;
    [SerializeField, TextArea, Tooltip("Shown in headset/debug HUD when Link exposes PCA support but the camera provider cannot start.")]
    private string pcaProviderRequirementHint =
        "PCA provider failed. Check Meta Horizon Link v85+, Quest 3/3S Horizon OS v85+, headset focus, and privacy/camera access. Use external screenshot fallback if Link PCA remains unavailable.";

    public event Action SummaryChanged;

    public string LatestSummary => _latestSummary;
    public DevicePassthroughCaptureRecord LastCapture => _lastCapture;
    public GeneratedObjectRequest LastGeneratedRequest => _lastGeneratedRequest;
    public string CurrentState => _lastState;
    public BestViewSemanticCategory TargetCategory => targetCategory;
    public bool HasBestCandidate => _bestCandidate.IsValid;
    public string BestAnchorDisplayName => _bestCandidate.IsValid
        ? $"{_bestCandidate.AnchorIndex:D2} ({_bestCandidate.Anchor.name})"
        : "none";
    public float BestCandidateScore => _bestCandidate.IsValid ? _bestCandidate.Score : 0f;
    public float BestCandidateDistance => _bestCandidate.IsValid ? _bestCandidate.Distance : 0f;
    public Vector2 BestCandidateViewportCenter => _bestCandidate.IsValid ? _bestCandidate.ViewportCenter : Vector2.zero;
    public Rect BestCandidateCropRect => _bestCandidate.IsValid ? _bestCandidate.CropRect : Rect.zero;
    public bool IsPcaSupported => passthroughCameraAccess != null && PassthroughCameraAccess.IsSupported;
    public bool IsCameraPermissionGranted => Permission.HasUserAuthorizedPermission(CameraPermission);
    public bool IsPcaPlaying => passthroughCameraAccess != null && passthroughCameraAccess.IsPlaying;
    public bool IsPcaRetryCoolingDown => IsPcaStartCoolingDown();
    public float PcaRetrySecondsRemaining => GetPcaRetrySecondsRemaining();
    public string PcaDiagnosticHint => _pcaDiagnosticHint;
    public string BestCandidateScoringSource => _bestCandidate.IsValid
        ? _bestCandidate.UsesPcaProjection ? "PCA" : "HeadPose"
        : "none";
    public string LastQueuedJobPath => _lastQueuedJobPath;
    public string CaptureInputHint => enableXrControllerCapture
        ? $"{captureKey} / {xrCaptureController} {xrCaptureButton}"
        : captureKey.ToString();

    private DevicePassthroughCandidate _bestCandidate;
    private DevicePassthroughCaptureRecord _lastCapture = new();
    private GeneratedObjectRequest _lastGeneratedRequest = new();
    private string _lastQueuedJobPath = string.Empty;
    private bool _wasXrCapturePressed;
    private bool _pcaStartAttemptPending;
    private float _pcaStartAttemptTime = -1f;
    private float _pcaRetryAllowedAt = -1f;
    private int _pcaStartAttemptCount;
    private string _pcaDiagnosticHint = "not attempted";
    private string _lastState = "waiting";
    private string _latestSummary =
        "[DevicePassthroughCaptureService]\nState: waiting\nHint: use Quest Link/device PCA, wait for room + camera, then press P.";

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
        if (!Application.isPlaying)
        {
            return;
        }

        if (trackBestCandidateDuringPlay)
        {
            RefreshBestCandidate();
        }

        var keyboardCapturePressed = enableKeyboardCapture && WasKeyboardCapturePressed(captureKey);
        var xrCapturePressed = enableXrControllerCapture && WasXrCapturePressed();
        if (!keyboardCapturePressed && !xrCapturePressed)
        {
            return;
        }

        CapturePassthroughFrame();
    }

    [ContextMenu("Refresh Best Passthrough Candidate")]
    public void RefreshBestPassthroughCandidate()
    {
        RefreshBestCandidate();
    }

    [ContextMenu("Capture Passthrough Frame")]
    public void CapturePassthroughFrame()
    {
        ResolveReferences();

        if (!Application.isPlaying)
        {
            PublishSummary("enter-play-mode");
            return;
        }

        if (!EnsureCameraReady())
        {
            return;
        }

        RefreshBestCandidate();
        if (!_bestCandidate.IsValid)
        {
            PublishSummary("no-visible-target");
            return;
        }

        StartCoroutine(CaptureAtEndOfFrame());
    }

    private void ResolveReferences()
    {
        if (passthroughCameraAccess == null)
        {
            passthroughCameraAccess = FindAnyObjectByType<PassthroughCameraAccess>();
        }

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

        var centerEyeAnchor = GameObject.Find("CenterEyeAnchor");
        if (referenceCamera == null && centerEyeAnchor != null)
        {
            referenceCamera = centerEyeAnchor.GetComponent<Camera>();
        }

        if (referenceCamera == null)
        {
            referenceCamera = Camera.main;
        }

        if (referenceCamera == null)
        {
            referenceCamera = FindAnyObjectByType<Camera>();
        }

        if (referenceCameraTransform == null && referenceCamera != null)
        {
            referenceCameraTransform = referenceCamera.transform;
        }

        if (referenceCameraTransform == null && centerEyeAnchor != null)
        {
            referenceCameraTransform = centerEyeAnchor.transform;
        }
    }

    private bool EnsureCameraReady()
    {
        if (passthroughCameraAccess == null)
        {
            PublishSummary("missing-pca-component");
            return false;
        }

        if (!PassthroughCameraAccess.IsSupported)
        {
            PublishSummary("pca-unsupported-headset-or-runtime");
            return false;
        }

        if (!Permission.HasUserAuthorizedPermission(CameraPermission))
        {
            if (requestPermissionOnCapture)
            {
                Permission.RequestUserPermission(CameraPermission);
            }

            PublishSummary("waiting-for-camera-permission");
            return false;
        }

        if (passthroughCameraAccess.IsPlaying)
        {
            ClearPcaStartFailureState();
            return true;
        }

        if (UpdatePendingPcaStartFailure())
        {
            return false;
        }

        if (IsPcaStartCoolingDown())
        {
            PublishSummary("pca-provider-cooldown");
            return false;
        }

        if (!passthroughCameraAccess.enabled)
        {
            BeginPcaStartAttempt();
            passthroughCameraAccess.enabled = true;
            PublishSummary("starting-pca-provider");
            return false;
        }

        if (!_pcaStartAttemptPending)
        {
            BeginPcaStartAttempt();
        }

        PublishSummary("waiting-for-pca-frame");
        return false;
    }

    private void RefreshBestCandidate()
    {
        ResolveReferences();

        if (roomSemanticBootstrap == null || !roomSemanticBootstrap.HasReadyRoom || roomSemanticBootstrap.CurrentRoom == null)
        {
            _bestCandidate = default;
            PublishSummary("waiting-for-room");
            return;
        }

        if (passthroughCameraAccess == null && referenceCamera == null && referenceCameraTransform == null)
        {
            _bestCandidate = default;
            PublishSummary("missing-camera-reference");
            return;
        }

        var usePcaProjection = passthroughCameraAccess != null && passthroughCameraAccess.IsPlaying;
        var canUseReferenceCamera = referenceCamera != null && referenceCameraTransform != null;
        if (!usePcaProjection && !canUseReferenceCamera)
        {
            UpdatePendingPcaStartFailure();
            _bestCandidate = default;
            PublishSummary("missing-camera-reference");
            return;
        }

        if (!usePcaProjection)
        {
            UpdatePendingPcaStartFailure();
        }

        var cameraPose = usePcaProjection
            ? passthroughCameraAccess.GetCameraPose()
            : new Pose(referenceCameraTransform.position, referenceCameraTransform.rotation);
        var room = roomSemanticBootstrap.CurrentRoom;
        var bestCandidate = default(DevicePassthroughCandidate);
        var hasCandidate = false;

        for (var index = 0; index < room.Anchors.Count; index++)
        {
            var anchor = room.Anchors[index];
            if (!MatchesTargetCategory(anchor))
            {
                continue;
            }

            if (!TryBuildCandidate(anchor, index, cameraPose, usePcaProjection, out var candidate))
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
        if (hasCandidate)
        {
            PublishSummary(usePcaProjection ? "tracking-pca" : GetHeadPoseTrackingState());
        }
        else
        {
            PublishSummary("no-visible-target");
        }
    }

    private bool TryBuildCandidate(
        MRUKAnchor anchor,
        int anchorIndex,
        Pose cameraPose,
        bool usePcaProjection,
        out DevicePassthroughCandidate candidate)
    {
        candidate = default;
        if (anchor == null)
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
        var cameraSpace = Quaternion.Inverse(cameraPose.rotation) * (worldCenter - cameraPose.position);
        if (cameraSpace.z <= 0f || cameraSpace.magnitude > maxCaptureDistance)
        {
            return false;
        }

        if (!TryWorldToViewportPoint(worldCenter, cameraPose, usePcaProjection, out var viewportPoint))
        {
            return false;
        }

        if (viewportPoint.x < viewportMargin || viewportPoint.x > 1f - viewportMargin ||
            viewportPoint.y < viewportMargin || viewportPoint.y > 1f - viewportMargin)
        {
            return false;
        }

        var centerDistance = Vector2.Distance(viewportPoint, new Vector2(0.5f, 0.5f));
        var centerScore = Mathf.Clamp01(1f - centerDistance / 0.65f);
        var distanceScore = Mathf.Clamp01(1f - cameraSpace.magnitude / maxCaptureDistance);
        var apparentSizeScore = Mathf.Clamp01(Mathf.Max(dimensions.x, dimensions.z) / 2f);

        candidate = new DevicePassthroughCandidate
        {
            Anchor = anchor,
            AnchorIndex = anchorIndex,
            WorldCenter = worldCenter,
            Dimensions = dimensions,
            ViewportCenter = viewportPoint,
            Distance = cameraSpace.magnitude,
            CameraPose = cameraPose,
            UsesPcaProjection = usePcaProjection,
            Score = centerScore * 0.55f + distanceScore * 0.25f + apparentSizeScore * 0.2f,
            CropRect = TryCalculateNormalizedCropRect(anchor, localCenter, dimensions, cameraPose, usePcaProjection, out var cropRect)
                ? cropRect
                : BuildFallbackCropRect(viewportPoint, dimensions, cameraSpace.magnitude),
        };

        return true;
    }

    private IEnumerator CaptureAtEndOfFrame()
    {
        yield return new WaitForEndOfFrame();

        if (!EnsureCameraReady())
        {
            yield break;
        }

        var sourceTexture = passthroughCameraAccess.GetTexture();
        if (sourceTexture == null)
        {
            PublishSummary("missing-pca-texture");
            yield break;
        }

        var capturedTexture = CopyTextureToReadableTexture(sourceTexture);
        if (capturedTexture == null)
        {
            PublishSummary("texture-copy-failed");
            yield break;
        }

        var capturedAtUtc = DateTime.UtcNow;
        var captureId = $"{targetCategory.ToString().ToLowerInvariant()}_{_bestCandidate.AnchorIndex:D2}_{capturedAtUtc:yyyyMMddHHmmss}";
        var outputDirectory = GetCaptureDirectory();
        Directory.CreateDirectory(outputDirectory);

        var imagePath = Path.Combine(outputDirectory, $"{captureId}.pca.png");
        var croppedImagePath = Path.Combine(outputDirectory, $"{captureId}.pca.crop.png");
        var metadataPath = Path.Combine(outputDirectory, $"{captureId}.pca.json");
        var requestPath = Path.Combine(outputDirectory, $"{captureId}.request.json");

        File.WriteAllBytes(imagePath, capturedTexture.EncodeToPNG());
        var croppedTexture = CropTexture(capturedTexture, _bestCandidate.CropRect);
        if (croppedTexture != null)
        {
            File.WriteAllBytes(croppedImagePath, croppedTexture.EncodeToPNG());
        }
        else
        {
            croppedImagePath = string.Empty;
        }

        _lastCapture = BuildCaptureRecord(
            captureId,
            imagePath,
            croppedImagePath,
            metadataPath,
            requestPath,
            capturedAtUtc,
            capturedTexture);
        _lastGeneratedRequest = BuildGeneratedObjectRequest(
            captureId,
            imagePath,
            croppedImagePath,
            metadataPath,
            requestPath,
            capturedAtUtc);

        File.WriteAllText(metadataPath, JsonUtility.ToJson(_lastCapture, true));
        File.WriteAllText(requestPath, JsonUtility.ToJson(_lastGeneratedRequest, true));

        if (queueGeneratedObjectJob)
        {
            _lastQueuedJobPath = QueueGeneratedObjectJob(_lastGeneratedRequest);
        }

        SafeDestroy(croppedTexture);
        SafeDestroy(capturedTexture);

        PublishSummary("captured");
        Debug.Log($"[DevicePassthroughCaptureService] Captured PCA request -> {requestPath}", this);
    }

    private DevicePassthroughCaptureRecord BuildCaptureRecord(
        string captureId,
        string imagePath,
        string croppedImagePath,
        string metadataPath,
        string requestPath,
        DateTime capturedAtUtc,
        Texture2D texture)
    {
        var intrinsics = passthroughCameraAccess.Intrinsics;
        var cameraPose = passthroughCameraAccess.GetCameraPose();
        return new DevicePassthroughCaptureRecord
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
            ImagePath = imagePath,
            CroppedImagePath = croppedImagePath,
            MetadataPath = metadataPath,
            GeneratedRequestPath = requestPath,
            ImageWidth = texture != null ? texture.width : 0,
            ImageHeight = texture != null ? texture.height : 0,
            CameraPosition = passthroughCameraAccess.CameraPosition.ToString(),
            CameraTimestampIsoUtc = passthroughCameraAccess.Timestamp == default
                ? string.Empty
                : passthroughCameraAccess.Timestamp.ToUniversalTime().ToString("O"),
            CameraPose = SerializablePose.From(cameraPose.position, cameraPose.rotation),
            IntrinsicsFocalLength = intrinsics.FocalLength,
            IntrinsicsPrincipalPoint = intrinsics.PrincipalPoint,
            IntrinsicsSensorResolution = intrinsics.SensorResolution,
            LensOffset = SerializablePose.From(intrinsics.LensOffset.position, intrinsics.LensOffset.rotation),
            WorldPosition = _bestCandidate.WorldCenter,
            Dimensions = _bestCandidate.Dimensions,
            ViewportCenter = _bestCandidate.ViewportCenter,
            NormalizedCropRect = SerializableRect.From(_bestCandidate.CropRect),
            Score = _bestCandidate.Score,
            PermissionGranted = Permission.HasUserAuthorizedPermission(CameraPermission),
            PcaSupported = PassthroughCameraAccess.IsSupported,
            PcaIsPlaying = passthroughCameraAccess.IsPlaying,
        };
    }

    private GeneratedObjectRequest BuildGeneratedObjectRequest(
        string captureId,
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
        var targetSize = CalculateTargetPhysicalSize(_bestCandidate.Dimensions);
        var cameraPose = passthroughCameraAccess != null && passthroughCameraAccess.IsPlaying
            ? passthroughCameraAccess.GetCameraPose()
            : _bestCandidate.CameraPose;

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
            WorldPose = SerializablePose.From(
                _bestCandidate.WorldCenter,
                _bestCandidate.Anchor != null ? _bestCandidate.Anchor.transform.rotation : Quaternion.identity),
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
            CaptureSourceMode = BestViewCaptureSourceMode.DevicePassthroughReserved,
            SourceOriginalInputPath = imagePath,
            SourceImagePath = !string.IsNullOrWhiteSpace(croppedImagePath) ? croppedImagePath : imagePath,
            SourceFullFrameImagePath = imagePath,
            SourceCroppedImagePath = croppedImagePath,
            SourceMetadataPath = metadataPath,
            SourceRequestPath = requestPath,
            NormalizedCropRect = SerializableRect.From(_bestCandidate.CropRect),
            BestViewCameraPose = SerializablePose.From(cameraPose.position, cameraPose.rotation),
            BestViewYawDegrees = GetHorizontalYawDegrees(cameraPose.rotation * Vector3.forward),
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

    private string QueueGeneratedObjectJob(GeneratedObjectRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.RequestId))
        {
            return string.Empty;
        }

        var jobDirectory = GetJobDirectory();
        Directory.CreateDirectory(jobDirectory);
        var jobPath = Path.Combine(jobDirectory, $"{request.RequestId}.job.json");
        var promptPath = Path.Combine(jobDirectory, $"{request.RequestId}.prompt.txt");
        File.WriteAllText(promptPath, request.ImageStylizationPrompt ?? string.Empty);

        var record = new GeneratedAssetRecord
        {
            RequestId = request.RequestId,
            ObjectId = request.ObjectId,
            ThemeId = request.ThemeId,
            CaptureSourceMode = request.CaptureSourceMode,
            State = GeneratedObjectJobState.CaptureReady,
            SourceInputImagePath = request.SourceImagePath,
            SourceRequestPath = request.SourceRequestPath,
            CoordinatorJobPath = jobPath,
            StatusNote = "Queued from native passthrough camera capture and wrote a Roomify-style prompt artifact.",
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
        Debug.Log($"[DevicePassthroughCaptureService] Queued generated-object job -> {jobPath}", this);
        return jobPath;
    }

    private string GetCaptureDirectory()
    {
#if UNITY_EDITOR
        var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
        return Path.Combine(projectRoot, "Library", NormalizeFolderName(captureFolderName));
#else
        return Path.Combine(Application.persistentDataPath, NormalizeFolderName(captureFolderName));
#endif
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

    private bool TryCalculateNormalizedCropRect(
        MRUKAnchor anchor,
        Vector3 localCenter,
        Vector3 dimensions,
        Pose cameraPose,
        bool usePcaProjection,
        out Rect cropRect)
    {
        cropRect = default;
        if (anchor == null)
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
            var cameraSpace = Quaternion.Inverse(cameraPose.rotation) * (worldPoints[index] - cameraPose.position);
            if (cameraSpace.z <= 0f)
            {
                continue;
            }

            if (!TryWorldToViewportPoint(worldPoints[index], cameraPose, usePcaProjection, out var viewportPoint))
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

    private bool TryWorldToViewportPoint(
        Vector3 worldPoint,
        Pose cameraPose,
        bool usePcaProjection,
        out Vector2 viewportPoint)
    {
        viewportPoint = default;

        if (usePcaProjection)
        {
            if (passthroughCameraAccess == null || !passthroughCameraAccess.IsPlaying)
            {
                return false;
            }

            viewportPoint = passthroughCameraAccess.WorldToViewportPoint(worldPoint, cameraPose);
            return true;
        }

        if (referenceCamera == null)
        {
            return false;
        }

        var projectedPoint = referenceCamera.WorldToViewportPoint(worldPoint);
        if (projectedPoint.z <= 0f)
        {
            return false;
        }

        viewportPoint = new Vector2(projectedPoint.x, projectedPoint.y);
        return true;
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

    private static Texture2D CopyTextureToReadableTexture(Texture source)
    {
        if (source == null || source.width <= 0 || source.height <= 0)
        {
            return null;
        }

        var previousActive = RenderTexture.active;
        var renderTexture = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);
        try
        {
            Graphics.Blit(source, renderTexture);
            RenderTexture.active = renderTexture;
            var readableTexture = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            readableTexture.ReadPixels(new Rect(0f, 0f, source.width, source.height), 0, 0);
            readableTexture.Apply(false, false);
            return readableTexture;
        }
        finally
        {
            RenderTexture.active = previousActive;
            RenderTexture.ReleaseTemporary(renderTexture);
        }
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
        var croppedTexture = new Texture2D(pixelRect.width, pixelRect.height, TextureFormat.RGBA32, false);
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

    private void PublishSummary(string state)
    {
        _lastState = state;
        var builder = new StringBuilder(640);
        builder.AppendLine("[DevicePassthroughCaptureService]");
        builder.AppendLine($"State: {state}");
        builder.AppendLine($"Target: {targetCategory}");
        builder.AppendLine($"Input: {CaptureInputHint} (Play mode)");
        builder.AppendLine($"PCA Component: {(passthroughCameraAccess != null ? passthroughCameraAccess.name : "missing")}");
        builder.AppendLine($"PCA Supported: {(passthroughCameraAccess != null ? PassthroughCameraAccess.IsSupported : false)}");
        builder.AppendLine($"Permission: {Permission.HasUserAuthorizedPermission(CameraPermission)}");
        builder.AppendLine($"PCA Playing: {(passthroughCameraAccess != null && passthroughCameraAccess.IsPlaying)}");
        builder.AppendLine($"Scoring Source: {BestCandidateScoringSource}");
        builder.AppendLine($"PCA Retry Cooldown: {GetPcaRetrySecondsRemaining():F1}s");
        builder.AppendLine($"PCA Attempts: {_pcaStartAttemptCount}");
        builder.AppendLine($"PCA Diagnostic: {_pcaDiagnosticHint}");

        if (_bestCandidate.IsValid)
        {
            builder.AppendLine($"Best Anchor: {_bestCandidate.AnchorIndex:D2} ({_bestCandidate.Anchor.name})");
            builder.AppendLine($"Score: {_bestCandidate.Score:F2}");
            builder.AppendLine($"Distance: {_bestCandidate.Distance:F2}m");
            builder.AppendLine($"Viewport: {_bestCandidate.ViewportCenter.x:F2}, {_bestCandidate.ViewportCenter.y:F2}");
            builder.AppendLine($"Crop: {_bestCandidate.CropRect.x:F2}, {_bestCandidate.CropRect.y:F2}, {_bestCandidate.CropRect.width:F2}, {_bestCandidate.CropRect.height:F2}");
        }
        else
        {
            builder.AppendLine("Best Anchor: none");
        }

        if (!string.IsNullOrWhiteSpace(_lastCapture.CaptureId))
        {
            builder.AppendLine($"Last Capture: {_lastCapture.CaptureId}");
            builder.AppendLine($"Image: {_lastCapture.ImagePath}");
            builder.AppendLine($"Crop: {_lastCapture.CroppedImagePath}");
            builder.AppendLine($"Request: {_lastCapture.GeneratedRequestPath}");
            builder.AppendLine($"Job: {_lastQueuedJobPath}");
        }
        else
        {
            builder.AppendLine("Last Capture: none");
        }

        _latestSummary = builder.ToString().TrimEnd();
        SummaryChanged?.Invoke();
    }

    private void BeginPcaStartAttempt()
    {
        _pcaStartAttemptPending = true;
        _pcaStartAttemptTime = Time.realtimeSinceStartup;
        _pcaStartAttemptCount++;
        _pcaDiagnosticHint = "starting PCA provider";
    }

    private bool UpdatePendingPcaStartFailure()
    {
        if (!_pcaStartAttemptPending)
        {
            return false;
        }

        if (passthroughCameraAccess != null && passthroughCameraAccess.IsPlaying)
        {
            ClearPcaStartFailureState();
            return false;
        }

        if (Time.realtimeSinceStartup - _pcaStartAttemptTime < pcaStartGraceSeconds)
        {
            return false;
        }

        _pcaStartAttemptPending = false;
        _pcaRetryAllowedAt = Time.realtimeSinceStartup + pcaFailureRetryCooldownSeconds;
        _pcaDiagnosticHint = pcaProviderRequirementHint;

        if (disablePcaComponentAfterStartFailure &&
            passthroughCameraAccess != null &&
            passthroughCameraAccess.enabled &&
            !passthroughCameraAccess.IsPlaying)
        {
            passthroughCameraAccess.enabled = false;
        }

        PublishSummary("pca-provider-start-failed");
        return true;
    }

    private string GetHeadPoseTrackingState()
    {
        if (IsPcaStartCoolingDown())
        {
            return "tracking-head-pose-pca-cooldown";
        }

        return passthroughCameraAccess != null && passthroughCameraAccess.enabled
            ? "tracking-head-pose-waiting-pca"
            : "tracking-head-pose";
    }

    private void ClearPcaStartFailureState()
    {
        _pcaStartAttemptPending = false;
        _pcaRetryAllowedAt = -1f;
        _pcaDiagnosticHint = "PCA provider playing";
    }

    private bool IsPcaStartCoolingDown()
    {
        return _pcaRetryAllowedAt > Time.realtimeSinceStartup;
    }

    private float GetPcaRetrySecondsRemaining()
    {
        return Mathf.Max(0f, _pcaRetryAllowedAt - Time.realtimeSinceStartup);
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

    private static string NormalizeFolderName(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "BestViewCaptures/DevicePassthrough"
            : value.Replace('\\', '/');
    }

    private static bool WasKeyboardCapturePressed(KeyCode keyCode)
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

    private bool WasXrCapturePressed()
    {
        var device = InputDevices.GetDeviceAtXRNode(xrCaptureController);
        if (!device.isValid || !TryGetXrButtonPressed(device, xrCaptureButton, out var isPressed))
        {
            _wasXrCapturePressed = false;
            return false;
        }

        var wasPressedThisFrame = isPressed && !_wasXrCapturePressed;
        _wasXrCapturePressed = isPressed;
        return wasPressedThisFrame;
    }

    private static bool TryGetXrButtonPressed(
        UnityEngine.XR.InputDevice device,
        XrControllerCaptureButton button,
        out bool isPressed)
    {
        return button switch
        {
            XrControllerCaptureButton.SecondaryButton => device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.secondaryButton, out isPressed),
            XrControllerCaptureButton.GripButton => device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.gripButton, out isPressed),
            XrControllerCaptureButton.TriggerButton => device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out isPressed),
            _ => device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryButton, out isPressed),
        };
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

    private struct DevicePassthroughCandidate
    {
        public MRUKAnchor Anchor;
        public int AnchorIndex;
        public Vector3 WorldCenter;
        public Vector3 Dimensions;
        public Vector2 ViewportCenter;
        public float Distance;
        public float Score;
        public Rect CropRect;
        public Pose CameraPose;
        public bool UsesPcaProjection;

        public bool IsValid => Anchor != null;
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
public class DevicePassthroughCaptureRecord
{
    public string CaptureId;
    public string RoomId;
    public string ThemeId;
    public string SemanticLabel;
    public string FunctionTag;
    public string AnchorName;
    public int AnchorIndex;
    public string CreatedAtIsoUtc;
    public string ImagePath;
    public string CroppedImagePath;
    public string MetadataPath;
    public string GeneratedRequestPath;
    public int ImageWidth;
    public int ImageHeight;
    public string CameraPosition;
    public string CameraTimestampIsoUtc;
    public SerializablePose CameraPose;
    public Vector2 IntrinsicsFocalLength;
    public Vector2 IntrinsicsPrincipalPoint;
    public Vector2Int IntrinsicsSensorResolution;
    public SerializablePose LensOffset;
    public Vector3 WorldPosition;
    public Vector3 Dimensions;
    public Vector2 ViewportCenter;
    public SerializableRect NormalizedCropRect;
    public float Score;
    public bool PermissionGranted;
    public bool PcaSupported;
    public bool PcaIsPlaying;
}

public enum XrControllerCaptureButton
{
    PrimaryButton,
    SecondaryButton,
    GripButton,
    TriggerButton,
}
