using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class GeneratedObjectReviewController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private RuntimeGeneratedModelLoader runtimeGeneratedModelLoader;
    [SerializeField] private CorrectionModeController correctionModeController;
    [SerializeField] private AnchorThemeApplier anchorThemeApplier;
    [SerializeField] private RoomSemanticBootstrap roomSemanticBootstrap;
    [SerializeField] private ThemeIntentController themeIntentController;
    [SerializeField] private RuntimeStyleIntentController runtimeStyleIntentController;

    [Header("Persistence")]
    [SerializeField] private string reviewFolderName = "GeneratedObjectReviews";
    [SerializeField] private bool writeReviewRecords = true;
    [SerializeField] private bool hideRejectedCandidates = true;
    [SerializeField] private bool reapplyFallbackOnReset = true;
    [SerializeField] private bool restoreReviewOnRuntimeLoad = true;
    [SerializeField] private bool restoreAcceptedOrCorrectedOnEnable = true;
    [SerializeField] private bool releaseRejectedOrResetRuntimeModels = true;
    [SerializeField, Min(0f)] private float startupRestoreDelaySeconds = 1.5f;
    [SerializeField] private bool filterRestoreByCurrentRoom = true;
    [SerializeField] private bool filterRestoreByCurrentStyle = true;

    [Header("Runtime State")]
    [SerializeField] private RuntimeGeneratedModelInstance selectedInstance;
    [SerializeField, TextArea(4, 8)] private string latestSummary = "[GeneratedObjectReview]\nState: waiting";

    public event Action ReviewChanged;

    public RuntimeGeneratedModelInstance SelectedInstance => selectedInstance;
    public string LatestSummary => latestSummary;
    public bool ReleaseRejectedOrResetRuntimeModels => releaseRejectedOrResetRuntimeModels;

    private RuntimeGeneratedModelLoader _subscribedRuntimeLoader;
    private Coroutine _startupRestoreRoutine;

    private void Reset()
    {
        ResolveReferences();
    }

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();
        SubscribeToRuntimeLoader();
        SelectLatestRuntimeInstance();
        ScheduleStartupRestore();
    }

    private void OnDisable()
    {
        if (_startupRestoreRoutine != null)
        {
            StopCoroutine(_startupRestoreRoutine);
            _startupRestoreRoutine = null;
        }

        UnsubscribeFromRuntimeLoader();
    }

    [ContextMenu("Select Latest Runtime Generated Object")]
    public void SelectLatestRuntimeInstance()
    {
        ResolveReferences();
        SubscribeToRuntimeLoader();
        var instance = runtimeGeneratedModelLoader != null
            ? runtimeGeneratedModelLoader.LastLoadedInstance
            : null;

        if (instance == null)
        {
            var instances = FindObjectsByType<RuntimeGeneratedModelInstance>(FindObjectsInactive.Exclude);
            if (instances.Length > 0)
            {
                instance = instances[^1];
            }
        }

        Select(instance);
    }

    public void Select(RuntimeGeneratedModelInstance instance)
    {
        selectedInstance = instance;
        if (selectedInstance == null)
        {
            PublishSummary("select", "No runtime generated candidate is selected.");
            return;
        }

        if (correctionModeController != null)
        {
            correctionModeController.SelectAppliedObject(
                selectedInstance.transform,
                selectedInstance.ObjectId,
                selectedInstance.RequestId,
                selectedInstance.SemanticLabel,
                "Runtime generated object",
                true);
        }

        if (TryReadReviewRecord(selectedInstance, out var reviewRecord))
        {
            ApplyPersistedReview(reviewRecord);
            PublishSummary("select", $"Selected {selectedInstance.DisplayLabel}; restored review={reviewRecord.ReviewState}.");
            return;
        }

        PublishSummary("select", $"Selected {selectedInstance.DisplayLabel}; review={selectedInstance.ReviewState}.");
    }

    public void RestoreReviewFor(RuntimeGeneratedModelInstance instance)
    {
        if (instance == null)
        {
            return;
        }

        Select(instance);
    }

    public bool TryRunPreDeviceReviewPersistenceProbe(
        RuntimeGeneratedModelInstance instance,
        CorrectionDelta correction,
        out string detail)
    {
        detail = string.Empty;
        ResolveReferences();
        if (!Application.isPlaying)
        {
            detail = "Review persistence probe must run in Play Mode or a Quest build.";
            return false;
        }

        if (!writeReviewRecords)
        {
            detail = "writeReviewRecords is disabled.";
            return false;
        }

        if (instance == null)
        {
            detail = "RuntimeGeneratedModelInstance is missing.";
            return false;
        }

        correction = EnsureUsableCorrection(correction);
        if (correction.IsIdentity)
        {
            correction.PositionOffset = Vector3.forward * 0.025f;
            correction.EulerOffset = new Vector3(0f, 5f, 0f);
            correction.ScaleMultiplier = Vector3.one;
            correction.Confirmed = true;
        }

        var states = new[]
        {
            GeneratedObjectReviewState.Accepted,
            GeneratedObjectReviewState.Rejected,
            GeneratedObjectReviewState.ResetToFallback,
            GeneratedObjectReviewState.Corrected,
        };
        var probeId = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
        var written = 0;
        var deleted = 0;
        var reviewDirectory = Path.Combine(Application.persistentDataPath, reviewFolderName);

        try
        {
            Directory.CreateDirectory(reviewDirectory);
            foreach (var state in states)
            {
                var stateCorrection = state == GeneratedObjectReviewState.Corrected
                    ? correction
                    : CorrectionDelta.Identity;
                var record = BuildReviewRecord(
                    instance,
                    state,
                    stateCorrection,
                    $"pre-device review persistence probe {state}");
                record.RequestId = $"{record.RequestId}_probe_{probeId}_{state}";
                var path = GetReviewRecordPath(record);
                File.WriteAllText(path, JsonUtility.ToJson(record, true));
                written++;

                var loaded = TryReadJson<GeneratedObjectReviewRecord>(path);
                if (!ProbeRecordMatches(record, loaded))
                {
                    detail = $"Roundtrip mismatch for {state}: {path}";
                    return false;
                }

                File.Delete(path);
                deleted++;
            }

            detail = $"states={states.Length}, written={written}, deleted={deleted}, folder={reviewDirectory}";
            return written == states.Length && deleted == states.Length;
        }
        catch (Exception exception)
        {
            detail = exception.Message;
            return false;
        }
    }

    [ContextMenu("Restore Latest Accepted/Corrected Runtime Model")]
    public async void RestoreLatestAcceptedOrCorrectedRuntimeModel()
    {
        await RestoreLatestAcceptedOrCorrectedRuntimeModelAsync();
    }

    public async Task<RuntimeGeneratedModelInstance> RestoreLatestAcceptedOrCorrectedRuntimeModelAsync()
    {
        ResolveReferences();
        SubscribeToRuntimeLoader();

        if (!Application.isPlaying)
        {
            PublishSummary("restore-blocked", "Accepted/corrected runtime model restore must run in Play Mode or a Quest build.");
            return null;
        }

        if (runtimeGeneratedModelLoader == null)
        {
            PublishSummary("restore", "RuntimeGeneratedModelLoader is missing.");
            return null;
        }

        if (!TryFindLatestRestorableReview(out var reviewRecord, out var reviewPath))
        {
            PublishSummary("restore", "No accepted/corrected runtime generated review record with a usable model source was found.");
            return null;
        }

        var record = BuildRuntimeRecordFromReview(reviewRecord);
        var jobPath = !string.IsNullOrWhiteSpace(reviewRecord.SourceJobPath) && File.Exists(reviewRecord.SourceJobPath)
            ? reviewRecord.SourceJobPath
            : null;

        PublishSummary("restore", $"Loading accepted/corrected candidate from {Path.GetFileName(reviewPath)}.");
        var instance = await runtimeGeneratedModelLoader.LoadFromRecordAsync(record, jobPath);
        if (instance == null)
        {
            PublishSummary("restore-failed", $"Runtime loader could not restore {ShortId(reviewRecord.RequestId)}.");
            return null;
        }

        if (selectedInstance != instance)
        {
            RestoreReviewFor(instance);
        }

        PublishSummary("restore", $"Restored {reviewRecord.ReviewState}: {instance.DisplayLabel}.");
        ReviewChanged?.Invoke();
        return instance;
    }

    [ContextMenu("Accept Selected Generated Object")]
    public void AcceptSelected()
    {
        EnsureSelection();
        if (selectedInstance == null)
        {
            PublishSummary("accept", "No runtime generated candidate is selected.");
            return;
        }

        var correction = correctionModeController != null
            ? correctionModeController.GetConfirmedOrCurrentDelta()
            : CorrectionDelta.Identity;
        var state = !correction.IsIdentity ? GeneratedObjectReviewState.Corrected : GeneratedObjectReviewState.Accepted;
        PersistDecision(state, correction, "accepted runtime generated candidate");
        selectedInstance.gameObject.SetActive(true);
    }

    [ContextMenu("Reject Selected Generated Object")]
    public void RejectSelected()
    {
        EnsureSelection();
        if (selectedInstance == null)
        {
            PublishSummary("reject", "No runtime generated candidate is selected.");
            return;
        }

        PersistDecision(GeneratedObjectReviewState.Rejected, CorrectionDelta.Identity, "rejected runtime generated candidate");
        if (hideRejectedCandidates)
        {
            selectedInstance.gameObject.SetActive(false);
        }

        ReleaseSelectedRuntimeCandidate("reject-selected", out _);
    }

    [ContextMenu("Reset Selected To Fallback")]
    public void ResetSelectedToFallback()
    {
        EnsureSelection();
        if (selectedInstance == null)
        {
            PublishSummary("reset", "No runtime generated candidate is selected.");
            return;
        }

        PersistDecision(GeneratedObjectReviewState.ResetToFallback, CorrectionDelta.Identity, "reset generated candidate to deterministic fallback");
        selectedInstance.gameObject.SetActive(false);

        if (reapplyFallbackOnReset)
        {
            if (anchorThemeApplier != null &&
                anchorThemeApplier.ForceDeterministicFallbackForObject(selectedInstance.ObjectId, out var fallbackDetail))
            {
                PublishSummary("reset", $"Reset {selectedInstance.DisplayLabel}; deterministic fallback active. {fallbackDetail}");
            }
            else
            {
                anchorThemeApplier?.ReapplyActiveTheme();
            }
        }

        ReleaseSelectedRuntimeCandidate("reset-selected-to-fallback", out _);
    }

    [ContextMenu("Persist Current Correction")]
    public void PersistCurrentCorrection()
    {
        EnsureSelection();
        if (selectedInstance == null)
        {
            PublishSummary("correction", "No runtime generated candidate is selected.");
            return;
        }

        var correction = correctionModeController != null
            ? correctionModeController.GetConfirmedOrCurrentDelta()
            : CorrectionDelta.Identity;
        PersistDecision(GeneratedObjectReviewState.Corrected, correction, "persisted bounded runtime correction");
    }

    private void PersistDecision(GeneratedObjectReviewState state, CorrectionDelta correction, string note)
    {
        correction = EnsureUsableCorrection(correction);
        selectedInstance.SetReviewState(state, correction, note);

        var record = BuildReviewRecord(selectedInstance, state, correction, note);
        WriteReviewRecord(record);
        UpdateJobReviewState(selectedInstance.JobPath, state, correction, note);

        PublishSummary("decision", $"{state}: {selectedInstance.DisplayLabel}");
        ReviewChanged?.Invoke();
    }

    private bool ReleaseSelectedRuntimeCandidate(string reason, out string detail)
    {
        detail = string.Empty;
        if (!releaseRejectedOrResetRuntimeModels || selectedInstance == null)
        {
            detail = $"releaseEnabled={releaseRejectedOrResetRuntimeModels}, selected={selectedInstance != null}";
            return false;
        }

        ResolveReferences();
        if (runtimeGeneratedModelLoader == null)
        {
            detail = "RuntimeGeneratedModelLoader is missing.";
            return false;
        }

        var releasedLabel = selectedInstance.DisplayLabel;
        var released = runtimeGeneratedModelLoader.TryReleaseRuntimeInstance(selectedInstance, reason, out detail);
        if (!released)
        {
            return false;
        }

        selectedInstance = null;
        correctionModeController?.ClearSelection();
        PublishSummary("release", $"Released hidden runtime candidate {releasedLabel}. {detail}");
        ReviewChanged?.Invoke();
        return true;
    }

    private void EnsureSelection()
    {
        if (selectedInstance == null || !selectedInstance.gameObject.scene.IsValid())
        {
            SelectLatestRuntimeInstance();
        }
    }

    private void ApplyPersistedReview(GeneratedObjectReviewRecord reviewRecord)
    {
        if (selectedInstance == null || reviewRecord == null)
        {
            return;
        }

        selectedInstance.SetReviewState(reviewRecord.ReviewState, reviewRecord.Correction, reviewRecord.Note);
        if (reviewRecord.ReviewState == GeneratedObjectReviewState.Corrected ||
            reviewRecord.ReviewState == GeneratedObjectReviewState.Accepted)
        {
            if (reviewRecord.ReviewState == GeneratedObjectReviewState.Corrected)
            {
                selectedInstance.ApplyPersistedCorrection(reviewRecord.Correction);
            }

            selectedInstance.gameObject.SetActive(true);
            return;
        }

        if (reviewRecord.ReviewState == GeneratedObjectReviewState.Rejected ||
            reviewRecord.ReviewState == GeneratedObjectReviewState.ResetToFallback)
        {
            selectedInstance.gameObject.SetActive(false);
            if (reviewRecord.ReviewState == GeneratedObjectReviewState.ResetToFallback && reapplyFallbackOnReset)
            {
                if (anchorThemeApplier != null &&
                    anchorThemeApplier.ForceDeterministicFallbackForObject(selectedInstance.ObjectId, out var fallbackDetail))
                {
                    PublishSummary("reset-restore", $"Restored reset fallback for {selectedInstance.DisplayLabel}. {fallbackDetail}");
                }
                else
                {
                    anchorThemeApplier?.ReapplyActiveTheme();
                }
            }

            ReleaseSelectedRuntimeCandidate($"restore-{reviewRecord.ReviewState}", out _);
        }
    }

    private GeneratedObjectReviewRecord BuildReviewRecord(
        RuntimeGeneratedModelInstance instance,
        GeneratedObjectReviewState state,
        CorrectionDelta correction,
        string note)
    {
        return new GeneratedObjectReviewRecord
        {
            RequestId = instance.RequestId,
            ObjectId = instance.ObjectId,
            RoomId = instance.RoomId,
            ThemeId = instance.ThemeId,
            StyleVariantId = instance.StyleVariantId,
            SemanticLabel = instance.SemanticLabel,
            ModelLocalPath = instance.ModelLocalPath,
            ModelUrl = instance.ModelUrl,
            ModelHash = instance.ModelHash,
            SourceJobPath = instance.JobPath,
            SourceRequestPath = TryReadGeneratedAssetRecord(instance.JobPath)?.SourceRequestPath,
            ReviewState = state,
            Correction = correction,
            DecisionIsoUtc = DateTime.UtcNow.ToString("O"),
            Note = note,
        };
    }

    private void WriteReviewRecord(GeneratedObjectReviewRecord record)
    {
        if (!writeReviewRecords || record == null)
        {
            return;
        }

        try
        {
            var path = GetReviewRecordPath(record);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? Application.persistentDataPath);
            File.WriteAllText(path, JsonUtility.ToJson(record, true));
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[GeneratedObjectReview] Failed to write review record: {exception.Message}");
        }
    }

    private bool TryReadReviewRecord(RuntimeGeneratedModelInstance instance, out GeneratedObjectReviewRecord record)
    {
        record = null;
        if (instance == null)
        {
            return false;
        }

        var template = new GeneratedObjectReviewRecord
        {
            RequestId = instance.RequestId,
            ObjectId = instance.ObjectId,
            RoomId = instance.RoomId,
            ThemeId = instance.ThemeId,
            StyleVariantId = instance.StyleVariantId,
        };
        var path = GetReviewRecordPath(template);
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            var json = File.ReadAllText(path);
            record = string.IsNullOrWhiteSpace(json) ? null : JsonUtility.FromJson<GeneratedObjectReviewRecord>(json);
            if (record != null)
            {
                record.Correction = EnsureUsableCorrection(record.Correction);
            }

            return record != null;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[GeneratedObjectReview] Failed to read review record {path}: {exception.Message}");
            return false;
        }
    }

    private string GetReviewRecordPath(GeneratedObjectReviewRecord record)
    {
        var key = $"{SafeName(record.RoomId)}_{SafeName(record.ObjectId)}_{SafeName(record.ThemeId)}_{SafeName(record.StyleVariantId)}";
        if (!string.IsNullOrWhiteSpace(record.RequestId))
        {
            key += $"_{SafeName(record.RequestId)}";
        }

        return Path.Combine(Application.persistentDataPath, reviewFolderName, $"{key}.review.json");
    }

    private void UpdateJobReviewState(string jobPath, GeneratedObjectReviewState state, CorrectionDelta correction, string note)
    {
        if (string.IsNullOrWhiteSpace(jobPath) || !File.Exists(jobPath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(jobPath);
            var record = string.IsNullOrWhiteSpace(json) ? null : JsonUtility.FromJson<GeneratedAssetRecord>(json);
            if (record == null)
            {
                return;
            }

            record.ReviewState = state;
            record.PersistedCorrection = EnsureUsableCorrection(correction);
            record.ReviewDecisionIsoUtc = DateTime.UtcNow.ToString("O");
            record.ReviewNote = note;
            record.UpdatedAtIsoUtc = DateTime.UtcNow.ToString("O");
            File.WriteAllText(jobPath, JsonUtility.ToJson(record, true));
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[GeneratedObjectReview] Failed to update job review state {jobPath}: {exception.Message}");
        }
    }

    private void ResolveReferences()
    {
        if (runtimeGeneratedModelLoader == null)
        {
            runtimeGeneratedModelLoader = FindAnyObjectByType<RuntimeGeneratedModelLoader>();
        }

        if (correctionModeController == null)
        {
            correctionModeController = FindAnyObjectByType<CorrectionModeController>();
        }

        if (anchorThemeApplier == null)
        {
            anchorThemeApplier = FindAnyObjectByType<AnchorThemeApplier>();
        }

        if (roomSemanticBootstrap == null)
        {
            roomSemanticBootstrap = FindAnyObjectByType<RoomSemanticBootstrap>();
        }

        if (themeIntentController == null)
        {
            themeIntentController = FindAnyObjectByType<ThemeIntentController>();
        }

        if (runtimeStyleIntentController == null)
        {
            runtimeStyleIntentController = FindAnyObjectByType<RuntimeStyleIntentController>();
        }
    }

    private void ScheduleStartupRestore()
    {
        if (!restoreAcceptedOrCorrectedOnEnable || !Application.isPlaying)
        {
            return;
        }

        if (_startupRestoreRoutine != null)
        {
            StopCoroutine(_startupRestoreRoutine);
        }

        _startupRestoreRoutine = StartCoroutine(RestoreAcceptedOrCorrectedAfterDelay());
    }

    private IEnumerator RestoreAcceptedOrCorrectedAfterDelay()
    {
        if (startupRestoreDelaySeconds > 0f)
        {
            yield return new WaitForSeconds(startupRestoreDelaySeconds);
        }

        _startupRestoreRoutine = null;
        if (!isActiveAndEnabled ||
            runtimeGeneratedModelLoader == null ||
            runtimeGeneratedModelLoader.LastLoadedInstance != null)
        {
            yield break;
        }

        _ = RestoreLatestAcceptedOrCorrectedRuntimeModelAsync();
    }

    private void SubscribeToRuntimeLoader()
    {
        if (!restoreReviewOnRuntimeLoad)
        {
            UnsubscribeFromRuntimeLoader();
            return;
        }

        if (_subscribedRuntimeLoader == runtimeGeneratedModelLoader)
        {
            return;
        }

        UnsubscribeFromRuntimeLoader();
        if (runtimeGeneratedModelLoader == null)
        {
            return;
        }

        _subscribedRuntimeLoader = runtimeGeneratedModelLoader;
        _subscribedRuntimeLoader.SummaryChanged += OnRuntimeModelLoaderSummaryChanged;
    }

    private void UnsubscribeFromRuntimeLoader()
    {
        if (_subscribedRuntimeLoader != null)
        {
            _subscribedRuntimeLoader.SummaryChanged -= OnRuntimeModelLoaderSummaryChanged;
            _subscribedRuntimeLoader = null;
        }
    }

    private void OnRuntimeModelLoaderSummaryChanged()
    {
        if (!restoreReviewOnRuntimeLoad || runtimeGeneratedModelLoader == null)
        {
            return;
        }

        var loadedInstance = runtimeGeneratedModelLoader.LastLoadedInstance;
        if (loadedInstance == null)
        {
            if (selectedInstance != null && !selectedInstance.gameObject.scene.IsValid())
            {
                selectedInstance = null;
                PublishSummary("select", "Runtime generated candidate was cleared.");
                ReviewChanged?.Invoke();
            }

            return;
        }

        if (selectedInstance == loadedInstance)
        {
            return;
        }

        RestoreReviewFor(loadedInstance);
        ReviewChanged?.Invoke();
    }

    private void PublishSummary(string state, string detail)
    {
        latestSummary =
            "[GeneratedObjectReview]\n" +
            $"State: {state}\n" +
            $"Detail: {detail}";
    }

    private bool TryFindLatestRestorableReview(out GeneratedObjectReviewRecord bestRecord, out string bestPath)
    {
        bestRecord = null;
        bestPath = null;
        var reviewDirectory = Path.Combine(Application.persistentDataPath, reviewFolderName);
        if (!Directory.Exists(reviewDirectory))
        {
            return false;
        }

        var bestTime = DateTime.MinValue;
        foreach (var path in Directory.GetFiles(reviewDirectory, "*.review.json", SearchOption.TopDirectoryOnly))
        {
            var record = TryReadJson<GeneratedObjectReviewRecord>(path);
            if (record == null ||
                !IsAcceptedOrCorrected(record.ReviewState) ||
                !HasUsableModelSource(record) ||
                !MatchesCurrentRestoreContext(record))
            {
                continue;
            }

            record.Correction = EnsureUsableCorrection(record.Correction);
            var decisionTime = DateTime.TryParse(record.DecisionIsoUtc, out var parsed)
                ? parsed.ToUniversalTime()
                : File.GetLastWriteTimeUtc(path);
            if (bestRecord != null && decisionTime <= bestTime)
            {
                continue;
            }

            bestRecord = record;
            bestPath = path;
            bestTime = decisionTime;
        }

        return bestRecord != null;
    }

    private GeneratedAssetRecord BuildRuntimeRecordFromReview(GeneratedObjectReviewRecord reviewRecord)
    {
        var sourceRecord = TryReadGeneratedAssetRecord(reviewRecord.SourceJobPath);
        var record = sourceRecord ?? new GeneratedAssetRecord();
        record.RequestId = FirstNonEmpty(record.RequestId, reviewRecord.RequestId);
        record.ObjectId = FirstNonEmpty(record.ObjectId, reviewRecord.ObjectId);
        record.ThemeId = FirstNonEmpty(record.ThemeId, reviewRecord.ThemeId);
        record.StyleVariantId = NormalizeStyleVariantId(FirstNonEmpty(record.StyleVariantId, reviewRecord.StyleVariantId));
        record.SourceRequestPath = FirstNonEmpty(record.SourceRequestPath, reviewRecord.SourceRequestPath);
        record.RuntimeModelUrl = FirstNonEmpty(record.RuntimeModelUrl, reviewRecord.ModelUrl);
        record.RuntimeModelLocalPath = FirstNonEmpty(record.RuntimeModelLocalPath, reviewRecord.ModelLocalPath);
        record.RuntimeModelHash = FirstNonEmpty(record.RuntimeModelHash, reviewRecord.ModelHash);
        record.ReviewState = reviewRecord.ReviewState;
        record.PersistedCorrection = EnsureUsableCorrection(reviewRecord.Correction);
        record.ReviewDecisionIsoUtc = reviewRecord.DecisionIsoUtc;
        record.ReviewNote = reviewRecord.Note;
        record.State = File.Exists(record.RuntimeModelLocalPath)
            ? GeneratedObjectJobState.RuntimeModelDownloaded
            : GeneratedObjectJobState.RuntimeModelReady;
        record.StatusNote = "Restoring accepted/corrected runtime generated candidate from persisted review.";
        record.FailureReason = string.Empty;
        record.UpdatedAtIsoUtc = DateTime.UtcNow.ToString("O");
        return record;
    }

    private bool MatchesCurrentRestoreContext(GeneratedObjectReviewRecord record)
    {
        if (filterRestoreByCurrentRoom)
        {
            var currentRoomId = ResolveCurrentRoomId();
            if (!IsUnknown(currentRoomId) &&
                (IsUnknown(record.RoomId) ||
                 !string.Equals(record.RoomId, currentRoomId, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }
        }

        if (filterRestoreByCurrentStyle)
        {
            var currentThemeId = ResolveCurrentThemeId();
            if (!string.IsNullOrWhiteSpace(currentThemeId) &&
                (string.IsNullOrWhiteSpace(record.ThemeId) ||
                 !string.Equals(record.ThemeId, currentThemeId, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            var currentStyleVariantId = NormalizeStyleVariantId(ResolveCurrentStyleVariantId());
            var recordStyleVariantId = NormalizeStyleVariantId(record.StyleVariantId);
            if (!string.IsNullOrWhiteSpace(currentStyleVariantId) &&
                !string.Equals(recordStyleVariantId, currentStyleVariantId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private bool HasUsableModelSource(GeneratedObjectReviewRecord record)
    {
        if (!string.IsNullOrWhiteSpace(record.ModelLocalPath) && File.Exists(record.ModelLocalPath))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(record.ModelUrl))
        {
            return true;
        }

        var sourceRecord = TryReadGeneratedAssetRecord(record.SourceJobPath);
        return sourceRecord != null &&
               (!string.IsNullOrWhiteSpace(sourceRecord.RuntimeModelUrl) ||
                (!string.IsNullOrWhiteSpace(sourceRecord.RuntimeModelLocalPath) && File.Exists(sourceRecord.RuntimeModelLocalPath)));
    }

    private string ResolveCurrentRoomId()
    {
        return roomSemanticBootstrap != null && roomSemanticBootstrap.CurrentRoom != null
            ? roomSemanticBootstrap.CurrentRoom.name
            : "unknown_room";
    }

    private string ResolveCurrentThemeId()
    {
        var theme = themeIntentController != null ? themeIntentController.ActiveTheme : null;
        return theme != null
            ? RuntimeStyleIntentRequestUtility.BuildEffectiveThemeId(theme, runtimeStyleIntentController != null ? runtimeStyleIntentController.CurrentIntent : null)
            : string.Empty;
    }

    private string ResolveCurrentStyleVariantId()
    {
        return SurfaceTexturePromptBuilder.BuildStyleVariantId(
            runtimeStyleIntentController != null ? runtimeStyleIntentController.CurrentIntent : null);
    }

    private static GeneratedAssetRecord TryReadGeneratedAssetRecord(string path)
    {
        return TryReadJson<GeneratedAssetRecord>(path);
    }

    private static T TryReadJson<T>(string path) where T : class
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return null;
            }

            var json = File.ReadAllText(path);
            return string.IsNullOrWhiteSpace(json) ? null : JsonUtility.FromJson<T>(json);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[GeneratedObjectReview] Failed to read JSON {path}: {exception.Message}");
            return null;
        }
    }

    private static bool IsAcceptedOrCorrected(GeneratedObjectReviewState state)
    {
        return state == GeneratedObjectReviewState.Accepted || state == GeneratedObjectReviewState.Corrected;
    }

    private static bool ProbeRecordMatches(
        GeneratedObjectReviewRecord expected,
        GeneratedObjectReviewRecord actual)
    {
        if (expected == null || actual == null)
        {
            return false;
        }

        if (!string.Equals(expected.RequestId, actual.RequestId, StringComparison.Ordinal) ||
            !string.Equals(expected.ObjectId, actual.ObjectId, StringComparison.Ordinal) ||
            !string.Equals(expected.RoomId, actual.RoomId, StringComparison.Ordinal) ||
            !string.Equals(expected.ThemeId, actual.ThemeId, StringComparison.Ordinal) ||
            !string.Equals(expected.StyleVariantId, actual.StyleVariantId, StringComparison.Ordinal) ||
            !string.Equals(expected.SemanticLabel, actual.SemanticLabel, StringComparison.Ordinal) ||
            expected.ReviewState != actual.ReviewState)
        {
            return false;
        }

        if (expected.ReviewState != GeneratedObjectReviewState.Corrected)
        {
            return true;
        }

        return !actual.Correction.IsIdentity &&
               actual.Correction.Confirmed &&
               Mathf.Abs(actual.Correction.EulerOffset.y) > 0.001f;
    }

    private static bool IsUnknown(string value)
    {
        return string.IsNullOrWhiteSpace(value) || string.Equals(value, "unknown_room", StringComparison.OrdinalIgnoreCase);
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
        return string.IsNullOrWhiteSpace(value) ? SurfaceTexturePromptBuilder.PresetStyleVariantId : value.Trim();
    }

    private static string ShortId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        return value.Length <= 18 ? value : value[..18];
    }

    private static string SafeName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalid, '_');
        }

        return value.Replace(' ', '_');
    }
}

[Serializable]
public sealed class GeneratedObjectReviewRecord
{
    public string RequestId;
    public string ObjectId;
    public string RoomId;
    public string ThemeId;
    public string StyleVariantId = "preset";
    public string SemanticLabel;
    public string ModelLocalPath;
    public string ModelUrl;
    public string ModelHash;
    public string SourceJobPath;
    public string SourceRequestPath;
    public GeneratedObjectReviewState ReviewState = GeneratedObjectReviewState.None;
    public CorrectionDelta Correction = CorrectionDelta.Identity;
    public string DecisionIsoUtc;
    public string Note;
}
