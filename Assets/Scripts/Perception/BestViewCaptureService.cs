using System;
using System.Collections;
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
    [SerializeField] private Camera referenceCamera;
    [SerializeField] private Transform referenceCameraTransform;

    [Header("Capture Target")]
    [SerializeField] private BestViewSemanticCategory targetCategory = BestViewSemanticCategory.Table;
    [SerializeField, Min(0.5f)] private float maxCaptureDistance = 5f;
    [SerializeField, Range(0f, 0.45f)] private float viewportMargin = 0.08f;
    [SerializeField] private bool trackBestCandidateDuringPlay = true;

    [Header("Capture Output")]
    [SerializeField] private string captureFolderName = "BestViewCaptures";
    [SerializeField] private bool enableKeyboardCapture = true;
    [SerializeField] private KeyCode captureKey = KeyCode.C;

    public event Action SummaryChanged;

    public string LatestSummary => _latestSummary;
    public bool HasBestCandidate => _bestCandidate.IsValid;
    public BestViewCaptureRecord LastCapture => _lastCapture;

    private BestViewCandidate _bestCandidate;
    private BestViewCaptureRecord _lastCapture = new();
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
        };

        return true;
    }

    private IEnumerator CaptureCurrentBestViewAtEndOfFrame()
    {
        yield return new WaitForEndOfFrame();

        var captureId = $"{targetCategory.ToString().ToLowerInvariant()}_{_bestCandidate.AnchorIndex:D2}_{DateTime.UtcNow:yyyyMMddHHmmss}";
        var outputDirectory = GetCaptureDirectory();
        Directory.CreateDirectory(outputDirectory);

        var imagePath = Path.Combine(outputDirectory, $"{captureId}.png");
        var metadataPath = Path.Combine(outputDirectory, $"{captureId}.json");

        ScreenCapture.CaptureScreenshot(imagePath);

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
            CreatedAtIsoUtc = DateTime.UtcNow.ToString("O"),
            ImagePath = imagePath,
            MetadataPath = metadataPath,
            WorldPosition = _bestCandidate.WorldCenter,
            Dimensions = _bestCandidate.Dimensions,
            CameraPosition = referenceCameraTransform != null ? referenceCameraTransform.position : Vector3.zero,
            CameraForward = referenceCameraTransform != null ? referenceCameraTransform.forward : Vector3.forward,
            ViewportCenter = _bestCandidate.ViewportCenter,
            Score = _bestCandidate.Score,
        };

        File.WriteAllText(metadataPath, JsonUtility.ToJson(_lastCapture, true));
        PublishSummary("captured");
        Debug.Log($"[BestViewCaptureService] Captured best-view request -> {imagePath}", this);
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

        if (_bestCandidate.IsValid)
        {
            builder.AppendLine($"Best Anchor: {_bestCandidate.AnchorIndex:D2} ({_bestCandidate.Anchor.name})");
            builder.AppendLine($"Score: {_bestCandidate.Score:F2}");
            builder.AppendLine($"Distance: {_bestCandidate.Distance:F2}m");
            builder.AppendLine($"Viewport: {_bestCandidate.ViewportCenter.x:F2}, {_bestCandidate.ViewportCenter.y:F2}");
            builder.AppendLine($"Dimensions: {_bestCandidate.Dimensions.x:F2}, {_bestCandidate.Dimensions.y:F2}, {_bestCandidate.Dimensions.z:F2}");
        }
        else
        {
            builder.AppendLine("Best Anchor: none");
        }

        if (!string.IsNullOrWhiteSpace(_lastCapture.CaptureId))
        {
            builder.AppendLine($"Last Capture: {_lastCapture.CaptureId}");
            builder.AppendLine($"Image Path: {_lastCapture.ImagePath}");
        }
        else
        {
            builder.AppendLine("Last Capture: none");
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

        var keyControl = keyCode switch
        {
            KeyCode.C => keyboard.cKey,
            _ => null,
        };

        return keyControl != null && keyControl.wasPressedThisFrame;
#else
        return Input.GetKeyDown(keyCode);
#endif
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

        public bool IsValid => Anchor != null;
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
    public string ImagePath;
    public string MetadataPath;
    public Vector3 WorldPosition;
    public Vector3 Dimensions;
    public Vector3 CameraPosition;
    public Vector3 CameraForward;
    public Vector2 ViewportCenter;
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
