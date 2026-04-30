using System.Collections.Generic;
using System.Text;
using Oculus.Interaction.Samples;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
public class SceneShiftUISetDashboard : MonoBehaviour
{
    private const string BackgroundObjectName = "SceneShiftDashboardBackground";
    private const string ContentObjectName = "SceneShiftDashboardContent";
    private const string OfficialBackplateObjectName = "UIBackplate";
    private const string RuntimeButtonLabelName = "SceneShiftButtonLabel";
    private const string RuntimeDropdownCaptionName = "SceneShiftDropdownCaption";
#if UNITY_EDITOR
    private const string MetaUISetPrimaryButtonPrefabPath = "Packages/com.meta.xr.sdk.interaction/Runtime/Sample/Objects/UISet/Prefabs/Button/UnityUIButtonBased/PrimaryButton_IconAndLabel_UnityUIButton.prefab";
    private const string MetaUISetSecondaryButtonPrefabPath = "Packages/com.meta.xr.sdk.interaction/Runtime/Sample/Objects/UISet/Prefabs/Button/UnityUIButtonBased/SecondaryButton_IconAndLabel_UnityUIButton.prefab";
    private const string MetaUISetDestructiveButtonPrefabPath = "Packages/com.meta.xr.sdk.interaction/Runtime/Sample/Objects/UISet/Prefabs/Button/UnityUIButtonBased/DestructiveButton_IconAndLabel_UnityUIButton.prefab";
    private const string MetaUISetDropdownPrefabPath = "Packages/com.meta.xr.sdk.interaction/Runtime/Sample/Objects/UISet/Prefabs/DropDown/DropDown1LineTextOnly.prefab";
#endif

    [Header("References")]
    [SerializeField] private Canvas canvas;
    [SerializeField] private Transform headTransform;
    [SerializeField] private DevicePassthroughCaptureService captureService;
    [SerializeField] private GenerationQueueStatusService generationQueueStatusService;
    [SerializeField] private ThemeIntentController themeIntentController;
    [SerializeField] private RuntimeStyleIntentController runtimeStyleIntentController;
    [SerializeField] private SurfaceOverrideApplier surfaceOverrideApplier;
    [SerializeField] private MRUKShellVisibilityToggle shellVisibilityToggle;
    [SerializeField] private GenerationJobWorldStatusOverlay worldStatusOverlay;
    [SerializeField] private RoomStyleCacheService roomStyleCacheService;
    [SerializeField] private CapturedFurnitureReuseService furnitureReuseService;
    [SerializeField] private GeneratedObjectRotationCorrectionController rotationCorrectionController;

    [Header("Meta UISet Prefabs")]
    [SerializeField] private bool useMetaUISetPrefabsWhenAvailable = true;
    [SerializeField] private bool autoLoadMetaUISetPrefabsInEditor = true;
    [SerializeField, Tooltip("Instantiate official Meta UISet control prefabs when available. Fallback controls are only used when package prefabs are missing.")]
    private bool instantiateMetaUISetControlPrefabs = true;
    [SerializeField] private GameObject primaryButtonPrefab;
    [SerializeField] private GameObject secondaryButtonPrefab;
    [SerializeField] private GameObject destructiveButtonPrefab;
    [SerializeField] private GameObject dropdownPrefab;

    [Header("Runtime Panel")]
    [SerializeField] private bool visibleInPlayMode = true;
    [SerializeField] private bool headLocked = true;
    [SerializeField] private bool createContentIfMissing = true;
    [SerializeField] private bool preferOfficialUISetBackplate = true;
    [SerializeField] private bool disableMetaUISetRuntimeComponents;
    [SerializeField] private bool hideInheritedUISetVisuals = true;
    [SerializeField] private bool useStableRuntimeControlsOnly;
    [SerializeField] private bool useUISetInspiredFallbackSkin = true;
    [SerializeField] private bool preferOfficialCanvasInteractions = true;
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
    private readonly Dictionary<Graphic, Color> _pointerOriginalGraphicColors = new();
    private Image _backgroundImage;
    private RectTransform _contentRoot;
    private TMP_Text _titleText;
    private TMP_Text _subtitleText;
    private TMP_Text _statusText;
    private TMP_Text _captureStatusText;
    private TMP_Text _styleStatusText;
    private TMP_Text _cacheStatusText;
    private TMP_Text _queueStatusText;
    private TMP_Text _reuseStatusText;
    private TMP_Text _rotationStatusText;
    private TMP_Text _cleanViewStatusText;
    private TMP_Text _objectStatusText;
    private TMP_Text _pointerHintText;
    private TMP_Text _themeDropdownCaptionText;
    private DropDownGroup _themeMetaDropdown;
    private TMP_Dropdown _themeTmpDropdown;
    private Dropdown _themeDropdown;
    private Button _captureButton;
    private Button _autoTargetButton;
    private Button _reuseCaptureButton;
    private Button _surfaceButton;
    private Button _shellButton;
    private Button _worldStatusButton;
    private Button _rotateSelectedButton;
    private Button _hoveredButton;
    private Image _hoveredDropdownImage;
    private Sprite _roundedBoxSprite;
    private Texture2D _roundedBoxTexture;
    private bool _triedAutoLoadUISetPrefabs;
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
        TryAutoLoadMetaUISetPrefabs();
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
        TryAutoLoadMetaUISetPrefabs();
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
            SetVisible(false);
            return;
        }

        ResolveReferences();
        TryAutoLoadMetaUISetPrefabs();
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

        if (ShouldUseControllerPointerFallback())
        {
            HandleControllerPointer();
        }
        else
        {
            ClearPointerHover();
            SetPointerHint("Use Interaction SDK ray / poke / hand UI.");
        }

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

    public void RegenerateFurnitureFromCaptures()
    {
        ResolveReferences();
        furnitureReuseService?.RegenerateCurrentRoomForCurrentTheme();
        generationQueueStatusService?.Refresh();
        roomStyleCacheService?.Refresh();
        UpdatePanel();
    }

    public void CycleTheme()
    {
        ResolveReferences();
        if (runtimeStyleIntentController != null && runtimeStyleIntentController.StyleOptionCount > 0)
        {
            runtimeStyleIntentController.CycleStyle(1);
        }
        else
        {
            themeIntentController?.CycleTheme(1);
        }

        roomStyleCacheService?.Refresh();
        UpdatePanel();
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

    public void RotateSelectedGeneratedObject90()
    {
        ResolveReferences();
        if (rotationCorrectionController == null)
        {
            return;
        }

        rotationCorrectionController.RefreshSelectionFromContext();
        rotationCorrectionController.RotateSelectedClockwise90();
        UpdatePanel();
    }

    public void TogglePanelVisibility()
    {
        _panelVisibleRuntime = !_panelVisibleRuntime;
        if (!_panelVisibleRuntime)
        {
            ClearPointerHover();
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
        if (runtimeStyleIntentController == null || !runtimeStyleIntentController.SelectStyleByIndex(index))
        {
            themeIntentController?.SelectThemeByIndex(index);
        }

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

        if (runtimeStyleIntentController == null)
        {
            runtimeStyleIntentController = FindAnyObjectByType<RuntimeStyleIntentController>();
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

        if (furnitureReuseService == null)
        {
            furnitureReuseService = FindAnyObjectByType<CapturedFurnitureReuseService>();
        }

        if (furnitureReuseService == null && Application.isPlaying)
        {
            furnitureReuseService = gameObject.AddComponent<CapturedFurnitureReuseService>();
        }

        if (rotationCorrectionController == null)
        {
            rotationCorrectionController = FindAnyObjectByType<GeneratedObjectRotationCorrectionController>();
        }

        if (rotationCorrectionController == null && Application.isPlaying)
        {
            rotationCorrectionController = gameObject.AddComponent<GeneratedObjectRotationCorrectionController>();
        }
    }

    private void TryAutoLoadMetaUISetPrefabs()
    {
        if (_triedAutoLoadUISetPrefabs || !useMetaUISetPrefabsWhenAvailable || !autoLoadMetaUISetPrefabsInEditor)
        {
            return;
        }

        _triedAutoLoadUISetPrefabs = true;
#if UNITY_EDITOR
        primaryButtonPrefab ??= AssetDatabase.LoadAssetAtPath<GameObject>(MetaUISetPrimaryButtonPrefabPath);
        secondaryButtonPrefab ??= AssetDatabase.LoadAssetAtPath<GameObject>(MetaUISetSecondaryButtonPrefabPath);
        destructiveButtonPrefab ??= AssetDatabase.LoadAssetAtPath<GameObject>(MetaUISetDestructiveButtonPrefabPath);
        dropdownPrefab ??= AssetDatabase.LoadAssetAtPath<GameObject>(MetaUISetDropdownPrefabPath);
#endif
    }

    private bool ShouldInstantiateMetaUISetPrefab(GameObject prefab)
    {
        if (!instantiateMetaUISetControlPrefabs)
        {
            return false;
        }

        if (useStableRuntimeControlsOnly && !useMetaUISetPrefabsWhenAvailable)
        {
            return false;
        }

        return useMetaUISetPrefabsWhenAvailable && prefab != null;
    }

    private bool ShouldUseControllerPointerFallback()
    {
        return enableControllerPointerInPlay;
    }

    private bool HasOfficialCanvasInteractionComponents()
    {
        foreach (var behaviour in GetComponentsInChildren<Behaviour>(true))
        {
            if (behaviour == null || !behaviour.enabled || Application.isPlaying && !behaviour.gameObject.activeInHierarchy)
            {
                continue;
            }

            var typeName = behaviour.GetType().FullName;
            if (typeName == "Oculus.Interaction.PointableCanvas" ||
                typeName == "Oculus.Interaction.PokeInteractable" ||
                typeName == "Oculus.Interaction.RayInteractable" ||
                typeName == "Oculus.Interaction.PointableCanvasUnityEventWrapper")
            {
                return true;
            }
        }

        return false;
    }

    private bool HasOfficialRayCanvasInteractionComponents()
    {
        foreach (var behaviour in GetComponentsInChildren<Behaviour>(true))
        {
            if (behaviour == null || !behaviour.enabled || Application.isPlaying && !behaviour.gameObject.activeInHierarchy)
            {
                continue;
            }

            if (behaviour.GetType().FullName == "Oculus.Interaction.RayInteractable")
            {
                return true;
            }
        }

        return false;
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
        layout.padding = new RectOffset(24, 24, 24, 24);
        layout.spacing = 14f;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        var fitter = _contentRoot.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

        CreateHeaderSection();
        CreateThemeDropdown();
        CreateStatusSection();
        CreateButtonSection("Capture", true);
        CreateButtonSection("Room", false);
        _pointerHintText = CreateText(_contentRoot, "PointerHint", "Point right controller at the panel, press Trigger.", 15f, FontStyles.Normal, new Color(0.72f, 0.86f, 0.96f, 1f));
        _pointerHintText.alignment = TextAlignmentOptions.Center;
        _pointerHintText.textWrappingMode = TextWrappingModes.NoWrap;
        _pointerHintText.overflowMode = TextOverflowModes.Ellipsis;
        SetLayoutSize(_pointerHintText.gameObject, 0f, 26f);
        NormalizeContentRoot();
        HideInheritedUISetVisuals();
    }

    private void EnsureBackground()
    {
        if (canvas == null)
        {
            return;
        }

        var officialBackplate = preferOfficialUISetBackplate ? canvas.transform.Find(OfficialBackplateObjectName) : null;
        if (officialBackplate != null)
        {
            var fallbackBackground = canvas.transform.Find(BackgroundObjectName);
            if (fallbackBackground != null)
            {
                fallbackBackground.gameObject.SetActive(false);
            }

            officialBackplate.gameObject.SetActive(true);
            var officialRect = officialBackplate.GetComponent<RectTransform>();
            if (officialRect != null)
            {
                StretchToParent(officialRect);
            }

            _backgroundImage = officialBackplate.GetComponent<Image>() ?? officialBackplate.GetComponentInChildren<Image>(true);
            if (_backgroundImage != null)
            {
                _backgroundImage.raycastTarget = false;
            }

            officialBackplate.SetAsFirstSibling();
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

        _backgroundImage.color = new Color(0.08f, 0.13f, 0.16f, 0.96f);
        ApplyUISetInspiredImage(_backgroundImage, true);
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

        var hasOfficialBackplate = preferOfficialUISetBackplate && canvas.transform.Find(OfficialBackplateObjectName) != null;
        for (var i = 0; i < canvas.transform.childCount; i++)
        {
            var child = canvas.transform.GetChild(i);
            var keep = !hasOfficialBackplate && child.name == BackgroundObjectName ||
                hasOfficialBackplate && child.name == OfficialBackplateObjectName ||
                child.name == ContentObjectName ||
                IsCanvasInteractionChild(child);
            if (child.gameObject.activeSelf != keep)
            {
                child.gameObject.SetActive(keep);
            }
        }
    }

    private static bool IsCanvasInteractionChild(Transform child)
    {
        if (child == null)
        {
            return false;
        }

        if (child.name.Contains("RayCanvasInteraction") ||
            child.name.Contains("RayInteraction") ||
            child.name.Contains("PokeInteraction"))
        {
            return true;
        }

        foreach (var behaviour in child.GetComponentsInChildren<Behaviour>(true))
        {
            if (behaviour == null)
            {
                continue;
            }

            var typeName = behaviour.GetType().FullName;
            if (typeName == "Oculus.Interaction.PointableCanvas" ||
                typeName == "Oculus.Interaction.PokeInteractable" ||
                typeName == "Oculus.Interaction.RayInteractable" ||
                typeName == "Oculus.Interaction.PointableCanvasUnityEventWrapper")
            {
                return true;
            }
        }

        return false;
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

        if (preferOfficialCanvasInteractions && HasOfficialCanvasInteractionComponents())
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

    private void CreateHeaderSection()
    {
        var headerObject = new GameObject("Header", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
        headerObject.transform.SetParent(_contentRoot, false);

        var header = headerObject.GetComponent<VerticalLayoutGroup>();
        header.spacing = 2f;
        header.childAlignment = TextAnchor.UpperLeft;
        header.childControlWidth = true;
        header.childControlHeight = true;
        header.childForceExpandWidth = true;
        header.childForceExpandHeight = false;

        var layout = headerObject.GetComponent<LayoutElement>();
        layout.minHeight = 66f;
        layout.preferredHeight = 66f;

        _titleText = CreateText(headerObject.transform, "Title", "SceneShift Control", 34f, FontStyles.Bold, new Color(0.95f, 0.98f, 1f, 1f));
        _titleText.textWrappingMode = TextWrappingModes.NoWrap;
        _titleText.overflowMode = TextOverflowModes.Ellipsis;
        SetLayoutSize(_titleText.gameObject, 0f, 38f);

        _subtitleText = CreateText(headerObject.transform, "Subtitle", "Quest Link / headset runtime panel", 16f, FontStyles.Normal, new Color(0.65f, 0.78f, 0.9f, 1f));
        _subtitleText.textWrappingMode = TextWrappingModes.NoWrap;
        _subtitleText.overflowMode = TextOverflowModes.Ellipsis;
        SetLayoutSize(_subtitleText.gameObject, 0f, 24f);
    }

    private void CreateStatusSection()
    {
        var cardObject = new GameObject("StatusCard", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(LayoutElement));
        cardObject.transform.SetParent(_contentRoot, false);

        var image = cardObject.GetComponent<Image>();
        image.color = new Color(0.035f, 0.075f, 0.105f, 0.88f);
        ApplyUISetInspiredImage(image, true);
        image.raycastTarget = false;

        var layout = cardObject.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(18, 18, 14, 14);
        layout.spacing = 6f;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        var cardLayout = cardObject.GetComponent<LayoutElement>();
        cardLayout.minHeight = 278f;
        cardLayout.preferredHeight = 278f;

        _captureStatusText = CreateStatusRow(cardObject.transform, "Capture");
        _styleStatusText = CreateStatusRow(cardObject.transform, "Style");
        _cacheStatusText = CreateStatusRow(cardObject.transform, "Cache");
        _queueStatusText = CreateStatusRow(cardObject.transform, "Jobs");
        _reuseStatusText = CreateStatusRow(cardObject.transform, "Reuse");
        _rotationStatusText = CreateStatusRow(cardObject.transform, "Rotate");
        _cleanViewStatusText = CreateStatusRow(cardObject.transform, "Clean");
        _objectStatusText = CreateStatusRow(cardObject.transform, "Cards");
        _statusText = _captureStatusText;
    }

    private TMP_Text CreateStatusRow(Transform parent, string labelText)
    {
        var rowObject = new GameObject($"{labelText}StatusRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        rowObject.transform.SetParent(parent, false);

        var row = rowObject.GetComponent<HorizontalLayoutGroup>();
        row.spacing = 10f;
        row.childAlignment = TextAnchor.MiddleLeft;
        row.childControlWidth = true;
        row.childControlHeight = true;
        row.childForceExpandWidth = false;
        row.childForceExpandHeight = false;

        var rowLayout = rowObject.GetComponent<LayoutElement>();
        rowLayout.minHeight = 25f;
        rowLayout.preferredHeight = 25f;

        var label = CreateText(rowObject.transform, $"{labelText}Label", labelText, 14.5f, FontStyles.Bold, new Color(0.68f, 0.8f, 0.9f, 1f));
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Ellipsis;
        SetLayoutSize(label.gameObject, 74f, 25f);

        var value = CreateText(rowObject.transform, $"{labelText}Value", "waiting", 14.5f, FontStyles.Normal, Color.white);
        value.alignment = TextAlignmentOptions.MidlineLeft;
        value.textWrappingMode = TextWrappingModes.NoWrap;
        value.overflowMode = TextOverflowModes.Ellipsis;
        var valueLayout = value.GetComponent<LayoutElement>();
        valueLayout.minWidth = 0f;
        valueLayout.preferredWidth = 0f;
        valueLayout.flexibleWidth = 1f;
        valueLayout.minHeight = 25f;
        valueLayout.preferredHeight = 25f;
        return value;
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
        rowLayout.minHeight = 56f;
        rowLayout.preferredHeight = 56f;

        var label = CreateText(rowObject.transform, "StyleLabel", "Style", 20f, FontStyles.Bold, new Color(0.72f, 0.84f, 0.95f, 1f));
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        var labelLayout = label.GetComponent<LayoutElement>();
        labelLayout.minWidth = 92f;
        labelLayout.preferredWidth = 92f;
        labelLayout.minHeight = 52f;
        labelLayout.preferredHeight = 52f;

        var dropdownObject = CreateDropdownObject(rowObject.transform);
        dropdownObject.name = "ThemeDropdown";

        var layout = dropdownObject.GetComponent<LayoutElement>();
        if (layout == null)
        {
            layout = dropdownObject.AddComponent<LayoutElement>();
        }

        layout.minWidth = 500f;
        layout.preferredWidth = 560f;
        layout.flexibleWidth = 1f;
        layout.minHeight = 52f;
        layout.preferredHeight = 52f;

        _themeTmpDropdown = dropdownObject.GetComponentInChildren<TMP_Dropdown>(true);
        _themeDropdown = dropdownObject.GetComponentInChildren<Dropdown>(true);
        _themeMetaDropdown = dropdownObject.GetComponentInChildren<DropDownGroup>(true);
        _themeDropdownCaptionText = dropdownObject.transform.Find(RuntimeDropdownCaptionName)?.GetComponent<TMP_Text>();
        if (_themeTmpDropdown == null && _themeDropdown == null && _themeMetaDropdown == null)
        {
            DestroyDropdownObject(dropdownObject);
            dropdownObject = CreateFallbackTmpDropdown(rowObject.transform);
            dropdownObject.name = "ThemeDropdown";
            _themeTmpDropdown = dropdownObject.GetComponent<TMP_Dropdown>();
            _themeMetaDropdown = null;
            _themeDropdownCaptionText = null;
        }

        WireThemeDropdown();
        PopulateThemeDropdown();
    }

    private GameObject CreateDropdownObject(Transform parent)
    {
        if (!ShouldInstantiateMetaUISetPrefab(dropdownPrefab))
        {
            return CreateFallbackTmpDropdown(parent);
        }

        var dropdownObject = Instantiate(dropdownPrefab, parent);
        dropdownObject.transform.localScale = Vector3.one;
        NormalizeDropdownObject(dropdownObject);
        return dropdownObject;
    }

    private TMP_Text CreateText(string name, string text, float fontSize, FontStyles fontStyle, Color color)
    {
        return CreateText(_contentRoot, name, text, fontSize, fontStyle, color);
    }

    private TMP_Text CreateText(Transform parent, string name, string text, float fontSize, FontStyles fontStyle, Color color)
    {
        var textObject = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
        textObject.transform.SetParent(parent != null ? parent : _contentRoot, false);

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
        layout.minHeight = 56f;
        layout.preferredHeight = 56f;

        if (captureSection)
        {
            _captureButton = CreateButton(rowObject.transform, primaryButtonPrefab, "Capture");
            _autoTargetButton = CreateButton(rowObject.transform, secondaryButtonPrefab, "Auto Target");
            _reuseCaptureButton = CreateButton(rowObject.transform, secondaryButtonPrefab, "Reuse Captures");
        }
        else
        {
            _surfaceButton = CreateButton(rowObject.transform, secondaryButtonPrefab, "Reapply Room");
            _shellButton = CreateButton(rowObject.transform, destructiveButtonPrefab != null ? destructiveButtonPrefab : secondaryButtonPrefab, "Clean View");
            _worldStatusButton = CreateButton(rowObject.transform, secondaryButtonPrefab, "Object Status");
            _rotateSelectedButton = CreateButton(rowObject.transform, secondaryButtonPrefab, "Rotate 90");
        }
    }

    private Button CreateButton(Transform parent, GameObject prefab, string label)
    {
        var buttonObject = ShouldInstantiateMetaUISetPrefab(prefab)
            ? Instantiate(prefab, parent)
            : CreateFallbackButton(parent);

        buttonObject.name = label.Replace(" ", string.Empty) + "Button";
        buttonObject.transform.localScale = Vector3.one;
        NormalizeButtonObject(buttonObject, label);

        var rect = buttonObject.GetComponent<RectTransform>();
        if (rect != null)
        {
            rect.sizeDelta = new Vector2(236f, 52f);
        }

        var layout = buttonObject.GetComponent<LayoutElement>();
        if (layout == null)
        {
            layout = buttonObject.AddComponent<LayoutElement>();
        }

        layout.minWidth = 0f;
        layout.preferredWidth = 236f;
        layout.flexibleWidth = 1f;
        layout.minHeight = 52f;
        layout.preferredHeight = 52f;

        return buttonObject.GetComponentInChildren<Button>(true);
    }

    private void NormalizeButtonObject(GameObject buttonObject, string label)
    {
        if (buttonObject == null)
        {
            return;
        }

        var button = buttonObject.GetComponentInChildren<Button>(true);
        var backgroundImage = FindNamedComponent<Image>(buttonObject.transform, "Background")
            ?? buttonObject.GetComponentInChildren<Image>(true);
        if (button != null && button.targetGraphic == null && backgroundImage != null)
        {
            button.targetGraphic = backgroundImage;
        }

        if (backgroundImage != null && backgroundImage.sprite == null)
        {
            backgroundImage.color = buttonNormalColor;
            ApplyUISetInspiredImage(backgroundImage, false);
        }

        HideNamedChild(buttonObject.transform, "Icon");
        HideNamedChild(buttonObject.transform, "Gap");

        var labelText = FindNamedComponent<TMP_Text>(buttonObject.transform, "Label")
            ?? FindNamedComponent<TMP_Text>(buttonObject.transform, "Text")
            ?? buttonObject.GetComponentInChildren<TMP_Text>(true);
        if (labelText != null)
        {
            labelText.text = label;
            labelText.alignment = TextAlignmentOptions.Center;
            labelText.color = Color.white;
            labelText.fontSize = Mathf.Clamp(Mathf.Max(18f, labelText.fontSize), 18f, 22f);
            labelText.textWrappingMode = TextWrappingModes.NoWrap;
            labelText.overflowMode = TextOverflowModes.Ellipsis;
            labelText.raycastTarget = false;
        }

        foreach (var text in buttonObject.GetComponentsInChildren<TMP_Text>(true))
        {
            if (text == null || text == labelText)
            {
                continue;
            }

            if (text.name == "Subtitle" || text.name == "Icon")
            {
                text.gameObject.SetActive(false);
            }
        }

        var runtimeLabel = EnsureRuntimeOverlayText(
            buttonObject.transform,
            RuntimeButtonLabelName,
            TextAlignmentOptions.Center,
            new Vector2(12f, 0f),
            new Vector2(-12f, 0f),
            17f);
        runtimeLabel.text = label;
        runtimeLabel.color = GetReadableTextColor(backgroundImage, Color.white);
        runtimeLabel.fontStyle = FontStyles.Bold;
        runtimeLabel.transform.SetAsLastSibling();
    }

    private void NormalizeDropdownObject(GameObject dropdownObject)
    {
        if (dropdownObject == null)
        {
            return;
        }

        var image = dropdownObject.GetComponentInChildren<Image>(true);
        if (image != null && image.sprite == null)
        {
            image.color = dropdownNormalColor;
            ApplyUISetInspiredImage(image, false);
        }

        foreach (var text in dropdownObject.GetComponentsInChildren<TMP_Text>(true))
        {
            if (text == null)
            {
                continue;
            }

            text.fontSize = Mathf.Clamp(Mathf.Max(18f, text.fontSize), 18f, 22f);
            text.color = Color.white;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
        }

        _themeDropdownCaptionText = EnsureRuntimeOverlayText(
            dropdownObject.transform,
            RuntimeDropdownCaptionName,
            TextAlignmentOptions.MidlineLeft,
            new Vector2(18f, 0f),
            new Vector2(-42f, 0f),
            17f);
        _themeDropdownCaptionText.fontStyle = FontStyles.Bold;
        _themeDropdownCaptionText.color = GetReadableTextColor(image, Color.white);
        _themeDropdownCaptionText.transform.SetAsLastSibling();
    }

    private static void HideNamedChild(Transform root, string childName)
    {
        if (root == null || string.IsNullOrWhiteSpace(childName))
        {
            return;
        }

        foreach (var child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child != null && child.name == childName)
            {
                child.gameObject.SetActive(false);
            }
        }
    }

    private static TMP_Text EnsureRuntimeOverlayText(
        Transform parent,
        string objectName,
        TextAlignmentOptions alignment,
        Vector2 offsetMin,
        Vector2 offsetMax,
        float fontSize)
    {
        var existing = parent.Find(objectName);
        GameObject textObject;
        if (existing != null)
        {
            textObject = existing.gameObject;
        }
        else
        {
            textObject = new GameObject(objectName, typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
            textObject.transform.SetParent(parent, false);
        }

        textObject.SetActive(true);
        var rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;

        var layout = textObject.GetComponent<LayoutElement>();
        if (layout == null)
        {
            layout = textObject.AddComponent<LayoutElement>();
        }

        layout.ignoreLayout = true;

        var text = textObject.GetComponent<TextMeshProUGUI>();
        text.alignment = alignment;
        text.fontSize = fontSize;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.raycastTarget = false;
        return text;
    }

    private static Color GetReadableTextColor(Image backgroundImage, Color fallback)
    {
        if (backgroundImage == null)
        {
            return fallback;
        }

        var color = backgroundImage.color;
        var luminance = 0.2126f * color.r + 0.7152f * color.g + 0.0722f * color.b;
        return luminance > 0.56f ? new Color(0.05f, 0.075f, 0.09f, 1f) : Color.white;
    }

    private static T FindNamedComponent<T>(Transform root, string componentObjectName) where T : Component
    {
        if (root == null || string.IsNullOrWhiteSpace(componentObjectName))
        {
            return null;
        }

        foreach (var component in root.GetComponentsInChildren<T>(true))
        {
            if (component != null && component.name == componentObjectName)
            {
                return component;
            }
        }

        return null;
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
        ApplyUISetInspiredImage(image, false);

        var labelObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelObject.transform.SetParent(buttonObject.transform, false);
        var labelRect = labelObject.GetComponent<RectTransform>();
        StretchToParent(labelRect);
        labelRect.offsetMin = new Vector2(12f, 0f);
        labelRect.offsetMax = new Vector2(-12f, 0f);

        var label = labelObject.GetComponent<TextMeshProUGUI>();
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
        label.fontSize = 19f;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.raycastTarget = false;
        return buttonObject;
    }

    private GameObject CreateFallbackTmpDropdown(Transform parent)
    {
        var root = new GameObject("FallbackThemeDropdown", typeof(RectTransform), typeof(Image), typeof(TMP_Dropdown));
        root.transform.SetParent(parent, false);

        var rect = root.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(560f, 52f);

        var image = root.GetComponent<Image>();
        image.color = dropdownNormalColor;
        ApplyUISetInspiredImage(image, false);

        var captionObject = new GameObject("Caption", typeof(RectTransform), typeof(TextMeshProUGUI));
        captionObject.transform.SetParent(root.transform, false);
        var captionRect = captionObject.GetComponent<RectTransform>();
        captionRect.anchorMin = Vector2.zero;
        captionRect.anchorMax = Vector2.one;
        captionRect.offsetMin = new Vector2(18f, 3f);
        captionRect.offsetMax = new Vector2(-50f, -4f);
        var captionText = captionObject.GetComponent<TextMeshProUGUI>();
        captionText.alignment = TextAlignmentOptions.MidlineLeft;
        captionText.fontSize = 19f;
        captionText.color = Color.white;
        captionText.textWrappingMode = TextWrappingModes.NoWrap;
        captionText.overflowMode = TextOverflowModes.Ellipsis;
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

        var templateImage = template.GetComponent<Image>();
        templateImage.color = new Color(0.055f, 0.08f, 0.105f, 0.98f);
        ApplyUISetInspiredImage(templateImage, true);

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
        var itemImage = item.GetComponent<Image>();
        itemImage.color = new Color(0.08f, 0.14f, 0.18f, 0.92f);
        ApplyUISetInspiredImage(itemImage, false);

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
        itemText.textWrappingMode = TextWrappingModes.NoWrap;
        itemText.overflowMode = TextOverflowModes.Ellipsis;
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
        _captureButton = FindContentButton("CaptureButtons/CaptureButton") ?? (buttons.Length > 0 ? buttons[0] : null);
        _autoTargetButton = FindContentButton("CaptureButtons/AutoTargetButton") ?? (buttons.Length > 1 ? buttons[1] : null);
        _reuseCaptureButton = FindContentButton("CaptureButtons/ReuseCapturesButton");
        _surfaceButton = FindContentButton("RoomButtons/ReapplyRoomButton");
        _shellButton = FindContentButton("RoomButtons/CleanViewButton");
        _worldStatusButton = FindContentButton("RoomButtons/ObjectStatusButton");
        _rotateSelectedButton = FindContentButton("RoomButtons/Rotate90Button");

        if (_reuseCaptureButton == null)
        {
            var captureButtons = _contentRoot.Find("CaptureButtons");
            if (captureButtons != null)
            {
                _reuseCaptureButton = CreateButton(captureButtons, secondaryButtonPrefab, "Reuse Captures");
            }
        }

        if (_worldStatusButton == null)
        {
            var roomButtons = _contentRoot.Find("RoomButtons");
            if (roomButtons != null)
            {
                _worldStatusButton = CreateButton(roomButtons, secondaryButtonPrefab, "Object Status");
            }
        }

        if (_rotateSelectedButton == null)
        {
            var roomButtons = _contentRoot.Find("RoomButtons");
            if (roomButtons != null)
            {
                _rotateSelectedButton = CreateButton(roomButtons, secondaryButtonPrefab, "Rotate 90");
            }
        }

        _themeTmpDropdown = _contentRoot.GetComponentInChildren<TMP_Dropdown>(true);
        _themeDropdown = _contentRoot.GetComponentInChildren<Dropdown>(true);
        _themeMetaDropdown = _contentRoot.GetComponentInChildren<DropDownGroup>(true);
        _themeDropdownCaptionText = _contentRoot.Find($"ThemeDropdown/{RuntimeDropdownCaptionName}")?.GetComponent<TMP_Text>();
        if (_themeTmpDropdown == null && _themeDropdown == null && _themeMetaDropdown == null)
        {
            CreateThemeDropdown();
        }
        else
        {
            WireThemeDropdown();
            PopulateThemeDropdown();
        }
    }

    private Button FindContentButton(string relativePath)
    {
        if (_contentRoot == null || string.IsNullOrWhiteSpace(relativePath))
        {
            return null;
        }

        var target = _contentRoot.Find(relativePath);
        return target != null ? target.GetComponentInChildren<Button>(true) : null;
    }

    private void WireButtons()
    {
        WireButton(_captureButton, CaptureCurrentTarget);
        WireButton(_autoTargetButton, SetAutoTargetFromGaze);
        WireButton(_reuseCaptureButton, RegenerateFurnitureFromCaptures);
        WireButton(_surfaceButton, ReapplySurfaces);
        WireButton(_shellButton, ToggleShells);
        WireButton(_worldStatusButton, ToggleObjectStatusCards);
        WireButton(_rotateSelectedButton, RotateSelectedGeneratedObject90);
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

        if (_themeMetaDropdown != null)
        {
            _themeMetaDropdown.WhenSelectionChanged ??= new UnityEngine.Events.UnityEvent<int>();
            _themeMetaDropdown.WhenSelectionChanged.RemoveListener(SelectThemeFromDropdown);
            _themeMetaDropdown.WhenSelectionChanged.AddListener(SelectThemeFromDropdown);
        }
    }

    private void HandleControllerPointer()
    {
        if (!enableControllerPointerInPlay || canvas == null || _contentRoot == null || !Application.isPlaying)
        {
            ClearPointerHover();
            return;
        }

        ResolvePointerRayOrigin();
        if (pointerRayOrigin == null)
        {
            ClearPointerHover();
            SetPointerHint("Pointer: right controller not found");
            return;
        }

        var ray = new Ray(pointerRayOrigin.position, pointerRayOrigin.forward);
        if (!TryGetPanelPoint(ray, out var localPoint, out var hitWorldPoint, out var hitDistance))
        {
            ClearPointerHover();
            SetPointerHint("Point right controller at the panel, press Trigger.");
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

        var officialRayInteractionActive = preferOfficialCanvasInteractions && HasOfficialRayCanvasInteractionComponents();
        if (hoveredButton != null)
        {
            SetPointerHint($"Trigger: {GetButtonLabel(hoveredButton)}");
            if (!officialRayInteractionActive && OVRInput.GetDown(pointerSelectButton, pointerController))
            {
                hoveredButton.onClick.Invoke();
            }

            return;
        }

        if (hoveredDropdownImage != null)
        {
            SetPointerHint("Trigger: switch style");
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
            "RightControllerAnchor",
            "RightControllerInHandAnchor",
            "RightController",
            "RController",
            "RightHandAnchor",
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
                : _themeMetaDropdown != null
                    ? _themeMetaDropdown.GetComponent<RectTransform>()
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

        RestoreGraphicColor(_hoveredButton != null ? _hoveredButton.targetGraphic : null);
        _hoveredButton = button;
        ApplyHoverColor(_hoveredButton != null ? _hoveredButton.targetGraphic : null, buttonHoverColor);
    }

    private void SetHoveredDropdown(Image image)
    {
        if (_hoveredDropdownImage == image)
        {
            return;
        }

        if (_hoveredDropdownImage != null)
        {
            RestoreGraphicColor(_hoveredDropdownImage);
        }

        _hoveredDropdownImage = image;
        if (_hoveredDropdownImage != null)
        {
            ApplyHoverColor(_hoveredDropdownImage, dropdownHoverColor);
        }
    }

    private void ClearPointerHover()
    {
        SetHoveredButton(null);
        SetHoveredDropdown(null);
    }

    private void ApplyHoverColor(Graphic graphic, Color color)
    {
        if (graphic == null)
        {
            return;
        }

        if (!_pointerOriginalGraphicColors.ContainsKey(graphic))
        {
            _pointerOriginalGraphicColors[graphic] = graphic.color;
        }

        graphic.color = color;
    }

    private void RestoreGraphicColor(Graphic graphic)
    {
        if (graphic == null)
        {
            return;
        }

        if (_pointerOriginalGraphicColors.TryGetValue(graphic, out var originalColor))
        {
            graphic.color = originalColor;
            _pointerOriginalGraphicColors.Remove(graphic);
        }
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

        if (_themeMetaDropdown != null)
        {
            return FindNamedComponent<Image>(_themeMetaDropdown.transform, "Background")
                ?? _themeMetaDropdown.GetComponentInChildren<Image>(true);
        }

        return null;
    }

    private void CycleThemeFromPointer()
    {
        if (runtimeStyleIntentController != null && runtimeStyleIntentController.StyleOptionCount > 0)
        {
            runtimeStyleIntentController.CycleStyle(1);
        }
        else if (themeIntentController != null)
        {
            themeIntentController.CycleTheme(1);
        }
        else
        {
            return;
        }

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

    private void PopulateThemeDropdown()
    {
        var labels = new List<string>();
        var selectedIndex = 0;

        if (runtimeStyleIntentController != null && runtimeStyleIntentController.StyleOptionCount > 0)
        {
            labels.AddRange(runtimeStyleIntentController.GetStyleOptionLabels());
            selectedIndex = runtimeStyleIntentController.ActiveStyleIndex;
        }
        else if (themeIntentController != null)
        {
            foreach (var theme in themeIntentController.AvailableThemes)
            {
                if (theme == null)
                {
                    labels.Add("Missing Theme");
                    continue;
                }

                labels.Add(theme.DisplayName);
            }

            selectedIndex = themeIntentController.ActiveThemeIndex;
        }

        if (labels.Count == 0)
        {
            labels.Add("No Styles");
        }

        _isSyncingThemeDropdown = true;
        if (_themeTmpDropdown != null)
        {
            _themeTmpDropdown.ClearOptions();
            _themeTmpDropdown.AddOptions(labels);
            _themeTmpDropdown.SetValueWithoutNotify(Mathf.Clamp(selectedIndex, 0, labels.Count - 1));
            _themeTmpDropdown.RefreshShownValue();
        }

        if (_themeDropdown != null)
        {
            _themeDropdown.ClearOptions();
            _themeDropdown.AddOptions(labels);
            _themeDropdown.SetValueWithoutNotify(Mathf.Clamp(selectedIndex, 0, labels.Count - 1));
            _themeDropdown.RefreshShownValue();
        }

        var clampedIndex = Mathf.Clamp(selectedIndex, 0, labels.Count - 1);
        SetDropdownCaption(labels[clampedIndex]);
        PopulateMetaUISetDropdown(labels, selectedIndex);

        _isSyncingThemeDropdown = false;
    }

    private void SetDropdownCaption(string label)
    {
        if (_themeDropdownCaptionText != null)
        {
            _themeDropdownCaptionText.text = label;
        }
    }

    private void PopulateMetaUISetDropdown(IReadOnlyList<string> labels, int selectedIndex)
    {
        if (_themeMetaDropdown == null || labels == null || labels.Count == 0)
        {
            return;
        }

        var toggleGroup = _themeMetaDropdown.GetComponentInChildren<ToggleGroup>(true);
        var toggles = toggleGroup != null
            ? toggleGroup.GetComponentsInChildren<Toggle>(true)
            : _themeMetaDropdown.GetComponentsInChildren<Toggle>(true);
        if (toggles == null || toggles.Length == 0)
        {
            return;
        }

        var clampedIndex = Mathf.Clamp(selectedIndex, 0, labels.Count - 1);
        var optionIndex = 0;
        foreach (var toggle in toggles)
        {
            if (toggle == null || toggleGroup != null && toggle.group != toggleGroup && !toggle.transform.IsChildOf(toggleGroup.transform))
            {
                continue;
            }

            var active = optionIndex < labels.Count;
            toggle.gameObject.SetActive(active);
            if (active)
            {
                SetDropdownToggleText(toggle.transform, labels[optionIndex]);
                toggle.SetIsOnWithoutNotify(optionIndex == clampedIndex);
            }

            optionIndex++;
        }

        if (_themeMetaDropdown.Title != null)
        {
            _themeMetaDropdown.Title.text = labels[clampedIndex];
        }

        if (_themeMetaDropdown.Subtitle != null)
        {
            _themeMetaDropdown.Subtitle.gameObject.SetActive(false);
        }
    }

    private static void SetDropdownToggleText(Transform toggleRoot, string label)
    {
        if (toggleRoot == null)
        {
            return;
        }

        TMP_Text title = null;
        foreach (var text in toggleRoot.GetComponentsInChildren<TMP_Text>(true))
        {
            if (text == null)
            {
                continue;
            }

            if (text.name == "Title" || text.name == "Label" || text.name == "Text")
            {
                title ??= text;
            }

            if (text.name == "Subtitle")
            {
                text.gameObject.SetActive(false);
            }
        }

        if (title != null)
        {
            title.gameObject.SetActive(true);
            title.text = label;
        }
    }

    private void UpdatePanel()
    {
        if (_statusText == null)
        {
            return;
        }

        _builder.Clear();
        PopulateThemeDropdown();
        var captureLine = BuildCaptureLine();
        var themeLine = BuildThemeLine();
        var cacheLine = BuildCacheLine();
        var queueLine = BuildQueueLine();
        var reuseLine = BuildReuseLine();
        var rotationLine = BuildRotationLine();
        var cleanLine = shellVisibilityToggle != null && shellVisibilityToggle.CleanViewActive ? "active" : "off";
        var cardsLine = worldStatusOverlay != null && worldStatusOverlay.IsOverlayVisible ? "visible" : "hidden";

        SetStatusValue(_captureStatusText, StripStatusPrefix(captureLine, "Capture"));
        SetStatusValue(_styleStatusText, StripStatusPrefix(themeLine, "Style"));
        SetStatusValue(_cacheStatusText, StripStatusPrefix(cacheLine, "Cache"));
        SetStatusValue(_queueStatusText, StripStatusPrefix(queueLine, "Queue"));
        SetStatusValue(_reuseStatusText, StripStatusPrefix(reuseLine, "Reuse"));
        SetStatusValue(_rotationStatusText, StripStatusPrefix(rotationLine, "Rotate"));
        SetStatusValue(_cleanViewStatusText, cleanLine);
        SetStatusValue(_objectStatusText, cardsLine);

        _builder.AppendLine(captureLine);
        _builder.AppendLine(themeLine);
        _builder.AppendLine(cacheLine);
        _builder.AppendLine(queueLine);
        _builder.AppendLine(reuseLine);
        _builder.AppendLine(rotationLine);
        _builder.AppendLine();
        _builder.AppendLine($"Clean View: {cleanLine}");
        _builder.AppendLine($"Object Status Cards: {cardsLine}");
        _builder.AppendLine("Controls: Capture | Auto Target | Reuse Captures | Reapply Room | Clean View | Object Status | Rotate 90 | Left Menu: Panel");

        _latestSummary = $"[SceneShiftUISetDashboard]\nState: visible\n{_builder.ToString().TrimEnd()}";

        if (_titleText != null)
        {
            _titleText.text = "SceneShift Control";
        }

        if (_subtitleText != null)
        {
            _subtitleText.text = "Quest Link / headset runtime panel";
        }
    }

    private static void SetStatusValue(TMP_Text target, string value)
    {
        if (target != null)
        {
            target.text = value;
        }
    }

    private static string StripStatusPrefix(string line, string prefix)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return "waiting";
        }

        var expected = prefix + ":";
        return line.StartsWith(expected) ? line[expected.Length..].Trim() : line.Trim();
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
        return $"Capture: {captureService.CurrentState} | target={target} | id={objectId} | score={score}";
    }

    private string BuildThemeLine()
    {
        if (themeIntentController == null || themeIntentController.ActiveTheme == null)
        {
            return "Style: none";
        }

        var theme = themeIntentController.ActiveTheme;
        var runtimeIntent = runtimeStyleIntentController != null ? runtimeStyleIntentController.CurrentIntent : null;
        var effectiveDisplayName = RuntimeStyleIntentRequestUtility.BuildEffectiveThemeDisplayName(theme, runtimeIntent);
        if (!RuntimeStyleIntentRequestUtility.HasUserStyleIntent(runtimeIntent))
        {
            return $"Style: {effectiveDisplayName}";
        }

        return $"Style: {effectiveDisplayName} | scaffold={theme.DisplayName}";
    }

    private string BuildCacheLine()
    {
        if (roomStyleCacheService == null || themeIntentController == null || themeIntentController.ActiveTheme == null)
        {
            return "Cache: unavailable";
        }

        return "Cache: " + CompactStatusLine(roomStyleCacheService.GetThemeCacheLine(themeIntentController.ActiveTheme), 118);
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
        return "Queue: " + CompactStatusLine(lines.Length > 1 ? lines[1].Trim() : summary.Trim(), 118);
    }

    private string BuildReuseLine()
    {
        if (furnitureReuseService == null)
        {
            return "Reuse: unavailable";
        }

        return $"Reuse: queued={furnitureReuseService.LastQueuedCount}, skipped={furnitureReuseService.LastSkippedCount}, failed={furnitureReuseService.LastFailedCount}";
    }

    private string BuildRotationLine()
    {
        if (rotationCorrectionController == null)
        {
            return "Rotate: unavailable";
        }

        rotationCorrectionController.RefreshSelectionFromContext();
        return $"Rotate: {rotationCorrectionController.StatusLine}";
    }

    private static string CompactStatusLine(string value, int maxCharacters)
    {
        if (string.IsNullOrWhiteSpace(value) || maxCharacters <= 4)
        {
            return string.IsNullOrWhiteSpace(value) ? "waiting" : value;
        }

        value = value.Replace('\n', ' ').Replace('\r', ' ').Trim();
        while (value.Contains("  "))
        {
            value = value.Replace("  ", " ");
        }

        return value.Length <= maxCharacters ? value : value[..(maxCharacters - 3)] + "...";
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

    private void ApplyUISetInspiredImage(Image image, bool isPanel)
    {
        if (!useUISetInspiredFallbackSkin || image == null || image.sprite != null)
        {
            return;
        }

        image.sprite = GetRoundedBoxSprite();
        image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = isPanel ? 0.75f : 1f;
    }

    private Sprite GetRoundedBoxSprite()
    {
        if (_roundedBoxSprite != null)
        {
            return _roundedBoxSprite;
        }

        const int size = 64;
        const int radius = 18;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "SceneShift_UISetInspiredRoundedBox",
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
        _roundedBoxTexture = texture;
        _roundedBoxSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f),
            100f,
            0,
            SpriteMeshType.FullRect,
            new Vector4(radius, radius, radius, radius));
        _roundedBoxSprite.name = "SceneShift_UISetInspiredRoundedBox";
        _roundedBoxSprite.hideFlags = HideFlags.DontSave;
        return _roundedBoxSprite;
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
