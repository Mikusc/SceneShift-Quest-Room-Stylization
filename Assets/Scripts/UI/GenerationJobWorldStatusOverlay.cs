using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
public class GenerationJobWorldStatusOverlay : MonoBehaviour
{
    private const string ProjectUISetStatusCardShellPrefabPath = "Assets/Prefabs/UI/SceneShift_StatusCardBackplate.prefab";
    private const string UISetCanvasRootName = "CanvasRoot";
    private const string UISetBackplateName = "UIBackplate";
    private const string UISetGradientEffectName = "GradientEffect";

    [Header("References")]
    [SerializeField] private Camera targetCamera;
    [SerializeField] private Transform overlayRoot;
    [SerializeField] private GameObject statusCardShellPrefab;

    [Header("Meta UISet Shell")]
    [SerializeField] private bool useStatusCardShellPrefab = true;
    [SerializeField] private bool disableStatusCardShellInteraction = true;
    [SerializeField] private bool applySceneShiftCardColors = true;

    [Header("Jobs")]
    [SerializeField] private string generatedObjectJobFolderName = "GeneratedObjectJobs";
    [SerializeField] private bool overlayVisible = true;
    [SerializeField] private bool showImportedJobs = true;
    [SerializeField] private bool showFailedJobs = true;
    [SerializeField] private bool showReviewJobs = true;

    [Header("Refresh")]
    [SerializeField, Min(0.2f)] private float refreshIntervalSeconds = 0.5f;
    [SerializeField, Min(0.1f)] private float verticalOffsetMeters = 0.35f;
    [SerializeField, Min(0.05f)] private float cardWidthMeters = 0.62f;
    [SerializeField, Min(0.03f)] private float cardHeightMeters = 0.24f;
    [SerializeField, Min(240f)] private float cardPixelWidth = 420f;
    [SerializeField, Min(6)] private int maxStatusNoteCharacters = 42;

    [Header("Colors")]
    [SerializeField] private Color cardColor = new(0.025f, 0.035f, 0.045f, 0.82f);
    [SerializeField] private Color cardGlowColor = new(0.25f, 0.45f, 0.6f, 0.24f);
    [SerializeField] private Color titleColor = new(0.95f, 0.98f, 1f, 1f);
    [SerializeField] private Color noteColor = new(0.7f, 0.8f, 0.9f, 1f);
    [SerializeField] private Color waitingColor = new(0.75f, 0.85f, 1f, 1f);
    [SerializeField] private Color runningColor = new(0.2f, 0.85f, 1f, 1f);
    [SerializeField] private Color readyColor = new(0.35f, 1f, 0.5f, 1f);
    [SerializeField] private Color reviewColor = new(1f, 0.75f, 0.2f, 1f);
    [SerializeField] private Color failedColor = new(1f, 0.25f, 0.25f, 1f);

    public string LatestSummary => _latestSummary;
    public bool IsOverlayVisible => overlayVisible;

    private readonly Dictionary<string, StatusCard> _cardsByRequestId = new();
    private readonly HashSet<string> _visibleRequestIds = new();
    private readonly StringBuilder _builder = new(256);
    private Sprite _roundedCardSprite;
    private Sprite _roundedPillSprite;
    private float _nextRefreshTime;
    private string _latestSummary = "[GenerationJobWorldStatusOverlay] State: waiting";

    private void Reset()
    {
        ResolveCamera();
        TryAutoLoadStatusCardShellPrefab();
    }

    private void Awake()
    {
        TryAutoLoadStatusCardShellPrefab();
        ResolveCamera();
        EnsureOverlayRoot();
    }

    private void OnEnable()
    {
        TryAutoLoadStatusCardShellPrefab();
        ResolveCamera();
        EnsureOverlayRoot();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        TryAutoLoadStatusCardShellPrefab();
    }
#endif

    private void OnDisable()
    {
        ClearLabels();
    }

    private void LateUpdate()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        if (!overlayVisible)
        {
            SetCardsActive(false);
            return;
        }

        ResolveCamera();
        EnsureOverlayRoot();

        if (Time.unscaledTime >= _nextRefreshTime)
        {
            _nextRefreshTime = Time.unscaledTime + refreshIntervalSeconds;
            RefreshLabels();
        }

        FaceCamera();
    }

    [ContextMenu("Refresh World Status Labels")]
    public void RefreshLabels()
    {
        if (!overlayVisible)
        {
            SetCardsActive(false);
            _latestSummary = "[GenerationJobWorldStatusOverlay] hidden";
            return;
        }

        _visibleRequestIds.Clear();

        var shown = 0;
        var skipped = 0;
        var directory = GetLibraryDirectory(generatedObjectJobFolderName);
        if (!Directory.Exists(directory))
        {
            ClearLabels();
            _latestSummary = $"[GenerationJobWorldStatusOverlay] Job folder missing: {directory}";
            return;
        }

        foreach (var jobPath in Directory.GetFiles(directory, "*.job.json", SearchOption.TopDirectoryOnly))
        {
            if (!TryReadJson(jobPath, out GeneratedAssetRecord record) ||
                string.IsNullOrWhiteSpace(record.RequestId) ||
                !ShouldShow(record))
            {
                skipped++;
                continue;
            }

            if (!TryReadRequest(record.SourceRequestPath, out var request))
            {
                skipped++;
                continue;
            }

            var label = GetOrCreateLabel(record.RequestId);
            UpdateLabel(label, record, request);
            _visibleRequestIds.Add(record.RequestId);
            shown++;
        }

        RemoveHiddenLabels();
        _latestSummary = $"[GenerationJobWorldStatusOverlay] shown={shown}, skipped={skipped}, folder={directory}";
    }

    [ContextMenu("Toggle Overlay Visible")]
    public void ToggleOverlayVisible()
    {
        SetOverlayVisible(!overlayVisible);
    }

    public void SetOverlayVisible(bool visible)
    {
        overlayVisible = visible;
        SetCardsActive(visible);
        _latestSummary = visible
            ? "[GenerationJobWorldStatusOverlay] visible"
            : "[GenerationJobWorldStatusOverlay] hidden";
    }

    private void UpdateLabel(StatusCard card, GeneratedAssetRecord record, GeneratedObjectRequest request)
    {
        ApplyCardTextLayout(card);
        var stateColor = GetStateColor(record.State);
        card.Root.transform.position = GetLabelPosition(request);
        card.Title.color = titleColor;
        card.Note.color = noteColor;
        card.Title.text = BuildTitleText(record, request);
        card.State.text = GetStateLabel(record.State);
        card.State.color = stateColor;
        card.Note.text = BuildNoteText(record);
        card.Id.text = ShortId(record.RequestId);
        SetGraphicColor(card.AccentImage, stateColor);
        SetGraphicColor(card.StatusPillImage, WithAlpha(stateColor, 0.18f));
        SetGraphicColor(card.ProgressFillImage, stateColor);
        if (card.ProgressFillImage != null)
        {
            card.ProgressFillImage.fillAmount = GetStateProgress(record.State);
        }

        card.Root.SetActive(true);
    }

    private Vector3 GetLabelPosition(GeneratedObjectRequest request)
    {
        var center = request.WorldBounds.Center;
        var size = request.WorldBounds.Size;
        if (size.sqrMagnitude > 0.0001f)
        {
            var heightOffset = Mathf.Max(0.15f, size.y * 0.5f) + verticalOffsetMeters;
            return center + Vector3.up * heightOffset;
        }

        return request.WorldPose.Position + Vector3.up * verticalOffsetMeters;
    }

    private string BuildTitleText(GeneratedAssetRecord record, GeneratedObjectRequest request)
    {
        _builder.Clear();
        _builder.Append(string.IsNullOrWhiteSpace(request.SemanticLabel) ? "OBJECT" : request.SemanticLabel.ToUpperInvariant());
        _builder.Append("  ");
        _builder.Append(GetStateLabel(record.State));
        return _builder.ToString();
    }

    private string BuildNoteText(GeneratedAssetRecord record)
    {
        _builder.Clear();
        if (!string.IsNullOrWhiteSpace(record.StatusNote))
        {
            _builder.Append(Shorten(record.StatusNote, maxStatusNoteCharacters));
        }
        else if (!string.IsNullOrWhiteSpace(record.FailureReason))
        {
            _builder.Append(Shorten(record.FailureReason, maxStatusNoteCharacters));
        }
        else
        {
            _builder.Append(GetStateHint(record.State));
        }
        return _builder.ToString();
    }

    private StatusCard GetOrCreateLabel(string requestId)
    {
        if (_cardsByRequestId.TryGetValue(requestId, out var existing) && existing.IsValid)
        {
            return existing;
        }

        EnsureOverlayRoot();
        EnsureStyleResources();

        var labelObject = new GameObject($"GenerationStatus_{requestId}");
        labelObject.transform.SetParent(overlayRoot != null ? overlayRoot : transform, false);
        var shell = CreateStatusCardShell(labelObject.transform);
        if (!shell.IsValid)
        {
            DestroyLabelObject(labelObject);
            return default;
        }

        var rect = shell.RectTransform;
        var canvas = shell.Canvas;
        var glow = shell.GlowImage;
        var background = shell.BackgroundImage;

        var accent = CreateImage("StatusAccent", rect, _roundedPillSprite, runningColor);
        SetLeftAccentLayout(accent.rectTransform);

        var contentRoot = new GameObject("ContentRoot", typeof(RectTransform), typeof(VerticalLayoutGroup));
        contentRoot.transform.SetParent(rect, false);
        var contentRect = contentRoot.GetComponent<RectTransform>();
        StretchToParent(contentRect);
        contentRect.offsetMin = new Vector2(32f, 18f);
        contentRect.offsetMax = new Vector2(-18f, -16f);
        var contentLayout = contentRoot.GetComponent<VerticalLayoutGroup>();
        contentLayout.padding = new RectOffset(0, 0, 0, 0);
        contentLayout.spacing = 8f;
        contentLayout.childControlWidth = true;
        contentLayout.childControlHeight = true;
        contentLayout.childForceExpandWidth = true;
        contentLayout.childForceExpandHeight = false;
        contentRoot.transform.SetAsLastSibling();

        var headerRow = CreateLayoutRow("HeaderRow", contentRoot.transform, 8f, 34f);
        var title = CreateText("Title", headerRow.transform, 23f, titleColor, TextAlignmentOptions.MidlineLeft);
        title.fontStyle = FontStyles.Bold;
        title.textWrappingMode = TextWrappingModes.NoWrap;
        title.overflowMode = TextOverflowModes.Ellipsis;
        var titleLayout = title.gameObject.AddComponent<LayoutElement>();
        titleLayout.flexibleWidth = 1f;
        titleLayout.preferredHeight = 34f;

        var statusPill = CreateImage("StatusPill", headerRow.transform, _roundedPillSprite, WithAlpha(runningColor, 0.18f));
        var statusPillLayout = statusPill.gameObject.AddComponent<LayoutElement>();
        statusPillLayout.preferredWidth = 132f;
        statusPillLayout.preferredHeight = 30f;
        var status = CreateText("State", statusPill.transform, 15f, runningColor, TextAlignmentOptions.Center);
        StretchToParent(status.rectTransform);
        status.margin = new Vector4(10f, 0f, 10f, 1f);
        status.textWrappingMode = TextWrappingModes.NoWrap;
        status.overflowMode = TextOverflowModes.Ellipsis;

        var note = CreateText("Note", contentRoot.transform, 17f, noteColor, TextAlignmentOptions.MidlineLeft);
        note.textWrappingMode = TextWrappingModes.Normal;
        note.overflowMode = TextOverflowModes.Ellipsis;
        var noteLayout = note.gameObject.AddComponent<LayoutElement>();
        noteLayout.minHeight = 42f;
        noteLayout.preferredHeight = 48f;

        var footerRow = CreateLayoutRow("FooterRow", contentRoot.transform, 10f, 24f);
        var id = CreateText("RequestId", footerRow.transform, 13f, WithAlpha(noteColor, 0.72f), TextAlignmentOptions.MidlineLeft);
        id.textWrappingMode = TextWrappingModes.NoWrap;
        id.overflowMode = TextOverflowModes.Ellipsis;
        var idLayout = id.gameObject.AddComponent<LayoutElement>();
        idLayout.preferredWidth = 170f;
        idLayout.preferredHeight = 22f;

        var progressTrack = CreateImage("ProgressTrack", footerRow.transform, _roundedPillSprite, new Color(1f, 1f, 1f, 0.1f));
        var progressTrackLayout = progressTrack.gameObject.AddComponent<LayoutElement>();
        progressTrackLayout.flexibleWidth = 1f;
        progressTrackLayout.preferredHeight = 8f;
        var progressFill = CreateImage("ProgressFill", progressTrack.transform, _roundedPillSprite, runningColor);
        progressFill.type = Image.Type.Filled;
        progressFill.fillMethod = Image.FillMethod.Horizontal;
        progressFill.fillOrigin = 0;
        progressFill.fillAmount = 0f;
        StretchToParent(progressFill.rectTransform);

        var card = new StatusCard
        {
            Root = labelObject,
            RectTransform = rect,
            Canvas = canvas,
            BackgroundImage = background,
            GlowImage = glow,
            AccentImage = accent,
            StatusPillImage = statusPill,
            ProgressFillImage = progressFill,
            Title = title,
            State = status,
            Note = note,
            Id = id,
        };

        ApplyCardTextLayout(card);
        _cardsByRequestId[requestId] = card;
        return card;
    }

    private StatusCardShell CreateStatusCardShell(Transform parent)
    {
        if (useStatusCardShellPrefab && statusCardShellPrefab != null)
        {
            var shellObject = Instantiate(statusCardShellPrefab, parent, false);
            shellObject.name = "UISetStatusCardShell";
            if (disableStatusCardShellInteraction)
            {
                DisableStatusCardShellInteraction(shellObject);
            }

            var canvasRoot = FindDeepChild(shellObject.transform, UISetCanvasRootName);
            var rect = (canvasRoot != null ? canvasRoot : shellObject.transform).GetComponent<RectTransform>();
            var canvas = (canvasRoot != null ? canvasRoot : shellObject.transform).GetComponent<Canvas>() ??
                shellObject.GetComponentInChildren<Canvas>(true);
            if (rect == null && canvas != null)
            {
                rect = canvas.GetComponent<RectTransform>();
            }

            ConfigureWorldCanvas(canvas);
            ConfigureCanvasScaler(canvas != null ? canvas.GetComponent<CanvasScaler>() : null);

            var background = FindDeepChild(shellObject.transform, UISetBackplateName)?.GetComponent<Image>();
            if (background != null)
            {
                ConfigureImage(background, applySceneShiftCardColors ? cardColor : background.color);
                StretchToParent(background.rectTransform);
            }

            var gradient = FindDeepChild(shellObject.transform, UISetGradientEffectName)?.GetComponent<Image>();
            if (gradient != null)
            {
                ConfigureImage(gradient, applySceneShiftCardColors ? cardGlowColor : gradient.color);
                StretchToParent(gradient.rectTransform);
            }

            DisableGraphicRaycasts(shellObject);
            var prefabShell = new StatusCardShell
            {
                Root = shellObject,
                RectTransform = rect,
                Canvas = canvas,
                BackgroundImage = background,
                GlowImage = gradient,
            };

            if (prefabShell.IsValid)
            {
                return prefabShell;
            }

            DestroyLabelObject(shellObject);
        }

        var canvasObject = new GameObject(UISetCanvasRootName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        canvasObject.transform.SetParent(parent, false);
        var generatedRect = canvasObject.GetComponent<RectTransform>();
        var generatedCanvas = canvasObject.GetComponent<Canvas>();
        ConfigureWorldCanvas(generatedCanvas);
        ConfigureCanvasScaler(canvasObject.GetComponent<CanvasScaler>());

        var generatedGlow = CreateImage("UISetGlow", generatedRect, _roundedCardSprite, cardGlowColor);
        StretchToParent(generatedGlow.rectTransform);
        generatedGlow.rectTransform.offsetMin = new Vector2(-18f, -18f);
        generatedGlow.rectTransform.offsetMax = new Vector2(18f, 18f);

        var generatedBackground = CreateImage("UISetBackplate", generatedRect, _roundedCardSprite, cardColor);
        StretchToParent(generatedBackground.rectTransform);

        return new StatusCardShell
        {
            Root = canvasObject,
            RectTransform = generatedRect,
            Canvas = generatedCanvas,
            BackgroundImage = generatedBackground,
            GlowImage = generatedGlow,
        };
    }

    private void ConfigureWorldCanvas(Canvas canvas)
    {
        if (canvas == null)
        {
            return;
        }

        canvas.renderMode = RenderMode.WorldSpace;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 32040;
        canvas.worldCamera = targetCamera;
    }

    private static void ConfigureCanvasScaler(CanvasScaler canvasScaler)
    {
        if (canvasScaler == null)
        {
            return;
        }

        canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        canvasScaler.dynamicPixelsPerUnit = 12f;
    }

    private static void ConfigureImage(Image image, Color color)
    {
        if (image == null)
        {
            return;
        }

        image.color = color;
        image.raycastTarget = false;
        if (image.sprite != null)
        {
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 1f;
        }
    }

    private static void DisableGraphicRaycasts(GameObject root)
    {
        if (root == null)
        {
            return;
        }

        foreach (var graphic in root.GetComponentsInChildren<Graphic>(true))
        {
            graphic.raycastTarget = false;
        }
    }

    private static void DisableStatusCardShellInteraction(GameObject root)
    {
        if (root == null)
        {
            return;
        }

        foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour == null)
            {
                continue;
            }

            var typeName = behaviour.GetType().FullName ?? behaviour.GetType().Name;
            if (typeName.StartsWith("Oculus.Interaction.", StringComparison.Ordinal) ||
                typeName.Contains("UIThemeManager", StringComparison.Ordinal))
            {
                behaviour.enabled = false;
            }
        }

        foreach (var audioSource in root.GetComponentsInChildren<AudioSource>(true))
        {
            audioSource.enabled = false;
            audioSource.playOnAwake = false;
        }
    }

    private static Transform FindDeepChild(Transform root, string childName)
    {
        if (root == null || string.IsNullOrWhiteSpace(childName))
        {
            return null;
        }

        if (root.name == childName)
        {
            return root;
        }

        for (var index = 0; index < root.childCount; index++)
        {
            var result = FindDeepChild(root.GetChild(index), childName);
            if (result != null)
            {
                return result;
            }
        }

        return null;
    }

    private void TryAutoLoadStatusCardShellPrefab()
    {
#if UNITY_EDITOR
        if (statusCardShellPrefab != null)
        {
            return;
        }

        statusCardShellPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ProjectUISetStatusCardShellPrefabPath);
#endif
    }

    private void ApplyCardTextLayout(StatusCard card)
    {
        if (!card.IsValid)
        {
            return;
        }

        var pixelSize = GetCardPixelSize();
        card.RectTransform.sizeDelta = pixelSize;
        card.RectTransform.localScale = Vector3.one * (cardWidthMeters / Mathf.Max(1f, pixelSize.x));
        if (card.Canvas != null)
        {
            card.Canvas.worldCamera = targetCamera;
        }
    }

    private void FaceCamera()
    {
        if (targetCamera == null)
        {
            return;
        }

        foreach (var card in _cardsByRequestId.Values)
        {
            if (!card.IsValid || !card.Root.activeSelf)
            {
                continue;
            }

            var direction = card.Root.transform.position - targetCamera.transform.position;
            if (direction.sqrMagnitude > 0.0001f)
            {
                card.Root.transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            }
        }
    }

    private void RemoveHiddenLabels()
    {
        var staleRequestIds = new List<string>();
        foreach (var pair in _cardsByRequestId)
        {
            if (!_visibleRequestIds.Contains(pair.Key))
            {
                staleRequestIds.Add(pair.Key);
            }
        }

        foreach (var requestId in staleRequestIds)
        {
            if (_cardsByRequestId.TryGetValue(requestId, out var card) && card.Root != null)
            {
                DestroyLabelObject(card.Root);
            }

            _cardsByRequestId.Remove(requestId);
        }
    }

    private bool ShouldShow(GeneratedAssetRecord record)
    {
        return record.State switch
        {
            GeneratedObjectJobState.Imported => showImportedJobs,
            GeneratedObjectJobState.Failed => showFailedJobs,
            GeneratedObjectJobState.NeedsReview => showReviewJobs,
            _ => true,
        };
    }

    private Color GetStateColor(GeneratedObjectJobState state)
    {
        return state switch
        {
            GeneratedObjectJobState.CaptureReady => waitingColor,
            GeneratedObjectJobState.BackendSubmitted => runningColor,
            GeneratedObjectJobState.StylizedImageReady => waitingColor,
            GeneratedObjectJobState.ModelGenerationSubmitted => runningColor,
            GeneratedObjectJobState.ModelReady => readyColor,
            GeneratedObjectJobState.Imported => readyColor,
            GeneratedObjectJobState.NeedsReview => reviewColor,
            GeneratedObjectJobState.RuntimeBackendSubmitted => runningColor,
            GeneratedObjectJobState.RuntimeModelReady => readyColor,
            GeneratedObjectJobState.RuntimeModelDownloaded => readyColor,
            GeneratedObjectJobState.RuntimeLoaded => readyColor,
            GeneratedObjectJobState.Failed => failedColor,
            _ => waitingColor,
        };
    }

    private static float GetStateProgress(GeneratedObjectJobState state)
    {
        return state switch
        {
            GeneratedObjectJobState.Pending => 0.08f,
            GeneratedObjectJobState.CaptureReady => 0.18f,
            GeneratedObjectJobState.BackendSubmitted => 0.42f,
            GeneratedObjectJobState.StylizedImageReady => 0.58f,
            GeneratedObjectJobState.ModelGenerationSubmitted => 0.76f,
            GeneratedObjectJobState.ModelReady => 0.9f,
            GeneratedObjectJobState.Imported => 1f,
            GeneratedObjectJobState.NeedsReview => 0.92f,
            GeneratedObjectJobState.RuntimeBackendSubmitted => 0.48f,
            GeneratedObjectJobState.RuntimeModelReady => 0.86f,
            GeneratedObjectJobState.RuntimeModelDownloaded => 0.93f,
            GeneratedObjectJobState.RuntimeLoaded => 1f,
            GeneratedObjectJobState.Failed => 1f,
            _ => 0.08f,
        };
    }

    private static Color WithAlpha(Color color, float alpha)
    {
        color.a = alpha;
        return color;
    }

    private static void SetGraphicColor(Graphic graphic, Color color)
    {
        if (graphic != null)
        {
            graphic.color = color;
        }
    }

    private static string GetStateLabel(GeneratedObjectJobState state)
    {
        return state switch
        {
            GeneratedObjectJobState.Pending => "pending",
            GeneratedObjectJobState.CaptureReady => "image queued",
            GeneratedObjectJobState.BackendSubmitted => "image2 running",
            GeneratedObjectJobState.StylizedImageReady => "upload / 3D queued",
            GeneratedObjectJobState.ModelGenerationSubmitted => "Seed3D running",
            GeneratedObjectJobState.ModelReady => "model ready",
            GeneratedObjectJobState.Imported => "placed",
            GeneratedObjectJobState.NeedsReview => "review",
            GeneratedObjectJobState.RuntimeBackendSubmitted => "runtime backend",
            GeneratedObjectJobState.RuntimeModelReady => "runtime model ready",
            GeneratedObjectJobState.RuntimeModelDownloaded => "downloaded",
            GeneratedObjectJobState.RuntimeLoaded => "runtime loaded",
            GeneratedObjectJobState.Failed => "failed",
            _ => state.ToString(),
        };
    }

    private static string GetStateHint(GeneratedObjectJobState state)
    {
        return state switch
        {
            GeneratedObjectJobState.Pending => "Waiting for capture input.",
            GeneratedObjectJobState.CaptureReady => "Captured image is waiting for image stylization.",
            GeneratedObjectJobState.BackendSubmitted => "Image generation is running.",
            GeneratedObjectJobState.StylizedImageReady => "Stylized image is ready for upload / 3D generation.",
            GeneratedObjectJobState.ModelGenerationSubmitted => "3D model generation is running.",
            GeneratedObjectJobState.ModelReady => "Model is ready for Unity import.",
            GeneratedObjectJobState.Imported => "Replacement is placed in the room.",
            GeneratedObjectJobState.NeedsReview => "Generated asset needs visual review.",
            GeneratedObjectJobState.RuntimeBackendSubmitted => "Headset backend job is running.",
            GeneratedObjectJobState.RuntimeModelReady => "Runtime model URL is ready for download.",
            GeneratedObjectJobState.RuntimeModelDownloaded => "Runtime model is downloaded and waiting to load.",
            GeneratedObjectJobState.RuntimeLoaded => "Runtime model is loaded and waiting for review.",
            GeneratedObjectJobState.Failed => "Generation failed. Check job record.",
            _ => "Waiting for next pipeline step.",
        };
    }

    private static string ShortId(string requestId)
    {
        if (string.IsNullOrWhiteSpace(requestId) || requestId.Length <= 28)
        {
            return requestId;
        }

        return requestId.Substring(0, 28);
    }

    private static string Shorten(string value, int maxCharacters)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length <= maxCharacters)
        {
            return value;
        }

        return value.Substring(0, Mathf.Max(0, maxCharacters - 3)) + "...";
    }

    private void ResolveCamera()
    {
        if (targetCamera != null)
        {
            return;
        }

        targetCamera = Camera.main;
        if (targetCamera == null)
        {
            targetCamera = FindAnyObjectByType<Camera>();
        }
    }

    private void EnsureOverlayRoot()
    {
        if (overlayRoot != null)
        {
            return;
        }

        var rootObject = new GameObject("GenerationJobWorldStatusRoot");
        rootObject.transform.SetParent(transform, false);
        overlayRoot = rootObject.transform;
    }

    private Vector2 GetCardPixelSize()
    {
        var width = Mathf.Max(240f, cardPixelWidth);
        var aspect = Mathf.Max(0.2f, cardHeightMeters / Mathf.Max(0.01f, cardWidthMeters));
        return new Vector2(width, Mathf.Max(120f, width * aspect));
    }

    private static GameObject CreateLayoutRow(string name, Transform parent, float spacing, float preferredHeight)
    {
        var row = new GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        row.transform.SetParent(parent, false);
        var layout = row.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = spacing;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        layout.childAlignment = TextAnchor.MiddleLeft;

        var layoutElement = row.GetComponent<LayoutElement>();
        layoutElement.minHeight = preferredHeight;
        layoutElement.preferredHeight = preferredHeight;
        layoutElement.flexibleWidth = 1f;
        return row;
    }

    private static Image CreateImage(string name, Transform parent, Sprite sprite, Color color)
    {
        var imageObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        imageObject.transform.SetParent(parent, false);
        var image = imageObject.GetComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        image.raycastTarget = false;
        if (sprite != null)
        {
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 1f;
        }

        return image;
    }

    private static TextMeshProUGUI CreateText(
        string name,
        Transform parent,
        float fontSize,
        Color color,
        TextAlignmentOptions alignment)
    {
        var textObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(parent, false);
        var text = textObject.GetComponent<TextMeshProUGUI>();
        text.alignment = alignment;
        text.fontSize = fontSize;
        text.color = color;
        text.richText = false;
        text.raycastTarget = false;
        return text;
    }

    private static void StretchToParent(RectTransform rectTransform)
    {
        if (rectTransform == null)
        {
            return;
        }

        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;
        rectTransform.localScale = Vector3.one;
        rectTransform.localRotation = Quaternion.identity;
    }

    private static void SetLeftAccentLayout(RectTransform rectTransform)
    {
        if (rectTransform == null)
        {
            return;
        }

        rectTransform.anchorMin = new Vector2(0f, 0.14f);
        rectTransform.anchorMax = new Vector2(0f, 0.86f);
        rectTransform.pivot = new Vector2(0f, 0.5f);
        rectTransform.sizeDelta = new Vector2(8f, 0f);
        rectTransform.anchoredPosition = new Vector2(14f, 0f);
        rectTransform.localScale = Vector3.one;
        rectTransform.localRotation = Quaternion.identity;
    }

    private void EnsureStyleResources()
    {
        _roundedCardSprite ??= CreateRoundedSprite("SceneShift_StatusCardRoundedBox", 64, 18);
        _roundedPillSprite ??= CreateRoundedSprite("SceneShift_StatusPillRoundedBox", 48, 20);
    }

    private static Sprite CreateRoundedSprite(string name, int size, int radius)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = name,
            hideFlags = HideFlags.DontSave,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        var clear = new Color32(255, 255, 255, 0);
        var solid = new Color32(255, 255, 255, 255);
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var dx = x < radius ? radius - x : x >= size - radius ? x - (size - radius - 1) : 0;
                var dy = y < radius ? radius - y : y >= size - radius ? y - (size - radius - 1) : 0;
                var inside = dx == 0 && dy == 0 || dx * dx + dy * dy <= radius * radius;
                texture.SetPixel(x, y, inside ? solid : clear);
            }
        }

        texture.Apply(false, true);
        var sprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f),
            100f,
            0,
            SpriteMeshType.FullRect,
            new Vector4(radius, radius, radius, radius));
        sprite.name = name;
        sprite.hideFlags = HideFlags.DontSave;
        return sprite;
    }

    private void ClearLabels()
    {
        foreach (var card in _cardsByRequestId.Values)
        {
            if (card.Root != null)
            {
                DestroyLabelObject(card.Root);
            }
        }

        _cardsByRequestId.Clear();
        _visibleRequestIds.Clear();
    }

    private void SetCardsActive(bool isActive)
    {
        foreach (var card in _cardsByRequestId.Values)
        {
            if (card.Root != null)
            {
                card.Root.SetActive(isActive);
            }
        }
    }

    private static void DestroyLabelObject(GameObject labelObject)
    {
        if (labelObject == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(labelObject);
        }
        else
        {
            DestroyImmediate(labelObject);
        }
    }

    private static bool TryReadRequest(string path, out GeneratedObjectRequest request)
    {
        request = null;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        return TryReadJson(path, out request) && request != null && !string.IsNullOrWhiteSpace(request.RequestId);
    }

    private static bool TryReadJson<T>(string path, out T value) where T : class
    {
        value = null;
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return false;
            }

            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            value = JsonUtility.FromJson<T>(json);
            return value != null;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[GenerationJobWorldStatusOverlay] Failed to read {path}: {exception.Message}");
            return false;
        }
    }

    private static string GetLibraryDirectory(string folderName)
    {
        var libraryRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Library"));
        return Path.Combine(libraryRoot, string.IsNullOrWhiteSpace(folderName) ? "GeneratedObjectJobs" : folderName);
    }

    private struct StatusCard
    {
        public GameObject Root;
        public RectTransform RectTransform;
        public Canvas Canvas;
        public Image BackgroundImage;
        public Image GlowImage;
        public Image AccentImage;
        public Image StatusPillImage;
        public Image ProgressFillImage;
        public TMP_Text Title;
        public TMP_Text State;
        public TMP_Text Note;
        public TMP_Text Id;

        public bool IsValid => Root != null && RectTransform != null && Title != null && State != null && Note != null && Id != null && AccentImage != null;
    }

    private struct StatusCardShell
    {
        public GameObject Root;
        public RectTransform RectTransform;
        public Canvas Canvas;
        public Image BackgroundImage;
        public Image GlowImage;

        public bool IsValid => Root != null && RectTransform != null && Canvas != null && BackgroundImage != null;
    }
}
