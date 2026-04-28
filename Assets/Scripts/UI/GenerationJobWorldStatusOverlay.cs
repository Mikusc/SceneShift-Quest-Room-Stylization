using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public class GenerationJobWorldStatusOverlay : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Camera targetCamera;
    [SerializeField] private Transform overlayRoot;

    [Header("Jobs")]
    [SerializeField] private string generatedObjectJobFolderName = "GeneratedObjectJobs";
    [SerializeField] private bool overlayVisible = true;
    [SerializeField] private bool showImportedJobs = true;
    [SerializeField] private bool showFailedJobs = true;
    [SerializeField] private bool showReviewJobs = true;

    [Header("Refresh")]
    [SerializeField, Min(0.2f)] private float refreshIntervalSeconds = 0.5f;
    [SerializeField, Min(0.1f)] private float verticalOffsetMeters = 0.35f;
    [SerializeField, Min(0.01f)] private float textWorldScale = 0.055f;
    [SerializeField, Min(0.1f)] private float textFontSize = 1.55f;
    [SerializeField, Min(0.05f)] private float cardWidthMeters = 0.62f;
    [SerializeField, Min(0.03f)] private float cardHeightMeters = 0.24f;
    [SerializeField, Min(0.001f)] private float accentStripWidthMeters = 0.018f;
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
    private Mesh _quadMesh;
    private Material _cardMaterial;
    private Material _accentMaterial;
    private Material _glowMaterial;
    private float _nextRefreshTime;
    private string _latestSummary = "[GenerationJobWorldStatusOverlay] State: waiting";

    private void Reset()
    {
        ResolveCamera();
    }

    private void Awake()
    {
        ResolveCamera();
        EnsureOverlayRoot();
    }

    private void OnEnable()
    {
        ResolveCamera();
        EnsureOverlayRoot();
    }

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
        card.Note.text = BuildNoteText(record);
        card.AccentRenderer.material.color = stateColor;
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

        _builder.AppendLine();
        _builder.Append(ShortId(record.RequestId));
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

        var glow = CreateQuad("CardGlow", labelObject.transform, new Vector3(0f, 0f, 0.018f), new Vector3(cardWidthMeters + 0.08f, cardHeightMeters + 0.08f, 1f), _glowMaterial);
        var background = CreateQuad("CardBackground", labelObject.transform, new Vector3(0f, 0f, 0.012f), new Vector3(cardWidthMeters, cardHeightMeters, 1f), _cardMaterial);
        var accent = CreateQuad(
            "StatusAccent",
            labelObject.transform,
            new Vector3(-cardWidthMeters * 0.5f + accentStripWidthMeters * 0.5f + 0.018f, 0f, 0.008f),
            new Vector3(accentStripWidthMeters, cardHeightMeters - 0.04f, 1f),
            _accentMaterial);

        var title = CreateText("Title", labelObject.transform, Vector3.zero, textFontSize, titleColor, TextAlignmentOptions.MidlineLeft);
        title.textWrappingMode = TextWrappingModes.NoWrap;
        title.overflowMode = TextOverflowModes.Ellipsis;

        var note = CreateText("Note", labelObject.transform, Vector3.zero, textFontSize * 0.7f, noteColor, TextAlignmentOptions.MidlineLeft);
        note.textWrappingMode = TextWrappingModes.Normal;
        note.overflowMode = TextOverflowModes.Ellipsis;

        var card = new StatusCard
        {
            Root = labelObject,
            BackgroundRenderer = background,
            GlowRenderer = glow,
            AccentRenderer = accent,
            Title = title,
            Note = note,
        };

        ApplyCardTextLayout(card);
        _cardsByRequestId[requestId] = card;
        return card;
    }

    private void ApplyCardTextLayout(StatusCard card)
    {
        if (!card.IsValid)
        {
            return;
        }

        var safeTextScale = Mathf.Max(0.001f, textWorldScale);
        var textX = -cardWidthMeters * 0.5f + accentStripWidthMeters + 0.045f;
        var textWidth = Mathf.Max(1f, (cardWidthMeters - 0.13f) / safeTextScale);
        var titleHeight = Mathf.Max(0.5f, 0.085f / safeTextScale);
        var noteHeight = Mathf.Max(0.8f, 0.13f / safeTextScale);

        ApplyTextLayout(card.Title, new Vector3(textX, 0.048f, 0f), new Vector2(textWidth, titleHeight), textWorldScale);
        ApplyTextLayout(card.Note, new Vector3(textX, -0.052f, 0f), new Vector2(textWidth, noteHeight), textWorldScale);
    }

    private static void ApplyTextLayout(TextMeshPro text, Vector3 localPosition, Vector2 sizeDelta, float localScale)
    {
        if (text == null)
        {
            return;
        }

        var rect = text.rectTransform;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.localPosition = localPosition;
        rect.localRotation = Quaternion.identity;
        rect.localScale = Vector3.one * Mathf.Max(0.001f, localScale);
        rect.sizeDelta = sizeDelta;
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
            GeneratedObjectJobState.Failed => failedColor,
            _ => waitingColor,
        };
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

    private MeshRenderer CreateQuad(string name, Transform parent, Vector3 localPosition, Vector3 localScale, Material material)
    {
        var quad = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        quad.transform.SetParent(parent, false);
        quad.transform.localPosition = localPosition;
        quad.transform.localRotation = Quaternion.identity;
        quad.transform.localScale = localScale;

        var meshFilter = quad.GetComponent<MeshFilter>();
        meshFilter.sharedMesh = _quadMesh;

        var renderer = quad.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        return renderer;
    }

    private TextMeshPro CreateText(string name, Transform parent, Vector3 localPosition, float fontSize, Color color, TextAlignmentOptions alignment)
    {
        var textObject = new GameObject(name, typeof(RectTransform), typeof(TextMeshPro));
        textObject.transform.SetParent(parent, false);

        var text = textObject.GetComponent<TextMeshPro>();
        text.alignment = alignment;
        text.fontSize = fontSize;
        text.color = color;
        text.richText = false;
        text.raycastTarget = false;
        ApplyTextLayout(text, localPosition, Vector2.zero, textWorldScale);
        return text;
    }

    private void EnsureStyleResources()
    {
        if (_quadMesh == null)
        {
            _quadMesh = CreateQuadMesh();
        }

        if (_cardMaterial == null)
        {
            _cardMaterial = CreateUnlitTransparentMaterial("SceneShift Status Card", cardColor);
        }

        if (_accentMaterial == null)
        {
            _accentMaterial = CreateUnlitTransparentMaterial("SceneShift Status Accent", runningColor);
        }

        if (_glowMaterial == null)
        {
            _glowMaterial = CreateUnlitTransparentMaterial("SceneShift Status Glow", cardGlowColor);
        }
    }

    private static Mesh CreateQuadMesh()
    {
        var mesh = new Mesh
        {
            name = "SceneShiftWorldStatusQuad",
            vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3(0.5f, -0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f),
            },
            uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f),
            },
            triangles = new[] { 0, 2, 1, 0, 3, 2 },
        };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Material CreateUnlitTransparentMaterial(string name, Color color)
    {
        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
        {
            shader = Shader.Find("Unlit/Color");
        }

        var material = new Material(shader)
        {
            name = name,
            color = color,
        };

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }

        if (material.HasProperty("_Surface"))
        {
            material.SetFloat("_Surface", 1f);
        }

        if (material.HasProperty("_Blend"))
        {
            material.SetFloat("_Blend", 0f);
        }

        if (material.HasProperty("_SrcBlend"))
        {
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        }

        if (material.HasProperty("_DstBlend"))
        {
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        }

        if (material.HasProperty("_ZWrite"))
        {
            material.SetFloat("_ZWrite", 0f);
        }

        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        return material;
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
        public MeshRenderer BackgroundRenderer;
        public MeshRenderer GlowRenderer;
        public MeshRenderer AccentRenderer;
        public TextMeshPro Title;
        public TextMeshPro Note;

        public bool IsValid => Root != null && Title != null && Note != null && AccentRenderer != null;
    }
}
