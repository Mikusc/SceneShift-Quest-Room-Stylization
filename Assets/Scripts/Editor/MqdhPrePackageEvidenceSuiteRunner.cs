using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class MqdhPrePackageEvidenceSuiteRunner
{
    private const string EvidenceFolderName = "MQDHHeadsetEvidence";
    private const string ReadinessFolderName = "PreDeviceBuildReadinessReports";

    [MenuItem("SceneShift/Validation/Run MQDH Pre-Package Evidence Suite")]
    public static string RunSuite()
    {
        var reportId = $"mqdh_prepackage_evidence_suite_{DateTime.UtcNow:yyyyMMddHHmmss}";
        var createdAtIsoUtc = DateTime.UtcNow.ToString("O");
        var projectRoot = GetProjectRoot();

        var readiness = PreDeviceBuildReadinessReportRunner.RunReport();
        var templatePath = MqdhHeadsetEvidenceTemplateWriter.CreateTemplate();
        var handoff = MqdhHandoffPreflightReportRunner.RunReport();

        var readinessMarkdown = Path.Combine(projectRoot, "Library", ReadinessFolderName, $"{readiness.ReportId}.md");
        var readinessJson = Path.Combine(projectRoot, "Library", ReadinessFolderName, $"{readiness.ReportId}.json");
        var handoffMarkdown = Path.Combine(projectRoot, "Library", EvidenceFolderName, $"{handoff.ReportId}.md");
        var handoffJson = Path.Combine(projectRoot, "Library", EvidenceFolderName, $"{handoff.ReportId}.json");
        var overall = BuildOverallStatus(readiness.OverallStatus, handoff.OverallStatus);

        var outputDirectory = Path.Combine(projectRoot, "Library", EvidenceFolderName);
        Directory.CreateDirectory(outputDirectory);
        var outputPath = Path.Combine(outputDirectory, $"{reportId}.md");
        File.WriteAllText(
            outputPath,
            BuildMarkdown(
                reportId,
                createdAtIsoUtc,
                overall,
                readiness,
                readinessMarkdown,
                readinessJson,
                templatePath,
                handoff,
                handoffMarkdown,
                handoffJson));

        Debug.Log($"[MQDHPrePackageEvidenceSuite] {overall} report written: {outputPath}");
        return outputPath;
    }

    private static string BuildMarkdown(
        string reportId,
        string createdAtIsoUtc,
        string overall,
        PreDeviceBuildReadinessReport readiness,
        string readinessMarkdown,
        string readinessJson,
        string templatePath,
        MqdhHandoffPreflightReport handoff,
        string handoffMarkdown,
        string handoffJson)
    {
        var builder = new StringBuilder(4096);
        builder.AppendLine($"# {reportId}");
        builder.AppendLine();
        builder.AppendLine($"- Created: `{createdAtIsoUtc}`");
        builder.AppendLine($"- Overall: `{overall}`");
        builder.AppendLine($"- Readiness overall: `{FormatValue(readiness?.OverallStatus)}`");
        builder.AppendLine($"- Handoff preflight overall: `{FormatValue(handoff?.OverallStatus)}`");
        builder.AppendLine();
        builder.AppendLine("## Generated Unity Evidence");
        builder.AppendLine();
        builder.AppendLine($"- Build readiness markdown: `{FormatPath(readinessMarkdown)}`");
        builder.AppendLine($"- Build readiness JSON: `{FormatPath(readinessJson)}`");
        builder.AppendLine($"- MQDH headset evidence template: `{FormatPath(templatePath)}`");
        builder.AppendLine($"- MQDH handoff preflight markdown: `{FormatPath(handoffMarkdown)}`");
        builder.AppendLine($"- MQDH handoff preflight JSON: `{FormatPath(handoffJson)}`");
        builder.AppendLine();
        builder.AppendLine("## Next Terminal Commands");
        builder.AppendLine();
        builder.AppendLine("Run these after this Unity suite completes:");
        builder.AppendLine();
        builder.AppendLine("```bash");
        builder.AppendLine("bash Tools/run_mqdh_terminal_prepackage_suite.sh");
        builder.AppendLine("bash Tools/audit_true_device_preflight.sh");
        builder.AppendLine("```");
        builder.AppendLine();
        builder.AppendLine("If that suite or audit fails for a local reason, debug the individual steps it reports: handoff bundle writer/verifier, local gate, local gate verifier, package build report verifier, and handoff status.");
        builder.AppendLine();
        builder.AppendLine("After Android Build Support is installed and the Editor build target is Android, use this Unity menu to create a package build report and run the final local gate automatically:");
        builder.AppendLine();
        builder.AppendLine("```text");
        builder.AppendLine("SceneShift/Validation/Build MQDH Test Package");
        builder.AppendLine("```");
        builder.AppendLine();
        builder.AppendLine("After the APK/AAB is built, run the final upload gate:");
        builder.AppendLine();
        builder.AppendLine("```bash");
        builder.AppendLine("bash Tools/run_predevice_local_gate.sh --package-artifact <apk-or-aab-path>");
        builder.AppendLine("bash Tools/verify_predevice_local_gate.sh --require-package-artifact");
        builder.AppendLine("```");
        builder.AppendLine();
        builder.AppendLine("## Blocker Interpretation");
        builder.AppendLine();
        if (string.Equals(overall, "Pass", StringComparison.Ordinal))
        {
            builder.AppendLine("- Unity-side pre-package evidence suite passed. Continue with terminal handoff bundle/local gate, Android switch, package build, and MQDH/test-channel validation.");
        }
        else if (HasReadinessCheck(readiness, "android_build_support_installed", "Fail"))
        {
            builder.AppendLine("- Do not package yet. Android Build Support is still missing for this Unity editor.");
            builder.AppendLine("- Install Android Build Support, Android SDK & NDK Tools, and OpenJDK for this exact Unity version, then run:");
            builder.AppendLine();
            builder.AppendLine("```bash");
            builder.AppendLine($"bash Tools/install_unity_android_support.sh --run --wait-for-close --version {Application.unityVersion}");
            builder.AppendLine("bash Tools/check_android_support_recovery.sh");
            builder.AppendLine("```");
        }
        else
        {
            builder.AppendLine("- Do not package yet. Inspect the generated readiness and handoff preflight reports above, then fix failing checks before continuing.");
        }

        if (readiness?.SuggestedNextActions != null && readiness.SuggestedNextActions.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Readiness Suggested Next Actions");
            builder.AppendLine();
            foreach (var action in readiness.SuggestedNextActions)
            {
                builder.Append("- ");
                builder.AppendLine(action);
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static string BuildOverallStatus(string readinessOverall, string handoffOverall)
    {
        if (string.Equals(readinessOverall, "Fail", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(handoffOverall, "Fail", StringComparison.OrdinalIgnoreCase))
        {
            return "Fail";
        }

        if (string.Equals(readinessOverall, "PassWithWarnings", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(handoffOverall, "PassWithWarnings", StringComparison.OrdinalIgnoreCase))
        {
            return "PassWithWarnings";
        }

        return string.Equals(readinessOverall, "Pass", StringComparison.OrdinalIgnoreCase) &&
               string.Equals(handoffOverall, "Pass", StringComparison.OrdinalIgnoreCase)
            ? "Pass"
            : "Unknown";
    }

    private static bool HasReadinessCheck(PreDeviceBuildReadinessReport report, string checkName, string status)
    {
        if (report?.Checks == null)
        {
            return false;
        }

        foreach (var check in report.Checks)
        {
            if (string.Equals(check.Name, checkName, StringComparison.Ordinal) &&
                string.Equals(check.Status, status, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
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
