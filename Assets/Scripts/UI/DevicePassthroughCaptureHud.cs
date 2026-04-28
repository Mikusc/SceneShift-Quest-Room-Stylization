using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class DevicePassthroughCaptureHud : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private DevicePassthroughCaptureService captureService;
    [SerializeField] private Transform headTransform;
    [SerializeField] private Canvas canvas;
    [SerializeField] private Image backgroundImage;
    [SerializeField] private TMP_Text statusText;

    [Header("Runtime HUD")]
    [SerializeField] private bool visibleInPlayMode = true;
    [SerializeField] private bool createRuntimeHudIfMissing = true;
    [SerializeField] private bool headLocked = true;
    [SerializeField, Min(0.03f)] private float updateIntervalSeconds = 0.12f;

    [Header("Head-Locked Placement")]
    [SerializeField] private Vector3 localOffset = new(0f, -0.22f, 1.15f);
    [SerializeField] private Vector3 localEulerOffset = new(8f, 0f, 0f);
    [SerializeField] private Vector2 panelSizePixels = new(760f, 360f);
    [SerializeField, Min(0.0001f)] private float worldScale = 0.00115f;

    [Header("Readiness")]
    [SerializeField, Range(0f, 1f)] private float goodScoreThreshold = 0.72f;
    [SerializeField, Min(0.5f)] private float goodMaxDistanceMeters = 3f;

    private readonly StringBuilder _builder = new(512);
    private float _nextUpdateTime;

    private void Reset()
    {
        ResolveReferences();
    }

    private void Awake()
    {
        ResolveReferences();
        EnsureRuntimeHud();
    }

    private void OnEnable()
    {
        ResolveReferences();
        EnsureRuntimeHud();
        UpdateHudText();
    }

    private void LateUpdate()
    {
        if (!visibleInPlayMode || !Application.isPlaying)
        {
            SetHudVisible(false);
            return;
        }

        ResolveReferences();
        EnsureRuntimeHud();
        SetHudVisible(true);

        if (headLocked)
        {
            UpdateHeadLockedPlacement();
        }

        if (Time.unscaledTime >= _nextUpdateTime)
        {
            _nextUpdateTime = Time.unscaledTime + updateIntervalSeconds;
            UpdateHudText();
        }
    }

    private void ResolveReferences()
    {
        if (captureService == null)
        {
            captureService = FindAnyObjectByType<DevicePassthroughCaptureService>();
        }

        if (headTransform == null)
        {
            var mainCamera = Camera.main;
            if (mainCamera != null)
            {
                headTransform = mainCamera.transform;
            }
            else
            {
                var fallbackCamera = FindAnyObjectByType<Camera>();
                if (fallbackCamera != null)
                {
                    headTransform = fallbackCamera.transform;
                }
            }
        }
    }

    private void EnsureRuntimeHud()
    {
        if (canvas != null || !createRuntimeHudIfMissing)
        {
            return;
        }

        var hudRoot = new GameObject("DevicePassthroughCaptureHeadsetHud", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        hudRoot.transform.SetParent(transform, false);

        canvas = hudRoot.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 32000;

        var rootRect = hudRoot.GetComponent<RectTransform>();
        rootRect.sizeDelta = panelSizePixels;
        hudRoot.transform.localScale = Vector3.one * worldScale;

        var scaler = hudRoot.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        scaler.dynamicPixelsPerUnit = 10f;

        var backgroundObject = new GameObject("Background", typeof(RectTransform), typeof(Image));
        backgroundObject.transform.SetParent(hudRoot.transform, false);
        backgroundImage = backgroundObject.GetComponent<Image>();
        backgroundImage.raycastTarget = false;
        StretchToParent(backgroundImage.rectTransform, Vector2.zero);

        var textObject = new GameObject("StatusText", typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(hudRoot.transform, false);
        statusText = textObject.GetComponent<TextMeshProUGUI>();
        statusText.raycastTarget = false;
        statusText.alignment = TextAlignmentOptions.TopLeft;
        statusText.textWrappingMode = TextWrappingModes.NoWrap;
        statusText.fontSize = 24f;
        statusText.color = Color.white;
        statusText.margin = new Vector4(18f, 14f, 18f, 14f);
        StretchToParent(statusText.rectTransform, Vector2.zero);
    }

    private void UpdateHeadLockedPlacement()
    {
        if (canvas == null || headTransform == null)
        {
            return;
        }

        var canvasTransform = canvas.transform;
        canvasTransform.position = headTransform.TransformPoint(localOffset);
        canvasTransform.rotation = headTransform.rotation * Quaternion.Euler(localEulerOffset);
        canvasTransform.localScale = Vector3.one * worldScale;
        canvas.worldCamera = headTransform.GetComponent<Camera>();
    }

    private void UpdateHudText()
    {
        if (statusText == null)
        {
            return;
        }

        _builder.Clear();
        _builder.AppendLine("PCA BEST VIEW CAPTURE");

        if (captureService == null)
        {
            _builder.AppendLine("State: missing DevicePassthroughCaptureService");
            statusText.text = _builder.ToString().TrimEnd();
            SetStatusColor(Color.red);
            return;
        }

        var canAttemptCapture = captureService.IsPcaSupported &&
                                captureService.IsCameraPermissionGranted &&
                                captureService.HasBestCandidate;
        var isPcaCaptureReady = canAttemptCapture && captureService.IsPcaPlaying;
        var hasGoodScore = captureService.HasBestCandidate &&
                           captureService.BestCandidateScore >= goodScoreThreshold &&
                           captureService.BestCandidateDistance <= goodMaxDistanceMeters;
        var isPcaBlocked = captureService.CurrentState.StartsWith("pca-provider", System.StringComparison.Ordinal);
        var stateLabel = isPcaCaptureReady
            ? hasGoodScore ? "READY - press input" : "ADJUST VIEW - usable but weak"
            : captureService.IsPcaRetryCoolingDown
                ? $"PCA PROVIDER BLOCKED - retry {captureService.PcaRetrySecondsRemaining:F0}s"
                : canAttemptCapture
                    ? hasGoodScore ? "VIEW READY - press input to start PCA" : "ADJUST VIEW - score weak"
                    : captureService.CurrentState;

        _builder.AppendLine($"State: {stateLabel}");
        _builder.AppendLine($"Target: {captureService.TargetSelectionLabel}");
        _builder.AppendLine($"PCA: supported={captureService.IsPcaSupported} permission={captureService.IsCameraPermissionGranted} playing={captureService.IsPcaPlaying}");
        _builder.AppendLine($"Scoring: {captureService.BestCandidateScoringSource}");
        if (captureService.IsPcaRetryCoolingDown || isPcaBlocked)
        {
            _builder.AppendLine($"PCA Hint: {captureService.PcaDiagnosticHint}");
        }

        if (captureService.HasBestCandidate)
        {
            var viewport = captureService.BestCandidateViewportCenter;
            var crop = captureService.BestCandidateCropRect;
            _builder.AppendLine($"Best Anchor: {captureService.BestAnchorDisplayName} [{captureService.BestCandidateCategory}]");
            _builder.AppendLine($"Object ID: {captureService.BestAnchorObjectId}");
            _builder.AppendLine($"Score: {captureService.BestCandidateScore:F2} ({captureService.BestCandidateScore * 100f:F0}%)");
            _builder.AppendLine($"Distance: {captureService.BestCandidateDistance:F2}m");
            _builder.AppendLine($"Viewport: {viewport.x:F2}, {viewport.y:F2}");
            _builder.AppendLine($"Crop: {crop.x:F2}, {crop.y:F2}, {crop.width:F2}, {crop.height:F2}");
        }
        else
        {
            _builder.AppendLine("Best Anchor: none");
            _builder.AppendLine("Score: 0.00 (0%)");
        }

        _builder.AppendLine($"Input: {captureService.CaptureInputHint}");
        if (captureService.AutoSelectTargetFromGaze)
        {
            _builder.AppendLine("Target Mode: auto gaze (no switch needed)");
        }
        else
        {
            _builder.AppendLine($"Switch Target: {captureService.TargetCycleInputHint}");
        }
        if (canAttemptCapture && !captureService.IsPcaPlaying)
        {
            _builder.AppendLine("Capture: PCA starts when input is pressed");
        }

        var lastCapture = captureService.LastCapture;
        if (lastCapture != null && !string.IsNullOrWhiteSpace(lastCapture.CaptureId))
        {
            _builder.AppendLine($"Last Capture: {lastCapture.CaptureId}");
            _builder.AppendLine($"Job: {ShortenPath(captureService.LastQueuedJobPath)}");
        }
        else
        {
            _builder.AppendLine("Last Capture: none");
        }

        statusText.text = _builder.ToString().TrimEnd();
        SetStatusColor(GetStatusColor(canAttemptCapture, hasGoodScore, isPcaBlocked || captureService.IsPcaRetryCoolingDown));
    }

    private static void StretchToParent(RectTransform rectTransform, Vector2 padding)
    {
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.offsetMin = padding;
        rectTransform.offsetMax = -padding;
    }

    private void SetHudVisible(bool isVisible)
    {
        if (canvas != null && canvas.gameObject.activeSelf != isVisible)
        {
            canvas.gameObject.SetActive(isVisible);
        }
    }

    private void SetStatusColor(Color color)
    {
        if (backgroundImage == null)
        {
            return;
        }

        backgroundImage.color = new Color(
            Mathf.Clamp01(color.r * 0.28f),
            Mathf.Clamp01(color.g * 0.28f),
            Mathf.Clamp01(color.b * 0.28f),
            0.74f);
    }

    private Color GetStatusColor(bool canAttemptCapture, bool hasGoodScore, bool isPcaBlocked)
    {
        if (captureService.CurrentState == "captured")
        {
            return new Color(0.2f, 0.8f, 1f);
        }

        if (isPcaBlocked || !captureService.IsPcaSupported || !captureService.IsCameraPermissionGranted)
        {
            return new Color(1f, 0.25f, 0.2f);
        }

        if (!canAttemptCapture)
        {
            return new Color(1f, 0.75f, 0.2f);
        }

        return hasGoodScore
            ? new Color(0.2f, 1f, 0.35f)
            : new Color(1f, 0.75f, 0.2f);
    }

    private static string ShortenPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "none";
        }

        var normalized = path.Replace('\\', '/');
        var index = normalized.LastIndexOf('/');
        return index >= 0 && index < normalized.Length - 1
            ? normalized[(index + 1)..]
            : normalized;
    }
}
