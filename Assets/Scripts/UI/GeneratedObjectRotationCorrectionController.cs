using System;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class GeneratedObjectRotationCorrectionController : MonoBehaviour
{
    [Header("Selection")]
    [SerializeField] private Transform gazeOrigin;
    [SerializeField] private DevicePassthroughCaptureService captureService;
    [SerializeField] private LayerMask raycastMask = ~0;
    [SerializeField, Min(0.5f)] private float maxSelectionDistanceMeters = 6f;
    [SerializeField, Range(0.02f, 0.45f)] private float fallbackViewportRadius = 0.18f;
    [SerializeField] private bool keepPreviousSelectionWhenTargetMissing = true;

    [Header("Rotation")]
    [SerializeField] private float clockwiseYawDegrees = 90f;
    [SerializeField] private bool logCorrections = true;

    public StylizedFurnitureInstance SelectedInstance => selectedInstance;
    public string SelectedLabel => selectedInstance != null ? selectedInstance.DisplayLabel : "none";
    public string LatestSummary => latestSummary;
    public string StatusLine => $"{SelectedLabel} | {lastAction}";

    private StylizedFurnitureInstance selectedInstance;
    private Camera cachedCamera;
    private string lastAction = "idle";
    private string latestSummary = "[GeneratedObjectRotationCorrection]\nState: waiting\nHint: aim at a stylized furniture object, then press Rotate 90.";

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();
    }

    public bool RefreshSelectionFromContext()
    {
        ResolveReferences();

        if (TrySelectFromCaptureTarget(out var captureTarget))
        {
            SetSelection(captureTarget, "capture-target");
            return true;
        }

        if (TrySelectFromRaycast(out var raycastTarget))
        {
            SetSelection(raycastTarget, "raycast");
            return true;
        }

        if (TrySelectFromViewportFallback(out var viewportTarget))
        {
            SetSelection(viewportTarget, "viewport");
            return true;
        }

        if (keepPreviousSelectionWhenTargetMissing && selectedInstance != null && selectedInstance.gameObject.activeInHierarchy)
        {
            PublishSummary("using-previous-selection");
            return true;
        }

        selectedInstance = null;
        lastAction = "no selectable object";
        PublishSummary("no-selection");
        return false;
    }

    public bool RotateSelectedClockwise90()
    {
        if (selectedInstance == null || !selectedInstance.gameObject.activeInHierarchy)
        {
            RefreshSelectionFromContext();
        }

        if (selectedInstance == null)
        {
            lastAction = "rotate failed: no selection";
            PublishSummary("rotate-without-selection");
            return false;
        }

        var target = selectedInstance.transform;
        target.Rotate(Vector3.up, clockwiseYawDegrees, Space.World);
        var yaw = NormalizeDegrees(target.eulerAngles.y);
        lastAction = $"rotated {clockwiseYawDegrees:0.#} deg, yaw={yaw:0.#}";
        PublishSummary("rotate-clockwise-90");

        if (logCorrections)
        {
            Debug.Log($"[GeneratedObjectRotationCorrection] Rotated {selectedInstance.DisplayLabel} by {clockwiseYawDegrees:0.#} degrees around world Y. New yaw={yaw:0.#}.");
        }

        return true;
    }

    private void ResolveReferences()
    {
        if (captureService == null)
        {
            captureService = FindAnyObjectByType<DevicePassthroughCaptureService>();
        }

        if (gazeOrigin == null)
        {
            var camera = ResolveCamera();
            if (camera != null)
            {
                gazeOrigin = camera.transform;
            }
        }
    }

    private bool TrySelectFromCaptureTarget(out StylizedFurnitureInstance target)
    {
        target = null;
        if (captureService == null || !captureService.HasBestCandidate)
        {
            return false;
        }

        var objectId = captureService.BestAnchorObjectId;
        if (string.IsNullOrWhiteSpace(objectId))
        {
            return false;
        }

        var instances = FindObjectsByType<StylizedFurnitureInstance>(FindObjectsInactive.Exclude);
        foreach (var instance in instances)
        {
            if (instance == null || !instance.gameObject.activeInHierarchy)
            {
                continue;
            }

            if (string.Equals(instance.ObjectId, objectId, StringComparison.OrdinalIgnoreCase))
            {
                target = instance;
                return true;
            }
        }

        return false;
    }

    private bool TrySelectFromRaycast(out StylizedFurnitureInstance target)
    {
        target = null;
        var origin = gazeOrigin != null ? gazeOrigin : ResolveCamera()?.transform;
        if (origin == null)
        {
            return false;
        }

        var ray = new Ray(origin.position, origin.forward);
        if (!Physics.Raycast(ray, out var hit, maxSelectionDistanceMeters, raycastMask, QueryTriggerInteraction.Ignore))
        {
            return false;
        }

        target = hit.transform != null ? hit.transform.GetComponentInParent<StylizedFurnitureInstance>() : null;
        return target != null && target.gameObject.activeInHierarchy;
    }

    private bool TrySelectFromViewportFallback(out StylizedFurnitureInstance target)
    {
        target = null;
        var camera = ResolveCamera();
        if (camera == null)
        {
            return false;
        }

        var maxViewportDistanceSquared = fallbackViewportRadius * fallbackViewportRadius;
        var bestScore = float.PositiveInfinity;
        var instances = FindObjectsByType<StylizedFurnitureInstance>(FindObjectsInactive.Exclude);
        foreach (var instance in instances)
        {
            if (instance == null || !instance.gameObject.activeInHierarchy)
            {
                continue;
            }

            var bounds = CalculateRendererBounds(instance.transform);
            var center = bounds.HasValue ? bounds.Value.center : instance.transform.position;
            var distance = Vector3.Distance(camera.transform.position, center);
            if (distance > maxSelectionDistanceMeters)
            {
                continue;
            }

            var viewportPoint = camera.WorldToViewportPoint(center);
            if (viewportPoint.z <= 0f)
            {
                continue;
            }

            var viewportDelta = new Vector2(viewportPoint.x - 0.5f, viewportPoint.y - 0.5f);
            var viewportDistanceSquared = viewportDelta.sqrMagnitude;
            if (viewportDistanceSquared > maxViewportDistanceSquared)
            {
                continue;
            }

            var score = viewportDistanceSquared + distance * 0.0025f;
            if (score >= bestScore)
            {
                continue;
            }

            bestScore = score;
            target = instance;
        }

        return target != null;
    }

    private void SetSelection(StylizedFurnitureInstance instance, string source)
    {
        selectedInstance = instance;
        lastAction = $"selected via {source}";
        PublishSummary(source);
    }

    private void PublishSummary(string reason)
    {
        latestSummary =
            "[GeneratedObjectRotationCorrection]\n" +
            $"State: {(selectedInstance != null ? "selected" : "idle")}\n" +
            $"Reason: {reason}\n" +
            $"Selected: {SelectedLabel}\n" +
            $"Action: {lastAction}";
    }

    private Camera ResolveCamera()
    {
        if (cachedCamera != null)
        {
            return cachedCamera;
        }

        cachedCamera = Camera.main;
        if (cachedCamera == null)
        {
            cachedCamera = FindAnyObjectByType<Camera>();
        }

        return cachedCamera;
    }

    private static Bounds? CalculateRendererBounds(Transform root)
    {
        if (root == null)
        {
            return null;
        }

        var renderers = root.GetComponentsInChildren<Renderer>(false);
        Bounds? bounds = null;
        foreach (var renderer in renderers)
        {
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
            {
                continue;
            }

            bounds = bounds.HasValue ? Encapsulate(bounds.Value, renderer.bounds) : renderer.bounds;
        }

        return bounds;
    }

    private static Bounds Encapsulate(Bounds current, Bounds next)
    {
        current.Encapsulate(next);
        return current;
    }

    private static float NormalizeDegrees(float degrees)
    {
        degrees %= 360f;
        return degrees < 0f ? degrees + 360f : degrees;
    }
}
