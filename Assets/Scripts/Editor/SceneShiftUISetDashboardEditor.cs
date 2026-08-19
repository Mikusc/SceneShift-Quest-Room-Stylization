#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

[CustomEditor(typeof(SceneShiftUISetDashboard))]
[CanEditMultipleObjects]
public sealed class SceneShiftUISetDashboardEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.Space(10f);
        EditorGUILayout.LabelField("Editor Hierarchy", EditorStyles.boldLabel);

        var dashboard = target as SceneShiftUISetDashboard;
        var state = dashboard != null && dashboard.HasBakedSceneHierarchy
            ? "The dashboard content is instantiated and saved in the scene hierarchy."
            : "The dashboard content is currently created at runtime.";
        EditorGUILayout.HelpBox(state, MessageType.Info);

        using (new EditorGUI.DisabledScope(EditorApplication.isPlaying))
        {
            if (GUILayout.Button("Build / Rebuild Editor Hierarchy", GUILayout.Height(28f)))
            {
                foreach (var selectedTarget in targets)
                {
                    ((SceneShiftUISetDashboard)selectedTarget).RebuildEditorHierarchy();
                }
            }

            if (GUILayout.Button("Remove Baked Editor Hierarchy"))
            {
                foreach (var selectedTarget in targets)
                {
                    ((SceneShiftUISetDashboard)selectedTarget).RemoveBakedEditorHierarchy();
                }
            }
        }
    }
}

public static class SceneShiftUISetDashboardEditorCommands
{
    private const string CanonicalScenePath = "Assets/Scenes/MR_RoomStylization.unity";

    [MenuItem("SceneShift/UI/Rebuild Dashboard Hierarchy In Open Scene", false, 200)]
    public static void RebuildOpenSceneDashboardHierarchy()
    {
        var dashboards = UnityEngine.Object.FindObjectsByType<SceneShiftUISetDashboard>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        if (dashboards.Length == 0)
        {
            Debug.LogWarning("[SceneShiftUISetDashboardEditor] No dashboard exists in the open scene.");
            return;
        }

        foreach (var dashboard in dashboards)
        {
            dashboard.RebuildEditorHierarchy();
        }

        Selection.activeObject = dashboards[0].gameObject;
    }

    [MenuItem("SceneShift/UI/Remove Baked Dashboard Hierarchy In Open Scene", false, 201)]
    public static void RemoveOpenSceneDashboardHierarchy()
    {
        var dashboards = UnityEngine.Object.FindObjectsByType<SceneShiftUISetDashboard>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        foreach (var dashboard in dashboards)
        {
            dashboard.RemoveBakedEditorHierarchy();
        }
    }

    // Used by batch validation to produce the same scene-authored hierarchy as the Inspector button.
    public static void BakeCanonicalSceneBatch()
    {
        var scene = EditorSceneManager.OpenScene(CanonicalScenePath, OpenSceneMode.Single);
        var dashboards = UnityEngine.Object.FindObjectsByType<SceneShiftUISetDashboard>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        if (dashboards.Length != 1)
        {
            throw new InvalidOperationException(
                $"Expected exactly one SceneShiftUISetDashboard in {CanonicalScenePath}, found {dashboards.Length}.");
        }

        dashboards[0].RebuildEditorHierarchy();
        if (!dashboards[0].HasBakedSceneHierarchy)
        {
            throw new InvalidOperationException("Dashboard hierarchy rebuild did not produce a complete hierarchy.");
        }

        if (!EditorSceneManager.SaveScene(scene))
        {
            throw new InvalidOperationException($"Failed to save {CanonicalScenePath}.");
        }

        Debug.Log($"[SceneShiftUISetDashboardEditor] Saved baked dashboard hierarchy to {CanonicalScenePath}.");
    }

    public static void ValidateCanonicalSceneBatch()
    {
        EditorSceneManager.OpenScene(CanonicalScenePath, OpenSceneMode.Single);
        var dashboards = UnityEngine.Object.FindObjectsByType<SceneShiftUISetDashboard>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        if (dashboards.Length != 1 || !dashboards[0].HasBakedSceneHierarchy)
        {
            throw new InvalidOperationException(
                $"Expected one complete baked dashboard in {CanonicalScenePath}, found {dashboards.Length}.");
        }

        var canvas = dashboards[0].GetComponentInChildren<Canvas>(true);
        var buttonCount = canvas != null ? canvas.GetComponentsInChildren<Button>(true).Length : 0;
        if (canvas == null || canvas.renderMode != RenderMode.WorldSpace || buttonCount < 14)
        {
            throw new InvalidOperationException(
                $"Baked dashboard is incomplete: canvas={(canvas != null ? canvas.renderMode.ToString() : "missing")}, buttons={buttonCount}.");
        }

        Debug.Log(
            $"[SceneShiftUISetDashboardEditor] Validation passed: world-space canvas, baked hierarchy, buttons={buttonCount}.");
    }
}
#endif
