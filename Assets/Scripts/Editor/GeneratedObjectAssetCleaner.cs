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
    private const string RuntimeModelFolderName = "GeneratedObjectRuntimeModels";
    private const string ReviewFolderName = "GeneratedObjectReviews";
    private const string PreDeviceRequestPrefix = "predevice_room_loop";

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

    private sealed class PreDeviceRuntimeCandidate
    {
        public GeneratedAssetRecord Record;
        public string RequestId;
        public string JobPath;
        public string RuntimeModelFolder;
        public List<string> JobArtifactPaths = new();
        public List<string> ReviewRecordPaths = new();
        public DateTime UpdatedAtUtc;
        public long Bytes;
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

    [MenuItem("SceneShift/Generated Objects/Report Pre-Device Runtime Artifacts")]
    public static void ReportPreDeviceRuntimeArtifacts()
    {
        RunPreDeviceRuntimeArtifactArchive(execute: false);
    }

    [MenuItem("SceneShift/Generated Objects/Archive Pre-Device Runtime Artifacts - Keep Latest")]
    public static void ArchivePreDeviceRuntimeArtifactsKeepLatest()
    {
        RunPreDeviceRuntimeArtifactArchive(execute: true);
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

    private static void RunPreDeviceRuntimeArtifactArchive(bool execute)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[GeneratedObjectAssetCleaner] Exit Play Mode before archiving pre-device runtime artifacts.");
            return;
        }

        var projectRoot = GetProjectRoot();
        var candidates = LoadPreDeviceRuntimeCandidates(projectRoot)
            .OrderByDescending(candidate => candidate.UpdatedAtUtc)
            .ThenByDescending(candidate => candidate.RequestId, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var keepCandidate = candidates.FirstOrDefault();
        var staleCandidates = keepCandidate == null
            ? new List<PreDeviceRuntimeCandidate>()
            : candidates.Where(candidate => !ReferenceEquals(candidate, keepCandidate)).ToList();
        var report = BuildPreDeviceRuntimeArtifactReport(candidates, keepCandidate, staleCandidates, execute);
        var reportPath = WritePreDeviceRuntimeArtifactReport(projectRoot, report, execute);
        Debug.Log($"[GeneratedObjectAssetCleaner] Pre-device runtime artifact report written: {reportPath}");

        if (!execute || staleCandidates.Count == 0)
        {
            return;
        }

        var archiveRoot = Path.Combine(
            projectRoot,
            "Library",
            ArchiveFolderName,
            "PreDeviceRuntimeArtifacts",
            DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(archiveRoot);

        var archivedCount = 0;
        foreach (var staleCandidate in staleCandidates)
        {
            if (ArchivePreDeviceRuntimeCandidate(staleCandidate, archiveRoot))
            {
                archivedCount++;
            }
        }

        File.WriteAllText(Path.Combine(archiveRoot, "archive_report.txt"), report);
        AssetDatabase.Refresh();
        Debug.Log($"[GeneratedObjectAssetCleaner] Archived {archivedCount}/{staleCandidates.Count} pre-device runtime artifact set(s) to {archiveRoot}");
    }

    private static List<PreDeviceRuntimeCandidate> LoadPreDeviceRuntimeCandidates(string projectRoot)
    {
        var candidates = new List<PreDeviceRuntimeCandidate>();
        var jobDirectory = Path.Combine(projectRoot, "Library", JobFolderName);
        if (!Directory.Exists(jobDirectory))
        {
            return candidates;
        }

        var runtimeModelRoot = Path.Combine(Application.persistentDataPath, RuntimeModelFolderName);
        var reviewRoot = Path.Combine(Application.persistentDataPath, ReviewFolderName);
        foreach (var jobPath in Directory.GetFiles(jobDirectory, $"{PreDeviceRequestPrefix}_*.job.json", SearchOption.TopDirectoryOnly))
        {
            var record = TryReadRecord(jobPath);
            if (record == null || string.IsNullOrWhiteSpace(record.RequestId))
            {
                continue;
            }

            if (!record.RequestId.StartsWith(PreDeviceRequestPrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var jobArtifactPaths = Directory.GetFiles(jobDirectory, $"{record.RequestId}.*", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var runtimeModelFolder = Path.Combine(runtimeModelRoot, record.RequestId);
            var reviewRecordPaths = Directory.Exists(reviewRoot)
                ? Directory.GetFiles(reviewRoot, $"*{record.RequestId}*.review.json", SearchOption.TopDirectoryOnly)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToList()
                : new List<string>();
            var bytes = jobArtifactPaths.Sum(GetFileBytes) +
                        (Directory.Exists(runtimeModelFolder) ? GetDirectoryBytes(runtimeModelFolder) : 0) +
                        reviewRecordPaths.Sum(GetFileBytes);
            candidates.Add(new PreDeviceRuntimeCandidate
            {
                Record = record,
                RequestId = record.RequestId,
                JobPath = jobPath,
                RuntimeModelFolder = runtimeModelFolder,
                JobArtifactPaths = jobArtifactPaths,
                ReviewRecordPaths = reviewRecordPaths,
                UpdatedAtUtc = ParseUtc(record.UpdatedAtIsoUtc),
                Bytes = bytes,
            });
        }

        return candidates;
    }

    private static bool ArchivePreDeviceRuntimeCandidate(PreDeviceRuntimeCandidate candidate, string archiveRoot)
    {
        if (candidate == null || string.IsNullOrWhiteSpace(candidate.RequestId))
        {
            return false;
        }

        var movedAny = false;
        var jobArchive = Path.Combine(archiveRoot, "Library", JobFolderName, candidate.RequestId);
        foreach (var path in candidate.JobArtifactPaths)
        {
            movedAny |= MoveFileToArchive(path, jobArchive);
        }

        if (!string.IsNullOrWhiteSpace(candidate.RuntimeModelFolder) && Directory.Exists(candidate.RuntimeModelFolder))
        {
            var runtimeArchive = Path.Combine(
                archiveRoot,
                "PersistentDataPath",
                RuntimeModelFolderName,
                Path.GetFileName(candidate.RuntimeModelFolder));
            movedAny |= MoveDirectoryToArchive(candidate.RuntimeModelFolder, runtimeArchive);
        }

        var reviewArchive = Path.Combine(archiveRoot, "PersistentDataPath", ReviewFolderName, candidate.RequestId);
        foreach (var path in candidate.ReviewRecordPaths)
        {
            movedAny |= MoveFileToArchive(path, reviewArchive);
        }

        return movedAny;
    }

    private static string BuildPreDeviceRuntimeArtifactReport(
        IReadOnlyCollection<PreDeviceRuntimeCandidate> candidates,
        PreDeviceRuntimeCandidate keepCandidate,
        IReadOnlyCollection<PreDeviceRuntimeCandidate> staleCandidates,
        bool execute)
    {
        var builder = new StringBuilder(4096);
        builder.AppendLine("SceneShift Pre-Device Runtime Artifact Report");
        builder.AppendLine($"CreatedUtc: {DateTime.UtcNow:O}");
        builder.AppendLine($"Mode: {(execute ? "archive" : "report")}");
        builder.AppendLine($"CandidateSets: {candidates.Count}");
        builder.AppendLine($"KeptRequestId: {keepCandidate?.RequestId ?? "none"}");
        builder.AppendLine($"ArchivedSets: {staleCandidates.Count}");
        builder.AppendLine($"ArchivedMegabytes: {staleCandidates.Sum(candidate => candidate.Bytes) / (1024f * 1024f):0.0}");
        builder.AppendLine();
        foreach (var candidate in candidates.OrderByDescending(candidate => candidate.UpdatedAtUtc))
        {
            var action = ReferenceEquals(candidate, keepCandidate) ? "KEEP" : execute ? "ARCHIVE" : "WOULD_ARCHIVE";
            builder.AppendLine($"{action} {FormatPreDeviceRuntimeCandidate(candidate)}");
        }

        return builder.ToString();
    }

    private static string FormatPreDeviceRuntimeCandidate(PreDeviceRuntimeCandidate candidate)
    {
        return $"{candidate.RequestId} | state={candidate.Record.State} | updated={candidate.UpdatedAtUtc:O} | " +
               $"jobArtifacts={candidate.JobArtifactPaths.Count} | runtimeModel={Directory.Exists(candidate.RuntimeModelFolder)} | " +
               $"reviewRecords={candidate.ReviewRecordPaths.Count} | mb={candidate.Bytes / (1024f * 1024f):0.0}";
    }

    private static string WritePreDeviceRuntimeArtifactReport(string projectRoot, string report, bool execute)
    {
        var reportDirectory = Path.Combine(projectRoot, "Library", ArchiveFolderName, "Reports");
        Directory.CreateDirectory(reportDirectory);
        var prefix = execute ? "archive" : "report";
        var reportPath = Path.Combine(
            reportDirectory,
            $"{prefix}_predevice_runtime_artifacts_{DateTime.UtcNow:yyyyMMdd_HHmmss}.txt");
        File.WriteAllText(reportPath, report);
        return reportPath;
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
            GeneratedObjectJobState.RuntimeLoaded => 5,
            GeneratedObjectJobState.Imported => 4,
            GeneratedObjectJobState.NeedsReview => 3,
            GeneratedObjectJobState.RuntimeModelDownloaded => 3,
            GeneratedObjectJobState.ModelReady => 2,
            GeneratedObjectJobState.RuntimeModelReady => 2,
            GeneratedObjectJobState.ModelGenerationSubmitted => 1,
            GeneratedObjectJobState.RuntimeBackendSubmitted => 1,
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

    private static long GetFileBytes(string path)
    {
        return !string.IsNullOrWhiteSpace(path) && File.Exists(path) ? new FileInfo(path).Length : 0;
    }

    private static bool MoveFileToArchive(string sourcePath, string archiveDirectory)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            return false;
        }

        try
        {
            Directory.CreateDirectory(archiveDirectory);
            var destinationPath = GetUniquePath(Path.Combine(archiveDirectory, Path.GetFileName(sourcePath)));
            File.Move(sourcePath, destinationPath);
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[GeneratedObjectAssetCleaner] Failed to archive file '{sourcePath}': {exception.Message}");
            return false;
        }
    }

    private static bool MoveDirectoryToArchive(string sourceDirectory, string destinationDirectory)
    {
        if (string.IsNullOrWhiteSpace(sourceDirectory) || !Directory.Exists(sourceDirectory))
        {
            return false;
        }

        try
        {
            var parent = Path.GetDirectoryName(destinationDirectory);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                Directory.CreateDirectory(parent);
            }

            Directory.Move(sourceDirectory, GetUniquePath(destinationDirectory));
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[GeneratedObjectAssetCleaner] Failed to archive folder '{sourceDirectory}': {exception.Message}");
            return false;
        }
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
