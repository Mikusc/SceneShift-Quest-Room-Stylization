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
    [SerializeField] private TMP_Text summaryText;

    private void Reset()
    {
        roomSemanticBootstrap = FindAnyObjectByType<RoomSemanticBootstrap>();
        themeIntentController = FindAnyObjectByType<ThemeIntentController>();
        stylizationPlanner = FindAnyObjectByType<StylizationPlanner>();
        anchorThemeApplier = FindAnyObjectByType<AnchorThemeApplier>();
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
    }

    private void HandleThemeChanged(ThemeProfile _)
    {
        Refresh();
    }
}
