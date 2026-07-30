using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

public static class MqdhHandoffPreflightReportRunner
{
    private const string ReportFolderName = "MQDHHeadsetEvidence";
    private const string ReadinessFolderName = "PreDeviceBuildReadinessReports";
    private const string SmokeFolderName = "PreDeviceSmokeReports";
    private const string VisualFolderName = "PreDeviceVisualEvidence";
    private const string HandoffScriptPath = "Tools/collect_mqdh_headset_evidence.sh";

    [MenuItem("SceneShift/Validation/Run MQDH Handoff Preflight")]
    public static MqdhHandoffPreflightReport RunReport()
    {
        var report = new MqdhHandoffPreflightReport
        {
            ReportId = $"mqdh_handoff_preflight_{DateTime.UtcNow:yyyyMMddHHmmss}",
            CreatedAtIsoUtc = DateTime.UtcNow.ToString("O"),
        };

        CheckLatestReadiness(report);
        CheckLatestEvidenceTemplate(report);
        CheckAdbEvidenceScript(report);
        CheckLocalEvidencePaths(report);

        report.OverallStatus = BuildOverallStatus(report);
        var reportPath = WriteReport(report);
        Debug.Log($"[MQDHHandoffPreflight] {report.OverallStatus} report written: {reportPath}");
        return report;
    }

    private static void CheckLatestReadiness(MqdhHandoffPreflightReport report)
    {
        var readinessPath = GetLatestFile(Path.Combine(GetProjectRoot(), "Library", ReadinessFolderName), "predevice_build_readiness_*.md");
        var readinessOverall = ExtractMarkdownValue(readinessPath, "Overall");
        AddCheck(
            report,
            "latest_build_readiness_exists",
            !string.IsNullOrWhiteSpace(readinessPath) ? MqdhHandoffPreflightStatus.Pass : MqdhHandoffPreflightStatus.Fail,
            FormatPath(readinessPath));
        AddCheck(
            report,
            "latest_build_readiness_not_fail",
            string.Equals(readinessOverall, "Fail", StringComparison.OrdinalIgnoreCase)
                ? MqdhHandoffPreflightStatus.Fail
                : !string.IsNullOrWhiteSpace(readinessOverall)
                    ? MqdhHandoffPreflightStatus.Pass
                    : MqdhHandoffPreflightStatus.Warn,
            $"overall={FormatValue(readinessOverall)}, path={FormatPath(readinessPath)}");
    }

    private static void CheckLatestEvidenceTemplate(MqdhHandoffPreflightReport report)
    {
        var templatePath = GetLatestFile(Path.Combine(GetProjectRoot(), "Library", ReportFolderName), "mqdh_headset_evidence_*.md");
        var readinessPath = GetLatestFile(Path.Combine(GetProjectRoot(), "Library", ReadinessFolderName), "predevice_build_readiness_*.md");
        var templateText = ReadFileOrEmpty(templatePath);
        var templateFileName = Path.GetFileName(templatePath);
        var referencesReadiness = !string.IsNullOrWhiteSpace(readinessPath) && templateText.Contains(readinessPath, StringComparison.Ordinal);
        var referencesSelf = !string.IsNullOrWhiteSpace(templateFileName) && templateText.Contains(templateFileName, StringComparison.Ordinal);
        var hasAdbCommand = templateText.Contains("Tools/collect_mqdh_headset_evidence.sh", StringComparison.Ordinal) &&
                            templateText.Contains("--package com.mikusc.sceneshiftroom.comp4145", StringComparison.Ordinal);
        var hasFinalPackageGateCommands =
            templateText.Contains("Tools/run_predevice_local_gate.sh --package-artifact <apk-or-aab-path>", StringComparison.Ordinal) &&
            templateText.Contains("Tools/verify_predevice_local_gate.sh --require-package-artifact", StringComparison.Ordinal);
        var hasPackageDebugCommand = templateText.Contains("Tools/verify_mqdh_package_artifact.sh <apk-or-aab-path>", StringComparison.Ordinal);
        var hasPackageBuildMenu = templateText.Contains("SceneShift/Validation/Build MQDH Test Package", StringComparison.Ordinal);
        var hasPackageBuildReportField = templateText.Contains("MQDH package build report", StringComparison.Ordinal);
        var hasTerminalSuiteCommand = templateText.Contains("Tools/run_mqdh_terminal_prepackage_suite.sh", StringComparison.Ordinal);
        var hasTrueDevicePreflightAuditCommand = templateText.Contains("Tools/audit_true_device_preflight.sh", StringComparison.Ordinal);
        var hasAndroidSupportInstallCommand = templateText.Contains("Tools/install_unity_android_support.sh --run --wait-for-close", StringComparison.Ordinal);
        var hasTerminalSuiteEvidenceFields =
            templateText.Contains("Existing terminal pre-package suite at template creation", StringComparison.Ordinal) &&
            templateText.Contains("Terminal pre-package suite report", StringComparison.Ordinal);
        var hasHandoffEvidenceFields =
            templateText.Contains("Existing MQDH handoff preflight at template creation", StringComparison.Ordinal) &&
            templateText.Contains("Existing handoff bundle manifest at template creation", StringComparison.Ordinal);
        var hasLocalGateEvidenceFields =
            templateText.Contains("Existing pre-device local gate at template creation", StringComparison.Ordinal) &&
            templateText.Contains("Pre-package local gate report", StringComparison.Ordinal) &&
            templateText.Contains("Final package local gate report", StringComparison.Ordinal) &&
            templateText.Contains("Final package gate verification result", StringComparison.Ordinal);

        AddCheck(
            report,
            "latest_mqdh_template_exists",
            !string.IsNullOrWhiteSpace(templatePath) ? MqdhHandoffPreflightStatus.Pass : MqdhHandoffPreflightStatus.Fail,
            FormatPath(templatePath));
        AddCheck(
            report,
            "mqdh_template_references_latest_readiness",
            referencesReadiness ? MqdhHandoffPreflightStatus.Pass : MqdhHandoffPreflightStatus.Warn,
            $"template={FormatPath(templatePath)}, readiness={FormatPath(readinessPath)}");
        AddCheck(
            report,
            "mqdh_template_references_self_for_adb",
            referencesSelf ? MqdhHandoffPreflightStatus.Pass : MqdhHandoffPreflightStatus.Warn,
            $"template={FormatPath(templatePath)}, fileName={FormatValue(templateFileName)}");
        AddCheck(
            report,
            "mqdh_template_has_adb_collection_command",
            hasAdbCommand ? MqdhHandoffPreflightStatus.Pass : MqdhHandoffPreflightStatus.Warn,
            "Expected template command: Tools/collect_mqdh_headset_evidence.sh --package com.mikusc.sceneshiftroom.comp4145 --template <template>.");
        AddCheck(
            report,
            "mqdh_template_has_final_package_gate_commands",
            hasFinalPackageGateCommands ? MqdhHandoffPreflightStatus.Pass : MqdhHandoffPreflightStatus.Warn,
            "Expected final upload gate commands: Tools/run_predevice_local_gate.sh --package-artifact <apk-or-aab-path> and Tools/verify_predevice_local_gate.sh --require-package-artifact.");
        AddCheck(
            report,
            "mqdh_template_has_package_debug_command",
            hasPackageDebugCommand ? MqdhHandoffPreflightStatus.Pass : MqdhHandoffPreflightStatus.Warn,
            "Expected optional package-only debug command: Tools/verify_mqdh_package_artifact.sh <apk-or-aab-path>.");
        AddCheck(
            report,
            "mqdh_template_has_package_build_menu",
            hasPackageBuildMenu && hasPackageBuildReportField ? MqdhHandoffPreflightStatus.Pass : MqdhHandoffPreflightStatus.Warn,
            "Expected package build menu and MQDH package build report field in the headset evidence template.");
        AddCheck(
            report,
            "mqdh_template_has_terminal_prepackage_suite_command",
            hasTerminalSuiteCommand ? MqdhHandoffPreflightStatus.Pass : MqdhHandoffPreflightStatus.Warn,
            "Expected terminal suite command: Tools/run_mqdh_terminal_prepackage_suite.sh.");
        AddCheck(
            report,
            "mqdh_template_has_true_device_preflight_audit_command",
            hasTrueDevicePreflightAuditCommand ? MqdhHandoffPreflightStatus.Pass : MqdhHandoffPreflightStatus.Warn,
            "Expected terminal audit command: Tools/audit_true_device_preflight.sh.");
        AddCheck(
            report,
            "mqdh_template_has_android_support_install_command",
            hasAndroidSupportInstallCommand ? MqdhHandoffPreflightStatus.Pass : MqdhHandoffPreflightStatus.Warn,
            "Expected Android Support install command: Tools/install_unity_android_support.sh --run --wait-for-close.");
        AddCheck(
            report,
            "mqdh_template_has_terminal_prepackage_suite_fields",
            hasTerminalSuiteEvidenceFields ? MqdhHandoffPreflightStatus.Pass : MqdhHandoffPreflightStatus.Warn,
            "Expected template fields for existing terminal suite context and headset-run terminal suite report.");
        AddCheck(
            report,
            "mqdh_template_has_handoff_bundle_fields",
            hasHandoffEvidenceFields ? MqdhHandoffPreflightStatus.Pass : MqdhHandoffPreflightStatus.Warn,
            "Expected template fields for existing MQDH handoff preflight and handoff bundle manifest at template creation.");
        AddCheck(
            report,
            "mqdh_template_has_local_gate_fields",
            hasLocalGateEvidenceFields ? MqdhHandoffPreflightStatus.Pass : MqdhHandoffPreflightStatus.Warn,
            "Expected template fields for pre-package local gate, final package local gate, and final package gate verification result.");
    }

    private static void CheckAdbEvidenceScript(MqdhHandoffPreflightReport report)
    {
        var scriptPath = Path.Combine(GetProjectRoot(), HandoffScriptPath);
        var scriptText = ReadFileOrEmpty(scriptPath);
        var bashInvocable = scriptText.StartsWith("#!/usr/bin/env bash", StringComparison.Ordinal) ||
                            scriptText.StartsWith("#!/bin/bash", StringComparison.Ordinal);
        AddCheck(
            report,
            "adb_evidence_script_exists",
            File.Exists(scriptPath) ? MqdhHandoffPreflightStatus.Pass : MqdhHandoffPreflightStatus.Fail,
            HandoffScriptPath);
        AddCheck(
            report,
            "adb_evidence_script_bash_invocable",
            bashInvocable ? MqdhHandoffPreflightStatus.Pass : MqdhHandoffPreflightStatus.Warn,
            "The documented command invokes this script through bash, so executable file mode is not required.");
        AddCheck(
            report,
            "adb_evidence_script_expected_commands",
            ContainsAll(scriptText, "devices -l", "logcat", "screencap", "pull", "run-as", "screenrecord")
                ? MqdhHandoffPreflightStatus.Pass
                : MqdhHandoffPreflightStatus.Warn,
            "Checks adb devices, logcat, screenshot, file pull, run-as, and optional screenrecord support.");
    }

    private static void CheckLocalEvidencePaths(MqdhHandoffPreflightReport report)
    {
        var projectRoot = GetProjectRoot();
        var latestSmoke = GetLatestFile(Path.Combine(projectRoot, "Library", SmokeFolderName), "predevice_smoke_*.md");
        var latestVisual = GetLatestFile(Path.Combine(projectRoot, "Library", VisualFolderName), "predevice_visual_review_*.md");
        var latestVisualImage = GetLatestFile(Path.Combine(projectRoot, "Library", VisualFolderName), "*.png");
        AddCheck(
            report,
            "latest_smoke_markdown_exists",
            !string.IsNullOrWhiteSpace(latestSmoke) ? MqdhHandoffPreflightStatus.Pass : MqdhHandoffPreflightStatus.Warn,
            FormatPath(latestSmoke));
        AddCheck(
            report,
            "latest_visual_review_exists",
            !string.IsNullOrWhiteSpace(latestVisual) ? MqdhHandoffPreflightStatus.Pass : MqdhHandoffPreflightStatus.Warn,
            FormatPath(latestVisual));
        AddCheck(
            report,
            "latest_visual_image_exists",
            !string.IsNullOrWhiteSpace(latestVisualImage) ? MqdhHandoffPreflightStatus.Pass : MqdhHandoffPreflightStatus.Warn,
            FormatPath(latestVisualImage));
    }

    private static string WriteReport(MqdhHandoffPreflightReport report)
    {
        var reportDirectory = Path.Combine(GetProjectRoot(), "Library", ReportFolderName);
        Directory.CreateDirectory(reportDirectory);
        var jsonPath = Path.Combine(reportDirectory, $"{report.ReportId}.json");
        File.WriteAllText(jsonPath, JsonUtility.ToJson(report, true));
        File.WriteAllText(Path.Combine(reportDirectory, $"{report.ReportId}.md"), BuildMarkdown(report));
        return jsonPath;
    }

    private static string BuildMarkdown(MqdhHandoffPreflightReport report)
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

        builder.AppendLine();
        builder.AppendLine("## Interpretation");
        builder.AppendLine();
        builder.AppendLine("- `Pass` means the MQDH/test-channel handoff artifact is present and current.");
        builder.AppendLine("- `Warn` means the handoff can be prepared locally, but the item should be regenerated or checked before headset validation.");
        builder.AppendLine("- `Fail` means do not package or start the headset run until the issue is resolved.");
        return builder.ToString();
    }

    private static string BuildOverallStatus(MqdhHandoffPreflightReport report)
    {
        var hasFail = false;
        var hasWarn = false;
        foreach (var check in report.Checks)
        {
            hasFail |= string.Equals(check.Status, MqdhHandoffPreflightStatus.Fail.ToString(), StringComparison.Ordinal);
            hasWarn |= string.Equals(check.Status, MqdhHandoffPreflightStatus.Warn.ToString(), StringComparison.Ordinal);
        }

        if (hasFail)
        {
            return "Fail";
        }

        return hasWarn ? "PassWithWarnings" : "Pass";
    }

    private static void AddCheck(
        MqdhHandoffPreflightReport report,
        string name,
        MqdhHandoffPreflightStatus status,
        string detail)
    {
        report.Checks.Add(new MqdhHandoffPreflightCheck
        {
            Name = name,
            Status = status.ToString(),
            Detail = detail ?? string.Empty,
        });
    }

    private static bool ContainsAll(string text, params string[] values)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        foreach (var value in values)
        {
            if (!text.Contains(value, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
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

    private static string ReadFileOrEmpty(string path)
    {
        return !string.IsNullOrWhiteSpace(path) && File.Exists(path) ? File.ReadAllText(path) : string.Empty;
    }

    private static string FormatPath(string path)
    {
        return string.IsNullOrWhiteSpace(path) ? "missing" : path;
    }

    private static string FormatValue(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "unknown" : value;
    }

    private static string EscapeMarkdown(string value)
    {
        return (value ?? string.Empty).Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
    }

    private static string GetProjectRoot()
    {
        return Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
    }
}

[Serializable]
public sealed class MqdhHandoffPreflightReport
{
    public string ReportId;
    public string CreatedAtIsoUtc;
    public string OverallStatus;
    public List<MqdhHandoffPreflightCheck> Checks = new();
}

[Serializable]
public sealed class MqdhHandoffPreflightCheck
{
    public string Name;
    public string Status;
    public string Detail;
}

public enum MqdhHandoffPreflightStatus
{
    Pass,
    Warn,
    Fail,
}
