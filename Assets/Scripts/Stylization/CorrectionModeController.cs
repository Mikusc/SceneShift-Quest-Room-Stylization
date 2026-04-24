using System;
using System.Text;
using UnityEngine;

[DisallowMultipleComponent]
public class CorrectionModeController : MonoBehaviour
{
    [Header("Selection")]
    [SerializeField] private Transform selectedObject;
    [SerializeField] private CorrectionTargetInfo selectedTarget = new();
    [SerializeField] private bool captureCurrentTransformOnEnable;

    [Header("Correction Limits")]
    [SerializeField, Min(0.01f)] private float nudgeStepMeters = 0.025f;
    [SerializeField, Min(0.01f)] private float maxHorizontalOffsetMeters = 0.25f;
    [SerializeField, Min(0f)] private float maxVerticalOffsetMeters = 0.05f;
    [SerializeField] private bool allowVerticalNudge;
    [SerializeField, Range(1f, 45f)] private float yawStepDegrees = 5f;
    [SerializeField, Range(1f, 90f)] private float maxYawOffsetDegrees = 25f;

    [Header("Runtime State")]
    [SerializeField] private CorrectionModeState state = CorrectionModeState.Idle;
    [SerializeField] private CorrectionDelta currentDelta = CorrectionDelta.Identity;
    [SerializeField, TextArea(4, 8)] private string latestSummary = "[CorrectionMode]\nState: idle\nHint: select an applied object.";

    public event Action SelectionChanged;
    public event Action CorrectionChanged;
    public event Action CorrectionConfirmed;
    public event Action CorrectionReset;

    public Transform SelectedObject => selectedObject;
    public CorrectionTargetInfo SelectedTarget => selectedTarget;
    public CorrectionModeState State => state;
    public CorrectionDelta CurrentDelta => currentDelta;
    public string LatestSummary => latestSummary;
    public bool HasSelection => selectedObject != null && _hasBaseTransform;
    public bool HasUnconfirmedChanges => HasSelection && !currentDelta.Confirmed && !currentDelta.IsIdentity;

    private Vector3 _baseLocalPosition;
    private Quaternion _baseLocalRotation = Quaternion.identity;
    private Vector3 _baseLocalScale = Vector3.one;
    private bool _hasBaseTransform;

    private void OnEnable()
    {
        if (captureCurrentTransformOnEnable && selectedObject != null)
        {
            CaptureSelection(selectedObject, selectedTarget, false);
        }
        else
        {
            PublishSummary("enabled");
        }
    }

    private void OnValidate()
    {
        nudgeStepMeters = Mathf.Max(0.01f, nudgeStepMeters);
        maxHorizontalOffsetMeters = Mathf.Max(nudgeStepMeters, maxHorizontalOffsetMeters);
        maxVerticalOffsetMeters = Mathf.Max(0f, maxVerticalOffsetMeters);
        yawStepDegrees = Mathf.Clamp(yawStepDegrees, 1f, maxYawOffsetDegrees);
        maxYawOffsetDegrees = Mathf.Clamp(maxYawOffsetDegrees, yawStepDegrees, 90f);
    }

    [ContextMenu("Inspect Selected Object")]
    public void InspectSelectedObject()
    {
        if (!HasSelection && selectedObject != null)
        {
            CaptureSelection(selectedObject, selectedTarget, false);
            return;
        }

        state = HasSelection ? CorrectionModeState.Inspecting : CorrectionModeState.Idle;
        PublishSummary("inspect");
    }

    public void SelectObject(Transform target)
    {
        CaptureSelection(target, new CorrectionTargetInfo(), true);
    }

    public void SelectAppliedObject(
        Transform target,
        string objectId,
        string planEntryId,
        string semanticLabel,
        string replacementDisplayName,
        bool collisionSensitive)
    {
        var targetInfo = new CorrectionTargetInfo
        {
            ObjectId = objectId,
            PlanEntryId = planEntryId,
            SemanticLabel = semanticLabel,
            ReplacementDisplayName = replacementDisplayName,
            CollisionSensitive = collisionSensitive,
        };

        CaptureSelection(target, targetInfo, true);
    }

    public void SelectFromPlanEntry(Transform target, StylizationPlanEntry planEntry)
    {
        var targetInfo = new CorrectionTargetInfo();
        if (planEntry != null)
        {
            targetInfo.ObjectId = planEntry.ObjectId;
            targetInfo.PlanEntryId = planEntry.EntryId;
            targetInfo.SemanticLabel = planEntry.OriginalSemanticLabel;
            targetInfo.FunctionTag = planEntry.OriginalFunctionTag;
            targetInfo.ReplacementMode = planEntry.ReplacementMode;
            targetInfo.ReplacementDisplayName = planEntry.ReplacementDisplayName;
            targetInfo.CollisionSensitive = planEntry.CollisionSensitive;
        }

        CaptureSelection(target, targetInfo, true);
    }

    public void ClearSelection()
    {
        selectedObject = null;
        selectedTarget = new CorrectionTargetInfo();
        currentDelta = CorrectionDelta.Identity;
        _hasBaseTransform = false;
        state = CorrectionModeState.Idle;
        PublishSummary("clear-selection");
        SelectionChanged?.Invoke();
    }

    [ContextMenu("Nudge Forward")]
    public void NudgeForward()
    {
        Nudge(Vector3.forward);
    }

    [ContextMenu("Nudge Back")]
    public void NudgeBack()
    {
        Nudge(Vector3.back);
    }

    [ContextMenu("Nudge Left")]
    public void NudgeLeft()
    {
        Nudge(Vector3.left);
    }

    [ContextMenu("Nudge Right")]
    public void NudgeRight()
    {
        Nudge(Vector3.right);
    }

    [ContextMenu("Nudge Up")]
    public void NudgeUp()
    {
        if (allowVerticalNudge)
        {
            Nudge(Vector3.up);
        }
    }

    [ContextMenu("Nudge Down")]
    public void NudgeDown()
    {
        if (allowVerticalNudge)
        {
            Nudge(Vector3.down);
        }
    }

    public void Nudge(Vector3 localDirection)
    {
        if (!HasSelection)
        {
            PublishSummary("nudge-without-selection");
            return;
        }

        var direction = SanitizeDirection(localDirection);
        if (direction.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        currentDelta.Confirmed = false;
        currentDelta.PositionOffset = ClampPositionOffset(currentDelta.PositionOffset + direction * nudgeStepMeters);
        ApplyCurrentDelta();
        state = CorrectionModeState.Correcting;
        PublishSummary("nudge");
        CorrectionChanged?.Invoke();
    }

    [ContextMenu("Rotate Yaw Left")]
    public void RotateYawLeft()
    {
        RotateYaw(-yawStepDegrees);
    }

    [ContextMenu("Rotate Yaw Right")]
    public void RotateYawRight()
    {
        RotateYaw(yawStepDegrees);
    }

    public void RotateYaw(float degrees)
    {
        if (!HasSelection)
        {
            PublishSummary("rotate-without-selection");
            return;
        }

        currentDelta.Confirmed = false;
        currentDelta.EulerOffset = new Vector3(
            0f,
            Mathf.Clamp(currentDelta.EulerOffset.y + degrees, -maxYawOffsetDegrees, maxYawOffsetDegrees),
            0f);
        ApplyCurrentDelta();
        state = CorrectionModeState.Correcting;
        PublishSummary("rotate-yaw");
        CorrectionChanged?.Invoke();
    }

    [ContextMenu("Reset Correction")]
    public void ResetCorrection()
    {
        if (!HasSelection)
        {
            PublishSummary("reset-without-selection");
            return;
        }

        currentDelta = CorrectionDelta.Identity;
        ApplyCurrentDelta();
        state = CorrectionModeState.Inspecting;
        PublishSummary("reset");
        CorrectionReset?.Invoke();
        CorrectionChanged?.Invoke();
    }

    [ContextMenu("Confirm Correction")]
    public void ConfirmCorrection()
    {
        if (!HasSelection)
        {
            PublishSummary("confirm-without-selection");
            return;
        }

        currentDelta.Confirmed = true;
        state = CorrectionModeState.Confirmed;
        PublishSummary("confirm");
        CorrectionConfirmed?.Invoke();
        CorrectionChanged?.Invoke();
    }

    public CorrectionDelta GetConfirmedOrCurrentDelta()
    {
        return currentDelta;
    }

    private void CaptureSelection(Transform target, CorrectionTargetInfo targetInfo, bool notify)
    {
        selectedObject = target;
        selectedTarget = targetInfo ?? new CorrectionTargetInfo();
        currentDelta = CorrectionDelta.Identity;

        if (selectedObject == null)
        {
            _hasBaseTransform = false;
            state = CorrectionModeState.Idle;
            PublishSummary("missing-selection");
            if (notify)
            {
                SelectionChanged?.Invoke();
            }

            return;
        }

        _baseLocalPosition = selectedObject.localPosition;
        _baseLocalRotation = selectedObject.localRotation;
        _baseLocalScale = selectedObject.localScale;
        _hasBaseTransform = true;
        state = CorrectionModeState.Inspecting;
        PublishSummary("select");

        if (notify)
        {
            SelectionChanged?.Invoke();
            CorrectionChanged?.Invoke();
        }
    }

    private void ApplyCurrentDelta()
    {
        if (!HasSelection)
        {
            return;
        }

        selectedObject.localPosition = _baseLocalPosition + currentDelta.PositionOffset;
        selectedObject.localRotation = _baseLocalRotation * Quaternion.Euler(0f, currentDelta.EulerOffset.y, 0f);
        selectedObject.localScale = Vector3.Scale(_baseLocalScale, currentDelta.ScaleMultiplier);
    }

    private Vector3 SanitizeDirection(Vector3 direction)
    {
        if (!allowVerticalNudge)
        {
            direction.y = 0f;
        }

        if (direction.sqrMagnitude <= 0.0001f)
        {
            return Vector3.zero;
        }

        return direction.normalized;
    }

    private Vector3 ClampPositionOffset(Vector3 offset)
    {
        var horizontal = new Vector2(offset.x, offset.z);
        if (horizontal.magnitude > maxHorizontalOffsetMeters)
        {
            horizontal = horizontal.normalized * maxHorizontalOffsetMeters;
        }

        return new Vector3(
            horizontal.x,
            Mathf.Clamp(offset.y, -maxVerticalOffsetMeters, maxVerticalOffsetMeters),
            horizontal.y);
    }

    private void PublishSummary(string reason)
    {
        var builder = new StringBuilder(384);
        builder.AppendLine("[CorrectionMode]");
        builder.AppendLine($"State: {state}");
        builder.AppendLine($"Reason: {reason}");
        builder.AppendLine($"Has Selection: {HasSelection}");

        if (selectedObject != null)
        {
            builder.AppendLine($"Target: {selectedObject.name}");
            builder.AppendLine($"Object Id: {selectedTarget.ObjectId}");
            builder.AppendLine($"Semantic: {selectedTarget.SemanticLabel}");
            builder.AppendLine($"Replacement: {selectedTarget.ReplacementDisplayName}");
            builder.AppendLine($"Collision Sensitive: {selectedTarget.CollisionSensitive}");
        }

        builder.AppendLine($"Offset: {FormatVector(currentDelta.PositionOffset)}");
        builder.AppendLine($"Yaw: {currentDelta.EulerOffset.y:0.#} deg");
        builder.Append($"Confirmed: {currentDelta.Confirmed}");
        latestSummary = builder.ToString();
    }

    private static string FormatVector(Vector3 value)
    {
        return FormattableString.Invariant($"{value.x:0.###}, {value.y:0.###}, {value.z:0.###}");
    }
}

[Serializable]
public class CorrectionTargetInfo
{
    public string ObjectId;
    public string PlanEntryId;
    public string SemanticLabel;
    public string FunctionTag;
    public ReplacementMode ReplacementMode = ReplacementMode.Skip;
    public string ReplacementDisplayName;
    public bool CollisionSensitive;
}

[Serializable]
public struct CorrectionDelta
{
    public Vector3 PositionOffset;
    public Vector3 EulerOffset;
    public Vector3 ScaleMultiplier;
    public bool Confirmed;

    public static CorrectionDelta Identity => new()
    {
        PositionOffset = Vector3.zero,
        EulerOffset = Vector3.zero,
        ScaleMultiplier = Vector3.one,
        Confirmed = false,
    };

    public bool IsIdentity =>
        PositionOffset.sqrMagnitude <= 0.000001f &&
        EulerOffset.sqrMagnitude <= 0.000001f &&
        Approximately(ScaleMultiplier, Vector3.one);

    private static bool Approximately(Vector3 left, Vector3 right)
    {
        return Mathf.Approximately(left.x, right.x) &&
               Mathf.Approximately(left.y, right.y) &&
               Mathf.Approximately(left.z, right.z);
    }
}

public enum CorrectionModeState
{
    Idle,
    Inspecting,
    Correcting,
    Confirmed,
}
