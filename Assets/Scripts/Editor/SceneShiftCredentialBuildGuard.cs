#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public sealed class SceneShiftCredentialBuildGuard : IPreprocessBuildWithReport
{
    private const string CredentialField =
        "apiKey|authToken|accessToken|bearerToken|clientSecret|password|secret|privateKey|connectionString|sasToken";

    private static readonly Regex YamlCredentialPattern = new(
        $@"^\s*(?<field>{CredentialField})(?:Override)?\s*:\s*(?<value>.*?)\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex JsonCredentialPattern = new(
        $@"""(?<field>{CredentialField})(?:Override)?""\s*:\s*""(?<value>[^""]*)""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly HashSet<string> ScannedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".asset",
        ".json",
        ".prefab",
        ".unity",
    };

    public int callbackOrder => -1000;

    public void OnPreprocessBuild(BuildReport report)
    {
        var findings = ScanProject();
        if (findings.Count > 0)
        {
            throw new BuildFailedException(BuildMessage(findings));
        }
    }

    [MenuItem("SceneShift/Validation/Check Serialized Credentials")]
    public static void ValidateFromMenu()
    {
        ValidateAndLog();
    }

    public static void ValidateFromCommandLine()
    {
        ValidateAndLog();
    }

    private static void ValidateAndLog()
    {
        var findings = ScanProject();
        if (findings.Count > 0)
        {
            throw new InvalidOperationException(BuildMessage(findings));
        }

        Debug.Log("[SceneShiftCredentialBuildGuard] Passed: no non-empty serialized credential fields were found.");
    }

    private static List<CredentialFinding> ScanProject()
    {
        var findings = new List<CredentialFinding>();
        ScanDirectory("Assets", findings);
        ScanDirectory("ProjectSettings", findings);
        return findings;
    }

    private static void ScanDirectory(string projectRelativeDirectory, List<CredentialFinding> findings)
    {
        if (!Directory.Exists(projectRelativeDirectory))
        {
            return;
        }

        foreach (var path in Directory.EnumerateFiles(projectRelativeDirectory, "*", SearchOption.AllDirectories))
        {
            if (!ScannedExtensions.Contains(Path.GetExtension(path)))
            {
                continue;
            }

            ScanFile(path.Replace('\\', '/'), findings);
        }
    }

    private static void ScanFile(string projectRelativePath, List<CredentialFinding> findings)
    {
        var absolutePath = Path.GetFullPath(projectRelativePath);
        if (!File.Exists(absolutePath))
        {
            return;
        }

        var lineNumber = 0;
        foreach (var line in File.ReadLines(absolutePath))
        {
            lineNumber++;
            if (TryGetNonEmptyCredentialField(line, out var fieldName))
            {
                findings.Add(new CredentialFinding(projectRelativePath, lineNumber, fieldName));
            }
        }
    }

    private static bool TryGetNonEmptyCredentialField(string line, out string fieldName)
    {
        fieldName = string.Empty;
        var yamlMatch = YamlCredentialPattern.Match(line);
        if (yamlMatch.Success && !string.IsNullOrWhiteSpace(yamlMatch.Groups["value"].Value))
        {
            fieldName = yamlMatch.Groups["field"].Value;
            return true;
        }

        foreach (Match jsonMatch in JsonCredentialPattern.Matches(line))
        {
            if (string.IsNullOrWhiteSpace(jsonMatch.Groups["value"].Value))
            {
                continue;
            }

            fieldName = jsonMatch.Groups["field"].Value;
            return true;
        }

        return false;
    }

    private static string BuildMessage(IReadOnlyList<CredentialFinding> findings)
    {
        var builder = new StringBuilder(512);
        builder.AppendLine("SceneShift build blocked: serialized credentials were found.");
        builder.AppendLine("Remove the values and use process environment variables or a trusted backend/proxy.");

        var limit = Math.Min(findings.Count, 20);
        for (var index = 0; index < limit; index++)
        {
            var finding = findings[index];
            builder.AppendLine($"- {finding.Path}:{finding.LineNumber} ({finding.FieldName})");
        }

        if (findings.Count > limit)
        {
            builder.AppendLine($"- ... and {findings.Count - limit} more");
        }

        return builder.ToString().TrimEnd();
    }

    private readonly struct CredentialFinding
    {
        public CredentialFinding(string path, int lineNumber, string fieldName)
        {
            Path = path;
            LineNumber = lineNumber;
            FieldName = fieldName;
        }

        public string Path { get; }
        public int LineNumber { get; }
        public string FieldName { get; }
    }
}
#endif
