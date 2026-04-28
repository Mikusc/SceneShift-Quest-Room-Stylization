using TMPro;
using UnityEngine;
using System.Text;

[DisallowMultipleComponent]
public class StylizationDebugPanel : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private RoomSemanticBootstrap roomSemanticBootstrap;
    [SerializeField] private ThemeIntentController themeIntentController;
    [SerializeField] private RuntimeStyleIntentController runtimeStyleIntentController;
    [SerializeField] private StylizationPlanner stylizationPlanner;
    [SerializeField] private AnchorThemeApplier anchorThemeApplier;
    [SerializeField] private SurfaceTexturePromptBuilder surfaceTexturePromptBuilder;
    [SerializeField] private ApimartSurfaceTextureBackendAdapter apimartSurfaceTextureBackendAdapter;
    [SerializeField] private SurfaceOverrideApplier surfaceOverrideApplier;
    [SerializeField] private BestViewCaptureService bestViewCaptureService;
    [SerializeField] private DevicePassthroughCaptureService devicePassthroughCaptureService;
    [SerializeField] private MRUKShellVisibilityToggle mrukShellVisibilityToggle;
    [SerializeField] private GenerativeObjectCoordinator generativeObjectCoordinator;
    [SerializeField] private LocalGeneratedObjectBackendAdapter localGeneratedObjectBackendAdapter;
    [SerializeField] private ApimartImageBackendAdapter apimartImageBackendAdapter;
    [SerializeField] private HostedImageUploadBridge hostedImageUploadBridge;
    [SerializeField] private Seed3DBackendAdapter seed3DBackendAdapter;
    [SerializeField] private GenerationQueueStatusService generationQueueStatusService;
    [SerializeField] private TMP_Text summaryText;

    private void Reset()
    {
        roomSemanticBootstrap = FindAnyObjectByType<RoomSemanticBootstrap>();
        themeIntentController = FindAnyObjectByType<ThemeIntentController>();
        runtimeStyleIntentController = FindAnyObjectByType<RuntimeStyleIntentController>();
        stylizationPlanner = FindAnyObjectByType<StylizationPlanner>();
        anchorThemeApplier = FindAnyObjectByType<AnchorThemeApplier>();
        surfaceTexturePromptBuilder = FindAnyObjectByType<SurfaceTexturePromptBuilder>();
        apimartSurfaceTextureBackendAdapter = FindAnyObjectByType<ApimartSurfaceTextureBackendAdapter>();
        surfaceOverrideApplier = FindAnyObjectByType<SurfaceOverrideApplier>();
        bestViewCaptureService = FindAnyObjectByType<BestViewCaptureService>();
        devicePassthroughCaptureService = FindAnyObjectByType<DevicePassthroughCaptureService>();
        mrukShellVisibilityToggle = FindAnyObjectByType<MRUKShellVisibilityToggle>();
        generativeObjectCoordinator = FindAnyObjectByType<GenerativeObjectCoordinator>();
        localGeneratedObjectBackendAdapter = FindAnyObjectByType<LocalGeneratedObjectBackendAdapter>();
        apimartImageBackendAdapter = FindAnyObjectByType<ApimartImageBackendAdapter>();
        hostedImageUploadBridge = FindAnyObjectByType<HostedImageUploadBridge>();
        seed3DBackendAdapter = FindAnyObjectByType<Seed3DBackendAdapter>();
        generationQueueStatusService = FindAnyObjectByType<GenerationQueueStatusService>();
        summaryText = GetComponentInChildren<TMP_Text>(true);
    }

    private void Awake()
    {
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

        if (stylizationPlanner == null)
        {
            stylizationPlanner = FindAnyObjectByType<StylizationPlanner>();
        }

        if (anchorThemeApplier == null)
        {
            anchorThemeApplier = FindAnyObjectByType<AnchorThemeApplier>();
        }

        if (surfaceTexturePromptBuilder == null)
        {
            surfaceTexturePromptBuilder = FindAnyObjectByType<SurfaceTexturePromptBuilder>();
        }

        if (apimartSurfaceTextureBackendAdapter == null)
        {
            apimartSurfaceTextureBackendAdapter = FindAnyObjectByType<ApimartSurfaceTextureBackendAdapter>();
        }

        if (surfaceOverrideApplier == null)
        {
            surfaceOverrideApplier = FindAnyObjectByType<SurfaceOverrideApplier>();
        }

        if (bestViewCaptureService == null)
        {
            bestViewCaptureService = FindAnyObjectByType<BestViewCaptureService>();
        }

        if (devicePassthroughCaptureService == null)
        {
            devicePassthroughCaptureService = FindAnyObjectByType<DevicePassthroughCaptureService>();
        }

        if (mrukShellVisibilityToggle == null)
        {
            mrukShellVisibilityToggle = FindAnyObjectByType<MRUKShellVisibilityToggle>();
        }

        if (generativeObjectCoordinator == null)
        {
            generativeObjectCoordinator = FindAnyObjectByType<GenerativeObjectCoordinator>();
        }

        if (localGeneratedObjectBackendAdapter == null)
        {
            localGeneratedObjectBackendAdapter = FindAnyObjectByType<LocalGeneratedObjectBackendAdapter>();
        }

        if (apimartImageBackendAdapter == null)
        {
            apimartImageBackendAdapter = FindAnyObjectByType<ApimartImageBackendAdapter>();
        }

        if (hostedImageUploadBridge == null)
        {
            hostedImageUploadBridge = FindAnyObjectByType<HostedImageUploadBridge>();
        }

        if (seed3DBackendAdapter == null)
        {
            seed3DBackendAdapter = FindAnyObjectByType<Seed3DBackendAdapter>();
        }

        if (generationQueueStatusService == null)
        {
            generationQueueStatusService = FindAnyObjectByType<GenerationQueueStatusService>();
        }

        if (summaryText == null)
        {
            summaryText = GetComponentInChildren<TMP_Text>(true);
        }
    }

    private void OnEnable()
    {
        Subscribe();
        Refresh();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    [ContextMenu("Refresh Debug Panel")]
    public void Refresh()
    {
        if (summaryText == null)
        {
            return;
        }

        if (mrukShellVisibilityToggle == null)
        {
            mrukShellVisibilityToggle = FindAnyObjectByType<MRUKShellVisibilityToggle>();
        }

        if (roomSemanticBootstrap == null)
        {
            summaryText.text = "[StylizationDebugPanel]\nRoomSemanticBootstrap missing";
            return;
        }

        var builder = new StringBuilder(512);
        builder.AppendLine(roomSemanticBootstrap.LatestPanelSummary);

        if (themeIntentController != null)
        {
            builder.AppendLine();
            builder.Append(themeIntentController.GetDebugSummary());
        }

        if (runtimeStyleIntentController != null)
        {
            builder.AppendLine();
            builder.Append(runtimeStyleIntentController.GetDebugSummary());
        }

        if (stylizationPlanner != null)
        {
            builder.AppendLine();
            builder.Append(stylizationPlanner.LatestSummary);
        }

        if (anchorThemeApplier != null)
        {
            builder.AppendLine();
            builder.Append(anchorThemeApplier.LatestSummary);
        }

        if (surfaceTexturePromptBuilder != null)
        {
            builder.AppendLine();
            builder.Append(surfaceTexturePromptBuilder.LatestSummary);
        }

        if (apimartSurfaceTextureBackendAdapter != null)
        {
            builder.AppendLine();
            builder.Append(apimartSurfaceTextureBackendAdapter.LatestSummary);
        }

        if (surfaceOverrideApplier != null)
        {
            builder.AppendLine();
            builder.Append(surfaceOverrideApplier.LatestSummary);
        }

        if (bestViewCaptureService != null)
        {
            builder.AppendLine();
            builder.Append(bestViewCaptureService.LatestSummary);
        }

        if (devicePassthroughCaptureService != null)
        {
            builder.AppendLine();
            builder.Append(devicePassthroughCaptureService.LatestSummary);
        }

        if (mrukShellVisibilityToggle != null)
        {
            builder.AppendLine();
            builder.Append(mrukShellVisibilityToggle.LatestSummary);
        }

        if (generativeObjectCoordinator != null)
        {
            builder.AppendLine();
            builder.Append(generativeObjectCoordinator.LatestSummary);
        }

        if (localGeneratedObjectBackendAdapter != null)
        {
            builder.AppendLine();
            builder.Append(localGeneratedObjectBackendAdapter.LatestSummary);
        }

        if (apimartImageBackendAdapter != null)
        {
            builder.AppendLine();
            builder.Append(apimartImageBackendAdapter.LatestSummary);
        }

        if (hostedImageUploadBridge != null)
        {
            builder.AppendLine();
            builder.Append(hostedImageUploadBridge.LatestSummary);
        }

        if (seed3DBackendAdapter != null)
        {
            builder.AppendLine();
            builder.Append(seed3DBackendAdapter.LatestSummary);
        }

        if (generationQueueStatusService != null)
        {
            builder.AppendLine();
            builder.Append(generationQueueStatusService.LatestSummary);
        }

        summaryText.text = builder.ToString().TrimEnd();
    }

    private void Subscribe()
    {
        if (roomSemanticBootstrap == null)
        {
            return;
        }

        roomSemanticBootstrap.SummaryChanged -= Refresh;
        roomSemanticBootstrap.SummaryChanged += Refresh;

        if (themeIntentController == null)
        {
            return;
        }

        themeIntentController.ThemeChanged -= HandleThemeChanged;
        themeIntentController.ThemeChanged += HandleThemeChanged;

        if (stylizationPlanner != null)
        {
            stylizationPlanner.PlanChanged -= Refresh;
            stylizationPlanner.PlanChanged += Refresh;
        }

        if (anchorThemeApplier == null)
        {
            return;
        }

        anchorThemeApplier.SummaryChanged -= Refresh;
        anchorThemeApplier.SummaryChanged += Refresh;

        if (surfaceTexturePromptBuilder != null)
        {
            surfaceTexturePromptBuilder.SummaryChanged -= Refresh;
            surfaceTexturePromptBuilder.SummaryChanged += Refresh;
        }

        if (apimartSurfaceTextureBackendAdapter != null)
        {
            apimartSurfaceTextureBackendAdapter.SummaryChanged -= Refresh;
            apimartSurfaceTextureBackendAdapter.SummaryChanged += Refresh;
        }

        if (surfaceOverrideApplier != null)
        {
            surfaceOverrideApplier.SummaryChanged -= Refresh;
            surfaceOverrideApplier.SummaryChanged += Refresh;
        }

        if (bestViewCaptureService == null)
        {
            return;
        }

        bestViewCaptureService.SummaryChanged -= Refresh;
        bestViewCaptureService.SummaryChanged += Refresh;

        if (devicePassthroughCaptureService != null)
        {
            devicePassthroughCaptureService.SummaryChanged -= Refresh;
            devicePassthroughCaptureService.SummaryChanged += Refresh;
        }

        if (mrukShellVisibilityToggle != null)
        {
            mrukShellVisibilityToggle.SummaryChanged -= Refresh;
            mrukShellVisibilityToggle.SummaryChanged += Refresh;
        }

        if (generativeObjectCoordinator == null)
        {
            return;
        }

        generativeObjectCoordinator.SummaryChanged -= Refresh;
        generativeObjectCoordinator.SummaryChanged += Refresh;

        if (localGeneratedObjectBackendAdapter == null)
        {
            return;
        }

        localGeneratedObjectBackendAdapter.SummaryChanged -= Refresh;
        localGeneratedObjectBackendAdapter.SummaryChanged += Refresh;

        if (apimartImageBackendAdapter != null)
        {
            apimartImageBackendAdapter.SummaryChanged -= Refresh;
            apimartImageBackendAdapter.SummaryChanged += Refresh;
        }

        if (hostedImageUploadBridge != null)
        {
            hostedImageUploadBridge.SummaryChanged -= Refresh;
            hostedImageUploadBridge.SummaryChanged += Refresh;
        }

        if (seed3DBackendAdapter != null)
        {
            seed3DBackendAdapter.SummaryChanged -= Refresh;
            seed3DBackendAdapter.SummaryChanged += Refresh;
        }

        if (generationQueueStatusService != null)
        {
            generationQueueStatusService.SummaryChanged -= Refresh;
            generationQueueStatusService.SummaryChanged += Refresh;
        }
    }

    private void Unsubscribe()
    {
        if (roomSemanticBootstrap != null)
        {
            roomSemanticBootstrap.SummaryChanged -= Refresh;
        }

        if (themeIntentController != null)
        {
            themeIntentController.ThemeChanged -= HandleThemeChanged;
        }

        if (stylizationPlanner != null)
        {
            stylizationPlanner.PlanChanged -= Refresh;
        }

        if (anchorThemeApplier != null)
        {
            anchorThemeApplier.SummaryChanged -= Refresh;
        }

        if (surfaceTexturePromptBuilder != null)
        {
            surfaceTexturePromptBuilder.SummaryChanged -= Refresh;
        }

        if (apimartSurfaceTextureBackendAdapter != null)
        {
            apimartSurfaceTextureBackendAdapter.SummaryChanged -= Refresh;
        }

        if (surfaceOverrideApplier != null)
        {
            surfaceOverrideApplier.SummaryChanged -= Refresh;
        }

        if (bestViewCaptureService != null)
        {
            bestViewCaptureService.SummaryChanged -= Refresh;
        }

        if (devicePassthroughCaptureService != null)
        {
            devicePassthroughCaptureService.SummaryChanged -= Refresh;
        }

        if (mrukShellVisibilityToggle != null)
        {
            mrukShellVisibilityToggle.SummaryChanged -= Refresh;
        }

        if (generativeObjectCoordinator != null)
        {
            generativeObjectCoordinator.SummaryChanged -= Refresh;
        }

        if (localGeneratedObjectBackendAdapter != null)
        {
            localGeneratedObjectBackendAdapter.SummaryChanged -= Refresh;
        }

        if (apimartImageBackendAdapter != null)
        {
            apimartImageBackendAdapter.SummaryChanged -= Refresh;
        }

        if (hostedImageUploadBridge != null)
        {
            hostedImageUploadBridge.SummaryChanged -= Refresh;
        }

        if (seed3DBackendAdapter != null)
        {
            seed3DBackendAdapter.SummaryChanged -= Refresh;
        }

        if (generationQueueStatusService != null)
        {
            generationQueueStatusService.SummaryChanged -= Refresh;
        }
    }

    private void HandleThemeChanged(ThemeProfile _)
    {
        Refresh();
    }
}
