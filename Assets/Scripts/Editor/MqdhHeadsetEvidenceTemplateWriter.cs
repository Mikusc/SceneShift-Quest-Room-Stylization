using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

public static class MqdhHeadsetEvidenceTemplateWriter
{
    private const string CanonicalScenePath = "Assets/Scenes/MR_RoomStylization.unity";
    private const string EvidenceFolderName = "MQDHHeadsetEvidence";
    private const string PreDeviceRequestPrefix = "predevice_room_loop";
    private const string GeneratedObjectJobFolderName = "GeneratedObjectJobs";
    private const string GeneratedObjectRuntimeModelFolderName = "GeneratedObjectRuntimeModels";

    [MenuItem("SceneShift/Validation/Create MQDH Headset Evidence Template")]
    public static string CreateTemplate()
    {
        var projectRoot = GetProjectRoot();
        var evidenceDirectory = Path.Combine(projectRoot, "Library", EvidenceFolderName);
        Directory.CreateDirectory(evidenceDirectory);

        var reportId = $"mqdh_headset_evidence_{DateTime.UtcNow:yyyyMMddHHmmss}";
        var path = Path.Combine(evidenceDirectory, $"{reportId}.md");
        File.WriteAllText(path, BuildTemplate(reportId, projectRoot));
        Debug.Log($"[MQDHHeadsetEvidence] Template written: {path}");
        return path;
    }

    private static string BuildTemplate(string reportId, string projectRoot)
    {
        var latestReadiness = GetLatestFile(Path.Combine(projectRoot, "Library", "PreDeviceBuildReadinessReports"), "predevice_build_readiness_*.md");
        var latestSmokeMarkdown = GetLatestFile(Path.Combine(projectRoot, "Library", "PreDeviceSmokeReports"), "predevice_smoke_*.md");
        var latestSmokeJson = GetLatestFile(Path.Combine(projectRoot, "Library", "PreDeviceSmokeReports"), "predevice_smoke_*.json");
        var latestVisualReview = GetLatestFile(Path.Combine(projectRoot, "Library", "PreDeviceVisualEvidence"), "predevice_visual_review_*.md");
        var latestVisualImage = GetLatestFile(Path.Combine(projectRoot, "Library", "PreDeviceVisualEvidence"), "*.png");
        var latestHandoffPreflight = GetLatestFile(Path.Combine(projectRoot, "Library", EvidenceFolderName), "mqdh_handoff_preflight_*.md");
        var latestTerminalSuite = GetLatestFile(Path.Combine(projectRoot, "Library", EvidenceFolderName), "mqdh_terminal_prepackage_suite_*.md");
        var latestHandoffBundle = GetLatestHandoffBundleManifest(projectRoot);
        var latestLocalGate = GetLatestFile(Path.Combine(projectRoot, "Library", EvidenceFolderName), "predevice_local_gate_*.md");
        var activeRuntimeEvidence = FindLatestPreDeviceRuntimeEvidence(projectRoot);
        var androidPlaybackEnginePath = GetAndroidPlaybackEnginePath();
        var readinessOverall = ExtractMarkdownValue(latestReadiness, "Overall");
        var smokeOverall = ExtractMarkdownValue(latestSmokeMarkdown, "Overall");
        var handoffOverall = ExtractMarkdownValue(latestHandoffPreflight, "Overall");
        var terminalSuiteOverall = ExtractMarkdownValue(latestTerminalSuite, "Overall");
        var localGateOverall = ExtractMarkdownValue(latestLocalGate, "Overall");
        var localGatePackageArtifact = ExtractMarkdownValue(latestLocalGate, "Package artifact");
        var buildReadinessBlocksPackaging = string.Equals(readinessOverall, "Fail", StringComparison.OrdinalIgnoreCase);

        var builder = new StringBuilder(4096);
        builder.AppendLine($"# {reportId}");
        builder.AppendLine();
        builder.AppendLine("## Purpose");
        builder.AppendLine();
        builder.AppendLine("Use this file during the first MQDH/test-channel headset run. Fill it in while installing, launching, recording logs, and validating the standalone Quest app.");
        builder.AppendLine();
        builder.AppendLine("## Pre-Package Snapshot");
        builder.AppendLine();
        builder.AppendLine($"- Created UTC: `{DateTime.UtcNow:O}`");
        builder.AppendLine($"- Unity version: `{Application.unityVersion}`");
        builder.AppendLine($"- Active build target: `{EditorUserBuildSettings.activeBuildTarget}`");
        builder.AppendLine($"- Selected build target group: `{EditorUserBuildSettings.selectedBuildTargetGroup}`");
        builder.AppendLine($"- Android playback engine path exists: `{Directory.Exists(androidPlaybackEnginePath)}`");
        builder.AppendLine($"- Android playback engine path: `{androidPlaybackEnginePath}`");
        builder.AppendLine($"- Application identifier: `{PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android)}`");
        builder.AppendLine($"- Bundle version: `{PlayerSettings.bundleVersion}`");
        builder.AppendLine($"- Android bundle version code: `{PlayerSettings.Android.bundleVersionCode}`");
        builder.AppendLine($"- Canonical scene: `{CanonicalScenePath}`");
        builder.AppendLine($"- Latest pre-device smoke: `{FormatPath(latestSmokeMarkdown)}`");
        builder.AppendLine($"- Latest pre-device smoke JSON: `{FormatPath(latestSmokeJson)}`");
        builder.AppendLine($"- Latest smoke overall: `{FormatValue(smokeOverall)}`");
        builder.AppendLine($"- Latest visual review: `{FormatPath(latestVisualReview)}`");
        builder.AppendLine($"- Latest visual image: `{FormatPath(latestVisualImage)}`");
        builder.AppendLine($"- Latest build readiness: `{FormatPath(latestReadiness)}`");
        builder.AppendLine($"- Latest build readiness overall: `{FormatValue(readinessOverall)}`");
        builder.AppendLine($"- Existing MQDH handoff preflight at template creation: `{FormatPath(latestHandoffPreflight)}`");
        builder.AppendLine($"- Existing MQDH handoff preflight overall at template creation: `{FormatValue(handoffOverall)}`");
        builder.AppendLine($"- Existing terminal pre-package suite at template creation: `{FormatPath(latestTerminalSuite)}`");
        builder.AppendLine($"- Existing terminal pre-package suite overall at template creation: `{FormatValue(terminalSuiteOverall)}`");
        builder.AppendLine($"- Existing handoff bundle manifest at template creation: `{FormatPath(latestHandoffBundle)}`");
        builder.AppendLine($"- Existing pre-device local gate at template creation: `{FormatPath(latestLocalGate)}`");
        builder.AppendLine($"- Existing pre-device local gate overall at template creation: `{FormatValue(localGateOverall)}`");
        builder.AppendLine($"- Existing local gate package artifact at template creation: `{FormatValue(localGatePackageArtifact)}`");
        builder.AppendLine($"- Packaging allowed now: `{(!buildReadinessBlocksPackaging).ToString()}`");
        builder.AppendLine();
        builder.AppendLine("## Active Runtime Evidence");
        builder.AppendLine();
        builder.AppendLine(activeRuntimeEvidence);
        builder.AppendLine();
        builder.AppendLine("## Stop Before Packaging If");
        builder.AppendLine();
        builder.AppendLine("- [ ] Latest build readiness has any `Fail`.");
        builder.AppendLine("- [ ] Latest MQDH handoff preflight has any `Fail`.");
        builder.AppendLine("- [ ] Latest terminal pre-package suite has not been rerun after the latest Unity MQDH pre-package suite.");
        builder.AppendLine("- [ ] Latest handoff bundle has not passed `bash Tools/verify_mqdh_handoff_bundle.sh`.");
        builder.AppendLine("- [ ] Latest pre-package local gate has not passed `bash Tools/verify_predevice_local_gate.sh`.");
        builder.AppendLine("- [ ] Android Build Support is missing for this Unity editor.");
        builder.AppendLine("- [ ] Unity Console has compile errors.");
        builder.AppendLine("- [ ] Latest smoke report is missing or failed.");
        builder.AppendLine("- [ ] Latest visual evidence does not reference the latest smoke report and screenshot.");
        builder.AppendLine("- [ ] Active pre-device runtime evidence is not the latest smoke-linked request.");
        builder.AppendLine("- [ ] Service credentials appear in scene files, packaged config/assets, or generated job records.");
        builder.AppendLine("- [ ] Android Build Support is missing and `bash Tools/install_unity_android_support.sh --run --wait-for-close` has not been run.");
        builder.AppendLine("- [ ] After APK/AAB build, final local gate has not been rerun with `--package-artifact <apk-or-aab-path>` and verified with `--require-package-artifact`.");
        builder.AppendLine();
        builder.AppendLine("## MQDH/Test-Channel Install");
        builder.AppendLine();
        builder.AppendLine("- Terminal pre-package suite report:");
        builder.AppendLine("- MQDH package build report:");
        builder.AppendLine("- Pre-package local gate report:");
        builder.AppendLine("- Pre-package local gate verification result:");
        builder.AppendLine("- Build artifact path:");
        builder.AppendLine("- Final package local gate report:");
        builder.AppendLine("- Final package gate verification result:");
        builder.AppendLine("- Package artifact verification result:");
        builder.AppendLine("- MQDH/test-channel install method:");
        builder.AppendLine("- Install/update time:");
        builder.AppendLine("- Headset model:");
        builder.AppendLine("- Headset OS version:");
        builder.AppendLine("- Meta Quest Developer Hub version:");
        builder.AppendLine("- App version shown in headset:");
        builder.AppendLine("- ADB device serial/log source:");
        builder.AppendLine();
        builder.AppendLine("## Headset Validation Flow");
        builder.AppendLine();
        builder.AppendLine("- [ ] App launches as a standalone headset app.");
        builder.AppendLine("- [ ] MRUK room becomes ready.");
        builder.AppendLine("- [ ] Intended room id/name is shown:");
        builder.AppendLine("- [ ] Dashboard text is readable and reachable.");
        builder.AppendLine("- [ ] Official ray/poke interaction is visible and usable.");
        builder.AppendLine("- [ ] Style selected or entered:");
        builder.AppendLine("- [ ] Safe target object id:");
        builder.AppendLine("- [ ] Safe target semantic:");
        builder.AppendLine("- [ ] `Submit+Load` or local-test runtime flow starts from the headset UI.");
        builder.AppendLine("- [ ] Runtime model URL or failure reason:");
        builder.AppendLine("- [ ] Runtime GLB downloads under the app runtime path.");
        builder.AppendLine("- [ ] Runtime model loads without an Editor import step.");
        builder.AppendLine("- [ ] Runtime model fits the target bounds.");
        builder.AppendLine("- [ ] `Accept` outcome:");
        builder.AppendLine("- [ ] `Reject` outcome:");
        builder.AppendLine("- [ ] `Reset` outcome:");
        builder.AppendLine("- [ ] Bounded correction outcome:");
        builder.AppendLine("- [ ] Reject/reset hides or releases the runtime model instance without growing loaded-object count.");
        builder.AppendLine("- [ ] Restart restore outcome:");
        builder.AppendLine("- [ ] Clean View outcome:");
        builder.AppendLine("- [ ] Passthrough-only toggle outcome:");
        builder.AppendLine();
        builder.AppendLine("## Logs And Media");
        builder.AppendLine();
        builder.AppendLine("Recommended ADB collection command after the app is installed and open on the headset:");
        builder.AppendLine();
        builder.AppendLine("```bash");
        builder.AppendLine($"bash Tools/collect_mqdh_headset_evidence.sh --package {PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android)} --template Library/{EvidenceFolderName}/{reportId}.md");
        builder.AppendLine("bash Tools/verify_mqdh_headset_evidence.sh");
        builder.AppendLine("```");
        builder.AppendLine();
        builder.AppendLine("Recommended terminal pre-package suite command after the Unity suite and before Android switching/package build:");
        builder.AppendLine();
        builder.AppendLine("```bash");
        builder.AppendLine("bash Tools/run_mqdh_terminal_prepackage_suite.sh");
        builder.AppendLine("bash Tools/audit_true_device_preflight.sh");
        builder.AppendLine("```");
        builder.AppendLine();
        builder.AppendLine("Recommended Android Support install command if readiness reports `android_build_support_installed=Fail`:");
        builder.AppendLine();
        builder.AppendLine("```bash");
        builder.AppendLine($"bash Tools/install_unity_android_support.sh --run --wait-for-close --version {Application.unityVersion}");
        builder.AppendLine("```");
        builder.AppendLine();
        builder.AppendLine("Recommended Unity package build menu after Android Build Support is installed and the Editor build target is Android:");
        builder.AppendLine();
        builder.AppendLine("```text");
        builder.AppendLine("SceneShift/Validation/Build MQDH Test Package");
        builder.AppendLine("```");
        builder.AppendLine();
        builder.AppendLine("Recommended final package gate commands before MQDH/test-channel upload:");
        builder.AppendLine();
        builder.AppendLine("```bash");
        builder.AppendLine("bash Tools/run_predevice_local_gate.sh --package-artifact <apk-or-aab-path>");
        builder.AppendLine("bash Tools/verify_predevice_local_gate.sh --require-package-artifact");
        builder.AppendLine("# Optional package-only debugging:");
        builder.AppendLine("bash Tools/verify_mqdh_package_artifact.sh <apk-or-aab-path>");
        builder.AppendLine("```");
        builder.AppendLine();
        builder.AppendLine("- MQDH/ADB log file or excerpt:");
        builder.AppendLine("- Headset video/screenshots:");
        builder.AppendLine("- Pulled persistent app files:");
        builder.AppendLine("- Headset evidence verification result:");
        builder.AppendLine("- Failure stage if any:");
        builder.AppendLine();
        builder.AppendLine("## Notes");
        builder.AppendLine();
        builder.AppendLine("- Keep this as true-device evidence only after the app was installed or updated on the headset through MQDH or the configured test release channel.");
        builder.AppendLine("- Do not mark native PCA capture passed from Editor, Simulator, or desktop screenshots.");
        builder.AppendLine("- Do not embed APIMart, Seed3D, DeepSeek, upload, or signing credentials in the APK.");
        return builder.ToString();
    }

    private static string FindLatestPreDeviceRuntimeEvidence(string projectRoot)
    {
        var jobDirectory = Path.Combine(projectRoot, "Library", GeneratedObjectJobFolderName);
        if (!Directory.Exists(jobDirectory))
        {
            return $"- Active request: `missing Library/{GeneratedObjectJobFolderName}`";
        }

        string latestRequestId = null;
        string latestJobPath = null;
        var latestTime = DateTime.MinValue;
        foreach (var jobPath in Directory.GetFiles(jobDirectory, $"{PreDeviceRequestPrefix}_*.job.json", SearchOption.TopDirectoryOnly))
        {
            var fileTime = File.GetLastWriteTimeUtc(jobPath);
            if (fileTime <= latestTime)
            {
                continue;
            }

            latestTime = fileTime;
            latestJobPath = jobPath;
            latestRequestId = ExtractRequestIdFromJobPath(jobPath);
        }

        if (string.IsNullOrWhiteSpace(latestRequestId))
        {
            return $"- Active request: `missing {PreDeviceRequestPrefix}_*.job.json`";
        }

        var requestPath = Path.Combine(jobDirectory, $"{latestRequestId}.request.json");
        var promptPath = Path.Combine(jobDirectory, $"{latestRequestId}.prompt.txt");
        var runtimeSubmissionPath = Path.Combine(jobDirectory, $"{latestRequestId}.runtime-submission.json");
        var runtimeResultPath = Path.Combine(jobDirectory, $"{latestRequestId}.runtime-result.json");
        var runtimeModelFolder = Path.Combine(Application.persistentDataPath, GeneratedObjectRuntimeModelFolderName, latestRequestId);
        var runtimeGlbCount = Directory.Exists(runtimeModelFolder)
            ? Directory.GetFiles(runtimeModelFolder, "*.glb", SearchOption.AllDirectories).Length
            : 0;

        var builder = new StringBuilder();
        builder.AppendLine($"- Active request: `{latestRequestId}`");
        builder.AppendLine($"- Job file: `{FormatPath(latestJobPath)}`");
        builder.AppendLine($"- Request file exists: `{File.Exists(requestPath)}`");
        builder.AppendLine($"- Prompt file exists: `{File.Exists(promptPath)}`");
        builder.AppendLine($"- Runtime submission exists: `{File.Exists(runtimeSubmissionPath)}`");
        builder.AppendLine($"- Runtime result exists: `{File.Exists(runtimeResultPath)}`");
        builder.AppendLine($"- Runtime model folder: `{runtimeModelFolder}`");
        builder.AppendLine($"- Runtime GLB count: `{runtimeGlbCount}`");
        return builder.ToString().TrimEnd();
    }

    private static string GetLatestFile(string directory, string searchPattern)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return string.Empty;
        }

        string latestPath = null;
        var latestTime = DateTime.MinValue;
        foreach (var path in Directory.GetFiles(directory, searchPattern, SearchOption.TopDirectoryOnly))
        {
            var writeTime = File.GetLastWriteTimeUtc(path);
            if (writeTime <= latestTime)
            {
                continue;
            }

            latestTime = writeTime;
            latestPath = path;
        }

        return latestPath ?? string.Empty;
    }

    private static string GetLatestHandoffBundleManifest(string projectRoot)
    {
        var evidenceDirectory = Path.Combine(projectRoot, "Library", EvidenceFolderName);
        if (string.IsNullOrWhiteSpace(evidenceDirectory) || !Directory.Exists(evidenceDirectory))
        {
            return string.Empty;
        }

        string latestPath = null;
        var latestTime = DateTime.MinValue;
        foreach (var directory in Directory.GetDirectories(evidenceDirectory, "handoff_bundle_*", SearchOption.TopDirectoryOnly))
        {
            var manifest = Path.Combine(directory, "manifest.md");
            if (!File.Exists(manifest))
            {
                continue;
            }

            var writeTime = File.GetLastWriteTimeUtc(manifest);
            if (writeTime <= latestTime)
            {
                continue;
            }

            latestTime = writeTime;
            latestPath = manifest;
        }

        return latestPath ?? string.Empty;
    }

    private static string GetAndroidPlaybackEnginePath()
    {
        foreach (var candidate in GetAndroidPlaybackEngineCandidatePaths())
        {
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return GetAndroidPlaybackEngineCandidatePaths()[0];
    }

    private static string[] GetAndroidPlaybackEngineCandidatePaths()
    {
        var embeddedPath = Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines", "AndroidPlayer");
        var appBundlePath = Directory.GetParent(EditorApplication.applicationContentsPath)?.FullName;
        var editorRootPath = string.IsNullOrWhiteSpace(appBundlePath)
            ? null
            : Directory.GetParent(appBundlePath)?.FullName;
        var hubModulePath = string.IsNullOrWhiteSpace(editorRootPath)
            ? embeddedPath
            : Path.Combine(editorRootPath, "PlaybackEngines", "AndroidPlayer");

        return new[] { hubModulePath, embeddedPath };
    }

    private static string ExtractRequestIdFromJobPath(string jobPath)
    {
        var fileName = Path.GetFileName(jobPath);
        const string suffix = ".job.json";
        return fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? fileName.Substring(0, fileName.Length - suffix.Length)
            : Path.GetFileNameWithoutExtension(jobPath);
    }

    private static string ExtractMarkdownValue(string path, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return string.Empty;
        }

        var pattern = "-\\s*" + Regex.Escape(fieldName) + "\\s*:\\s*`([^`]*)`";
        foreach (var line in File.ReadLines(path))
        {
            var match = Regex.Match(line, pattern, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return match.Groups[1].Value.Trim();
            }
        }

        return string.Empty;
    }

    private static string FormatPath(string path)
    {
        return string.IsNullOrWhiteSpace(path) ? "missing" : path;
    }

    private static string FormatValue(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "unknown" : value;
    }

    private static string GetProjectRoot()
    {
        return Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
    }
}
