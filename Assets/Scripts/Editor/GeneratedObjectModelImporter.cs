using System;
using System.IO;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class GeneratedObjectModelImporter
{
    private const string JobFolderName = "GeneratedObjectJobs";
    private const string ThemeAssetFolder = "Assets/Generated/ThemeAssets";
    private const float MinimumBoundsExtent = 0.01f;
    private const float MaximumAspectRatioError = 0.45f;
    private const float MaximumHeightRatioError = 0.65f;
    private const float MinimumHeightToFootprintRatio = 0.12f;

    static GeneratedObjectModelImporter()
    {
        EditorApplication.delayCall += ImportReadyModelJobs;
    }

    [MenuItem("SceneShift/Generated Objects/Import Ready Model Jobs")]
    public static void ImportReadyModelJobs()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return;
        }

        var jobDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Library", JobFolderName));
        if (!Directory.Exists(jobDirectory))
        {
            return;
        }

        var importedCount = 0;
        foreach (var jobPath in Directory.GetFiles(jobDirectory, "*.job.json", SearchOption.TopDirectoryOnly))
        {
            if (TryImportModelJob(jobPath))
            {
                importedCount++;
            }
        }

        if (importedCount > 0)
        {
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[GeneratedObjectModelImporter] Imported {importedCount} generated-object model job(s).");
        }
    }

    private static bool TryImportModelJob(string jobPath)
    {
        var jobJson = File.ReadAllText(jobPath);
        if (string.IsNullOrWhiteSpace(jobJson))
        {
            return false;
        }

        var record = JsonUtility.FromJson<GeneratedAssetRecord>(jobJson);
        if (record == null || string.IsNullOrWhiteSpace(record.RequestId))
        {
            return false;
        }

        if (record.State != GeneratedObjectJobState.ModelReady)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(record.GeneratedModelPath) || !File.Exists(record.GeneratedModelPath))
        {
            return false;
        }

        if (!TryGetProjectRelativePath(record.GeneratedModelPath, out var modelAssetPath))
        {
            return false;
        }

        AssetDatabase.ImportAsset(modelAssetPath, ImportAssetOptions.ForceUpdate);
        var importedModel = AssetDatabase.LoadAssetAtPath<GameObject>(modelAssetPath);
        if (importedModel == null)
        {
            record.StatusNote = $"Generated model was found but Unity could not load it as a GameObject: {modelAssetPath}";
            record.UpdatedAtIsoUtc = DateTime.UtcNow.ToString("O");
            File.WriteAllText(jobPath, JsonUtility.ToJson(record, true));
            return false;
        }

        var outputFolder = $"{ThemeAssetFolder}/{record.RequestId}";
        Directory.CreateDirectory(Path.Combine(GetProjectRoot(), outputFolder));
        var prefabAssetPath = $"{outputFolder}/{record.RequestId}.generated_table_proxy.prefab";

        var wrapper = new GameObject($"{record.RequestId}_GeneratedTableProxy");
        try
        {
            var instance = PrefabUtility.InstantiatePrefab(importedModel) as GameObject;
            if (instance == null)
            {
                instance = UnityEngine.Object.Instantiate(importedModel);
            }

            instance.name = $"{Path.GetFileNameWithoutExtension(modelAssetPath)}_Model";
            instance.transform.SetParent(wrapper.transform, false);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;

            RemoveImportedColliders(wrapper);
            NormalizeToCenteredBottomPivot(wrapper.transform);

            if (!TryCalculateLocalBounds(wrapper.transform, out var normalizedBounds))
            {
                record.StatusNote = $"Generated model import produced no renderable bounds: {modelAssetPath}";
                record.UpdatedAtIsoUtc = DateTime.UtcNow.ToString("O");
                File.WriteAllText(jobPath, JsonUtility.ToJson(record, true));
                return false;
            }

            var quality = EvaluateQuality(record, normalizedBounds);

            var prefab = PrefabUtility.SaveAsPrefabAsset(wrapper, prefabAssetPath);
            if (prefab == null)
            {
                return false;
            }

            record.State = quality.Passed ? GeneratedObjectJobState.Imported : GeneratedObjectJobState.NeedsReview;
            record.ImportedPrefabPath = Path.GetFullPath(Path.Combine(GetProjectRoot(), prefabAssetPath));
            record.ImportedBounds = SerializableBounds.From(normalizedBounds.center, normalizedBounds.size);
            record.QualityReviewPassed = quality.Passed;
            record.QualityScore = quality.Score;
            record.QualityReviewStatus = quality.Status;
            record.QualityReviewWarnings = quality.Warnings;
            record.StatusNote = quality.Passed
                ? "Generated model imported as a normalized prefab and passed the automatic quality gate. Runtime applier may use it in Editor/Simulator and fall back to deterministic proxy when unavailable."
                : "Generated model imported as a normalized prefab but needs review before runtime use. Runtime applier should continue using deterministic fallback or a previously approved generated asset.";
            record.UpdatedAtIsoUtc = DateTime.UtcNow.ToString("O");
            File.WriteAllText(jobPath, JsonUtility.ToJson(record, true));
            Debug.Log($"[GeneratedObjectModelImporter] Imported generated model prefab -> {prefabAssetPath} ({record.State}, quality={quality.Score:0.##})");
            return true;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(wrapper);
        }
    }

    private static void NormalizeToCenteredBottomPivot(Transform root)
    {
        if (root == null || !TryCalculateLocalBounds(root, out var bounds))
        {
            return;
        }

        var offset = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
        for (var index = 0; index < root.childCount; index++)
        {
            var child = root.GetChild(index);
            child.localPosition += offset;
        }
    }

    private static void RemoveImportedColliders(GameObject root)
    {
        if (root == null)
        {
            return;
        }

        var colliders = root.GetComponentsInChildren<Collider>(true);
        foreach (var collider in colliders)
        {
            if (collider != null)
            {
                UnityEngine.Object.DestroyImmediate(collider);
            }
        }
    }

    private static bool TryCalculateLocalBounds(Transform root, out Bounds bounds)
    {
        bounds = default;
        if (root == null)
        {
            return false;
        }

        var renderers = root.GetComponentsInChildren<Renderer>(true);
        var hasBounds = false;
        foreach (var renderer in renderers)
        {
            if (renderer == null || renderer is ParticleSystemRenderer)
            {
                continue;
            }

            var worldBounds = renderer.bounds;
            var min = worldBounds.min;
            var max = worldBounds.max;
            for (var x = 0; x <= 1; x++)
            {
                for (var y = 0; y <= 1; y++)
                {
                    for (var z = 0; z <= 1; z++)
                    {
                        var worldPoint = new Vector3(
                            x == 0 ? min.x : max.x,
                            y == 0 ? min.y : max.y,
                            z == 0 ? min.z : max.z);
                        var localPoint = root.InverseTransformPoint(worldPoint);
                        if (!hasBounds)
                        {
                            bounds = new Bounds(localPoint, Vector3.zero);
                            hasBounds = true;
                        }
                        else
                        {
                            bounds.Encapsulate(localPoint);
                        }
                    }
                }
            }
        }

        return hasBounds;
    }

    private static GeneratedModelQualityResult EvaluateQuality(GeneratedAssetRecord record, Bounds bounds)
    {
        var result = new GeneratedModelQualityResult
        {
            Passed = true,
            Score = 1f,
            Status = "Passed",
            Warnings = string.Empty,
        };

        var size = bounds.size;
        if (size.x < MinimumBoundsExtent || size.y < MinimumBoundsExtent || size.z < MinimumBoundsExtent)
        {
            result.AddWarning("Renderable bounds are too small on one or more axes.");
            result.Score -= 0.5f;
        }

        var horizontalLong = Mathf.Max(size.x, size.z);
        var horizontalShort = Mathf.Min(size.x, size.z);
        if (horizontalShort < MinimumBoundsExtent || horizontalLong < MinimumBoundsExtent)
        {
            result.AddWarning("Horizontal footprint bounds are too small for reliable table registration.");
            result.Score -= 0.35f;
        }
        else
        {
            var sourceAspect = horizontalLong / horizontalShort;
            var targetAspect = record != null && record.TargetAspectRatio > 0.01f ? record.TargetAspectRatio : 0f;
            if (targetAspect > 0f)
            {
                var aspectError = Mathf.Abs(sourceAspect - targetAspect) / Mathf.Max(targetAspect, 0.01f);
                if (aspectError > MaximumAspectRatioError)
                {
                    result.AddWarning($"Footprint aspect ratio differs from target by {aspectError:P0}.");
                    result.Score -= Mathf.Clamp01(aspectError) * 0.35f;
                }
            }

            var sourceHeightRatio = size.y / horizontalLong;
            if (sourceHeightRatio < MinimumHeightToFootprintRatio)
            {
                result.AddWarning("Generated model is very flat and may be tabletop-only.");
                result.Score -= 0.35f;
            }

            if (record != null && record.TargetLengthMeters > 0.01f && record.TargetHeightMeters > 0.01f)
            {
                var targetHeightRatio = record.TargetHeightMeters / record.TargetLengthMeters;
                var heightRatioError = Mathf.Abs(sourceHeightRatio - targetHeightRatio) / Mathf.Max(targetHeightRatio, 0.01f);
                if (heightRatioError > MaximumHeightRatioError)
                {
                    result.AddWarning($"Height-to-length ratio differs from target by {heightRatioError:P0}.");
                    result.Score -= Mathf.Clamp01(heightRatioError) * 0.25f;
                }
            }
        }

        result.Score = Mathf.Clamp01(result.Score);
        if (!string.IsNullOrWhiteSpace(result.Warnings) || result.Score < 0.72f)
        {
            result.Passed = false;
            result.Status = "NeedsReview";
        }

        return result;
    }

    private static bool TryGetProjectRelativePath(string absoluteOrRelativePath, out string assetPath)
    {
        assetPath = string.Empty;
        if (string.IsNullOrWhiteSpace(absoluteOrRelativePath))
        {
            return false;
        }

        if (absoluteOrRelativePath.StartsWith("Assets/", StringComparison.Ordinal))
        {
            assetPath = absoluteOrRelativePath;
            return true;
        }

        var projectRoot = GetProjectRoot();
        var fullPath = Path.GetFullPath(absoluteOrRelativePath);
        if (!fullPath.StartsWith(projectRoot, StringComparison.Ordinal))
        {
            return false;
        }

        assetPath = fullPath[(projectRoot.Length + 1)..].Replace(Path.DirectorySeparatorChar, '/');
        return assetPath.StartsWith("Assets/", StringComparison.Ordinal);
    }

    private static string GetProjectRoot()
    {
        return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    }

    private struct GeneratedModelQualityResult
    {
        public bool Passed;
        public float Score;
        public string Status;
        public string Warnings;

        public void AddWarning(string warning)
        {
            if (string.IsNullOrWhiteSpace(warning))
            {
                return;
            }

            Warnings = string.IsNullOrWhiteSpace(Warnings)
                ? warning
                : $"{Warnings} {warning}";
        }
    }
}
