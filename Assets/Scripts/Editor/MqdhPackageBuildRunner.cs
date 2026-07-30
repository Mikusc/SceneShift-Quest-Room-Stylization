using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Unity.Android.Types;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

public static class MqdhPackageBuildRunner
{
    private const string ReportFolderName = "MQDHPackageBuildReports";
    private const string BuildFolderName = "Builds/MQDH";

    [MenuItem("SceneShift/Validation/Build MQDH Test Package")]
    public static MqdhPackageBuildReportData BuildPackage()
    {
        var projectRoot = GetProjectRoot();
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var report = new MqdhPackageBuildReportData
        {
            ReportId = $"mqdh_package_build_{timestamp}",
            CreatedAtIsoUtc = DateTime.UtcNow.ToString("O"),
            ActiveBuildTarget = EditorUserBuildSettings.activeBuildTarget.ToString(),
            SelectedBuildTargetGroup = EditorUserBuildSettings.selectedBuildTargetGroup.ToString(),
            AndroidBuildAppBundle = EditorUserBuildSettings.buildAppBundle,
            PackageFormat = EditorUserBuildSettings.buildAppBundle ? "aab" : "apk",
        };

        var extension = report.AndroidBuildAppBundle ? "aab" : "apk";
        report.ArtifactPath = Path.Combine(projectRoot, BuildFolderName, $"SceneShiftQuest_{timestamp}.{extension}");

        var androidPlaybackEnginePath = GetAndroidPlaybackEnginePath();
        var hasAndroidPlaybackEngine = Directory.Exists(androidPlaybackEnginePath);
        var activeBuildTargetIsAndroid = EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android;
        if (hasAndroidPlaybackEngine && activeBuildTargetIsAndroid)
        {
            var readiness = PreDeviceBuildReadinessReportRunner.RunReport();
            report.ReadinessOverall = readiness?.OverallStatus ?? "unknown";
            report.ReadinessReportPath = readiness == null
                ? "missing"
                : Path.Combine(projectRoot, "Library", "PreDeviceBuildReadinessReports", $"{readiness.ReportId}.md");
        }
        else
        {
            report.ReadinessReportPath = GetLatestFile(Path.Combine(projectRoot, "Library", "PreDeviceBuildReadinessReports"), "predevice_build_readiness_*.md");
            report.ReadinessOverall = ExtractMarkdownValue(report.ReadinessReportPath, "Overall");
        }

        AddCheck(
            report,
            "android_support_files_present",
            hasAndroidPlaybackEngine,
            $"AndroidPlayerPathExists={hasAndroidPlaybackEngine}, path={androidPlaybackEnginePath}, candidates={string.Join(";", GetAndroidPlaybackEngineCandidatePaths())}");

        AddCheck(
            report,
            "readiness_not_fail",
            !string.Equals(report.ReadinessOverall, "Fail", StringComparison.OrdinalIgnoreCase),
            $"overall={report.ReadinessOverall}, report={report.ReadinessReportPath}");

        AddCheck(
            report,
            "active_build_target_android",
            activeBuildTargetIsAndroid,
            $"active={EditorUserBuildSettings.activeBuildTarget}, selectedGroup={EditorUserBuildSettings.selectedBuildTargetGroup}");

        var scenes = GetEnabledBuildScenes();
        report.SceneCount = scenes.Length;
        AddCheck(
            report,
            "enabled_build_scenes",
            scenes.Length > 0,
            scenes.Length == 0 ? "No enabled scenes in Build Settings." : string.Join(", ", scenes));

        ConfigureAndroidDebugSymbols(report);

        if (HasFailedCheck(report))
        {
            report.OverallStatus = DetermineBlockedStatus(report);
            report.SuggestedNextActions.Add("Do not build yet. Resolve the failing checks above, rerun `SceneShift/Validation/Run MQDH Pre-Package Evidence Suite`, then run `bash Tools/run_mqdh_terminal_prepackage_suite.sh`.");
            WriteReport(projectRoot, report);
            Debug.LogWarning($"[MQDHPackageBuild] {report.OverallStatus} report written for blocked package build: {report.ReportPath}");
            return report;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(report.ArtifactPath));

        var buildOptions = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = report.ArtifactPath,
            target = BuildTarget.Android,
            options = BuildOptions.None,
        };

        try
        {
            var unityBuildReport = BuildPipeline.BuildPlayer(buildOptions);
            CaptureUnityBuildSummary(report, unityBuildReport);
        }
        catch (Exception exception)
        {
            report.UnityBuildResult = "Exception";
            report.UnityBuildMessages = exception.ToString();
        }

        report.ArtifactExists = File.Exists(report.ArtifactPath);
        report.ArtifactBytes = report.ArtifactExists ? new FileInfo(report.ArtifactPath).Length : 0L;

        AddCheck(
            report,
            "unity_build_succeeded",
            string.Equals(report.UnityBuildResult, BuildResult.Succeeded.ToString(), StringComparison.Ordinal) && report.ArtifactExists,
            $"result={report.UnityBuildResult}, artifactExists={report.ArtifactExists}, bytes={report.ArtifactBytes}");

        if (report.ArtifactExists)
        {
            RunFinalGate(projectRoot, report);
        }

        if (HasFailedCheck(report))
        {
            report.OverallStatus = report.ArtifactExists ? "BuiltButFinalGateFailed" : "BuildFailed";
        }
        else
        {
            report.OverallStatus = "BuiltAndVerified";
            report.SuggestedNextActions.Add("Package build and final local gate passed. Continue with MQDH/test-channel upload or install, then collect headset evidence.");
        }

        WriteReport(projectRoot, report);
        if (string.Equals(report.OverallStatus, "BuiltAndVerified", StringComparison.Ordinal))
        {
            Debug.Log($"[MQDHPackageBuild] {report.OverallStatus}: {report.ArtifactPath}");
        }
        else
        {
            Debug.LogWarning($"[MQDHPackageBuild] {report.OverallStatus} report written: {report.ReportPath}");
        }

        return report;
    }

    private static void ConfigureAndroidDebugSymbols(MqdhPackageBuildReportData report)
    {
        UserBuildSettings.DebugSymbols.level = DebugSymbolLevel.Full;
        UserBuildSettings.DebugSymbols.format = DebugSymbolFormat.Zip | DebugSymbolFormat.LegacyExtensions;

        AddCheck(
            report,
            "android_debug_symbols_full_zip",
            UserBuildSettings.DebugSymbols.level == DebugSymbolLevel.Full &&
            (UserBuildSettings.DebugSymbols.format & DebugSymbolFormat.Zip) == DebugSymbolFormat.Zip,
            $"level={UserBuildSettings.DebugSymbols.level}, format={UserBuildSettings.DebugSymbols.format}");
    }

    private static void RunFinalGate(string projectRoot, MqdhPackageBuildReportData report)
    {
        try
        {
            var suitePath = MqdhPrePackageEvidenceSuiteRunner.RunSuite();
            report.ReadinessReportPath = GetLatestFile(
                Path.Combine(projectRoot, "Library", "PreDeviceBuildReadinessReports"),
                "predevice_build_readiness_*.md");
            report.ReadinessOverall = ExtractMarkdownValue(report.ReadinessReportPath, "Overall");
            report.FinalGateCommands.Add(new ShellCommandResult
            {
                Command = "SceneShift/Validation/Run MQDH Pre-Package Evidence Suite",
                ExitCode = 0,
                Output = $"Report: {suitePath}",
            });
        }
        catch (Exception exception)
        {
            report.FinalGateCommands.Add(new ShellCommandResult
            {
                Command = "SceneShift/Validation/Run MQDH Pre-Package Evidence Suite",
                ExitCode = 1,
                Output = exception.ToString(),
            });
            AddCheck(report, "final_prepackage_evidence_refresh", false, "Unity MQDH pre-package evidence refresh failed.");
            return;
        }

        var terminalSuite = RunShellCommand(projectRoot, "bash Tools/run_mqdh_terminal_prepackage_suite.sh");
        report.FinalGateCommands.Add(terminalSuite);
        AddCheck(
            report,
            "final_prepackage_terminal_refresh",
            terminalSuite.ExitCode == 0,
            $"exit={terminalSuite.ExitCode}");

        if (terminalSuite.ExitCode != 0)
        {
            return;
        }

        var localGate = RunShellCommand(projectRoot, $"bash Tools/run_predevice_local_gate.sh --package-artifact {ShellQuote(report.ArtifactPath)}");
        report.FinalGateCommands.Add(localGate);
        AddCheck(
            report,
            "final_local_gate_with_package_artifact",
            localGate.ExitCode == 0,
            $"exit={localGate.ExitCode}");

        var verifyGate = RunShellCommand(projectRoot, "bash Tools/verify_predevice_local_gate.sh --require-package-artifact");
        report.FinalGateCommands.Add(verifyGate);
        AddCheck(
            report,
            "final_local_gate_package_required_verification",
            verifyGate.ExitCode == 0,
            $"exit={verifyGate.ExitCode}");
    }

    private static ShellCommandResult RunShellCommand(string workingDirectory, string command)
    {
        var result = new ShellCommandResult
        {
            Command = command,
        };

        var startInfo = new ProcessStartInfo
        {
            FileName = "/bin/bash",
            Arguments = $"-lc {ShellQuote(command)}",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using (var process = Process.Start(startInfo))
        {
            if (process == null)
            {
                result.ExitCode = -1;
                result.Output = "Failed to start /bin/bash.";
                return result;
            }

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            result.ExitCode = process.ExitCode;
            result.Output = string.IsNullOrWhiteSpace(stderr)
                ? stdout.TrimEnd()
                : $"{stdout.TrimEnd()}\n{stderr.TrimEnd()}".Trim();
        }

        return result;
    }

    private static void CaptureUnityBuildSummary(MqdhPackageBuildReportData report, BuildReport unityBuildReport)
    {
        if (unityBuildReport == null)
        {
            report.UnityBuildResult = "MissingBuildReport";
            report.UnityBuildMessages = "BuildPipeline.BuildPlayer returned null.";
            return;
        }

        var summary = unityBuildReport.summary;
        report.UnityBuildResult = summary.result.ToString();
        report.UnityBuildTotalErrors = summary.totalErrors;
        report.UnityBuildTotalWarnings = summary.totalWarnings;
        report.UnityBuildTotalSize = (long)summary.totalSize;
        report.UnityBuildTimeSeconds = summary.totalTime.TotalSeconds;

        var builder = new StringBuilder();
        foreach (var step in unityBuildReport.steps)
        {
            foreach (var message in step.messages)
            {
                if (message.type == LogType.Error || message.type == LogType.Exception || message.type == LogType.Warning)
                {
                    builder.Append('[');
                    builder.Append(message.type);
                    builder.Append("] ");
                    builder.Append(step.name);
                    builder.Append(": ");
                    builder.AppendLine(message.content);
                }
            }
        }

        report.UnityBuildMessages = builder.ToString().TrimEnd();
    }

    private static void AddCheck(MqdhPackageBuildReportData report, string name, bool passed, string detail)
    {
        report.Checks.Add(new MqdhPackageBuildCheck
        {
            Name = name,
            Status = passed ? "Pass" : "Fail",
            Detail = detail ?? string.Empty,
        });
    }

    private static bool HasFailedCheck(MqdhPackageBuildReportData report)
    {
        foreach (var check in report.Checks)
        {
            if (string.Equals(check.Status, "Fail", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string DetermineBlockedStatus(MqdhPackageBuildReportData report)
    {
        foreach (var check in report.Checks)
        {
            if (string.Equals(check.Name, "android_support_files_present", StringComparison.Ordinal) &&
                string.Equals(check.Status, "Fail", StringComparison.Ordinal))
            {
                return "BlockedAndroidSupport";
            }
        }

        foreach (var check in report.Checks)
        {
            if (string.Equals(check.Name, "readiness_not_fail", StringComparison.Ordinal) &&
                string.Equals(check.Status, "Fail", StringComparison.Ordinal))
            {
                return "BlockedReadiness";
            }
        }

        foreach (var check in report.Checks)
        {
            if (string.Equals(check.Name, "active_build_target_android", StringComparison.Ordinal) &&
                string.Equals(check.Status, "Fail", StringComparison.Ordinal))
            {
                return "BlockedBuildTarget";
            }
        }

        return "BlockedPreBuild";
    }

    private static string[] GetEnabledBuildScenes()
    {
        var scenes = new List<string>();
        foreach (var scene in EditorBuildSettings.scenes)
        {
            if (scene.enabled)
            {
                scenes.Add(scene.path);
            }
        }

        return scenes.ToArray();
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

    private static string GetLatestFile(string directory, string searchPattern)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return "missing";
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

        return latestPath ?? "missing";
    }

    private static string ExtractMarkdownValue(string path, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return "unknown";
        }

        var prefix = $"- {fieldName}: `";
        foreach (var line in File.ReadLines(path))
        {
            if (!line.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var end = line.IndexOf('`', prefix.Length);
            if (end > prefix.Length)
            {
                return line.Substring(prefix.Length, end - prefix.Length);
            }
        }

        return "unknown";
    }

    private static void WriteReport(string projectRoot, MqdhPackageBuildReportData report)
    {
        var reportDirectory = Path.Combine(projectRoot, "Library", ReportFolderName);
        Directory.CreateDirectory(reportDirectory);
        report.ReportPath = Path.Combine(reportDirectory, $"{report.ReportId}.md");
        File.WriteAllText(Path.Combine(reportDirectory, $"{report.ReportId}.json"), JsonUtility.ToJson(report, true));
        File.WriteAllText(report.ReportPath, BuildMarkdown(report));
    }

    private static string BuildMarkdown(MqdhPackageBuildReportData report)
    {
        var builder = new StringBuilder(4096);
        builder.AppendLine($"# {report.ReportId}");
        builder.AppendLine();
        builder.AppendLine($"- Created: `{report.CreatedAtIsoUtc}`");
        builder.AppendLine($"- Overall: `{report.OverallStatus}`");
        builder.AppendLine($"- Package format: `{report.PackageFormat}`");
        builder.AppendLine($"- Artifact path: `{report.ArtifactPath}`");
        builder.AppendLine($"- Artifact exists: `{report.ArtifactExists}`");
        builder.AppendLine($"- Artifact bytes: `{report.ArtifactBytes}`");
        builder.AppendLine($"- Active build target: `{report.ActiveBuildTarget}`");
        builder.AppendLine($"- Selected build target group: `{report.SelectedBuildTargetGroup}`");
        builder.AppendLine($"- Android build app bundle: `{report.AndroidBuildAppBundle}`");
        builder.AppendLine($"- Readiness report: `{report.ReadinessReportPath}`");
        builder.AppendLine($"- Readiness overall: `{report.ReadinessOverall}`");
        builder.AppendLine($"- Unity build result: `{report.UnityBuildResult}`");
        builder.AppendLine($"- Unity build errors: `{report.UnityBuildTotalErrors}`");
        builder.AppendLine($"- Unity build warnings: `{report.UnityBuildTotalWarnings}`");
        builder.AppendLine($"- Unity build total size: `{report.UnityBuildTotalSize}`");
        builder.AppendLine($"- Unity build time seconds: `{report.UnityBuildTimeSeconds:F1}`");
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

        if (!string.IsNullOrWhiteSpace(report.UnityBuildMessages))
        {
            builder.AppendLine();
            builder.AppendLine("## Unity Build Messages");
            builder.AppendLine();
            builder.AppendLine("````text");
            builder.AppendLine(report.UnityBuildMessages);
            builder.AppendLine("````");
        }

        if (report.FinalGateCommands.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Final Gate Commands");
            foreach (var command in report.FinalGateCommands)
            {
                builder.AppendLine();
                builder.AppendLine($"### `{command.Command}`");
                builder.AppendLine();
                builder.AppendLine($"- Exit: `{command.ExitCode}`");
                builder.AppendLine();
                builder.AppendLine("````text");
                builder.AppendLine(command.Output);
                builder.AppendLine("````");
            }
        }

        if (report.SuggestedNextActions.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Suggested Next Actions");
            builder.AppendLine();
            foreach (var action in report.SuggestedNextActions)
            {
                builder.Append("- ");
                builder.AppendLine(action);
            }
        }

        builder.AppendLine();
        builder.AppendLine("## Manual Fallback");
        builder.AppendLine();
        builder.AppendLine("If this Unity build runner is not used, build the APK/AAB through Unity's Android Build Settings, then run:");
        builder.AppendLine();
        builder.AppendLine("```bash");
        builder.AppendLine("bash Tools/run_predevice_local_gate.sh --package-artifact <apk-or-aab-path>");
        builder.AppendLine("bash Tools/verify_predevice_local_gate.sh --require-package-artifact");
        builder.AppendLine("```");
        return builder.ToString().TrimEnd();
    }

    private static string EscapeMarkdown(string value)
    {
        return (value ?? string.Empty).Replace("|", "\\|").Replace("\n", "<br>");
    }

    private static string ShellQuote(string value)
    {
        return "'" + (value ?? string.Empty).Replace("'", "'\"'\"'") + "'";
    }

    private static string GetProjectRoot()
    {
        return Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
    }
}

[Serializable]
public sealed class MqdhPackageBuildReportData
{
    public string ReportId;
    public string CreatedAtIsoUtc;
    public string OverallStatus;
    public string ReportPath;
    public string ArtifactPath;
    public bool ArtifactExists;
    public long ArtifactBytes;
    public string PackageFormat;
    public string ActiveBuildTarget;
    public string SelectedBuildTargetGroup;
    public bool AndroidBuildAppBundle;
    public int SceneCount;
    public string ReadinessReportPath;
    public string ReadinessOverall;
    public string UnityBuildResult;
    public int UnityBuildTotalErrors;
    public int UnityBuildTotalWarnings;
    public long UnityBuildTotalSize;
    public double UnityBuildTimeSeconds;
    public string UnityBuildMessages;
    public List<MqdhPackageBuildCheck> Checks = new List<MqdhPackageBuildCheck>();
    public List<ShellCommandResult> FinalGateCommands = new List<ShellCommandResult>();
    public List<string> SuggestedNextActions = new List<string>();
}

[Serializable]
public sealed class MqdhPackageBuildCheck
{
    public string Name;
    public string Status;
    public string Detail;
}

[Serializable]
public sealed class ShellCommandResult
{
    public string Command;
    public int ExitCode;
    public string Output;
}
