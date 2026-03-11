using SessionGuard.Core.Models;
using SessionGuard.Core.Services;

namespace SessionGuard.Core.Configuration;

public sealed class PolicyConfiguration
{
    public bool Enabled { get; init; } = true;

    public int DefaultApprovalWindowMinutes { get; init; } = 60;

    public IReadOnlyList<PolicyRuleDefinition> Rules { get; init; } = Array.Empty<PolicyRuleDefinition>();

    public PolicyConfiguration Normalize()
    {
        return new PolicyConfiguration
        {
            Enabled = Enabled,
            DefaultApprovalWindowMinutes = Math.Clamp(DefaultApprovalWindowMinutes, 5, 480),
            Rules = Rules
                .Select(rule => rule.Normalize())
                .Where(rule => !string.IsNullOrWhiteSpace(rule.Id))
                .OrderBy(rule => rule.Priority)
                .ThenBy(rule => rule.Title, StringComparer.OrdinalIgnoreCase)
                .ThenBy(rule => rule.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }
}

public sealed class PolicyRuleDefinition
{
    public string Id { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public bool Enabled { get; init; } = true;

    public int Priority { get; init; } = 100;

    public PolicyRuleKind Kind { get; init; }

    public bool MatchWhenRestartPendingOnly { get; init; }

    public IReadOnlyList<DayOfWeek> Days { get; init; } = Array.Empty<DayOfWeek>();

    public int StartHour { get; init; } = 8;

    public int EndHour { get; init; } = 18;

    public IReadOnlyList<string> ProcessNames { get; init; } = Array.Empty<string>();

    public IReadOnlyList<WorkspaceCategory> WorkspaceCategories { get; init; } = Array.Empty<WorkspaceCategory>();

    public int MinimumInstances { get; init; } = 1;

    public RestartRiskLevel MinimumRiskLevel { get; init; } = RestartRiskLevel.Elevated;

    public int? ApprovalWindowMinutes { get; init; }

    public PolicyRuleDefinition Normalize()
    {
        var normalizedId = string.IsNullOrWhiteSpace(Id)
            ? string.Empty
            : Id.Trim();
        var normalizedTitle = string.IsNullOrWhiteSpace(Title)
            ? HumanizeId(normalizedId)
            : Title.Trim();
        var normalizedDescription = Description?.Trim() ?? string.Empty;
        var normalizedDays = Days
            .Distinct()
            .OrderBy(static day => (int)day)
            .ToArray();
        var normalizedProcesses = ProcessNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(ProcessMatcher.CanonicalizeDisplayName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var normalizedCategories = WorkspaceCategories
            .Distinct()
            .OrderBy(static category => category)
            .ToArray();

        return new PolicyRuleDefinition
        {
            Id = normalizedId,
            Title = normalizedTitle,
            Description = normalizedDescription,
            Enabled = Enabled,
            Priority = Math.Clamp(Priority, 0, 10_000),
            Kind = Kind,
            MatchWhenRestartPendingOnly = MatchWhenRestartPendingOnly,
            Days = normalizedDays,
            StartHour = Math.Clamp(StartHour, 0, 23),
            EndHour = Math.Clamp(EndHour, 0, 23),
            ProcessNames = normalizedProcesses,
            WorkspaceCategories = normalizedCategories,
            MinimumInstances = Math.Max(1, MinimumInstances),
            MinimumRiskLevel = NormalizeMinimumRiskLevel(MinimumRiskLevel),
            ApprovalWindowMinutes = ApprovalWindowMinutes.HasValue
                ? Math.Clamp(ApprovalWindowMinutes.Value, 5, 480)
                : null
        };
    }

    private static RestartRiskLevel NormalizeMinimumRiskLevel(RestartRiskLevel riskLevel)
    {
        return riskLevel == RestartRiskLevel.Unknown
            ? RestartRiskLevel.Elevated
            : riskLevel;
    }

    private static string HumanizeId(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return "Unnamed policy rule";
        }

        var tokens = id
            .Replace('-', ' ')
            .Replace('_', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (tokens.Length == 0)
        {
            return "Unnamed policy rule";
        }

        return string.Join(
            " ",
            tokens.Select(static token =>
                char.ToUpperInvariant(token[0]) + token[1..].ToLowerInvariant()));
    }
}
