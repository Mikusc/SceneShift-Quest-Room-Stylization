using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Meta.XR.MRUtilityKit;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class PreDeviceSmokeReportRunner : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private RoomSemanticBootstrap roomSemanticBootstrap;
    [SerializeField] private ThemeIntentController themeIntentController;
    [SerializeField] private RuntimeStyleIntentController runtimeStyleIntentController;
    [SerializeField] private StylizationPlanner stylizationPlanner;
    [SerializeField] private SurfaceOverrideApplier surfaceOverrideApplier;
    [SerializeField] private MRUKShellVisibilityToggle shellVisibilityToggle;
    [SerializeField] private GenerationJobWorldStatusOverlay worldStatusOverlay;
    [SerializeField] private PassthroughOnlyVisibilityToggle passthroughOnlyVisibilityToggle;
    [SerializeField] private SceneShiftUISetDashboard dashboard;
    [SerializeField] private GenerationQueueStatusService generationQueueStatusService;
    [SerializeField] private PreDeviceRuntimeLoopValidator runtimeLoopValidator;
    [SerializeField] private QuestRuntimeGenerationClient runtimeGenerationClient;
    [SerializeField] private RuntimeGeneratedModelLoader runtimeGeneratedModelLoader;
    [SerializeField] private GeneratedObjectReviewController generatedObjectReviewController;
    [SerializeField] private CorrectionModeController correctionModeController;
    [SerializeField] private AnchorThemeApplier anchorThemeApplier;

    [Header("Report")]
    [SerializeField] private string reportFolderName = "PreDeviceSmokeReports";
    [SerializeField] private bool writeMarkdownReport = true;
    [SerializeField, TextArea(4, 10)] private string latestSummary = "[PreDeviceSmokeReport]\nState: waiting";

    public string LatestSummary => latestSummary;
    public string LastReportPath => lastReportPath;

    private string lastReportPath = string.Empty;

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
    }

    [ContextMenu("Run Pre-Device Smoke Report")]
    public void RunPreDeviceSmokeReport()
    {
        RunSmokeReport();
    }

    public PreDeviceSmokeReport RunSmokeReport()
    {
        ResolveReferences();
        var report = new PreDeviceSmokeReport
        {
            ReportId = $"predevice_smoke_{DateTime.UtcNow:yyyyMMddHHmmss}",
            CreatedAtIsoUtc = DateTime.UtcNow.ToString("O"),
        };

        AddCheck(
            report,
            "play_mode",
            Application.isPlaying ? PreDeviceSmokeStatus.Pass : PreDeviceSmokeStatus.Fail,
            Application.isPlaying
                ? "Editor is in Play Mode."
                : "Enter Play Mode before running the pre-device smoke report.");

        RefreshRuntimeSystems();
        CheckRoom(report);
        CheckStyleAndPlan(report);
        CheckSurfaceOverrides(report);
        CheckRuntimeGeneratedObjectWiring(report);
        CheckDashboard(report);
        CheckCleanViewToggle(report);
        CheckPassthroughOnlyToggle(report);
        CheckManualVisualGates(report);

        report.OverallStatus = BuildOverallStatus(report);
        WriteReport(report);
        latestSummary = BuildSummary(report);
        Debug.Log(latestSummary, this);
        return report;
    }

    private void CheckRoom(PreDeviceSmokeReport report)
    {
        if (roomSemanticBootstrap == null)
        {
            AddCheck(report, "room_bootstrap", PreDeviceSmokeStatus.Fail, "RoomSemanticBootstrap is missing.");
            return;
        }

        report.RoomSummary = roomSemanticBootstrap.LatestPanelSummary;
        if (!roomSemanticBootstrap.HasReadyRoom || roomSemanticBootstrap.CurrentRoom == null)
        {
            AddCheck(report, "room_ready", PreDeviceSmokeStatus.Fail, roomSemanticBootstrap.LatestPanelSummary);
            return;
        }

        var room = roomSemanticBootstrap.CurrentRoom;
        var tableCount = CountAnchorsWithLabel(room, MRUKAnchor.SceneLabels.TABLE);
        var storageCount = CountAnchorsWithLabel(room, MRUKAnchor.SceneLabels.STORAGE);
        var seatingCount = CountAnchorsWithLabel(room, MRUKAnchor.SceneLabels.COUCH);
        var detail =
            $"room={room.name}, anchors={room.Anchors.Count}, walls={room.WallAnchors.Count}, floors={room.FloorAnchors.Count}, ceilings={room.CeilingAnchors.Count}, table={tableCount}, storage={storageCount}, seating={seatingCount}";
        AddCheck(report, "room_ready", room.Anchors.Count > 0 ? PreDeviceSmokeStatus.Pass : PreDeviceSmokeStatus.Fail, detail);
        AddCheck(report, "safe_table_target", tableCount > 0 ? PreDeviceSmokeStatus.Pass : PreDeviceSmokeStatus.Warn, $"TABLE anchors={tableCount}");
    }

    private void CheckStyleAndPlan(PreDeviceSmokeReport report)
    {
        var activeTheme = themeIntentController != null ? themeIntentController.ActiveTheme : null;
        var currentIntent = runtimeStyleIntentController != null ? runtimeStyleIntentController.CurrentIntent : null;
        var styleId = RuntimeStyleIntentRequestUtility.BuildEffectiveThemeId(activeTheme, currentIntent);
        var styleDisplay = RuntimeStyleIntentRequestUtility.BuildEffectiveThemeDisplayName(activeTheme, currentIntent);
        report.StyleSummary = $"style={styleDisplay}, id={styleId}, variant={SurfaceTexturePromptBuilder.BuildStyleVariantId(currentIntent)}";
        AddCheck(
            report,
            "style_identity",
            activeTheme != null ? PreDeviceSmokeStatus.Pass : PreDeviceSmokeStatus.Fail,
            report.StyleSummary);

        if (stylizationPlanner == null)
        {
            AddCheck(report, "stylization_plan", PreDeviceSmokeStatus.Fail, "StylizationPlanner is missing.");
            return;
        }

        stylizationPlanner.RebuildStylizationPlan();
        var plan = stylizationPlanner.CurrentPlan;
        report.PlanSummary = stylizationPlanner.LatestSummary;
        var planStatus = PreDeviceSmokeStatus.Fail;
        if (plan != null && plan.EntryCount > 0)
        {
            planStatus = plan.WarningCount > 0 ? PreDeviceSmokeStatus.Warn : PreDeviceSmokeStatus.Pass;
        }

        AddCheck(
            report,
            "stylization_plan",
            planStatus,
            plan != null ? $"entries={plan.EntryCount}, warnings={plan.WarningCount}" : "CurrentPlan is null.");

        AddCheck(
            report,
            "core_semantic_coverage",
            HasPlanSemantic(plan, "wall") && HasPlanSemantic(plan, "floor") && HasPlanSemantic(plan, "ceiling") && HasPlanSemantic(plan, "table")
                ? PreDeviceSmokeStatus.Pass
                : PreDeviceSmokeStatus.Warn,
            BuildPlanCoverageLine(plan));
    }

    private void CheckSurfaceOverrides(PreDeviceSmokeReport report)
    {
        if (surfaceOverrideApplier == null)
        {
            AddCheck(report, "surface_overrides", PreDeviceSmokeStatus.Fail, "SurfaceOverrideApplier is missing.");
            return;
        }

        if (Application.isPlaying)
        {
            surfaceOverrideApplier.ReapplySurfaceOverrides();
        }

        report.SurfaceSummary = surfaceOverrideApplier.LatestSummary;
        var applied = ContainsLine(surfaceOverrideApplier.LatestSummary, "State: applied");
        AddCheck(
            report,
            "surface_overrides",
            applied ? PreDeviceSmokeStatus.Pass : PreDeviceSmokeStatus.Warn,
            FirstLine(surfaceOverrideApplier.LatestSummary, "State:"));
    }

    private void CheckRuntimeGeneratedObjectWiring(PreDeviceSmokeReport report)
    {
        AddCheck(report, "runtime_loop_validator", runtimeLoopValidator != null ? PreDeviceSmokeStatus.Pass : PreDeviceSmokeStatus.Fail, runtimeLoopValidator != null ? runtimeLoopValidator.LatestSummary : "PreDeviceRuntimeLoopValidator is missing.");
        AddCheck(report, "runtime_client", runtimeGenerationClient != null ? PreDeviceSmokeStatus.Pass : PreDeviceSmokeStatus.Fail, runtimeGenerationClient != null ? runtimeGenerationClient.LatestSummary : "QuestRuntimeGenerationClient is missing.");
        AddCheck(report, "runtime_loader", runtimeGeneratedModelLoader != null ? PreDeviceSmokeStatus.Pass : PreDeviceSmokeStatus.Fail, runtimeGeneratedModelLoader != null ? runtimeGeneratedModelLoader.LatestSummary : "RuntimeGeneratedModelLoader is missing.");
        AddCheck(report, "review_controller", generatedObjectReviewController != null ? PreDeviceSmokeStatus.Pass : PreDeviceSmokeStatus.Fail, generatedObjectReviewController != null ? generatedObjectReviewController.LatestSummary : "GeneratedObjectReviewController is missing.");
        AddCheck(report, "correction_controller", correctionModeController != null ? PreDeviceSmokeStatus.Pass : PreDeviceSmokeStatus.Fail, correctionModeController != null ? correctionModeController.LatestSummary : "CorrectionModeController is missing.");
        AddCheck(report, "fallback_applier", anchorThemeApplier != null ? PreDeviceSmokeStatus.Pass : PreDeviceSmokeStatus.Fail, anchorThemeApplier != null ? anchorThemeApplier.LatestSummary : "AnchorThemeApplier is missing.");

        generationQueueStatusService?.Refresh();
        report.QueueSummary = generationQueueStatusService != null ? generationQueueStatusService.LatestSummary : string.Empty;
        AddCheck(report, "queue_status", generationQueueStatusService != null ? PreDeviceSmokeStatus.Pass : PreDeviceSmokeStatus.Warn, report.QueueSummary);
        CheckRuntimeRequestJobContract(report);
        CheckRuntimeBackendArtifactContract(report);
        CheckRuntimeLoadedInstanceMetadata(report);
        CheckRuntimeReviewEditabilityPersistence(report);
        CheckRuntimeResetDeterministicFallback(report);
        CheckRuntimeRejectResetReleasePolicy(report);
    }

    private void CheckRuntimeRequestJobContract(PreDeviceSmokeReport report)
    {
        var instances = FindObjectsByType<RuntimeGeneratedModelInstance>(FindObjectsInactive.Include);
        var best = SelectBestRuntimeInstance(instances);
        if (best == null)
        {
            AddCheck(
                report,
                "runtime_request_job_contract",
                PreDeviceSmokeStatus.Warn,
                "No RuntimeGeneratedModelInstance is available for request/job contract validation.");
            return;
        }

        var hasJobPath = !string.IsNullOrWhiteSpace(best.JobPath) && File.Exists(best.JobPath);
        var record = hasJobPath ? TryReadJson<GeneratedAssetRecord>(best.JobPath) : null;
        var hasSourceRequestPath = !string.IsNullOrWhiteSpace(record?.SourceRequestPath) &&
                                   File.Exists(record.SourceRequestPath);
        var request = hasSourceRequestPath ? TryReadJson<GeneratedObjectRequest>(record.SourceRequestPath) : null;
        var hasPromptArtifact = !string.IsNullOrWhiteSpace(record?.PromptArtifactPath) &&
                                File.Exists(record.PromptArtifactPath) &&
                                new FileInfo(record.PromptArtifactPath).Length > 0;

        var requestIdentityMatches = request != null &&
                                     SameNonEmpty(best.RequestId, record?.RequestId) &&
                                     SameNonEmpty(record?.RequestId, request.RequestId);
        var objectIdentityMatches = request != null &&
                                    SameNonEmpty(best.ObjectId, record?.ObjectId) &&
                                    SameNonEmpty(record?.ObjectId, request.ObjectId);
        var roomIdentityPresent = request != null && SameNonEmpty(best.RoomId, request.RoomId);
        var styleIdentityMatches = request != null &&
                                   SameNonEmpty(best.ThemeId, record?.ThemeId) &&
                                   SameNonEmpty(record?.ThemeId, request.ThemeId) &&
                                   SameNonEmpty(best.StyleVariantId, record?.StyleVariantId) &&
                                   SameNonEmpty(record?.StyleVariantId, request.StyleVariantId);
        var semanticMatches = request != null && SameNonEmpty(best.SemanticLabel, request.SemanticLabel);
        var requestPathRoundtrip = request != null && SamePath(record.SourceRequestPath, request.SourceRequestPath);
        var promptVersionMatches = request != null &&
                                   SameNonEmpty(record?.PromptVersion, request.PromptVersion);
        var hasRequestBounds = request != null &&
                               IsUsableSize(request.WorldBounds.Size) &&
                               IsUsableSize(request.Dimensions) &&
                               request.TargetLengthMeters > 0f &&
                               request.TargetWidthMeters > 0f &&
                               request.TargetHeightMeters > 0f &&
                               request.TargetAspectRatio > 0f;
        var runtimeStateLoaded = record != null && record.State == GeneratedObjectJobState.RuntimeLoaded;
        var hasRuntimeModelUrl = !string.IsNullOrWhiteSpace(record?.RuntimeModelUrl) &&
                                 Uri.TryCreate(record.RuntimeModelUrl, UriKind.Absolute, out var modelUri) &&
                                 modelUri.Scheme == Uri.UriSchemeHttps;
        var hasRuntimeLocalModel = !string.IsNullOrWhiteSpace(record?.RuntimeModelLocalPath) &&
                                   File.Exists(record.RuntimeModelLocalPath) &&
                                   SamePath(record.RuntimeModelLocalPath, best.ModelLocalPath);

        var pass = hasJobPath &&
                   record != null &&
                   request != null &&
                   hasSourceRequestPath &&
                   hasPromptArtifact &&
                   requestIdentityMatches &&
                   objectIdentityMatches &&
                   roomIdentityPresent &&
                   styleIdentityMatches &&
                   semanticMatches &&
                   requestPathRoundtrip &&
                   promptVersionMatches &&
                   hasRequestBounds &&
                   runtimeStateLoaded &&
                   hasRuntimeModelUrl &&
                   hasRuntimeLocalModel;

        AddCheck(
            report,
            "runtime_request_job_contract",
            pass ? PreDeviceSmokeStatus.Pass : PreDeviceSmokeStatus.Warn,
            $"request={ShortId(best.RequestId)}, object={best.ObjectId}, semantic={best.SemanticLabel}, jobFile={hasJobPath}, sourceRequest={hasSourceRequestPath}, promptArtifact={hasPromptArtifact}, requestMatch={requestIdentityMatches}, objectMatch={objectIdentityMatches}, roomMatch={roomIdentityPresent}, styleMatch={styleIdentityMatches}, semanticMatch={semanticMatches}, requestPathRoundtrip={requestPathRoundtrip}, promptVersion={promptVersionMatches}, bounds={hasRequestBounds}, runtimeState={record?.State.ToString() ?? "missing"}, modelUrlHttps={hasRuntimeModelUrl}, runtimeLocalModel={hasRuntimeLocalModel}");
    }

    private void CheckRuntimeBackendArtifactContract(PreDeviceSmokeReport report)
    {
        var instances = FindObjectsByType<RuntimeGeneratedModelInstance>(FindObjectsInactive.Include);
        var best = SelectBestRuntimeInstance(instances);
        if (best == null)
        {
            AddCheck(
                report,
                "runtime_backend_artifact_contract",
                PreDeviceSmokeStatus.Warn,
                "No RuntimeGeneratedModelInstance is available for runtime backend artifact validation.");
            return;
        }

        var hasJobPath = !string.IsNullOrWhiteSpace(best.JobPath) && File.Exists(best.JobPath);
        var record = hasJobPath ? TryReadJson<GeneratedAssetRecord>(best.JobPath) : null;
        var hasSubmissionFile = !string.IsNullOrWhiteSpace(record?.RuntimeBackendSubmissionPath) &&
                                File.Exists(record.RuntimeBackendSubmissionPath);
        var hasResultFile = !string.IsNullOrWhiteSpace(record?.RuntimeBackendResultPath) &&
                            File.Exists(record.RuntimeBackendResultPath);
        var submission = hasSubmissionFile
            ? TryReadJson<RuntimeGenerationBackendSubmission>(record.RuntimeBackendSubmissionPath)
            : null;
        var result = hasResultFile
            ? TryReadJson<RuntimeGenerationBackendResult>(record.RuntimeBackendResultPath)
            : null;

        var artifactDirectoryMatches = hasSubmissionFile &&
                                       hasResultFile &&
                                       SameDirectory(best.JobPath, record.RuntimeBackendSubmissionPath) &&
                                       SameDirectory(best.JobPath, record.RuntimeBackendResultPath);
        var submissionIdentityMatches = submission != null &&
                                        SameNonEmpty(best.RequestId, submission.RequestId) &&
                                        SameNonEmpty(best.ObjectId, submission.ObjectId) &&
                                        SameNonEmpty(best.RoomId, submission.RoomId) &&
                                        SameNonEmpty(best.ThemeId, submission.ThemeId) &&
                                        SameNonEmpty(best.StyleVariantId, submission.StyleVariantId) &&
                                        SameNonEmpty(best.SemanticLabel, submission.SemanticLabel);
        var submissionPathsValid = submission != null &&
                                   !string.IsNullOrWhiteSpace(submission.SourceRequestPath) &&
                                   File.Exists(submission.SourceRequestPath) &&
                                   !string.IsNullOrWhiteSpace(submission.PromptArtifactPath) &&
                                   File.Exists(submission.PromptArtifactPath);
        var submissionBoundsValid = submission != null &&
                                    IsUsableSize(submission.WorldBounds.Size) &&
                                    submission.TargetLengthMeters > 0f &&
                                    submission.TargetWidthMeters > 0f &&
                                    submission.TargetHeightMeters > 0f &&
                                    submission.TargetAspectRatio > 0f;
        var resultIdentityMatches = result != null &&
                                    SameNonEmpty(best.RequestId, result.RequestId) &&
                                    SameNonEmpty(best.ObjectId, result.ObjectId) &&
                                    SameNonEmpty(best.ThemeId, result.ThemeId) &&
                                    SameNonEmpty(best.StyleVariantId, result.StyleVariantId);
        var resultReady = result != null && result.OutputState == GeneratedObjectJobState.RuntimeModelReady;
        var resultJobMatches = result != null &&
                               SameNonEmpty(record?.RuntimeBackendJobId, result.RuntimeBackendJobId);
        var isLocalTestResult = result != null &&
                                !string.IsNullOrWhiteSpace(result.RuntimeBackendJobId) &&
                                result.RuntimeBackendJobId.StartsWith("local-test-", StringComparison.OrdinalIgnoreCase);
        var submissionUploadPayloadValid = submission != null &&
                                           (isLocalTestResult ||
                                            (!string.IsNullOrWhiteSpace(submission.SourceRequestJson) &&
                                             !string.IsNullOrWhiteSpace(submission.PromptText) &&
                                             !string.IsNullOrWhiteSpace(submission.SourceImageFileName) &&
                                             !string.IsNullOrWhiteSpace(submission.SourceImageMimeType) &&
                                             !string.IsNullOrWhiteSpace(submission.SourceImageSha256) &&
                                             submission.SourceImageByteLength > 0L));
        var resultHashValid = result != null &&
                              (isLocalTestResult ||
                               !string.IsNullOrWhiteSpace(result.RuntimeModelHash) ||
                               !string.IsNullOrWhiteSpace(record?.RuntimeModelHash));
        var resultModelUrlMatches = result != null &&
                                    SameNonEmpty(record?.RuntimeModelUrl, result.RuntimeModelUrl) &&
                                    Uri.TryCreate(result.RuntimeModelUrl, UriKind.Absolute, out var modelUri) &&
                                    modelUri.Scheme == Uri.UriSchemeHttps;

        var pass = hasJobPath &&
                   record != null &&
                   hasSubmissionFile &&
                   hasResultFile &&
                   artifactDirectoryMatches &&
                   submissionIdentityMatches &&
                   submissionPathsValid &&
                   submissionUploadPayloadValid &&
                   submissionBoundsValid &&
                   resultIdentityMatches &&
                   resultReady &&
                   resultJobMatches &&
                   resultHashValid &&
                   resultModelUrlMatches;

        AddCheck(
            report,
            "runtime_backend_artifact_contract",
            pass ? PreDeviceSmokeStatus.Pass : PreDeviceSmokeStatus.Warn,
            $"request={ShortId(best.RequestId)}, submissionFile={hasSubmissionFile}, resultFile={hasResultFile}, sameDirectory={artifactDirectoryMatches}, submissionIdentity={submissionIdentityMatches}, submissionPaths={submissionPathsValid}, uploadPayload={submissionUploadPayloadValid}, submissionBounds={submissionBoundsValid}, resultIdentity={resultIdentityMatches}, resultState={result?.OutputState.ToString() ?? "missing"}, resultJobMatch={resultJobMatches}, resultHash={resultHashValid}, modelUrlHttps={resultModelUrlMatches}, localTest={isLocalTestResult}");
    }

    private void CheckRuntimeLoadedInstanceMetadata(PreDeviceSmokeReport report)
    {
        var instances = FindObjectsByType<RuntimeGeneratedModelInstance>(FindObjectsInactive.Include);
        if (instances == null || instances.Length == 0)
        {
            AddCheck(
                report,
                "runtime_loaded_instance_metadata",
                PreDeviceSmokeStatus.Warn,
                "No RuntimeGeneratedModelInstance is present in the current Play session. Use Submit+Load or Load Latest Job before closing the runtime-loaded pre-device gate.");
            return;
        }

        var best = SelectBestRuntimeInstance(instances);
        if (best == null)
        {
            AddCheck(
                report,
                "runtime_loaded_instance_metadata",
                PreDeviceSmokeStatus.Warn,
                $"instances={instances.Length}, no usable RuntimeGeneratedModelInstance found.");
            return;
        }

        var marker = best.GetComponent<StylizedFurnitureInstance>();
        var hasMarker = marker != null;
        var markerObjectMatches = hasMarker &&
                                  !string.IsNullOrWhiteSpace(best.ObjectId) &&
                                  string.Equals(marker.ObjectId, best.ObjectId, StringComparison.OrdinalIgnoreCase);
        var markerSemanticMatches = hasMarker &&
                                    !string.IsNullOrWhiteSpace(best.SemanticLabel) &&
                                    string.Equals(marker.SemanticLabel, best.SemanticLabel, StringComparison.OrdinalIgnoreCase);
        var hasIdentity = !string.IsNullOrWhiteSpace(best.RequestId) &&
                          !string.IsNullOrWhiteSpace(best.ObjectId) &&
                          !string.IsNullOrWhiteSpace(best.RoomId) &&
                          !string.IsNullOrWhiteSpace(best.ThemeId) &&
                          !string.IsNullOrWhiteSpace(best.SemanticLabel);
        var hasModel = !string.IsNullOrWhiteSpace(best.ModelLocalPath) && File.Exists(best.ModelLocalPath);
        var hasJob = !string.IsNullOrWhiteSpace(best.JobPath) && File.Exists(best.JobPath);
        var hasBounds = IsUsableSize(best.FittedWorldBounds.Size) && IsUsableSize(best.SourceLocalBounds.Size);
        var hasScale = IsUsableScale(best.AppliedScale);
        var parentName = best.transform.parent != null ? best.transform.parent.name : string.Empty;
        var underRuntimeRoot = string.Equals(parentName, "RuntimeGeneratedModels", StringComparison.Ordinal);
        var pass = hasIdentity &&
                   hasModel &&
                   hasJob &&
                   hasMarker &&
                   markerObjectMatches &&
                   markerSemanticMatches &&
                   hasBounds &&
                   hasScale &&
                   underRuntimeRoot;

        AddCheck(
            report,
            "runtime_loaded_instance_metadata",
            pass ? PreDeviceSmokeStatus.Pass : PreDeviceSmokeStatus.Warn,
            $"instances={instances.Length}, request={ShortId(best.RequestId)}, object={best.ObjectId}, semantic={best.SemanticLabel}, review={best.ReviewState}, modelFile={hasModel}, jobFile={hasJob}, marker={hasMarker}, markerObjectMatch={markerObjectMatches}, markerSemanticMatch={markerSemanticMatches}, bounds={hasBounds}, scale={hasScale}, parent={parentName}");
    }

    private void CheckRuntimeReviewEditabilityPersistence(PreDeviceSmokeReport report)
    {
        if (generatedObjectReviewController == null || correctionModeController == null)
        {
            AddCheck(
                report,
                "runtime_review_editability_persistence",
                PreDeviceSmokeStatus.Warn,
                $"reviewController={generatedObjectReviewController != null}, correctionController={correctionModeController != null}");
            return;
        }

        var instances = FindObjectsByType<RuntimeGeneratedModelInstance>(FindObjectsInactive.Include);
        var best = SelectBestRuntimeInstance(instances);
        if (best == null)
        {
            AddCheck(
                report,
                "runtime_review_editability_persistence",
                PreDeviceSmokeStatus.Warn,
                "No RuntimeGeneratedModelInstance is available for the editability/persistence probe.");
            return;
        }

        var originalPosition = best.transform.localPosition;
        var originalRotation = best.transform.localRotation;
        var originalScale = best.transform.localScale;
        try
        {
            generatedObjectReviewController.Select(best);
            var selected = correctionModeController.HasSelection &&
                           correctionModeController.SelectedObject == best.transform;
            correctionModeController.NudgeForward();
            correctionModeController.RotateYawRight();
            correctionModeController.ConfirmCorrection();
            var correction = correctionModeController.GetConfirmedOrCurrentDelta();
            var boundedCorrection = selected &&
                                    correction.Confirmed &&
                                    !correction.IsIdentity &&
                                    correction.PositionOffset.sqrMagnitude > 0.000001f &&
                                    Mathf.Abs(correction.EulerOffset.y) > 0.001f &&
                                    IsUsableScale(correction.ScaleMultiplier);
            var persistenceOk = generatedObjectReviewController.TryRunPreDeviceReviewPersistenceProbe(
                best,
                correction,
                out var persistenceDetail);

            AddCheck(
                report,
                "runtime_review_editability_persistence",
                boundedCorrection && persistenceOk ? PreDeviceSmokeStatus.Pass : PreDeviceSmokeStatus.Warn,
                $"selected={selected}, correctionConfirmed={correction.Confirmed}, offset={FormatVector(correction.PositionOffset)}, yaw={correction.EulerOffset.y:0.###}, persistence={persistenceOk}, {persistenceDetail}");
        }
        catch (Exception exception)
        {
            AddCheck(
                report,
                "runtime_review_editability_persistence",
                PreDeviceSmokeStatus.Warn,
                exception.Message);
        }
        finally
        {
            correctionModeController.ResetCorrection();
            best.transform.localPosition = originalPosition;
            best.transform.localRotation = originalRotation;
            best.transform.localScale = originalScale;
        }
    }

    private void CheckRuntimeResetDeterministicFallback(PreDeviceSmokeReport report)
    {
        if (anchorThemeApplier == null)
        {
            AddCheck(
                report,
                "runtime_reset_deterministic_fallback",
                PreDeviceSmokeStatus.Warn,
                "AnchorThemeApplier is missing.");
            return;
        }

        var instances = FindObjectsByType<RuntimeGeneratedModelInstance>(FindObjectsInactive.Include);
        var best = SelectBestRuntimeInstance(instances);
        if (best == null)
        {
            AddCheck(
                report,
                "runtime_reset_deterministic_fallback",
                PreDeviceSmokeStatus.Warn,
                "No RuntimeGeneratedModelInstance is available for the reset fallback probe.");
            return;
        }

        var wasForced = anchorThemeApplier.IsDeterministicFallbackForcedForObject(best.ObjectId);
        var originalActiveSelf = best.gameObject.activeSelf;
        try
        {
            best.gameObject.SetActive(false);
            var candidateHidden = !best.gameObject.activeInHierarchy;
            var fallbackActivated = anchorThemeApplier.ForceDeterministicFallbackForObject(best.ObjectId, out var fallbackDetail);
            var fallbackVisible = anchorThemeApplier.HasActiveDeterministicFallbackForObject(best.ObjectId);

            AddCheck(
                report,
                "runtime_reset_deterministic_fallback",
                candidateHidden && fallbackActivated && fallbackVisible ? PreDeviceSmokeStatus.Pass : PreDeviceSmokeStatus.Warn,
                $"object={best.ObjectId}, candidateHidden={candidateHidden}, fallbackActivated={fallbackActivated}, fallbackVisible={fallbackVisible}, {fallbackDetail}");
        }
        catch (Exception exception)
        {
            AddCheck(
                report,
                "runtime_reset_deterministic_fallback",
                PreDeviceSmokeStatus.Warn,
                exception.Message);
        }
        finally
        {
            best.gameObject.SetActive(originalActiveSelf);
            if (!wasForced)
            {
                anchorThemeApplier.ClearDeterministicFallbackForObject(best.ObjectId, out _);
            }
        }
    }

    private void CheckRuntimeRejectResetReleasePolicy(PreDeviceSmokeReport report)
    {
        if (generatedObjectReviewController == null || runtimeGeneratedModelLoader == null)
        {
            AddCheck(
                report,
                "runtime_reject_reset_release_policy",
                PreDeviceSmokeStatus.Warn,
                $"reviewController={generatedObjectReviewController != null}, runtimeLoader={runtimeGeneratedModelLoader != null}");
            return;
        }

        GameObject probeRoot = null;
        try
        {
            probeRoot = new GameObject("PreDeviceRuntimeReleaseProbe");
            var probeInstance = probeRoot.AddComponent<RuntimeGeneratedModelInstance>();
            var policyEnabled = generatedObjectReviewController.ReleaseRejectedOrResetRuntimeModels;
            var released = runtimeGeneratedModelLoader.TryReleaseRuntimeInstance(
                probeInstance,
                "pre-device reject/reset release probe",
                out var releaseDetail);
            var inactiveAfterRelease = probeRoot == null || !probeRoot.activeSelf;

            AddCheck(
                report,
                "runtime_reject_reset_release_policy",
                policyEnabled && released && inactiveAfterRelease ? PreDeviceSmokeStatus.Pass : PreDeviceSmokeStatus.Warn,
                $"policyEnabled={policyEnabled}, released={released}, inactiveAfterRelease={inactiveAfterRelease}, {releaseDetail}");
        }
        catch (Exception exception)
        {
            AddCheck(
                report,
                "runtime_reject_reset_release_policy",
                PreDeviceSmokeStatus.Warn,
                exception.Message);
        }
        finally
        {
            if (probeRoot != null)
            {
                Destroy(probeRoot);
            }
        }
    }

    private void CheckDashboard(PreDeviceSmokeReport report)
    {
        if (dashboard == null)
        {
            AddCheck(report, "dashboard", PreDeviceSmokeStatus.Fail, "SceneShiftUISetDashboard is missing.");
            return;
        }

        report.DashboardSummary = dashboard.LatestSummary;
        var buttonCount = dashboard.GetComponentsInChildren<Button>(true).Length;
        var textValues = CollectDashboardText();
        var requiredLabels = new[]
        {
            "Submit+Load",
            "Load Test GLB",
            "Load Latest Job",
            "Clean View",
            "Object Status",
            "Accept",
            "Reject",
            "Reset",
        };

        var missing = new List<string>();
        foreach (var label in requiredLabels)
        {
            if (!ContainsText(textValues, label))
            {
                missing.Add(label);
            }
        }

        AddCheck(
            report,
            "dashboard_controls",
            missing.Count == 0 ? PreDeviceSmokeStatus.Pass : PreDeviceSmokeStatus.Warn,
            missing.Count == 0
                ? $"buttons={buttonCount}, required controls present"
                : $"buttons={buttonCount}, missing label(s): {string.Join(", ", missing)}");

        AddCheck(
            report,
            "custom_pointer_ray_removed",
            GameObject.Find("SceneShiftDashboardPointerRay") == null ? PreDeviceSmokeStatus.Pass : PreDeviceSmokeStatus.Fail,
            "No custom SceneShiftDashboardPointerRay object should exist; use official Interaction SDK ray/poke.");
    }

    private void CheckCleanViewToggle(PreDeviceSmokeReport report)
    {
        if (shellVisibilityToggle == null)
        {
            AddCheck(report, "clean_view_toggle", PreDeviceSmokeStatus.Fail, "MRUKShellVisibilityToggle is missing.");
            return;
        }

        var originalClean = shellVisibilityToggle.CleanViewActive;
        var originalOverlayVisible = worldStatusOverlay != null && worldStatusOverlay.IsOverlayVisible;
        try
        {
            shellVisibilityToggle.SetCleanViewActive(true);
            var activated = shellVisibilityToggle.CleanViewActive;
            var cardsHidden = worldStatusOverlay == null || !worldStatusOverlay.IsOverlayVisible;
            shellVisibilityToggle.SetCleanViewActive(false);
            var deactivated = !shellVisibilityToggle.CleanViewActive;
            AddCheck(
                report,
                "clean_view_toggle",
                activated && deactivated && cardsHidden ? PreDeviceSmokeStatus.Pass : PreDeviceSmokeStatus.Fail,
                $"activated={activated}, deactivated={deactivated}, objectCardsHidden={cardsHidden}");
        }
        finally
        {
            shellVisibilityToggle.SetCleanViewActive(originalClean);
            if (worldStatusOverlay != null)
            {
                worldStatusOverlay.SetOverlayVisible(originalOverlayVisible);
            }
        }
    }

    private void CheckPassthroughOnlyToggle(PreDeviceSmokeReport report)
    {
        if (passthroughOnlyVisibilityToggle == null)
        {
            AddCheck(report, "passthrough_only_toggle", PreDeviceSmokeStatus.Fail, "PassthroughOnlyVisibilityToggle is missing.");
            return;
        }

        var originalState = passthroughOnlyVisibilityToggle.PassthroughOnlyActive;
        try
        {
            passthroughOnlyVisibilityToggle.SetPassthroughOnlyActive(true);
            var activated = passthroughOnlyVisibilityToggle.PassthroughOnlyActive;
            passthroughOnlyVisibilityToggle.SetPassthroughOnlyActive(false);
            var deactivated = !passthroughOnlyVisibilityToggle.PassthroughOnlyActive;
            AddCheck(
                report,
                "passthrough_only_toggle",
                activated && deactivated ? PreDeviceSmokeStatus.Pass : PreDeviceSmokeStatus.Fail,
                $"activated={activated}, deactivated={deactivated}");
        }
        finally
        {
            passthroughOnlyVisibilityToggle.SetPassthroughOnlyActive(originalState);
        }
    }

    private void CheckManualVisualGates(PreDeviceSmokeReport report)
    {
        AddCheck(
            report,
            "surface_visual_quality",
            PreDeviceSmokeStatus.Manual,
            "Requires Game View or headset visual inspection: wall/floor/ceiling scale, trim seams, full door panel, open window frame, and outside vista placement.");
        AddCheck(
            report,
            "dashboard_visual_layout",
            PreDeviceSmokeStatus.Manual,
            "Requires Game View or headset visual inspection: readable text, no button overlap, ray/poke interaction visible, and bottom review row reachable.");
    }

    private void RefreshRuntimeSystems()
    {
        generationQueueStatusService?.Refresh();
        worldStatusOverlay?.RefreshLabels();
    }

    private void ResolveReferences()
    {
        roomSemanticBootstrap ??= FindAnyObjectByType<RoomSemanticBootstrap>();
        themeIntentController ??= FindAnyObjectByType<ThemeIntentController>();
        runtimeStyleIntentController ??= FindAnyObjectByType<RuntimeStyleIntentController>();
        stylizationPlanner ??= FindAnyObjectByType<StylizationPlanner>();
        surfaceOverrideApplier ??= FindAnyObjectByType<SurfaceOverrideApplier>();
        shellVisibilityToggle ??= FindAnyObjectByType<MRUKShellVisibilityToggle>();
        worldStatusOverlay ??= FindAnyObjectByType<GenerationJobWorldStatusOverlay>();
        passthroughOnlyVisibilityToggle ??= FindAnyObjectByType<PassthroughOnlyVisibilityToggle>();
        dashboard ??= FindAnyObjectByType<SceneShiftUISetDashboard>();
        generationQueueStatusService ??= FindAnyObjectByType<GenerationQueueStatusService>();
        runtimeLoopValidator ??= FindAnyObjectByType<PreDeviceRuntimeLoopValidator>();
        runtimeGenerationClient ??= FindAnyObjectByType<QuestRuntimeGenerationClient>();
        runtimeGeneratedModelLoader ??= FindAnyObjectByType<RuntimeGeneratedModelLoader>();
        generatedObjectReviewController ??= FindAnyObjectByType<GeneratedObjectReviewController>();
        correctionModeController ??= FindAnyObjectByType<CorrectionModeController>();
        anchorThemeApplier ??= FindAnyObjectByType<AnchorThemeApplier>();
    }

    private void WriteReport(PreDeviceSmokeReport report)
    {
        var directory = GetReportDirectory();
        Directory.CreateDirectory(directory);
        var jsonPath = Path.Combine(directory, $"{report.ReportId}.json");
        File.WriteAllText(jsonPath, JsonUtility.ToJson(report, true));
        lastReportPath = jsonPath;
        if (writeMarkdownReport)
        {
            File.WriteAllText(Path.Combine(directory, $"{report.ReportId}.md"), BuildMarkdown(report));
        }
    }

    private string GetReportDirectory()
    {
#if UNITY_EDITOR
        var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
        return Path.Combine(projectRoot, "Library", string.IsNullOrWhiteSpace(reportFolderName) ? "PreDeviceSmokeReports" : reportFolderName);
#else
        return Path.Combine(Application.persistentDataPath, string.IsNullOrWhiteSpace(reportFolderName) ? "PreDeviceSmokeReports" : reportFolderName);
#endif
    }

    private List<string> CollectDashboardText()
    {
        var values = new List<string>();
        foreach (var text in dashboard.GetComponentsInChildren<TMP_Text>(true))
        {
            if (text != null && !string.IsNullOrWhiteSpace(text.text))
            {
                values.Add(text.text);
            }
        }

        foreach (var text in dashboard.GetComponentsInChildren<Text>(true))
        {
            if (text != null && !string.IsNullOrWhiteSpace(text.text))
            {
                values.Add(text.text);
            }
        }

        return values;
    }

    private static bool ContainsText(IEnumerable<string> values, string expected)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value) &&
                value.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private static RuntimeGeneratedModelInstance SelectBestRuntimeInstance(
        IReadOnlyList<RuntimeGeneratedModelInstance> instances)
    {
        RuntimeGeneratedModelInstance best = null;
        var bestScore = int.MinValue;
        for (var index = 0; index < instances.Count; index++)
        {
            var instance = instances[index];
            if (instance == null)
            {
                continue;
            }

            var score = 0;
            score += string.IsNullOrWhiteSpace(instance.RequestId) ? 0 : 4;
            score += string.IsNullOrWhiteSpace(instance.ObjectId) ? 0 : 4;
            score += string.IsNullOrWhiteSpace(instance.ModelLocalPath) || !File.Exists(instance.ModelLocalPath) ? 0 : 4;
            score += instance.GetComponent<StylizedFurnitureInstance>() != null ? 4 : 0;
            score += instance.gameObject.activeInHierarchy ? 2 : 0;
            score += IsUsableSize(instance.FittedWorldBounds.Size) ? 2 : 0;
            score += instance.transform.parent != null && string.Equals(instance.transform.parent.name, "RuntimeGeneratedModels", StringComparison.Ordinal) ? 2 : 0;
            if (score > bestScore)
            {
                best = instance;
                bestScore = score;
            }
        }

        return best;
    }

    private static bool IsUsableSize(Vector3 size)
    {
        return size.x > 0.0001f && size.y > 0.0001f && size.z > 0.0001f;
    }

    private static bool IsUsableScale(Vector3 scale)
    {
        return scale.x > 0.0001f && scale.y > 0.0001f && scale.z > 0.0001f;
    }

    private static bool SameNonEmpty(string first, string second)
    {
        return !string.IsNullOrWhiteSpace(first) &&
               !string.IsNullOrWhiteSpace(second) &&
               string.Equals(first.Trim(), second.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool SamePath(string first, string second)
    {
        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second))
        {
            return false;
        }

        try
        {
            return string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(first.Trim(), second.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }

    private static bool SameDirectory(string firstPath, string secondPath)
    {
        if (string.IsNullOrWhiteSpace(firstPath) || string.IsNullOrWhiteSpace(secondPath))
        {
            return false;
        }

        try
        {
            var firstDirectory = Path.GetDirectoryName(Path.GetFullPath(firstPath));
            var secondDirectory = Path.GetDirectoryName(Path.GetFullPath(secondPath));
            return !string.IsNullOrWhiteSpace(firstDirectory) &&
                   !string.IsNullOrWhiteSpace(secondDirectory) &&
                   string.Equals(firstDirectory, secondDirectory, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static T TryReadJson<T>(string path) where T : class
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonUtility.FromJson<T>(File.ReadAllText(path));
        }
        catch
        {
            return null;
        }
    }

    private static int CountAnchorsWithLabel(MRUKRoom room, MRUKAnchor.SceneLabels label)
    {
        var count = 0;
        if (room == null || room.Anchors == null)
        {
            return count;
        }

        foreach (var anchor in room.Anchors)
        {
            if (anchor != null && anchor.HasAnyLabel(label))
            {
                count++;
            }
        }

        return count;
    }

    private static bool HasPlanSemantic(StylizationPlan plan, string semantic)
    {
        if (plan == null || plan.Entries == null)
        {
            return false;
        }

        foreach (var entry in plan.Entries)
        {
            if (entry != null && string.Equals(entry.OriginalSemanticLabel, semantic, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string BuildPlanCoverageLine(StylizationPlan plan)
    {
        if (plan == null || plan.Entries == null)
        {
            return "plan missing";
        }

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in plan.Entries)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.OriginalSemanticLabel))
            {
                continue;
            }

            counts.TryGetValue(entry.OriginalSemanticLabel, out var count);
            counts[entry.OriginalSemanticLabel] = count + 1;
        }

        var builder = new StringBuilder();
        foreach (var pair in counts)
        {
            if (builder.Length > 0)
            {
                builder.Append(", ");
            }

            builder.Append(pair.Key);
            builder.Append('=');
            builder.Append(pair.Value);
        }

        return builder.Length > 0 ? builder.ToString() : "no semantic entries";
    }

    private static bool ContainsLine(string value, string needle)
    {
        return !string.IsNullOrWhiteSpace(value) &&
               value.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string FirstLine(string value, string startsWith)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var lines = value.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            if (line.TrimStart().StartsWith(startsWith, StringComparison.OrdinalIgnoreCase))
            {
                return line.Trim();
            }
        }

        return lines.Length > 0 ? lines[0].Trim() : string.Empty;
    }

    private static string FormatVector(Vector3 value)
    {
        return FormattableString.Invariant($"{value.x:0.###}, {value.y:0.###}, {value.z:0.###}");
    }

    private static string ShortId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "none";
        }

        return value.Length <= 10 ? value : value.Substring(0, 10);
    }

    private static void AddCheck(PreDeviceSmokeReport report, string name, PreDeviceSmokeStatus status, string detail)
    {
        report.Checks.Add(new PreDeviceSmokeCheck
        {
            Name = name,
            Status = status.ToString(),
            Detail = detail ?? string.Empty,
        });
    }

    private static string BuildOverallStatus(PreDeviceSmokeReport report)
    {
        var hasFail = false;
        var hasWarn = false;
        var hasManual = false;
        foreach (var check in report.Checks)
        {
            hasFail |= string.Equals(check.Status, PreDeviceSmokeStatus.Fail.ToString(), StringComparison.Ordinal);
            hasWarn |= string.Equals(check.Status, PreDeviceSmokeStatus.Warn.ToString(), StringComparison.Ordinal);
            hasManual |= string.Equals(check.Status, PreDeviceSmokeStatus.Manual.ToString(), StringComparison.Ordinal);
        }

        if (hasFail)
        {
            return "Fail";
        }

        if (hasWarn)
        {
            return "PassWithWarnings";
        }

        return hasManual ? "PassWithManualVisualChecks" : "Pass";
    }

    private static string BuildSummary(PreDeviceSmokeReport report)
    {
        var builder = new StringBuilder(512);
        builder.AppendLine("[PreDeviceSmokeReport]");
        builder.AppendLine($"State: {report.OverallStatus}");
        builder.AppendLine($"Report: {report.ReportId}");
        foreach (var check in report.Checks)
        {
            builder.AppendLine($"- {check.Status}: {check.Name} - {Shorten(check.Detail, 120)}");
        }

        return builder.ToString().TrimEnd();
    }

    private static string BuildMarkdown(PreDeviceSmokeReport report)
    {
        var builder = new StringBuilder(2048);
        builder.AppendLine($"# {report.ReportId}");
        builder.AppendLine();
        builder.AppendLine($"- Created: `{report.CreatedAtIsoUtc}`");
        builder.AppendLine($"- Overall: `{report.OverallStatus}`");
        builder.AppendLine();
        builder.AppendLine("## Checks");
        builder.AppendLine();
        builder.AppendLine("| Check | Status | Detail |");
        builder.AppendLine("| --- | --- | --- |");
        foreach (var check in report.Checks)
        {
            builder.Append("| ");
            builder.Append(EscapeMarkdown(check.Name));
            builder.Append(" | `");
            builder.Append(EscapeMarkdown(check.Status));
            builder.Append("` | ");
            builder.Append(EscapeMarkdown(check.Detail));
            builder.AppendLine(" |");
        }

        AppendSection(builder, "Room", report.RoomSummary);
        AppendSection(builder, "Style", report.StyleSummary);
        AppendSection(builder, "Plan", report.PlanSummary);
        AppendSection(builder, "Surface", report.SurfaceSummary);
        AppendSection(builder, "Dashboard", report.DashboardSummary);
        AppendSection(builder, "Queue", report.QueueSummary);
        return builder.ToString().TrimEnd();
    }

    private static void AppendSection(StringBuilder builder, string title, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine($"## {title}");
        builder.AppendLine();
        builder.AppendLine("```text");
        builder.AppendLine(value.Trim());
        builder.AppendLine("```");
    }

    private static string EscapeMarkdown(string value)
    {
        return (value ?? string.Empty).Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
    }

    private static string Shorten(string value, int maxCharacters)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length <= maxCharacters)
        {
            return value ?? string.Empty;
        }

        return value.Substring(0, Mathf.Max(0, maxCharacters - 3)) + "...";
    }
}

[Serializable]
public sealed class PreDeviceSmokeReport
{
    public string ReportId;
    public string CreatedAtIsoUtc;
    public string OverallStatus;
    public List<PreDeviceSmokeCheck> Checks = new();
    public string RoomSummary;
    public string StyleSummary;
    public string PlanSummary;
    public string SurfaceSummary;
    public string DashboardSummary;
    public string QueueSummary;
}

[Serializable]
public sealed class PreDeviceSmokeCheck
{
    public string Name;
    public string Status;
    public string Detail;
}

public enum PreDeviceSmokeStatus
{
    Pass,
    Warn,
    Fail,
    Manual,
}
