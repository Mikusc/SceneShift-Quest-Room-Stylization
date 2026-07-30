using System;
using System.Collections.Generic;
using System.Text;
using Meta.XR.MRUtilityKit;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[AddComponentMenu("Scene Shift/MRUK Shell Visibility Toggle")]
[DefaultExecutionOrder(1000)]
[DisallowMultipleComponent]
public class MRUKShellVisibilityToggle : MonoBehaviour
{
    private const string RuntimeObjectName = "MRUKShellVisibilityToggle";
    private const string StylizedContentRootName = "StylizedContentRoot";
    private const string RoomModelName = "RoomModel";

    [Header("Toggle")]
    [SerializeField] private bool shellsVisible = true;
    [SerializeField] private bool hideShellsOnStart;
    [SerializeField, Tooltip("Keyboard fallback for Quest Link or Editor Play Mode.")]
    private bool enableKeyboardToggleInPlay = true;
    [SerializeField] private KeyCode keyboardToggleKey = KeyCode.B;
    [SerializeField, Tooltip("Quest controller fallback for true-device Link validation.")]
    private bool enableOvrControllerToggleInPlay = true;
    [SerializeField] private OVRInput.RawButton ovrToggleButton = OVRInput.RawButton.B;
    [SerializeField, Min(0.2f), Tooltip("How often to pick up MRUK shell renderers that appear after room load.")]
    private float refreshIntervalSeconds = 0.75f;
    [SerializeField] private bool logToggleEvents = true;

    [Header("Clean View")]
    [SerializeField, Tooltip("B/controller B enters a clean demo view: stylized room plus the main SceneShift dashboard only.")]
    private bool cleanViewActive;
    [SerializeField] private bool hideObjectStatusCardsInCleanView = true;
    [SerializeField] private bool hideLegacyHeadsetOverlaysInCleanView = true;
    [SerializeField] private bool hideRuntimeButtonInCleanView = true;

    [Header("Targets")]
    [SerializeField] private bool includeVolumeShells = true;
    [SerializeField] private bool includePlaneShells = true;
    [SerializeField] private bool includePrefabSpawnerShells = true;
    [SerializeField] private bool includeDebugRoomModel = true;
    [SerializeField, Tooltip("Fallback for MRUK anchor renderers whose runtime names differ from the prefab spawner naming.")]
    private bool includeMrukAnchorRenderers = true;

    [Header("Headset Button")]
    [SerializeField] private bool createRuntimeButtonIfMissing;
    [SerializeField] private bool showRuntimeButtonInPlay;
    [SerializeField] private Transform headTransform;
    [SerializeField] private Canvas canvas;
    [SerializeField] private Button toggleButton;
    [SerializeField] private TMP_Text buttonLabel;
    [SerializeField] private Vector3 localOffset = new(0.42f, -0.36f, 1.1f);
    [SerializeField] private Vector3 localEulerOffset = new(8f, 0f, 0f);
    [SerializeField] private Vector2 buttonSizePixels = new(360f, 92f);
    [SerializeField, Min(0.0001f)] private float worldScale = 0.0011f;

    public event Action SummaryChanged;

    public bool ShellsVisible => shellsVisible;
    public bool CleanViewActive => cleanViewActive;
    public string LatestSummary => _latestSummary;

    private readonly Dictionary<Renderer, bool> _originalRendererStates = new();
    private readonly List<Renderer> _trackedRenderers = new(64);
    private readonly StringBuilder _summaryBuilder = new(192);
    private float _nextRefreshTime;
    private bool _lastOvrButtonPressed;
    private string _latestSummary = "[MRUKShellVisibilityToggle]\nState: waiting\nInput: keyboard B / controller B";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureRuntimeInstance()
    {
        if (FindAnyObjectByType<MRUKShellVisibilityToggle>() != null)
        {
            return;
        }

        var runtimeObject = new GameObject(RuntimeObjectName);
        runtimeObject.AddComponent<MRUKShellVisibilityToggle>();
    }

    private void Reset()
    {
        ResolveReferences();
    }

    private void Awake()
    {
        ResolveReferences();
        EnsureRuntimeButton();

        if (hideShellsOnStart)
        {
            shellsVisible = false;
        }

        if (cleanViewActive)
        {
            shellsVisible = false;
        }

        ApplyVisibility("awake", true, false);
    }

    private void OnEnable()
    {
        ResolveReferences();
        EnsureRuntimeButton();
        if (cleanViewActive)
        {
            shellsVisible = false;
        }

        ApplyVisibility("enabled", true, false);
        if (cleanViewActive)
        {
            EnforceCleanViewOverlays();
        }
    }

    private void OnDisable()
    {
        RestoreOriginalRendererStates();
    }

    private void OnDestroy()
    {
        RestoreOriginalRendererStates();
    }

    private void Update()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        ResolveReferences();
        EnsureRuntimeButton();
        SetButtonVisible(showRuntimeButtonInPlay && (!cleanViewActive || !hideRuntimeButtonInCleanView));
        UpdateHeadLockedPlacement();

        if (cleanViewActive)
        {
            EnforceCleanViewOverlays();
        }

        if (WasKeyboardTogglePressed() || WasOvrControllerTogglePressed())
        {
            ToggleCleanView();
            return;
        }

        if (Time.unscaledTime < _nextRefreshTime)
        {
            return;
        }

        _nextRefreshTime = Time.unscaledTime + refreshIntervalSeconds;
        ApplyVisibility("refresh", true, false);
    }

    [ContextMenu("Toggle Clean View")]
    public void ToggleCleanView()
    {
        SetCleanViewActive(!cleanViewActive);
    }

    public void SetCleanViewActive(bool active)
    {
        cleanViewActive = active;
        shellsVisible = !active;
        ApplyVisibility(active ? "clean-view-on" : "clean-view-off", true, true);

        if (active)
        {
            EnforceCleanViewOverlays();
        }
        else
        {
            SetObjectStatusCardsVisible(true);
            SetButtonVisible(showRuntimeButtonInPlay);
        }
    }

    [ContextMenu("Toggle Shells")]
    public void ToggleShells()
    {
        SetShellsVisible(!shellsVisible);
    }

    [ContextMenu("Show Shells")]
    public void ShowShells()
    {
        SetShellsVisible(true);
    }

    [ContextMenu("Hide Shells")]
    public void HideShells()
    {
        SetShellsVisible(false);
    }

    public void SetShellsVisible(bool visible)
    {
        cleanViewActive = false;
        shellsVisible = visible;
        ApplyVisibility(visible ? "shells-visible" : "shells-hidden", true, true);
    }

    private void ResolveReferences()
    {
        if (headTransform != null)
        {
            return;
        }

        var mainCamera = Camera.main;
        if (mainCamera != null)
        {
            headTransform = mainCamera.transform;
            return;
        }

        var centerEyeAnchor = GameObject.Find("CenterEyeAnchor");
        if (centerEyeAnchor != null)
        {
            headTransform = centerEyeAnchor.transform;
            return;
        }

        var fallbackCamera = FindAnyObjectByType<Camera>();
        if (fallbackCamera != null)
        {
            headTransform = fallbackCamera.transform;
        }
    }

    private void EnsureRuntimeButton()
    {
        if (canvas != null || !createRuntimeButtonIfMissing)
        {
            UpdateButtonLabel();
            return;
        }

        var canvasObject = new GameObject("MRUKShellVisibilityHeadsetButton", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);

        canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 32010;

        var canvasRect = canvasObject.GetComponent<RectTransform>();
        canvasRect.sizeDelta = buttonSizePixels;
        canvasObject.transform.localScale = Vector3.one * worldScale;

        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        scaler.dynamicPixelsPerUnit = 10f;

        var buttonObject = new GameObject("ToggleMRUKShellsButton", typeof(RectTransform), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(canvasObject.transform, false);
        StretchToParent(buttonObject.GetComponent<RectTransform>());

        var buttonImage = buttonObject.GetComponent<Image>();
        buttonImage.raycastTarget = true;

        toggleButton = buttonObject.GetComponent<Button>();
        toggleButton.targetGraphic = buttonImage;
        toggleButton.onClick.RemoveListener(ToggleCleanView);
        toggleButton.onClick.RemoveListener(ToggleShells);
        toggleButton.onClick.AddListener(ToggleCleanView);

        var labelObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelObject.transform.SetParent(buttonObject.transform, false);
        StretchToParent(labelObject.GetComponent<RectTransform>());

        buttonLabel = labelObject.GetComponent<TextMeshProUGUI>();
        buttonLabel.alignment = TextAlignmentOptions.Center;
        buttonLabel.fontSize = 28f;
        buttonLabel.fontStyle = FontStyles.Bold;
        buttonLabel.raycastTarget = false;
        buttonLabel.color = Color.white;
        buttonLabel.margin = new Vector4(12f, 8f, 12f, 8f);

        UpdateButtonLabel();
    }

    private void UpdateHeadLockedPlacement()
    {
        if (canvas == null || headTransform == null)
        {
            return;
        }

        var canvasTransform = canvas.transform;
        if (canvasTransform.parent != headTransform)
        {
            canvasTransform.SetParent(headTransform, false);
        }

        canvasTransform.localPosition = localOffset;
        canvasTransform.localRotation = Quaternion.Euler(localEulerOffset);
        canvasTransform.localScale = Vector3.one * worldScale;
        canvas.worldCamera = headTransform.GetComponent<Camera>();
    }

    private void SetButtonVisible(bool isVisible)
    {
        if (canvas != null && canvas.gameObject.activeSelf != isVisible)
        {
            canvas.gameObject.SetActive(isVisible);
        }
    }

    private int ApplyVisibility(string reason, bool refreshTargets, bool logEvent)
    {
        if (refreshTargets)
        {
            RefreshTrackedRenderers();
        }

        var affectedCount = 0;
        for (var index = _trackedRenderers.Count - 1; index >= 0; index--)
        {
            var renderer = _trackedRenderers[index];
            if (renderer == null)
            {
                _trackedRenderers.RemoveAt(index);
                continue;
            }

            if (!_originalRendererStates.TryGetValue(renderer, out var originalEnabled))
            {
                originalEnabled = renderer.enabled;
                _originalRendererStates[renderer] = originalEnabled;
            }

            renderer.enabled = shellsVisible && originalEnabled;
            affectedCount++;
        }

        UpdateButtonLabel();
        PublishSummary(reason, affectedCount);

        if (logToggleEvents && logEvent)
        {
            Debug.Log($"[MRUKShellVisibilityToggle] {reason}: {(shellsVisible ? "visible" : "hidden")} renderers={affectedCount}");
        }

        return affectedCount;
    }

    private void EnforceCleanViewOverlays()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        SetObjectStatusCardsVisible(false);

        if (hideLegacyHeadsetOverlaysInCleanView)
        {
            foreach (var hud in FindObjectsByType<DevicePassthroughCaptureHud>(FindObjectsInactive.Include))
            {
                hud.enabled = false;
                SetChildCanvasesVisible(hud.transform, false);
            }

            foreach (var debugPanel in FindObjectsByType<StylizationDebugPanel>(FindObjectsInactive.Include))
            {
                debugPanel.enabled = false;
                SetChildCanvasesVisible(debugPanel.transform, false);
            }
        }

        if (hideRuntimeButtonInCleanView)
        {
            SetButtonVisible(false);
        }
    }

    private void SetObjectStatusCardsVisible(bool visible)
    {
        if (!hideObjectStatusCardsInCleanView)
        {
            return;
        }

        foreach (var overlay in FindObjectsByType<GenerationJobWorldStatusOverlay>(FindObjectsInactive.Include))
        {
            overlay.SetOverlayVisible(visible);
        }
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

    private void RefreshTrackedRenderers()
    {
        _trackedRenderers.Clear();

        var allRenderers = FindObjectsByType<Renderer>(FindObjectsInactive.Include);
        for (var index = 0; index < allRenderers.Length; index++)
        {
            var renderer = allRenderers[index];
            if (!ShouldControlRenderer(renderer))
            {
                continue;
            }

            if (!_originalRendererStates.ContainsKey(renderer))
            {
                _originalRendererStates[renderer] = renderer.enabled;
            }

            _trackedRenderers.Add(renderer);
        }
    }

    private bool ShouldControlRenderer(Renderer renderer)
    {
        if (renderer == null || renderer is ParticleSystemRenderer)
        {
            return false;
        }

        var current = renderer.transform;
        if (current == null || current.IsChildOf(transform) || IsUnderStylizedContent(current))
        {
            return false;
        }

        while (current != null)
        {
            var objectName = current.name;
            if (includeDebugRoomModel && string.Equals(objectName, RoomModelName, StringComparison.Ordinal))
            {
                return true;
            }

            if (includePrefabSpawnerShells && objectName.Contains("(PrefabSpawner Clone)", StringComparison.Ordinal))
            {
                return true;
            }

            if (includeVolumeShells && objectName.StartsWith("Volume(", StringComparison.Ordinal))
            {
                return true;
            }

            if (includePlaneShells && objectName.StartsWith("PlaneMesh(", StringComparison.Ordinal))
            {
                return true;
            }

            current = current.parent;
        }

        return includeMrukAnchorRenderers && renderer.GetComponentInParent<MRUKAnchor>() != null;
    }

    private static bool IsUnderStylizedContent(Transform current)
    {
        while (current != null)
        {
            if (string.Equals(current.name, StylizedContentRootName, StringComparison.Ordinal))
            {
                return true;
            }

            current = current.parent;
        }

        return false;
    }

    private void RestoreOriginalRendererStates()
    {
        foreach (var pair in _originalRendererStates)
        {
            if (pair.Key != null)
            {
                pair.Key.enabled = pair.Value;
            }
        }

        _trackedRenderers.Clear();
        _originalRendererStates.Clear();
        shellsVisible = true;
        cleanViewActive = false;
        PublishSummary("restored", 0);
    }

    private bool WasKeyboardTogglePressed()
    {
        if (!enableKeyboardToggleInPlay)
        {
            return false;
        }

#if ENABLE_INPUT_SYSTEM
        var keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return false;
        }

        if (!Enum.TryParse(keyboardToggleKey.ToString(), true, out UnityEngine.InputSystem.Key inputKey))
        {
            return false;
        }

        var keyControl = keyboard[inputKey];
        return keyControl != null && keyControl.wasPressedThisFrame;
#else
        return Input.GetKeyDown(keyboardToggleKey);
#endif
    }

    private bool WasOvrControllerTogglePressed()
    {
        if (!enableOvrControllerToggleInPlay)
        {
            return false;
        }

        var isPressed = OVRInput.Get(ovrToggleButton);
        var wasPressedThisFrame = isPressed && !_lastOvrButtonPressed;
        _lastOvrButtonPressed = isPressed;
        return wasPressedThisFrame;
    }

    private void UpdateButtonLabel()
    {
        if (buttonLabel == null)
        {
            return;
        }

        buttonLabel.text = cleanViewActive
            ? "Exit Clean View\nB / Controller B"
            : "Clean View\nB / Controller B";

        var buttonImage = toggleButton != null ? toggleButton.targetGraphic as Image : null;
        if (buttonImage != null)
        {
            buttonImage.color = cleanViewActive
                ? new Color(0.08f, 0.42f, 0.22f, 0.82f)
                : new Color(0.08f, 0.28f, 0.52f, 0.82f);
        }
    }

    private void PublishSummary(string reason, int affectedCount)
    {
        _summaryBuilder.Clear();
        _summaryBuilder.AppendLine("[MRUKShellVisibilityToggle]");
        _summaryBuilder.AppendLine($"State: {(shellsVisible ? "visible" : "hidden")} ({reason})");
        _summaryBuilder.AppendLine($"Clean View: {(cleanViewActive ? "active" : "off")}");
        _summaryBuilder.AppendLine($"Tracked Shell Renderers: {affectedCount}");
        _summaryBuilder.AppendLine($"Input: keyboard {keyboardToggleKey} / controller {ovrToggleButton}");
        _latestSummary = _summaryBuilder.ToString().TrimEnd();
        SummaryChanged?.Invoke();
    }

    private static void StretchToParent(RectTransform rectTransform)
    {
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;
    }
}
