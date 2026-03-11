namespace SessionGuard.Core.Models;

public sealed record PolicyValidationReport(
    string Summary,
    IReadOnlyList<PolicyValidationIssue> Issues)
{
    public static PolicyValidationReport None { get; } = new(
        "Policy config: no validation issues detected.",
        Array.Empty<PolicyValidationIssue>());

    public bool HasIssues => Issues.Count > 0;

    public bool HasErrors => Issues.Any(issue => issue.Severity == PolicyValidationSeverity.Error);

    public bool HasWarnings => Issues.Any(issue => issue.Severity == PolicyValidationSeverity.Warning);

    public int ErrorCount => Issues.Count(issue => issue.Severity == PolicyValidationSeverity.Error);

    public int WarningCount => Issues.Count(issue => issue.Severity == PolicyValidationSeverity.Warning);

    public int InformationCount => Issues.Count(issue => issue.Severity == PolicyValidationSeverity.Information);

    public static PolicyValidationReport Create(IEnumerable<PolicyValidationIssue> issues)
    {
        var normalizedIssues = issues
            .OrderByDescending(GetSeverityRank)
            .ThenBy(issue => issue.RuleId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(issue => issue.Code, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalizedIssues.Length == 0)
        {
            return None;
        }

        return new PolicyValidationReport(
            BuildSummary(normalizedIssues),
            normalizedIssues);
    }

    private static int GetSeverityRank(PolicyValidationIssue issue)
    {
        return issue.Severity switch
        {
            PolicyValidationSeverity.Error => 3,
            PolicyValidationSeverity.Warning => 2,
            PolicyValidationSeverity.Information => 1,
            _ => 0
        };
    }

    private static string BuildSummary(IReadOnlyCollection<PolicyValidationIssue> issues)
    {
        var errorCount = issues.Count(issue => issue.Severity == PolicyValidationSeverity.Error);
        var warningCount = issues.Count(issue => issue.Severity == PolicyValidationSeverity.Warning);
        var infoCount = issues.Count(issue => issue.Severity == PolicyValidationSeverity.Information);
        var parts = new List<string>();

        if (errorCount > 0)
        {
            parts.Add($"{errorCount} error(s)");
        }

        if (warningCount > 0)
        {
            parts.Add($"{warningCount} warning(s)");
        }

        if (infoCount > 0)
        {
            parts.Add($"{infoCount} info note(s)");
        }

        return $"Policy config: {string.Join(", ", parts)} detected.";
    }
}
