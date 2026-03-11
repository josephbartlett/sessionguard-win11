using SessionGuard.Core.Models;

namespace SessionGuard.Core.Services;

public static class WorkspaceRiskAnalyzer
{
    private static readonly string[] TerminalProcesses =
    {
        "WINDOWSTERMINAL",
        "POWERSHELL",
        "PWSH",
        "CMD",
        "WSL",
        "BASH"
    };

    private static readonly string[] EditorProcesses =
    {
        "CODE",
        "DEVENV",
        "RIDER64"
    };

    private static readonly string[] BrowserProcesses =
    {
        "CHROME",
        "MSEDGE",
        "FIREFOX"
    };

    private static readonly string[] DevServerProcesses =
    {
        "DOTNET",
        "NODE",
        "PYTHON",
        "PYTHONW",
        "JAVA",
        "JAVAW",
        "RUBY",
        "PHP",
        "DENO"
    };

    public static WorkspaceStateSnapshot Analyze(
        WorkspaceProcessObservation observation,
        DateTimeOffset timestamp)
    {
        var riskItems = new List<WorkspaceRiskItem>();
        var protectedLookup = observation.ProtectedProcesses
            .ToDictionary(
                match => ProcessMatcher.NormalizeExecutableName(match.DisplayName),
                match => match,
                StringComparer.OrdinalIgnoreCase);
        var runningLookup = observation.RunningProcesses
            .ToDictionary(
                process => ProcessMatcher.NormalizeExecutableName(process.DisplayName),
                process => process,
                StringComparer.OrdinalIgnoreCase);

        AddProtectedCategory(
            riskItems,
            protectedLookup,
            WorkspaceCategory.TerminalShell,
            "Terminal and shell sessions",
            WorkspaceRiskSeverity.High,
            WorkspaceConfidence.High,
            "Interactive shells often hold live commands, remote sessions, or transient console context that would be hard to reconstruct after a restart.",
            TerminalProcesses);
        AddProtectedCategory(
            riskItems,
            protectedLookup,
            WorkspaceCategory.EditorOrIde,
            "Editor and IDE sessions",
            WorkspaceRiskSeverity.Elevated,
            WorkspaceConfidence.High,
            "Editor and IDE processes suggest active development context. SessionGuard cannot verify unsaved buffers, so this remains an advisory risk signal.",
            EditorProcesses);
        AddProtectedCategory(
            riskItems,
            protectedLookup,
            WorkspaceCategory.Browser,
            "Browser sessions",
            WorkspaceRiskSeverity.Elevated,
            WorkspaceConfidence.Medium,
            "Browser processes are running. SessionGuard cannot count tabs or confirm session persistence, so the disruption risk is inferred from process presence only.",
            BrowserProcesses);
        AddRunningCategory(
            riskItems,
            runningLookup,
            WorkspaceCategory.LocalDevServer,
            "Local dev-server style runtimes",
            WorkspaceRiskSeverity.High,
            riskItems.Count > 0 ? WorkspaceConfidence.High : WorkspaceConfidence.Medium,
            riskItems.Count > 0
                ? "Runtime processes commonly used for local servers or long-running tasks are active alongside interactive tools."
                : "Runtime processes commonly used for local servers or long-running tasks are active. SessionGuard cannot confirm workload type from process names alone.",
            DevServerProcesses);

        var handledProtectedProcesses = new HashSet<string>(
            TerminalProcesses
                .Concat(EditorProcesses)
                .Concat(BrowserProcesses),
            StringComparer.OrdinalIgnoreCase);

        var genericProtected = observation.ProtectedProcesses
            .Where(match => !handledProtectedProcesses.Contains(ProcessMatcher.NormalizeExecutableName(match.DisplayName)))
            .OrderBy(match => match.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (genericProtected.Length > 0)
        {
            riskItems.Add(new WorkspaceRiskItem(
                "Other configured protected tools",
                WorkspaceCategory.ProtectedTool,
                WorkspaceRiskSeverity.Elevated,
                WorkspaceConfidence.Medium,
                genericProtected.Sum(match => match.InstanceCount),
                "Configured protected tools are active. SessionGuard is honoring the operator-defined protection list, even when it cannot infer richer workspace context.",
                genericProtected.Select(match => match.DisplayName).ToArray()));
        }

        if (riskItems.Count == 0)
        {
            return WorkspaceStateSnapshot.None(timestamp);
        }

        var highestSeverity = riskItems.MaxBy(static item => item.Severity)?.Severity ?? WorkspaceRiskSeverity.None;
        var confidence = riskItems.MaxBy(static item => item.Confidence)?.Confidence ?? WorkspaceConfidence.Low;
        var summary = BuildSummary(riskItems, highestSeverity);

        return new WorkspaceStateSnapshot(
            timestamp,
            HasRisk: true,
            highestSeverity,
            confidence,
            summary,
            riskItems);
    }

    private static void AddProtectedCategory(
        List<WorkspaceRiskItem> riskItems,
        IReadOnlyDictionary<string, ProtectedProcessMatch> protectedLookup,
        WorkspaceCategory category,
        string title,
        WorkspaceRiskSeverity severity,
        WorkspaceConfidence confidence,
        string reason,
        IEnumerable<string> processNames)
    {
        var matches = processNames
            .Where(protectedLookup.ContainsKey)
            .Select(processName => protectedLookup[processName])
            .OrderBy(match => match.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (matches.Length == 0)
        {
            return;
        }

        riskItems.Add(new WorkspaceRiskItem(
            title,
            category,
            severity,
            confidence,
            matches.Sum(match => match.InstanceCount),
            reason,
            matches.Select(match => match.DisplayName).ToArray()));
    }

    private static void AddRunningCategory(
        List<WorkspaceRiskItem> riskItems,
        IReadOnlyDictionary<string, ObservedProcessInfo> runningLookup,
        WorkspaceCategory category,
        string title,
        WorkspaceRiskSeverity severity,
        WorkspaceConfidence confidence,
        string reason,
        IEnumerable<string> processNames)
    {
        var matches = processNames
            .Where(runningLookup.ContainsKey)
            .Select(processName => runningLookup[processName])
            .OrderBy(match => match.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (matches.Length == 0)
        {
            return;
        }

        riskItems.Add(new WorkspaceRiskItem(
            title,
            category,
            severity,
            confidence,
            matches.Sum(match => match.InstanceCount),
            reason,
            matches.Select(match => match.DisplayName).ToArray()));
    }

    private static string BuildSummary(
        IReadOnlyList<WorkspaceRiskItem> riskItems,
        WorkspaceRiskSeverity highestSeverity)
    {
        var topGroups = riskItems
            .OrderByDescending(item => item.Severity)
            .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .Select(item => item.Title)
            .ToArray();

        return highestSeverity switch
        {
            WorkspaceRiskSeverity.High => $"Workspace-risk heuristics flagged high-impact activity: {string.Join(", ", topGroups)}.",
            WorkspaceRiskSeverity.Elevated => $"Workspace-risk heuristics flagged elevated disruption risk: {string.Join(", ", topGroups)}.",
            WorkspaceRiskSeverity.Advisory => $"Workspace-risk heuristics flagged advisory activity: {string.Join(", ", topGroups)}.",
            _ => "No workspace-risk heuristics were triggered during the latest scan."
        };
    }
}
