using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[AddComponentMenu("Scene Shift/Passthrough Only Visibility Toggle")]
[DefaultExecutionOrder(20000)]
[DisallowMultipleComponent]
public class PassthroughOnlyVisibilityToggle : MonoBehaviour
{
    private const string RuntimeObjectName = "PassthroughOnlyVisibilityToggle";

    [Header("Input")]
    [SerializeField, Tooltip("Keyboard fallback for Editor/Link testing.")]
    private bool enableKeyboardToggleInPlay = true;
    [SerializeField] private KeyCode keyboardToggleKey = KeyCode.Y;
    [SerializeField, Tooltip("Quest left controller Y toggle for a pure passthrough view.")]
    private bool enableOvrControllerToggleInPlay = true;
    [SerializeField] private OVRInput.RawButton ovrToggleButton = OVRInput.RawButton.Y;
    [SerializeField, Tooltip("Unity XR fallback for devices that do not expose OVRInput. Keep disabled when OVRInput is active, otherwise one Y press can be read twice.")]
    private bool enableXrControllerToggleInPlay;
    [SerializeField, Tooltip("Prevents OVRInput and Unity XR from both consuming the same physical controller press.")]
    private bool useXrFallbackOnlyWhenOvrDisabled = true;
    [SerializeField] private XRNode xrToggleController = XRNode.LeftHand;
    [SerializeField] private PassthroughOnlyXrButton xrToggleButton = PassthroughOnlyXrButton.SecondaryButton;

    [Header("Suppression")]
    [SerializeField, Tooltip("Set active runtime camera culling masks to Nothing while pure passthrough is active.")]
    private bool suppressCameraCulling = true;
    [SerializeField, Tooltip("Use a transparent solid-color camera clear while pure passthrough is active so skybox/backgrounds do not render over passthrough.")]
    private bool forceTransparentCameraClear = true;
    [SerializeField, Tooltip("Disable all renderers while pure passthrough is active, including generated furniture, surface meshes, controller visuals, and rays.")]
    private bool suppressRenderers = true;
    [SerializeField, Tooltip("Disable all canvases while pure passthrough is active, including dashboards, HUDs, and object status cards.")]
    private bool suppressCanvases = true;
    [SerializeField, Tooltip("Keep the main SceneShift dashboard visible when Y enters pure passthrough view.")]
    private bool keepSceneShiftDashboardVisible = true;
    [SerializeField, Min(0.05f), Tooltip("How often to catch new runtime objects created while pure passthrough is active.")]
    private float refreshIntervalSeconds = 0.2f;
    [SerializeField] private bool logToggleEvents = true;

    public event Action SummaryChanged;

    public bool PassthroughOnlyActive => passthroughOnlyActive;
    public string LatestSummary => _latestSummary;

    [SerializeField] private bool passthroughOnlyActive;

    private readonly Dictionary<Camera, CameraState> _cameraStates = new();
    private readonly Dictionary<Renderer, bool> _rendererStates = new();
    private readonly Dictionary<Canvas, bool> _canvasStates = new();
    private float _nextRefreshTime;
    private bool _wasOvrTogglePressed;
    private bool _wasXrTogglePressed;
    private string _latestSummary = "[PassthroughOnlyVisibilityToggle]\nState: virtual-visible\nInput: Left controller Y / keyboard Y";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureRuntimeInstance()
    {
        if (FindAnyObjectByType<PassthroughOnlyVisibilityToggle>() != null)
        {
            return;
        }

        var runtimeObject = new GameObject(RuntimeObjectName);
        DontDestroyOnLoad(runtimeObject);
        runtimeObject.AddComponent<PassthroughOnlyVisibilityToggle>();
    }

    private void Update()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        if (WasTogglePressed())
        {
            SetPassthroughOnlyActive(!passthroughOnlyActive);
        }

        if (passthroughOnlyActive && Time.unscaledTime >= _nextRefreshTime)
        {
            ApplySuppression();
            _nextRefreshTime = Time.unscaledTime + refreshIntervalSeconds;
        }
    }

    private void OnDisable()
    {
        RestoreVirtualVisibility("disabled");
    }

    private void OnDestroy()
    {
        RestoreVirtualVisibility("destroyed");
    }

    [ContextMenu("Toggle Passthrough Only")]
    public void TogglePassthroughOnly()
    {
        SetPassthroughOnlyActive(!passthroughOnlyActive);
    }

    public void SetPassthroughOnlyActive(bool active)
    {
        if (passthroughOnlyActive == active)
        {
            return;
        }

        passthroughOnlyActive = active;
        if (passthroughOnlyActive)
        {
            ApplySuppression();
            _nextRefreshTime = Time.unscaledTime + refreshIntervalSeconds;
            PublishSummary("passthrough-only", "virtual content hidden");
        }
        else
        {
            RestoreVirtualVisibility("restored");
        }

        if (logToggleEvents)
        {
            Debug.Log($"[PassthroughOnlyVisibilityToggle] {(passthroughOnlyActive ? "Pure passthrough ON" : "Pure passthrough OFF")}", this);
        }
    }

    private void ApplySuppression()
    {
        if (suppressCameraCulling)
        {
            foreach (var sceneCamera in FindObjectsByType<Camera>(FindObjectsInactive.Include))
            {
                if (sceneCamera == null)
                {
                    continue;
                }

                if (!_cameraStates.ContainsKey(sceneCamera))
                {
                    _cameraStates[sceneCamera] = CameraState.From(sceneCamera);
                }

                sceneCamera.cullingMask = keepSceneShiftDashboardVisible ? GetVisibleDashboardLayerMask() : 0;
                if (forceTransparentCameraClear)
                {
                    sceneCamera.clearFlags = CameraClearFlags.SolidColor;
                    sceneCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
                }
            }
        }

        if (suppressRenderers)
        {
            foreach (var rendererComponent in FindObjectsByType<Renderer>(FindObjectsInactive.Include))
            {
                if (rendererComponent == null)
                {
                    continue;
                }

                if (ShouldKeepDashboardObjectVisible(rendererComponent.transform))
                {
                    continue;
                }

                if (!_rendererStates.ContainsKey(rendererComponent))
                {
                    _rendererStates[rendererComponent] = rendererComponent.enabled;
                }

                rendererComponent.enabled = false;
            }
        }

        if (suppressCanvases)
        {
            foreach (var canvasComponent in FindObjectsByType<Canvas>(FindObjectsInactive.Include))
            {
                if (canvasComponent == null)
                {
                    continue;
                }

                if (ShouldKeepDashboardObjectVisible(canvasComponent.transform))
                {
                    continue;
                }

                if (!_canvasStates.ContainsKey(canvasComponent))
                {
                    _canvasStates[canvasComponent] = canvasComponent.enabled;
                }

                canvasComponent.enabled = false;
            }
        }
    }

    private int GetVisibleDashboardLayerMask()
    {
        if (!keepSceneShiftDashboardVisible)
        {
            return 0;
        }

        var mask = 0;
        foreach (var dashboard in FindObjectsByType<SceneShiftUISetDashboard>(FindObjectsInactive.Include))
        {
            if (dashboard == null)
            {
                continue;
            }

            foreach (var child in dashboard.GetComponentsInChildren<Transform>(true))
            {
                if (child != null)
                {
                    mask |= 1 << child.gameObject.layer;
                }
            }
        }

        return mask;
    }

    private bool ShouldKeepDashboardObjectVisible(Transform target)
    {
        return keepSceneShiftDashboardVisible &&
            target != null &&
            target.GetComponentInParent<SceneShiftUISetDashboard>(true) != null;
    }

    private void RestoreVirtualVisibility(string reason)
    {
        foreach (var pair in _cameraStates)
        {
            if (pair.Key != null)
            {
                pair.Value.ApplyTo(pair.Key);
            }
        }

        foreach (var pair in _rendererStates)
        {
            if (pair.Key != null)
            {
                pair.Key.enabled = pair.Value;
            }
        }

        foreach (var pair in _canvasStates)
        {
            if (pair.Key != null)
            {
                pair.Key.enabled = pair.Value;
            }
        }

        _cameraStates.Clear();
        _rendererStates.Clear();
        _canvasStates.Clear();
        passthroughOnlyActive = false;
        PublishSummary("virtual-visible", reason);
    }

    private bool WasTogglePressed()
    {
        if (enableKeyboardToggleInPlay && WasKeyboardTogglePressed())
        {
            return true;
        }

        if (enableOvrControllerToggleInPlay && WasOvrTogglePressed())
        {
            return true;
        }

        return enableXrControllerToggleInPlay &&
            (!useXrFallbackOnlyWhenOvrDisabled || !enableOvrControllerToggleInPlay) &&
            WasXrTogglePressed();
    }

    private bool WasKeyboardTogglePressed()
    {
#if ENABLE_INPUT_SYSTEM
        var keyboard = Keyboard.current;
        if (keyboard == null || keyboardToggleKey == KeyCode.None)
        {
            return false;
        }

        if (!Enum.TryParse(keyboardToggleKey.ToString(), true, out Key inputKey))
        {
            return false;
        }

        var keyControl = keyboard[inputKey];
        return keyControl != null && keyControl.wasPressedThisFrame;
#else
        return keyboardToggleKey != KeyCode.None && Input.GetKeyDown(keyboardToggleKey);
#endif
    }

    private bool WasOvrTogglePressed()
    {
        if (ovrToggleButton == OVRInput.RawButton.None)
        {
            _wasOvrTogglePressed = false;
            return false;
        }

        var isPressed = OVRInput.Get(ovrToggleButton);
        var wasPressedThisFrame = isPressed && !_wasOvrTogglePressed;
        _wasOvrTogglePressed = isPressed;
        return wasPressedThisFrame;
    }

    private bool WasXrTogglePressed()
    {
        var device = InputDevices.GetDeviceAtXRNode(xrToggleController);
        if (!device.isValid || !TryGetXrButtonPressed(device, xrToggleButton, out var isPressed))
        {
            _wasXrTogglePressed = false;
            return false;
        }

        var wasPressedThisFrame = isPressed && !_wasXrTogglePressed;
        _wasXrTogglePressed = isPressed;
        return wasPressedThisFrame;
    }

    private static bool TryGetXrButtonPressed(UnityEngine.XR.InputDevice device, PassthroughOnlyXrButton button, out bool isPressed)
    {
        return button switch
        {
            PassthroughOnlyXrButton.PrimaryButton => device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryButton, out isPressed),
            PassthroughOnlyXrButton.SecondaryButton => device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.secondaryButton, out isPressed),
            PassthroughOnlyXrButton.GripButton => device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.gripButton, out isPressed),
            PassthroughOnlyXrButton.TriggerButton => device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out isPressed),
            _ => Fail(out isPressed),
        };
    }

    private static bool Fail(out bool isPressed)
    {
        isPressed = false;
        return false;
    }

    private void PublishSummary(string state, string reason)
    {
        _latestSummary = $"[PassthroughOnlyVisibilityToggle]\nState: {state}\nReason: {reason}\nInput: Left controller Y / keyboard Y";
        SummaryChanged?.Invoke();
    }

    private readonly struct CameraState
    {
        private readonly int _cullingMask;
        private readonly CameraClearFlags _clearFlags;
        private readonly Color _backgroundColor;

        private CameraState(int cullingMask, CameraClearFlags clearFlags, Color backgroundColor)
        {
            _cullingMask = cullingMask;
            _clearFlags = clearFlags;
            _backgroundColor = backgroundColor;
        }

        public static CameraState From(Camera sceneCamera)
        {
            return new CameraState(sceneCamera.cullingMask, sceneCamera.clearFlags, sceneCamera.backgroundColor);
        }

        public void ApplyTo(Camera sceneCamera)
        {
            sceneCamera.cullingMask = _cullingMask;
            sceneCamera.clearFlags = _clearFlags;
            sceneCamera.backgroundColor = _backgroundColor;
        }
    }
}

public enum PassthroughOnlyXrButton
{
    PrimaryButton,
    SecondaryButton,
    GripButton,
    TriggerButton,
}
