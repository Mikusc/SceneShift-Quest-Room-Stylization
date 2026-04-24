using TMPro;
using UnityEngine;
using System.Text;

[DisallowMultipleComponent]
public class StylizationDebugPanel : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private RoomSemanticBootstrap roomSemanticBootstrap;
    [SerializeField] private ThemeIntentController themeIntentController;
    [SerializeField] private StylizationPlanner stylizationPlanner;
    [SerializeField] private AnchorThemeApplier anchorThemeApplier;
    [SerializeField] private SurfaceTexturePromptBuilder surfaceTexturePromptBuilder;
    [SerializeField] private SurfaceOverrideApplier surfaceOverrideApplier;
    [SerializeField] private BestViewCaptureService bestViewCaptureService;
    [SerializeField] private GenerativeObjectCoordinator generativeObjectCoordinator;
    [SerializeField] private LocalGeneratedObjectBackendAdapter localGeneratedObjectBackendAdapter;
    [SerializeField] private TMP_Text summaryText;

    private void Reset()
    {
        roomSemanticBootstrap = FindAnyObjectByType<RoomSemanticBootstrap>();
        themeIntentController = FindAnyObjectByType<ThemeIntentController>();
        stylizationPlanner = FindAnyObjectByType<StylizationPlanner>();
        anchorThemeApplier = FindAnyObjectByType<AnchorThemeApplier>();
        surfaceTexturePromptBuilder = FindAnyObjectByType<SurfaceTexturePromptBuilder>();
        surfaceOverrideApplier = FindAnyObjectByType<SurfaceOverrideApplier>();
        bestViewCaptureService = FindAnyObjectByType<BestViewCaptureService>();
        generativeObjectCoordinator = FindAnyObjectByType<GenerativeObjectCoordinator>();
        localGeneratedObjectBackendAdapter = FindAnyObjectByType<LocalGeneratedObjectBackendAdapter>();
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

        if (surfaceOverrideApplier == null)
        {
            surfaceOverrideApplier = FindAnyObjectByType<SurfaceOverrideApplier>();
        }

        if (bestViewCaptureService == null)
        {
            bestViewCaptureService = FindAnyObjectByType<BestViewCaptureService>();
        }

        if (generativeObjectCoordinator == null)
        {
            generativeObjectCoordinator = FindAnyObjectByType<GenerativeObjectCoordinator>();
        }

        if (localGeneratedObjectBackendAdapter == null)
        {
            localGeneratedObjectBackendAdapter = FindAnyObjectByType<LocalGeneratedObjectBackendAdapter>();
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

        if (surfaceOverrideApplier != null)
        {
            surfaceOverrideApplier.SummaryChanged -= Refresh;
        }

        if (bestViewCaptureService != null)
        {
            bestViewCaptureService.SummaryChanged -= Refresh;
        }

        if (generativeObjectCoordinator != null)
        {
            generativeObjectCoordinator.SummaryChanged -= Refresh;
        }

        if (localGeneratedObjectBackendAdapter != null)
        {
            localGeneratedObjectBackendAdapter.SummaryChanged -= Refresh;
        }
    }

    private void HandleThemeChanged(ThemeProfile _)
    {
        Refresh();
    }
}
