using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class SceneShiftUISetDashboard : MonoBehaviour
{
    private const string BackgroundObjectName = "SceneShiftDashboardBackground";
    private const string ContentObjectName = "SceneShiftDashboardContent";

    [Header("References")]
    [SerializeField] private Canvas canvas;
    [SerializeField] private Transform headTransform;
    [SerializeField] private DevicePassthroughCaptureService captureService;
    [SerializeField] private GenerationQueueStatusService generationQueueStatusService;
    [SerializeField] private ThemeIntentController themeIntentController;
    [SerializeField] private SurfaceOverrideApplier surfaceOverrideApplier;
    [SerializeField] private MRUKShellVisibilityToggle shellVisibilityToggle;
    [SerializeField] private GenerationJobWorldStatusOverlay worldStatusOverlay;
    [SerializeField] private RoomStyleCacheService roomStyleCacheService;

    [Header("Meta UISet Prefabs")]
    [SerializeField] private GameObject primaryButtonPrefab;
    [SerializeField] private GameObject secondaryButtonPrefab;
    [SerializeField] private GameObject destructiveButtonPrefab;
    [SerializeField] private GameObject dropdownPrefab;

    [Header("Runtime Panel")]
    [SerializeField] private bool visibleInPlayMode = true;
    [SerializeField] private bool headLocked = true;
    [SerializeField] private bool createContentIfMissing = true;
    [SerializeField] private bool disableMetaUISetRuntimeComponents = true;
    [SerializeField] private bool hideInheritedUISetVisuals = true;
    [SerializeField] private bool useStableRuntimeControlsOnly = true;
    [SerializeField] private bool suppressLegacyHeadsetOverlays = true;
    [SerializeField, Min(0.1f)] private float updateIntervalSeconds = 0.25f;
    [SerializeField] private bool enablePanelVisibilityToggleInPlay = true;
    [SerializeField] private OVRInput.RawButton panelVisibilityToggleButton = OVRInput.RawButton.Start;

    [Header("Head-Locked Placement")]
    [SerializeField] private Vector3 localOffset = new(0f, -0.06f, 1.15f);
    [SerializeField] private Vector3 localEulerOffset = new(7f, 0f, 0f);
    [SerializeField] private Vector2 panelSizePixels = new(840f, 680f);
    [SerializeField, Min(0.0001f)] private float worldScale = 0.00105f;

    [Header("Controller Pointer")]
    [SerializeField] private bool enableControllerPointerInPlay = true;
    [SerializeField] private bool showControllerPointerRay = true;
    [SerializeField] private Transform pointerRayOrigin;
    [SerializeField] private OVRInput.Controller pointerController = OVRInput.Controller.RTouch;
    [SerializeField] private OVRInput.Button pointerSelectButton = OVRInput.Button.PrimaryIndexTrigger;
    [SerializeField, Min(0.2f)] private float pointerMaxDistanceMeters = 3f;
    [SerializeField, Min(0f)] private float pointerPanelHitPaddingPixels = 48f;
    [SerializeField] private Color buttonNormalColor = new(0.035f, 0.12f, 0.17f, 0.95f);
    [SerializeField] private Color buttonHoverColor = new(0.08f, 0.28f, 0.36f, 0.98f);
    [SerializeField] private Color dropdownNormalColor = new(0.04f, 0.09f, 0.13f, 0.95f);
    [SerializeField] private Color dropdownHoverColor = new(0.07f, 0.2f, 0.27f, 0.98f);

    public string LatestSummary => _latestSummary;

    private readonly StringBuilder _builder = new(1024);
    private Image _backgroundImage;
    private RectTransform _contentRoot;
    private TMP_Text _titleText;
    private TMP_Text _subtitleText;
    private TMP_Text _statusText;
    private TMP_Text _pointerHintText;
    private TMP_Dropdown _themeTmpDropdown;
    private Dropdown _themeDropdown;
    private Button _captureButton;
    private Button _autoTargetButton;
    private Button _surfaceButton;
    private Button _shellButton;
    private Button _worldStatusButton;
    private Button _hoveredButton;
    private Image _hoveredDropdownImage;
    private LineRenderer _pointerLine;
    private Material _pointerLineMaterial;
    private bool _isSyncingThemeDropdown;
    private bool _panelVisibleRuntime = true;
    private float _nextUpdateTime;
    private float _nextPointerOriginResolveTime;
    private string _latestSummary = "[SceneShiftUISetDashboard] State: waiting";

    private void Reset()
    {
        ResolveReferences();
    }

    private void Awake()
    {
        _panelVisibleRuntime = visibleInPlayMode;
        ResolveReferences();
        DisableProblematicMetaRuntimeComponents();
        NormalizeCanvasRoot();
        EnsureContent();
        NormalizeContentRoot();
        HideInheritedUISetVisuals();
        WireButtons();
    }

    private void OnEnable()
    {
        _panelVisibleRuntime = visibleInPlayMode;
        ResolveReferences();
        DisableProblematicMetaRuntimeComponents();
        NormalizeCanvasRoot();
        EnsureContent();
        NormalizeContentRoot();
        HideInheritedUISetVisuals();
        WireButtons();
        UpdatePanel();
    }

    private void LateUpdate()
    {
        if (!Application.isPlaying)
        {
            SetVisible(false);
            return;
        }

        HandlePanelVisibilityToggleInput();

        if (!visibleInPlayMode || !_panelVisibleRuntime)
        {
            ClearPointerHover();
            SetPointerLineVisible(false);
            SetVisible(false);
            return;
        }

        ResolveReferences();
        DisableProblematicMetaRuntimeComponents();
        NormalizeCanvasRoot();
        EnsureContent();
        NormalizeContentRoot();
        HideInheritedUISetVisuals();
        SetVisible(true);
        SuppressLegacyHeadsetOverlays();

        if (headLocked)
        {
            UpdateHeadLockedPlacement();
        }

        HandleControllerPointer();

        if (Time.unscaledTime >= _nextUpdateTime)
        {
            _nextUpdateTime = Time.unscaledTime + updateIntervalSeconds;
            UpdatePanel();
        }
    }

    public void CaptureCurrentTarget()
    {
        ResolveReferences();
        captureService?.CapturePassthroughFrame();
    }

    public void SetAutoTargetFromGaze()
    {
        ResolveReferences();
        captureService?.SetTargetSelectionAuto();
    }

    public void CycleTheme()
    {
        ResolveReferences();
        themeIntentController?.CycleTheme(1);
    }

    public void ReapplySurfaces()
    {
        ResolveReferences();
        surfaceOverrideApplier?.ReapplySurfaceOverrides();
    }

    public void ToggleShells()
    {
        ResolveReferences();
        shellVisibilityToggle?.ToggleCleanView();
    }

    public void ToggleObjectStatusCards()
    {
        ResolveReferences();
        worldStatusOverlay?.ToggleOverlayVisible();
    }

    public void TogglePanelVisibility()
    {
        _panelVisibleRuntime = !_panelVisibleRuntime;
        if (!_panelVisibleRuntime)
        {
            ClearPointerHover();
            SetPointerLineVisible(false);
        }

        SetVisible(_panelVisibleRuntime);
    }

    public void SelectThemeFromDropdown(int index)
    {
        if (_isSyncingThemeDropdown)
        {
            return;
        }

        ResolveReferences();
        themeIntentController?.SelectThemeByIndex(index);
        roomStyleCacheService?.Refresh();
        UpdatePanel();
    }

    private void ResolveReferences()
    {
        if (canvas == null)
        {
            canvas = GetComponentInChildren<Canvas>(true);
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

        if (captureService == null)
        {
            captureService = FindAnyObjectByType<DevicePassthroughCaptureService>();
        }

        if (generationQueueStatusService == null)
        {
            generationQueueStatusService = FindAnyObjectByType<GenerationQueueStatusService>();
        }

        if (themeIntentController == null)
        {
            themeIntentController = FindAnyObjectByType<ThemeIntentController>();
        }

        if (surfaceOverrideApplier == null)
        {
            surfaceOverrideApplier = FindAnyObjectByType<SurfaceOverrideApplier>();
        }

        if (shellVisibilityToggle == null)
        {
            shellVisibilityToggle = FindAnyObjectByType<MRUKShellVisibilityToggle>();
        }

        if (worldStatusOverlay == null)
        {
            worldStatusOverlay = FindAnyObjectByType<GenerationJobWorldStatusOverlay>();
        }

        if (roomStyleCacheService == null)
        {
            roomStyleCacheService = FindAnyObjectByType<RoomStyleCacheService>();
        }
    }

    private void EnsureContent()
    {
        if (!createContentIfMissing || canvas == null)
        {
            return;
        }

        NormalizeCanvasRoot();

        if (_contentRoot != null)
        {
            EnsureBackground();
            NormalizeContentRoot();
            return;
        }

        EnsureBackground();

        var existing = canvas.transform.Find(ContentObjectName);
        if (existing != null)
        {
            _contentRoot = existing as RectTransform;
            ClearChildren(_contentRoot);
            NormalizeContentRoot();
        }
        else
        {
            var contentObject = new GameObject(ContentObjectName, typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            contentObject.transform.SetParent(canvas.transform, false);
            _contentRoot = contentObject.GetComponent<RectTransform>();
            NormalizeContentRoot();
        }

        var layout = _contentRoot.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(34, 34, 28, 28);
        layout.spacing = 10f;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        var fitter = _contentRoot.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

        _titleText = CreateText("Title", "SceneShift Control", 38f, FontStyles.Bold, new Color(0.95f, 0.98f, 1f, 1f));
        _subtitleText = CreateText("Subtitle", "Room-aware stylization pipeline", 18f, FontStyles.Normal, new Color(0.65f, 0.78f, 0.9f, 1f));
        CreateThemeDropdown();
        _statusText = CreateText("Status", "Waiting for Play mode.", 18f, FontStyles.Normal, Color.white);
        _statusText.textWrappingMode = TextWrappingModes.Normal;
        SetLayoutSize(_statusText.gameObject, 0f, 180f);
        _pointerHintText = CreateText("PointerHint", "Point right controller at a button, press Trigger.", 15f, FontStyles.Normal, new Color(0.72f, 0.86f, 0.96f, 1f));
        SetLayoutSize(_pointerHintText.gameObject, 0f, 28f);

        CreateButtonSection("Capture", true);
        CreateButtonSection("Room", false);
        NormalizeContentRoot();
        HideInheritedUISetVisuals();
    }

    private void EnsureBackground()
    {
        if (canvas == null)
        {
            return;
        }

        var existing = canvas.transform.Find(BackgroundObjectName);
        if (existing == null)
        {
            var backgroundObject = new GameObject(BackgroundObjectName, typeof(RectTransform), typeof(Image));
            backgroundObject.transform.SetParent(canvas.transform, false);
            existing = backgroundObject.transform;
        }

        _backgroundImage = existing.GetComponent<Image>();
        if (_backgroundImage == null)
        {
            _backgroundImage = existing.gameObject.AddComponent<Image>();
        }

        var rect = existing.GetComponent<RectTransform>();
        if (rect != null)
        {
            StretchToParent(rect);
        }

        _backgroundImage.color = new Color(0.025f, 0.035f, 0.045f, 0.9f);
        _backgroundImage.raycastTarget = false;
        existing.SetAsFirstSibling();
    }

    private void NormalizeCanvasRoot()
    {
        if (canvas == null)
        {
            return;
        }

        var canvasTransform = canvas.transform;
        canvasTransform.localPosition = Vector3.zero;
        canvasTransform.localRotation = Quaternion.identity;
        canvasTransform.localScale = Vector3.one;

        var scaler = canvas.GetComponent<CanvasScaler>();
        if (scaler != null)
        {
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.dynamicPixelsPerUnit = 10f;
        }

        var canvasRect = canvas.GetComponent<RectTransform>();
        if (canvasRect != null)
        {
            canvasRect.anchorMin = new Vector2(0.5f, 0.5f);
            canvasRect.anchorMax = new Vector2(0.5f, 0.5f);
            canvasRect.pivot = new Vector2(0.5f, 0.5f);
            canvasRect.anchoredPosition = Vector2.zero;
            canvasRect.sizeDelta = panelSizePixels;
        }

        // UISet prefabs can carry root layout/scale settings that make this generated panel
        // nearly invisible in world space. Keep layout only on our generated content rows.
        foreach (var layoutGroup in canvas.GetComponents<LayoutGroup>())
        {
            layoutGroup.enabled = false;
        }

        var rootFitter = canvas.GetComponent<ContentSizeFitter>();
        if (rootFitter != null)
        {
            rootFitter.enabled = false;
        }
    }

    private void NormalizeContentRoot()
    {
        if (_contentRoot == null)
        {
            return;
        }

        _contentRoot.anchorMin = new Vector2(0.5f, 0.5f);
        _contentRoot.anchorMax = new Vector2(0.5f, 0.5f);
        _contentRoot.pivot = new Vector2(0.5f, 0.5f);
        _contentRoot.anchoredPosition = Vector2.zero;
        _contentRoot.localPosition = Vector3.zero;
        _contentRoot.localRotation = Quaternion.identity;
        _contentRoot.localScale = Vector3.one;
        _contentRoot.sizeDelta = panelSizePixels;
    }

    private void HideInheritedUISetVisuals()
    {
        if (!hideInheritedUISetVisuals || canvas == null)
        {
            return;
        }

        for (var i = 0; i < canvas.transform.childCount; i++)
        {
            var child = canvas.transform.GetChild(i);
            var keep = child.name == BackgroundObjectName || child.name == ContentObjectName;
            if (child.gameObject.activeSelf != keep)
            {
                child.gameObject.SetActive(keep);
            }
        }
    }

    private void SuppressLegacyHeadsetOverlays()
    {
        if (!suppressLegacyHeadsetOverlays || !Application.isPlaying)
        {
            return;
        }

        foreach (var hud in FindObjectsByType<DevicePassthroughCaptureHud>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (IsSameOrParentedWithDashboard(hud.transform))
            {
                continue;
            }

            hud.enabled = false;
            SetChildCanvasesVisible(hud.transform, false);
        }

        foreach (var debugPanel in FindObjectsByType<StylizationDebugPanel>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (IsSameOrParentedWithDashboard(debugPanel.transform))
            {
                continue;
            }

            debugPanel.enabled = false;
            SetChildCanvasesVisible(debugPanel.transform, false);
        }

        foreach (var shellToggle in FindObjectsByType<MRUKShellVisibilityToggle>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (IsSameOrParentedWithDashboard(shellToggle.transform))
            {
                continue;
            }

            SetChildCanvasesVisible(shellToggle.transform, false);
        }
    }

    private bool IsSameOrParentedWithDashboard(Transform candidate)
    {
        return candidate == transform || candidate.IsChildOf(transform) || transform.IsChildOf(candidate);
    }

    private static void SetChildCanvasesVisible(Transform root, bool visible)
    {
        if (root == null)
        {
            return;
        }

        foreach (var childCanvas in root.GetComponentsInChildren<Canvas>(true))
        {
            childCanvas.gameObject.SetActive(visible);
        }
    }

    private static void ClearChildren(Transform root)
    {
        if (root == null)
        {
            return;
        }

        for (var i = root.childCount - 1; i >= 0; i--)
        {
            var child = root.GetChild(i).gameObject;
            if (Application.isPlaying)
            {
                Destroy(child);
            }
            else
            {
                DestroyImmediate(child);
            }
        }
    }

    private void DisableProblematicMetaRuntimeComponents()
    {
        if (!disableMetaUISetRuntimeComponents)
        {
            return;
        }

        foreach (var behaviour in GetComponentsInChildren<Behaviour>(true))
        {
            var typeName = behaviour.GetType().FullName;
            if (typeName == "Oculus.Interaction.UIThemeManager" ||
                typeName == "Oculus.Interaction.PointableCanvas" ||
                typeName == "Oculus.Interaction.PokeInteractable" ||
                typeName == "Oculus.Interaction.RayInteractable" ||
                typeName == "Oculus.Interaction.PointableCanvasUnityEventWrapper")
            {
                behaviour.enabled = false;
            }
        }
    }

    private void CreateThemeDropdown()
    {
        var rowObject = new GameObject("ThemeDropdownRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        rowObject.transform.SetParent(_contentRoot, false);

        var row = rowObject.GetComponent<HorizontalLayoutGroup>();
        row.spacing = 12f;
        row.childAlignment = TextAnchor.MiddleLeft;
        row.childControlWidth = true;
        row.childControlHeight = true;
        row.childForceExpandWidth = false;
        row.childForceExpandHeight = false;

        var rowLayout = rowObject.GetComponent<LayoutElement>();
        rowLayout.minHeight = 68f;
        rowLayout.preferredHeight = 68f;

        var label = CreateText("ThemeLabel", "Theme", 22f, FontStyles.Bold, new Color(0.72f, 0.84f, 0.95f, 1f));
        label.transform.SetParent(rowObject.transform, false);
        var labelLayout = label.GetComponent<LayoutElement>();
        labelLayout.minWidth = 95f;
        labelLayout.preferredWidth = 95f;

        var dropdownObject = CreateDropdownObject(rowObject.transform);
        dropdownObject.name = "ThemeDropdown";

        var layout = dropdownObject.GetComponent<LayoutElement>();
        if (layout == null)
        {
            layout = dropdownObject.AddComponent<LayoutElement>();
        }

        layout.minWidth = 380f;
        layout.preferredWidth = 420f;
        layout.minHeight = 58f;
        layout.preferredHeight = 58f;

        _themeTmpDropdown = dropdownObject.GetComponentInChildren<TMP_Dropdown>(true);
        _themeDropdown = dropdownObject.GetComponentInChildren<Dropdown>(true);
        if (_themeTmpDropdown == null && _themeDropdown == null)
        {
            DestroyDropdownObject(dropdownObject);
            dropdownObject = CreateFallbackTmpDropdown(rowObject.transform);
            dropdownObject.name = "ThemeDropdown";
            _themeTmpDropdown = dropdownObject.GetComponent<TMP_Dropdown>();
        }

        WireThemeDropdown();
        PopulateThemeDropdown();
    }

    private GameObject CreateDropdownObject(Transform parent)
    {
        if (useStableRuntimeControlsOnly || dropdownPrefab == null)
        {
            return CreateFallbackTmpDropdown(parent);
        }

        var dropdownObject = Instantiate(dropdownPrefab, parent);
        dropdownObject.transform.localScale = Vector3.one;
        return dropdownObject;
    }

    private TMP_Text CreateText(string name, string text, float fontSize, FontStyles fontStyle, Color color)
    {
        var textObject = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
        textObject.transform.SetParent(_contentRoot, false);

        var label = textObject.GetComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = fontSize;
        label.fontStyle = fontStyle;
        label.color = color;
        label.raycastTarget = false;
        label.textWrappingMode = TextWrappingModes.Normal;

        var layout = textObject.GetComponent<LayoutElement>();
        layout.minHeight = Mathf.Max(36f, fontSize + 12f);
        layout.preferredHeight = layout.minHeight;
        return label;
    }

    private void CreateButtonSection(string name, bool captureSection)
    {
        var rowObject = new GameObject($"{name}Buttons", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        rowObject.transform.SetParent(_contentRoot, false);

        var row = rowObject.GetComponent<HorizontalLayoutGroup>();
        row.spacing = 12f;
        row.childAlignment = TextAnchor.MiddleLeft;
        row.childControlWidth = true;
        row.childControlHeight = true;
        row.childForceExpandWidth = false;
        row.childForceExpandHeight = false;

        var layout = rowObject.GetComponent<LayoutElement>();
        layout.minHeight = 74f;
        layout.preferredHeight = 74f;

        if (captureSection)
        {
            _captureButton = CreateButton(rowObject.transform, primaryButtonPrefab, "Capture");
            _autoTargetButton = CreateButton(rowObject.transform, secondaryButtonPrefab, "Auto Target");
        }
        else
        {
            _surfaceButton = CreateButton(rowObject.transform, secondaryButtonPrefab, "Reapply Room");
            _shellButton = CreateButton(rowObject.transform, destructiveButtonPrefab != null ? destructiveButtonPrefab : secondaryButtonPrefab, "Clean View");
            _worldStatusButton = CreateButton(rowObject.transform, secondaryButtonPrefab, "Object Status");
        }
    }

    private Button CreateButton(Transform parent, GameObject prefab, string label)
    {
        var buttonObject = !useStableRuntimeControlsOnly && prefab != null
            ? Instantiate(prefab, parent)
            : CreateFallbackButton(parent);

        buttonObject.name = label.Replace(" ", string.Empty) + "Button";
        buttonObject.transform.localScale = Vector3.one;

        var rect = buttonObject.GetComponent<RectTransform>();
        if (rect != null)
        {
            rect.sizeDelta = new Vector2(180f, 62f);
        }

        var layout = buttonObject.GetComponent<LayoutElement>();
        if (layout == null)
        {
            layout = buttonObject.AddComponent<LayoutElement>();
        }

        layout.minWidth = 170f;
        layout.preferredWidth = 180f;
        layout.minHeight = 62f;
        layout.preferredHeight = 62f;

        var text = buttonObject.GetComponentInChildren<TMP_Text>(true);
        if (text != null)
        {
            text.text = label;
            text.fontSize = Mathf.Max(18f, text.fontSize);
        }

        return buttonObject.GetComponentInChildren<Button>(true);
    }

    private static void SetLayoutSize(GameObject target, float preferredWidth, float preferredHeight)
    {
        if (target == null)
        {
            return;
        }

        var layout = target.GetComponent<LayoutElement>();
        if (layout == null)
        {
            layout = target.AddComponent<LayoutElement>();
        }

        if (preferredWidth > 0f)
        {
            layout.minWidth = preferredWidth;
            layout.preferredWidth = preferredWidth;
        }

        if (preferredHeight > 0f)
        {
            layout.minHeight = preferredHeight;
            layout.preferredHeight = preferredHeight;
        }
    }

    private GameObject CreateFallbackButton(Transform parent)
    {
        var buttonObject = new GameObject("FallbackButton", typeof(RectTransform), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(parent, false);

        var image = buttonObject.GetComponent<Image>();
        image.color = buttonNormalColor;

        var labelObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelObject.transform.SetParent(buttonObject.transform, false);
        var labelRect = labelObject.GetComponent<RectTransform>();
        StretchToParent(labelRect);

        var label = labelObject.GetComponent<TextMeshProUGUI>();
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
        label.fontSize = 20f;
        label.raycastTarget = false;
        return buttonObject;
    }

    private GameObject CreateFallbackTmpDropdown(Transform parent)
    {
        var root = new GameObject("FallbackThemeDropdown", typeof(RectTransform), typeof(Image), typeof(TMP_Dropdown));
        root.transform.SetParent(parent, false);

        var rect = root.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(420f, 58f);

        var image = root.GetComponent<Image>();
        image.color = dropdownNormalColor;

        var captionObject = new GameObject("Caption", typeof(RectTransform), typeof(TextMeshProUGUI));
        captionObject.transform.SetParent(root.transform, false);
        var captionRect = captionObject.GetComponent<RectTransform>();
        captionRect.anchorMin = Vector2.zero;
        captionRect.anchorMax = Vector2.one;
        captionRect.offsetMin = new Vector2(18f, 4f);
        captionRect.offsetMax = new Vector2(-50f, -4f);
        var captionText = captionObject.GetComponent<TextMeshProUGUI>();
        captionText.alignment = TextAlignmentOptions.MidlineLeft;
        captionText.fontSize = 21f;
        captionText.color = Color.white;
        captionText.raycastTarget = false;

        var arrowObject = new GameObject("Arrow", typeof(RectTransform), typeof(TextMeshProUGUI));
        arrowObject.transform.SetParent(root.transform, false);
        var arrowRect = arrowObject.GetComponent<RectTransform>();
        arrowRect.anchorMin = new Vector2(1f, 0f);
        arrowRect.anchorMax = new Vector2(1f, 1f);
        arrowRect.sizeDelta = new Vector2(42f, 0f);
        arrowRect.anchoredPosition = new Vector2(-24f, 0f);
        var arrowText = arrowObject.GetComponent<TextMeshProUGUI>();
        arrowText.text = "v";
        arrowText.alignment = TextAlignmentOptions.Center;
        arrowText.fontSize = 24f;
        arrowText.color = new Color(0.72f, 0.84f, 0.95f, 1f);
        arrowText.raycastTarget = false;

        var template = CreateDropdownTemplate(root.transform);
        var itemLabel = template.GetComponentInChildren<TMP_Text>(true);

        var dropdown = root.GetComponent<TMP_Dropdown>();
        dropdown.targetGraphic = image;
        dropdown.captionText = captionText;
        dropdown.template = template.GetComponent<RectTransform>();
        dropdown.itemText = itemLabel;
        return root;
    }

    private GameObject CreateDropdownTemplate(Transform parent)
    {
        var template = new GameObject("Template", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
        template.transform.SetParent(parent, false);
        template.SetActive(false);

        var templateRect = template.GetComponent<RectTransform>();
        templateRect.anchorMin = new Vector2(0f, 0f);
        templateRect.anchorMax = new Vector2(1f, 0f);
        templateRect.pivot = new Vector2(0.5f, 1f);
        templateRect.anchoredPosition = new Vector2(0f, -6f);
        templateRect.sizeDelta = new Vector2(0f, 190f);

        template.GetComponent<Image>().color = new Color(0.035f, 0.055f, 0.075f, 0.98f);

        var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
        viewport.transform.SetParent(template.transform, false);
        StretchToParent(viewport.GetComponent<RectTransform>());
        viewport.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.04f);
        viewport.GetComponent<Mask>().showMaskGraphic = false;

        var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        content.transform.SetParent(viewport.transform, false);
        var contentRect = content.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.anchoredPosition = Vector2.zero;
        contentRect.sizeDelta = Vector2.zero;

        var contentLayout = content.GetComponent<VerticalLayoutGroup>();
        contentLayout.childControlWidth = true;
        contentLayout.childControlHeight = true;
        contentLayout.childForceExpandWidth = true;
        contentLayout.childForceExpandHeight = false;

        var fitter = content.GetComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var item = new GameObject("Item", typeof(RectTransform), typeof(Toggle), typeof(Image));
        item.transform.SetParent(content.transform, false);
        var itemRect = item.GetComponent<RectTransform>();
        itemRect.sizeDelta = new Vector2(0f, 46f);
        item.GetComponent<Image>().color = new Color(0.08f, 0.14f, 0.18f, 0.92f);

        var itemLabel = new GameObject("Item Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        itemLabel.transform.SetParent(item.transform, false);
        var labelRect = itemLabel.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(18f, 3f);
        labelRect.offsetMax = new Vector2(-18f, -3f);
        var itemText = itemLabel.GetComponent<TextMeshProUGUI>();
        itemText.alignment = TextAlignmentOptions.MidlineLeft;
        itemText.fontSize = 20f;
        itemText.color = Color.white;
        itemText.raycastTarget = false;

        var scrollRect = template.GetComponent<ScrollRect>();
        scrollRect.viewport = viewport.GetComponent<RectTransform>();
        scrollRect.content = contentRect;
        scrollRect.horizontal = false;
        scrollRect.vertical = true;

        return template;
    }

    private void ResolveContentReferences()
    {
        RemoveLegacyThemeButton();

        var texts = _contentRoot.GetComponentsInChildren<TMP_Text>(true);
        if (texts.Length > 0)
        {
            _titleText = texts[0];
        }

        if (texts.Length > 1)
        {
            _subtitleText = texts[1];
        }

        if (texts.Length > 2)
        {
            _statusText = texts[2];
        }

        if (texts.Length > 3)
        {
            _pointerHintText = texts[3];
        }

        var buttons = _contentRoot.GetComponentsInChildren<Button>(true);
        if (buttons.Length > 0) _captureButton = buttons[0];
        if (buttons.Length > 1) _autoTargetButton = buttons[1];
        if (buttons.Length > 2) _surfaceButton = buttons[2];
        if (buttons.Length > 3) _shellButton = buttons[3];
        if (buttons.Length > 4) _worldStatusButton = buttons[4];

        if (_worldStatusButton == null)
        {
            var roomButtons = _contentRoot.Find("RoomButtons");
            if (roomButtons != null)
            {
                _worldStatusButton = CreateButton(roomButtons, secondaryButtonPrefab, "Object Status");
            }
        }

        _themeTmpDropdown = _contentRoot.GetComponentInChildren<TMP_Dropdown>(true);
        _themeDropdown = _contentRoot.GetComponentInChildren<Dropdown>(true);
        if (_themeTmpDropdown == null && _themeDropdown == null)
        {
            CreateThemeDropdown();
        }
        else
        {
            WireThemeDropdown();
            PopulateThemeDropdown();
        }
    }

    private void WireButtons()
    {
        WireButton(_captureButton, CaptureCurrentTarget);
        WireButton(_autoTargetButton, SetAutoTargetFromGaze);
        WireButton(_surfaceButton, ReapplySurfaces);
        WireButton(_shellButton, ToggleShells);
        WireButton(_worldStatusButton, ToggleObjectStatusCards);
        WireThemeDropdown();
    }

    private static void WireButton(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button == null)
        {
            return;
        }

        button.onClick.RemoveListener(action);
        button.onClick.AddListener(action);
    }

    private void WireThemeDropdown()
    {
        if (_themeTmpDropdown != null)
        {
            _themeTmpDropdown.onValueChanged.RemoveListener(SelectThemeFromDropdown);
            _themeTmpDropdown.onValueChanged.AddListener(SelectThemeFromDropdown);
        }

        if (_themeDropdown != null)
        {
            _themeDropdown.onValueChanged.RemoveListener(SelectThemeFromDropdown);
            _themeDropdown.onValueChanged.AddListener(SelectThemeFromDropdown);
        }
    }

    private void HandleControllerPointer()
    {
        if (!enableControllerPointerInPlay || canvas == null || _contentRoot == null || !Application.isPlaying)
        {
            ClearPointerHover();
            SetPointerLineVisible(false);
            return;
        }

        ResolvePointerRayOrigin();
        if (pointerRayOrigin == null)
        {
            ClearPointerHover();
            SetPointerHint("Pointer: right controller not found");
            SetPointerLineVisible(false);
            return;
        }

        var ray = new Ray(pointerRayOrigin.position, pointerRayOrigin.forward);
        if (!TryGetPanelPoint(ray, out var localPoint, out var hitWorldPoint, out var hitDistance))
        {
            ClearPointerHover();
            SetPointerHint("Point right controller at the panel, press Trigger.");
            UpdatePointerLine(ray.origin, ray.origin + ray.direction * Mathf.Min(pointerMaxDistanceMeters, 1.2f), false);
            return;
        }

        Button hoveredButton = null;
        if (TryFindButtonAtLocalPoint(localPoint, out var button))
        {
            hoveredButton = button;
        }

        Image hoveredDropdownImage = null;
        if (hoveredButton == null && IsThemeDropdownAtLocalPoint(localPoint))
        {
            hoveredDropdownImage = GetDropdownImage();
        }

        SetHoveredButton(hoveredButton);
        SetHoveredDropdown(hoveredDropdownImage);
        UpdatePointerLine(ray.origin, hitWorldPoint, true);

        if (hoveredButton != null)
        {
            SetPointerHint($"Trigger: {GetButtonLabel(hoveredButton)}");
            if (OVRInput.GetDown(pointerSelectButton, pointerController))
            {
                hoveredButton.onClick.Invoke();
            }

            return;
        }

        if (hoveredDropdownImage != null)
        {
            SetPointerHint("Trigger: switch theme");
            if (OVRInput.GetDown(pointerSelectButton, pointerController))
            {
                CycleThemeFromPointer();
            }

            return;
        }

        SetPointerHint($"Panel hit {hitDistance:F1}m | aim at a button");
    }

    private void HandlePanelVisibilityToggleInput()
    {
        if (!enablePanelVisibilityToggleInPlay || panelVisibilityToggleButton == OVRInput.RawButton.None)
        {
            return;
        }

        if (OVRInput.GetDown(panelVisibilityToggleButton))
        {
            TogglePanelVisibility();
        }
    }

    private void ResolvePointerRayOrigin()
    {
        if (pointerRayOrigin != null || Time.unscaledTime < _nextPointerOriginResolveTime)
        {
            return;
        }

        _nextPointerOriginResolveTime = Time.unscaledTime + 1f;

        var candidates = new[]
        {
            "RightHandAnchor",
            "RightControllerAnchor",
            "RightController",
            "RController",
        };

        foreach (var candidate in candidates)
        {
            var candidateObject = GameObject.Find(candidate);
            if (candidateObject != null)
            {
                pointerRayOrigin = candidateObject.transform;
                return;
            }
        }
    }

    private bool TryGetPanelPoint(Ray ray, out Vector2 localPoint, out Vector3 hitWorldPoint, out float hitDistance)
    {
        localPoint = Vector2.zero;
        hitWorldPoint = Vector3.zero;
        hitDistance = 0f;

        var canvasRect = canvas.GetComponent<RectTransform>();
        if (canvasRect == null)
        {
            return false;
        }

        var plane = new Plane(canvas.transform.forward, canvas.transform.position);
        if (!plane.Raycast(ray, out hitDistance) || hitDistance < 0f || hitDistance > pointerMaxDistanceMeters)
        {
            return false;
        }

        hitWorldPoint = ray.GetPoint(hitDistance);
        var local3 = canvasRect.InverseTransformPoint(hitWorldPoint);
        localPoint = new Vector2(local3.x, local3.y);
        var rect = canvasRect.rect;
        rect.xMin -= pointerPanelHitPaddingPixels;
        rect.xMax += pointerPanelHitPaddingPixels;
        rect.yMin -= pointerPanelHitPaddingPixels;
        rect.yMax += pointerPanelHitPaddingPixels;
        return rect.Contains(localPoint);
    }

    private bool TryFindButtonAtLocalPoint(Vector2 canvasLocalPoint, out Button button)
    {
        button = null;
        if (_contentRoot == null)
        {
            return false;
        }

        var buttons = _contentRoot.GetComponentsInChildren<Button>(true);
        foreach (var candidate in buttons)
        {
            if (candidate == null || !candidate.gameObject.activeInHierarchy || !candidate.interactable)
            {
                continue;
            }

            var rect = candidate.GetComponent<RectTransform>();
            if (rect != null && RectContainsCanvasLocalPoint(rect, canvasLocalPoint))
            {
                button = candidate;
                return true;
            }
        }

        return false;
    }

    private bool IsThemeDropdownAtLocalPoint(Vector2 canvasLocalPoint)
    {
        var rect = _themeTmpDropdown != null
            ? _themeTmpDropdown.GetComponent<RectTransform>()
            : _themeDropdown != null
                ? _themeDropdown.GetComponent<RectTransform>()
                : null;
        return rect != null && RectContainsCanvasLocalPoint(rect, canvasLocalPoint);
    }

    private bool RectContainsCanvasLocalPoint(RectTransform target, Vector2 canvasLocalPoint)
    {
        if (target == null || canvas == null)
        {
            return false;
        }

        var canvasRect = canvas.GetComponent<RectTransform>();
        if (canvasRect == null)
        {
            return false;
        }

        var worldPoint = canvasRect.TransformPoint(new Vector3(canvasLocalPoint.x, canvasLocalPoint.y, 0f));
        var local = target.InverseTransformPoint(worldPoint);
        return target.rect.Contains(new Vector2(local.x, local.y));
    }

    private void SetHoveredButton(Button button)
    {
        if (_hoveredButton == button)
        {
            return;
        }

        SetButtonColor(_hoveredButton, buttonNormalColor);
        _hoveredButton = button;
        SetButtonColor(_hoveredButton, buttonHoverColor);
    }

    private void SetHoveredDropdown(Image image)
    {
        if (_hoveredDropdownImage == image)
        {
            return;
        }

        if (_hoveredDropdownImage != null)
        {
            _hoveredDropdownImage.color = dropdownNormalColor;
        }

        _hoveredDropdownImage = image;
        if (_hoveredDropdownImage != null)
        {
            _hoveredDropdownImage.color = dropdownHoverColor;
        }
    }

    private void ClearPointerHover()
    {
        SetHoveredButton(null);
        SetHoveredDropdown(null);
    }

    private void SetButtonColor(Button button, Color color)
    {
        if (button == null || button.targetGraphic == null)
        {
            return;
        }

        button.targetGraphic.color = color;
    }

    private Image GetDropdownImage()
    {
        if (_themeTmpDropdown != null && _themeTmpDropdown.targetGraphic is Image tmpImage)
        {
            return tmpImage;
        }

        if (_themeDropdown != null && _themeDropdown.targetGraphic is Image image)
        {
            return image;
        }

        return null;
    }

    private void CycleThemeFromPointer()
    {
        if (themeIntentController == null)
        {
            return;
        }

        themeIntentController.CycleTheme(1);
        roomStyleCacheService?.Refresh();
        PopulateThemeDropdown();
        UpdatePanel();
    }

    private string GetButtonLabel(Button button)
    {
        if (button == null)
        {
            return "button";
        }

        var text = button.GetComponentInChildren<TMP_Text>(true);
        return text != null && !string.IsNullOrWhiteSpace(text.text) ? text.text.Replace("\n", " ") : button.name;
    }

    private void SetPointerHint(string message)
    {
        if (_pointerHintText != null)
        {
            _pointerHintText.text = message;
        }
    }

    private void UpdatePointerLine(Vector3 start, Vector3 end, bool hit)
    {
        if (!showControllerPointerRay)
        {
            SetPointerLineVisible(false);
            return;
        }

        EnsurePointerLine();
        if (_pointerLine == null)
        {
            return;
        }

        _pointerLine.enabled = true;
        _pointerLine.startColor = hit ? buttonHoverColor : new Color(0.45f, 0.7f, 0.9f, 0.45f);
        _pointerLine.endColor = _pointerLine.startColor;
        _pointerLine.SetPosition(0, start);
        _pointerLine.SetPosition(1, end);
    }

    private void EnsurePointerLine()
    {
        if (_pointerLine != null)
        {
            return;
        }

        var lineObject = new GameObject("SceneShiftDashboardPointerRay", typeof(LineRenderer));
        lineObject.transform.SetParent(transform, false);
        _pointerLine = lineObject.GetComponent<LineRenderer>();
        _pointerLine.useWorldSpace = true;
        _pointerLine.positionCount = 2;
        _pointerLine.startWidth = 0.006f;
        _pointerLine.endWidth = 0.003f;
        _pointerLineMaterial = new Material(Shader.Find("Sprites/Default"));
        _pointerLine.sharedMaterial = _pointerLineMaterial;
    }

    private void SetPointerLineVisible(bool visible)
    {
        if (_pointerLine != null)
        {
            _pointerLine.enabled = visible;
        }
    }

    private void PopulateThemeDropdown()
    {
        if (themeIntentController == null)
        {
            return;
        }

        var labels = new List<string>();
        foreach (var theme in themeIntentController.AvailableThemes)
        {
            labels.Add(theme != null ? theme.DisplayName : "Missing Theme");
        }

        if (labels.Count == 0)
        {
            labels.Add("No Themes");
        }

        _isSyncingThemeDropdown = true;
        if (_themeTmpDropdown != null)
        {
            _themeTmpDropdown.ClearOptions();
            _themeTmpDropdown.AddOptions(labels);
            _themeTmpDropdown.SetValueWithoutNotify(Mathf.Clamp(themeIntentController.ActiveThemeIndex, 0, labels.Count - 1));
            _themeTmpDropdown.RefreshShownValue();
        }

        if (_themeDropdown != null)
        {
            _themeDropdown.ClearOptions();
            _themeDropdown.AddOptions(labels);
            _themeDropdown.SetValueWithoutNotify(Mathf.Clamp(themeIntentController.ActiveThemeIndex, 0, labels.Count - 1));
            _themeDropdown.RefreshShownValue();
        }

        _isSyncingThemeDropdown = false;
    }

    private void UpdatePanel()
    {
        if (_statusText == null)
        {
            return;
        }

        _builder.Clear();
        PopulateThemeDropdown();
        _builder.AppendLine(BuildCaptureLine());
        _builder.AppendLine(BuildThemeLine());
        _builder.AppendLine(BuildCacheLine());
        _builder.AppendLine(BuildQueueLine());
        _builder.AppendLine();
        _builder.AppendLine($"Clean View: {(shellVisibilityToggle != null && shellVisibilityToggle.CleanViewActive ? "active" : "off")}");
        _builder.AppendLine($"Object Status Cards: {(worldStatusOverlay != null && worldStatusOverlay.IsOverlayVisible ? "visible" : "hidden")}");
        _builder.AppendLine("Controls: Capture | Auto Target | Reapply Room | Clean View | Object Status | Left Menu: Panel");

        _statusText.text = _builder.ToString().TrimEnd();
        _latestSummary = $"[SceneShiftUISetDashboard]\nState: visible\n{_statusText.text}";

        if (_titleText != null)
        {
            _titleText.text = "SceneShift Control";
        }

        if (_subtitleText != null)
        {
            _subtitleText.text = "Quest Link / headset runtime panel";
        }
    }

    private string BuildCaptureLine()
    {
        if (captureService == null)
        {
            return "Capture: missing DevicePassthroughCaptureService";
        }

        var target = string.IsNullOrWhiteSpace(captureService.TargetSelectionLabel) ? "none" : captureService.TargetSelectionLabel;
        var score = captureService.HasBestCandidate ? $"{captureService.BestCandidateScore * 100f:F0}%" : "none";
        var objectId = captureService.HasBestCandidate && !string.IsNullOrWhiteSpace(captureService.BestAnchorObjectId)
            ? captureService.BestAnchorObjectId
            : "none";
        var anchor = captureService.HasBestCandidate ? captureService.BestAnchorDisplayName : "none";
        return $"Capture: {captureService.CurrentState} | target={target} | id={objectId} | anchor={anchor} | score={score}";
    }

    private string BuildThemeLine()
    {
        if (themeIntentController == null || themeIntentController.ActiveTheme == null)
        {
            return "Theme: none";
        }

        return $"Theme: {themeIntentController.ActiveTheme.DisplayName}";
    }

    private string BuildCacheLine()
    {
        if (roomStyleCacheService == null || themeIntentController == null || themeIntentController.ActiveTheme == null)
        {
            return "Cache: unavailable";
        }

        return "Cache: " + roomStyleCacheService.GetThemeCacheLine(themeIntentController.ActiveTheme);
    }

    private string BuildQueueLine()
    {
        if (generationQueueStatusService == null)
        {
            return "Queue: missing GenerationQueueStatusService";
        }

        var summary = generationQueueStatusService.LatestSummary;
        if (string.IsNullOrWhiteSpace(summary))
        {
            return "Queue: waiting";
        }

        var lines = summary.Split('\n');
        return lines.Length > 1 ? lines[1].Trim() : summary.Trim();
    }

    private void UpdateHeadLockedPlacement()
    {
        if (canvas == null || headTransform == null)
        {
            return;
        }

        var root = transform;
        root.position = headTransform.TransformPoint(localOffset);
        root.rotation = headTransform.rotation * Quaternion.Euler(localEulerOffset);
        root.localScale = Vector3.one * worldScale;

        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 32050;
        canvas.worldCamera = headTransform.GetComponent<Camera>();

        var rect = canvas.GetComponent<RectTransform>();
        if (rect != null)
        {
            rect.sizeDelta = panelSizePixels;
        }

        NormalizeCanvasRoot();
        NormalizeContentRoot();
    }

    private void SetVisible(bool isVisible)
    {
        if (canvas != null)
        {
            canvas.gameObject.SetActive(isVisible);
        }
    }

    private static void StretchToParent(RectTransform rectTransform)
    {
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;
    }

    private static void DestroyDropdownObject(GameObject target)
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

    private void RemoveLegacyThemeButton()
    {
        if (_contentRoot == null)
        {
            return;
        }

        var legacy = _contentRoot.Find("RoomButtons/NextThemeButton");
        if (legacy == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(legacy.gameObject);
        }
        else
        {
            DestroyImmediate(legacy.gameObject);
        }
    }
}
