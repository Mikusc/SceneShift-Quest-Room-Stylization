using System;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class RuntimeGeneratedModelInstance : MonoBehaviour
{
    [Header("Identity")]
    [SerializeField] private string requestId;
    [SerializeField] private string objectId;
    [SerializeField] private string roomId;
    [SerializeField] private string themeId;
    [SerializeField] private string styleVariantId = "preset";
    [SerializeField] private string semanticLabel;

    [Header("Runtime Model")]
    [SerializeField] private string jobPath;
    [SerializeField] private string modelUrl;
    [SerializeField] private string modelLocalPath;
    [SerializeField] private string modelHash;
    [SerializeField] private SerializableBounds sourceLocalBounds;
    [SerializeField] private SerializableBounds fittedWorldBounds;
    [SerializeField] private Vector3 appliedScale = Vector3.one;
    [SerializeField] private Vector3 appliedEulerDegrees;

    [Header("Review")]
    [SerializeField] private GeneratedObjectReviewState reviewState = GeneratedObjectReviewState.Previewing;
    [SerializeField] private string reviewDecisionIsoUtc;
    [SerializeField] private CorrectionDelta persistedCorrection = CorrectionDelta.Identity;
    [SerializeField] private Vector3 correctionBaseLocalPosition;
    [SerializeField] private Quaternion correctionBaseLocalRotation = Quaternion.identity;
    [SerializeField] private Vector3 correctionBaseLocalScale = Vector3.one;

    public string RequestId => requestId;
    public string ObjectId => objectId;
    public string RoomId => roomId;
    public string ThemeId => themeId;
    public string StyleVariantId => styleVariantId;
    public string SemanticLabel => semanticLabel;
    public string JobPath => jobPath;
    public string ModelUrl => modelUrl;
    public string ModelLocalPath => modelLocalPath;
    public string ModelHash => modelHash;
    public SerializableBounds SourceLocalBounds => sourceLocalBounds;
    public SerializableBounds FittedWorldBounds => fittedWorldBounds;
    public Vector3 AppliedScale => appliedScale;
    public Vector3 AppliedEulerDegrees => appliedEulerDegrees;
    public GeneratedObjectReviewState ReviewState => reviewState;
    public string ReviewDecisionIsoUtc => reviewDecisionIsoUtc;
    public CorrectionDelta PersistedCorrection => persistedCorrection;

    public string DisplayLabel
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(objectId))
            {
                return objectId;
            }

            return string.IsNullOrWhiteSpace(requestId) ? name : requestId;
        }
    }

    public void Initialize(
        GeneratedAssetRecord record,
        GeneratedObjectRequest request,
        string recordJobPath,
        string localModelPath,
        Bounds normalizedLocalBounds,
        Bounds worldBounds,
        Vector3 runtimeScale,
        Vector3 runtimeEulerDegrees)
    {
        requestId = FirstNonEmpty(record?.RequestId, request?.RequestId);
        objectId = FirstNonEmpty(record?.ObjectId, request?.ObjectId);
        roomId = request?.RoomId;
        themeId = FirstNonEmpty(record?.ThemeId, request?.ThemeId);
        styleVariantId = NormalizeStyleVariantId(FirstNonEmpty(record?.StyleVariantId, request?.StyleVariantId));
        semanticLabel = request?.SemanticLabel;
        jobPath = recordJobPath;
        modelUrl = record?.RuntimeModelUrl;
        modelLocalPath = FirstNonEmpty(localModelPath, record?.RuntimeModelLocalPath);
        modelHash = record?.RuntimeModelHash;
        sourceLocalBounds = SerializableBounds.From(normalizedLocalBounds.center, normalizedLocalBounds.size);
        fittedWorldBounds = SerializableBounds.From(worldBounds.center, worldBounds.size);
        appliedScale = runtimeScale;
        appliedEulerDegrees = runtimeEulerDegrees;
        reviewState = record != null && record.ReviewState != GeneratedObjectReviewState.None
            ? record.ReviewState
            : GeneratedObjectReviewState.Previewing;
        reviewDecisionIsoUtc = record?.ReviewDecisionIsoUtc;
        persistedCorrection = EnsureUsableCorrection(record != null ? record.PersistedCorrection : CorrectionDelta.Identity);
        CaptureCorrectionBaseTransform();
    }

    public void SetReviewState(GeneratedObjectReviewState state, CorrectionDelta correction, string note)
    {
        reviewState = state;
        persistedCorrection = EnsureUsableCorrection(correction);
        reviewDecisionIsoUtc = DateTime.UtcNow.ToString("O");
    }

    public void ApplyPersistedCorrection(CorrectionDelta correction)
    {
        persistedCorrection = EnsureUsableCorrection(correction);
        transform.localPosition = correctionBaseLocalPosition + persistedCorrection.PositionOffset;
        transform.localRotation = correctionBaseLocalRotation * Quaternion.Euler(0f, persistedCorrection.EulerOffset.y, 0f);
        transform.localScale = Vector3.Scale(correctionBaseLocalScale, persistedCorrection.ScaleMultiplier);
    }

    private void CaptureCorrectionBaseTransform()
    {
        correctionBaseLocalPosition = transform.localPosition;
        correctionBaseLocalRotation = transform.localRotation;
        correctionBaseLocalScale = transform.localScale;
    }

    private static CorrectionDelta EnsureUsableCorrection(CorrectionDelta correction)
    {
        if (correction.ScaleMultiplier == Vector3.zero)
        {
            correction.ScaleMultiplier = Vector3.one;
        }

        return correction;
    }

    private static string FirstNonEmpty(string first, string second)
    {
        return !string.IsNullOrWhiteSpace(first) ? first : second;
    }

    private static string NormalizeStyleVariantId(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "preset" : value.Trim();
    }
}
