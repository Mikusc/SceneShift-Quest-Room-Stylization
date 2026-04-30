using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class GeneratedObjectAssetCleaner
{
    private const string JobFolderName = "GeneratedObjectJobs";
    private const string ThemeAssetFolder = "Assets/Generated/ThemeAssets";
    private const string ArchiveFolderName = "GeneratedObjectArchive";

    private enum KeepPolicy
    {
        Newest,
        BestPassed,
    }

    private sealed class Candidate
    {
        public GeneratedAssetRecord Record;
        public string JobPath;
        public string AssetFolder;
        public string GeneratedModelPath;
        public string ImportedPrefabPath;
        public string GroupKey;
        public DateTime UpdatedAtUtc;
        public long FolderBytes;

        public string RequestId => string.IsNullOrWhiteSpace(Record.RequestId) ? Path.GetFileNameWithoutExtension(JobPath) : Record.RequestId;
    }

    [MenuItem("SceneShift/Generated Objects/Report Duplicate Models")]
    public static void ReportDuplicateModels()
    {
        RunDuplicateCleanup(KeepPolicy.Newest, execute: false);
    }

    [MenuItem("SceneShift/Generated Objects/Clean Duplicate Models - Keep Newest")]
    public static void CleanDuplicateModelsKeepNewest()
    {
        RunDuplicateCleanup(KeepPolicy.Newest, execute: true);
    }

    [MenuItem("SceneShift/Generated Objects/Clean Duplicate Models - Keep Best Passed")]
    public static void CleanDuplicateModelsKeepBestPassed()
    {
        RunDuplicateCleanup(KeepPolicy.BestPassed, execute: true);
    }

    private static void RunDuplicateCleanup(KeepPolicy keepPolicy, bool execute)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[GeneratedObjectAssetCleaner] Exit Play Mode before cleaning generated model assets.");
            return;
        }

        var projectRoot = GetProjectRoot();
        var candidates = LoadCandidates(projectRoot);
        var duplicateGroups = candidates
            .GroupBy(candidate => candidate.GroupKey, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var staleCandidates = new List<Candidate>();
        var keptByGroup = new Dictionary<string, Candidate>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in duplicateGroups)
        {
            var keeper = SelectKeeper(group, keepPolicy);
            keptByGroup[group.Key] = keeper;
            staleCandidates.AddRange(group.Where(candidate => !ReferenceEquals(candidate, keeper)));
        }

        var report = BuildReport(candidates, duplicateGroups, staleCandidates, keptByGroup, keepPolicy, execute);
        var reportPath = WriteReport(projectRoot, report, execute);
        Debug.Log($"[GeneratedObjectAssetCleaner] Duplicate report written: {reportPath}");

        if (!execute)
        {
            return;
        }

        if (staleCandidates.Count == 0)
        {
            Debug.Log("[GeneratedObjectAssetCleaner] No duplicate generated model folders to clean.");
            return;
        }

        var staleMegabytes = staleCandidates.Sum(candidate => candidate.FolderBytes) / (1024f * 1024f);
        var confirmed = EditorUtility.DisplayDialog(
            "Clean duplicate generated models",
            $"Move {staleCandidates.Count} duplicate generated model folder(s) to Library/{ArchiveFolderName}?\n\n" +
            $"Policy: {FormatKeepPolicy(keepPolicy)}\n" +
            $"Estimated moved size: {staleMegabytes:0.0} MB\n\n" +
            "This does not delete files. It moves stale model folders out of Assets so Unity no longer imports or loads them.",
            "Move To Backup",
            "Cancel");
        if (!confirmed)
        {
            Debug.Log("[GeneratedObjectAssetCleaner] Cleanup cancelled.");
            return;
        }

        var archiveRoot = Path.Combine(
            projectRoot,
            "Library",
            ArchiveFolderName,
            DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(archiveRoot);

        var movedCount = 0;
        foreach (var staleCandidate in staleCandidates)
        {
            if (TryMoveCandidateToArchive(staleCandidate, archiveRoot, keepPolicy, keptByGroup[staleCandidate.GroupKey]))
            {
                movedCount++;
            }
        }

        File.WriteAllText(Path.Combine(archiveRoot, "cleanup_report.txt"), report);
        AssetDatabase.Refresh();
        Debug.Log($"[GeneratedObjectAssetCleaner] Moved {movedCount}/{staleCandidates.Count} duplicate generated model folder(s) to {archiveRoot}");
    }

    private static List<Candidate> LoadCandidates(string projectRoot)
    {
        var result = new List<Candidate>();
        var jobDirectory = Path.Combine(projectRoot, "Library", JobFolderName);
        if (!Directory.Exists(jobDirectory))
        {
            return result;
        }

        foreach (var jobPath in Directory.GetFiles(jobDirectory, "*.job.json", SearchOption.TopDirectoryOnly))
        {
            var record = TryReadRecord(jobPath);
            if (record == null)
            {
                continue;
            }

            if (!TryGetGeneratedAssetFolder(projectRoot, record, out var assetFolder, out var modelPath, out var prefabPath))
            {
                continue;
            }

            if (!Directory.EnumerateFiles(assetFolder, "*.glb", SearchOption.TopDirectoryOnly).Any())
            {
                continue;
            }

            result.Add(new Candidate
            {
                Record = record,
                JobPath = jobPath,
                AssetFolder = assetFolder,
                GeneratedModelPath = modelPath,
                ImportedPrefabPath = prefabPath,
                GroupKey = BuildGroupKey(record),
                UpdatedAtUtc = ParseUtc(record.UpdatedAtIsoUtc),
                FolderBytes = GetDirectoryBytes(assetFolder),
            });
        }

        return result;
    }

    private static bool TryGetGeneratedAssetFolder(
        string projectRoot,
        GeneratedAssetRecord record,
        out string assetFolder,
        out string modelPath,
        out string prefabPath)
    {
        assetFolder = string.Empty;
        modelPath = string.Empty;
        prefabPath = string.Empty;

        var themeAssetRoot = Path.GetFullPath(Path.Combine(projectRoot, ThemeAssetFolder));
        modelPath = ToFullPath(projectRoot, record.GeneratedModelPath);
        prefabPath = ToFullPath(projectRoot, record.ImportedPrefabPath);

        foreach (var candidatePath in new[] { prefabPath, modelPath })
        {
            if (string.IsNullOrWhiteSpace(candidatePath) || !File.Exists(candidatePath))
            {
                continue;
            }

            var folder = Path.GetDirectoryName(candidatePath);
            if (string.IsNullOrWhiteSpace(folder))
            {
                continue;
            }

            folder = Path.GetFullPath(folder);
            if (!folder.StartsWith(themeAssetRoot, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            assetFolder = folder;
            return true;
        }

        return false;
    }

    private static Candidate SelectKeeper(IEnumerable<Candidate> group, KeepPolicy keepPolicy)
    {
        return keepPolicy switch
        {
            KeepPolicy.BestPassed => group
                .OrderByDescending(candidate => GetStatePriority(candidate.Record.State))
                .ThenByDescending(candidate => candidate.Record.QualityReviewPassed)
                .ThenByDescending(candidate => candidate.Record.QualityScore)
                .ThenByDescending(candidate => candidate.UpdatedAtUtc)
                .ThenBy(candidate => candidate.RequestId, StringComparer.OrdinalIgnoreCase)
                .First(),
            _ => group
                .OrderByDescending(candidate => candidate.UpdatedAtUtc)
                .ThenByDescending(candidate => GetStatePriority(candidate.Record.State))
                .ThenByDescending(candidate => candidate.Record.QualityScore)
                .ThenBy(candidate => candidate.RequestId, StringComparer.OrdinalIgnoreCase)
                .First(),
        };
    }

    private static int GetStatePriority(GeneratedObjectJobState state)
    {
        return state switch
        {
            GeneratedObjectJobState.Imported => 4,
            GeneratedObjectJobState.NeedsReview => 3,
            GeneratedObjectJobState.ModelReady => 2,
            GeneratedObjectJobState.ModelGenerationSubmitted => 1,
            _ => 0,
        };
    }

    private static bool TryMoveCandidateToArchive(
        Candidate candidate,
        string archiveRoot,
        KeepPolicy keepPolicy,
        Candidate keptCandidate)
    {
        if (candidate == null || string.IsNullOrWhiteSpace(candidate.AssetFolder) || !Directory.Exists(candidate.AssetFolder))
        {
            return false;
        }

        var destinationFolder = GetUniquePath(Path.Combine(archiveRoot, Path.GetFileName(candidate.AssetFolder)));
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destinationFolder) ?? archiveRoot);
            Directory.Move(candidate.AssetFolder, destinationFolder);
            MoveMetaFileIfPresent(candidate.AssetFolder, destinationFolder);

            var oldModelPath = candidate.GeneratedModelPath;
            var oldPrefabPath = candidate.ImportedPrefabPath;
            candidate.Record.GeneratedModelPath = RemapMovedPath(oldModelPath, candidate.AssetFolder, destinationFolder);
            candidate.Record.ImportedPrefabPath = RemapMovedPath(oldPrefabPath, candidate.AssetFolder, destinationFolder);
            candidate.Record.StatusNote = BuildArchivedStatusNote(candidate, keepPolicy, keptCandidate);
            candidate.Record.UpdatedAtIsoUtc = DateTime.UtcNow.ToString("O");
            File.WriteAllText(candidate.JobPath, JsonUtility.ToJson(candidate.Record, true));
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[GeneratedObjectAssetCleaner] Failed to archive '{candidate.RequestId}': {exception.Message}");
            return false;
        }
    }

    private static string RemapMovedPath(string oldPath, string oldFolder, string newFolder)
    {
        if (string.IsNullOrWhiteSpace(oldPath))
        {
            return string.Empty;
        }

        var fullOldPath = Path.GetFullPath(oldPath);
        var fullOldFolder = Path.GetFullPath(oldFolder);
        if (!fullOldPath.StartsWith(fullOldFolder, StringComparison.OrdinalIgnoreCase))
        {
            return oldPath;
        }

        var relative = fullOldPath.Length == fullOldFolder.Length
            ? string.Empty
            : fullOldPath[(fullOldFolder.Length + 1)..];
        return Path.Combine(newFolder, relative);
    }

    private static string BuildArchivedStatusNote(Candidate candidate, KeepPolicy keepPolicy, Candidate keptCandidate)
    {
        var previous = string.IsNullOrWhiteSpace(candidate.Record.StatusNote)
            ? string.Empty
            : candidate.Record.StatusNote.Trim();
        var archiveNote =
            $"Archived duplicate generated model by GeneratedObjectAssetCleaner. " +
            $"Group={candidate.GroupKey}; policy={FormatKeepPolicy(keepPolicy)}; kept={keptCandidate.RequestId}.";
        return string.IsNullOrWhiteSpace(previous) ? archiveNote : $"{previous} {archiveNote}";
    }

    private static string BuildReport(
        IReadOnlyCollection<Candidate> candidates,
        IReadOnlyCollection<IGrouping<string, Candidate>> duplicateGroups,
        IReadOnlyCollection<Candidate> staleCandidates,
        IReadOnlyDictionary<string, Candidate> keptByGroup,
        KeepPolicy keepPolicy,
        bool execute)
    {
        var builder = new StringBuilder(4096);
        builder.AppendLine("SceneShift Generated Object Duplicate Report");
        builder.AppendLine($"CreatedUtc: {DateTime.UtcNow:O}");
        builder.AppendLine($"Mode: {(execute ? "cleanup" : "report")}");
        builder.AppendLine($"KeepPolicy: {FormatKeepPolicy(keepPolicy)}");
        builder.AppendLine($"CandidateFolders: {candidates.Count}");
        builder.AppendLine($"DuplicateGroups: {duplicateGroups.Count}");
        builder.AppendLine($"StaleFolders: {staleCandidates.Count}");
        builder.AppendLine($"StaleMegabytes: {staleCandidates.Sum(candidate => candidate.FolderBytes) / (1024f * 1024f):0.0}");
        builder.AppendLine();

        foreach (var group in duplicateGroups)
        {
            builder.AppendLine($"GROUP {group.Key}");
            var keeper = keptByGroup[group.Key];
            builder.AppendLine("  KEEP " + FormatCandidate(keeper));
            foreach (var stale in group.Where(candidate => !ReferenceEquals(candidate, keeper))
                         .OrderByDescending(candidate => candidate.UpdatedAtUtc))
            {
                builder.AppendLine("  MOVE " + FormatCandidate(stale));
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static string FormatCandidate(Candidate candidate)
    {
        return $"{candidate.RequestId} | state={candidate.Record.State} | passed={candidate.Record.QualityReviewPassed} | " +
               $"score={candidate.Record.QualityScore:0.###} | updated={candidate.UpdatedAtUtc:O} | " +
               $"mb={candidate.FolderBytes / (1024f * 1024f):0.0}";
    }

    private static string WriteReport(string projectRoot, string report, bool execute)
    {
        var reportDirectory = Path.Combine(projectRoot, "Library", ArchiveFolderName, "Reports");
        Directory.CreateDirectory(reportDirectory);
        var prefix = execute ? "cleanup" : "report";
        var reportPath = Path.Combine(
            reportDirectory,
            $"{prefix}_duplicate_generated_models_{DateTime.UtcNow:yyyyMMdd_HHmmss}.txt");
        File.WriteAllText(reportPath, report);
        return reportPath;
    }

    private static GeneratedAssetRecord TryReadRecord(string jobPath)
    {
        try
        {
            var json = File.ReadAllText(jobPath);
            return string.IsNullOrWhiteSpace(json) ? null : JsonUtility.FromJson<GeneratedAssetRecord>(json);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[GeneratedObjectAssetCleaner] Failed to read job '{jobPath}': {exception.Message}");
            return null;
        }
    }

    private static string BuildGroupKey(GeneratedAssetRecord record)
    {
        var themeId = NormalizeToken(record.ThemeId, "unknown_theme");
        var styleVariantId = NormalizeToken(record.StyleVariantId, "preset");
        var objectId = NormalizeToken(record.ObjectId, InferObjectId(record.RequestId));
        return $"{themeId}|{styleVariantId}|{objectId}";
    }

    private static string InferObjectId(string requestId)
    {
        if (string.IsNullOrWhiteSpace(requestId))
        {
            return "unknown_object";
        }

        var tokens = requestId.Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            return "unknown_object";
        }

        var semantic = tokens[0];
        foreach (var token in tokens.Skip(1))
        {
            if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            {
                return $"{semantic}_{token}";
            }
        }

        return requestId;
    }

    private static string NormalizeToken(string value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value)
            ? fallback
            : value.Trim().ToLowerInvariant();
    }

    private static DateTime ParseUtc(string value)
    {
        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.ToUniversalTime()
            : DateTime.MinValue;
    }

    private static long GetDirectoryBytes(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return 0;
        }

        return Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
            .Sum(path => new FileInfo(path).Length);
    }

    private static string ToFullPath(string projectRoot, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        if (Path.IsPathRooted(path))
        {
            return Path.GetFullPath(path);
        }

        return Path.GetFullPath(Path.Combine(projectRoot, path));
    }

    private static string GetUniquePath(string path)
    {
        if (!Directory.Exists(path) && !File.Exists(path))
        {
            return path;
        }

        var parent = Path.GetDirectoryName(path) ?? string.Empty;
        var name = Path.GetFileName(path);
        for (var index = 1; index < 1000; index++)
        {
            var candidate = Path.Combine(parent, $"{name}_{index:000}");
            if (!Directory.Exists(candidate) && !File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(parent, $"{name}_{Guid.NewGuid():N}");
    }

    private static void MoveMetaFileIfPresent(string oldFolder, string newFolder)
    {
        var oldMeta = oldFolder + ".meta";
        if (!File.Exists(oldMeta))
        {
            return;
        }

        var destinationMeta = Path.Combine(newFolder, Path.GetFileName(oldFolder) + ".folder.meta");
        File.Move(oldMeta, GetUniquePath(destinationMeta));
    }

    private static string FormatKeepPolicy(KeepPolicy keepPolicy)
    {
        return keepPolicy switch
        {
            KeepPolicy.BestPassed => "keep best passed/highest quality",
            _ => "keep newest",
        };
    }

    private static string GetProjectRoot()
    {
        return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    }
}
