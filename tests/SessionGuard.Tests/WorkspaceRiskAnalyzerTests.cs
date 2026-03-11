using SessionGuard.Core.Models;
using SessionGuard.Core.Services;

namespace SessionGuard.Tests;

public sealed class WorkspaceRiskAnalyzerTests
{
    [Fact]
    public void Analyze_GroupsKnownWorkspaceSignalsAndDevRuntimes()
    {
        var observation = new WorkspaceProcessObservation(
            new[]
            {
                new ProtectedProcessMatch("WindowsTerminal.exe", 1),
                new ProtectedProcessMatch("Code.exe", 1),
                new ProtectedProcessMatch("chrome.exe", 2)
            },
            new[]
            {
                new ObservedProcessInfo("pwsh.exe", 1),
                new ObservedProcessInfo("node.exe", 2),
                new ObservedProcessInfo("chrome.exe", 2)
            });

        var snapshot = WorkspaceRiskAnalyzer.Analyze(observation, DateTimeOffset.Parse("2026-03-11T09:00:00-05:00"));

        Assert.True(snapshot.HasRisk);
        Assert.Equal(WorkspaceRiskSeverity.High, snapshot.HighestSeverity);
        Assert.Equal(WorkspaceConfidence.High, snapshot.Confidence);
        Assert.Contains(snapshot.RiskItems, item => item.Category == WorkspaceCategory.TerminalShell);
        Assert.Contains(snapshot.RiskItems, item => item.Category == WorkspaceCategory.EditorOrIde);
        Assert.Contains(snapshot.RiskItems, item => item.Category == WorkspaceCategory.Browser);
        Assert.Contains(snapshot.RiskItems, item => item.Category == WorkspaceCategory.LocalDevServer);
        Assert.Contains("high-impact activity", snapshot.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Analyze_ReturnsNone_WhenNoWorkspaceSignalsArePresent()
    {
        var observation = new WorkspaceProcessObservation(
            Array.Empty<ProtectedProcessMatch>(),
            new[] { new ObservedProcessInfo("svchost.exe", 20) });

        var snapshot = WorkspaceRiskAnalyzer.Analyze(observation, DateTimeOffset.Parse("2026-03-11T09:05:00-05:00"));

        Assert.False(snapshot.HasRisk);
        Assert.Equal(WorkspaceRiskSeverity.None, snapshot.HighestSeverity);
        Assert.Empty(snapshot.RiskItems);
    }

    [Fact]
    public void Analyze_PreservesGenericConfiguredProtectedTools()
    {
        var observation = new WorkspaceProcessObservation(
            new[]
            {
                new ProtectedProcessMatch("obs64.exe", 1)
            },
            new[]
            {
                new ObservedProcessInfo("obs64.exe", 1)
            });

        var snapshot = WorkspaceRiskAnalyzer.Analyze(observation, DateTimeOffset.Parse("2026-03-11T09:10:00-05:00"));

        var item = Assert.Single(snapshot.RiskItems);
        Assert.Equal(WorkspaceCategory.ProtectedTool, item.Category);
        Assert.Contains("operator-defined protection list", item.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Analyze_AssignsMediumConfidenceToBrowserOnlySessions()
    {
        var observation = new WorkspaceProcessObservation(
            new[]
            {
                new ProtectedProcessMatch("chrome.exe", 3)
            },
            new[]
            {
                new ObservedProcessInfo("chrome.exe", 3)
            });

        var snapshot = WorkspaceRiskAnalyzer.Analyze(observation, DateTimeOffset.Parse("2026-03-11T09:12:00-05:00"));

        var item = Assert.Single(snapshot.RiskItems);
        Assert.Equal(WorkspaceCategory.Browser, item.Category);
        Assert.Equal(WorkspaceRiskSeverity.Elevated, item.Severity);
        Assert.Equal(WorkspaceConfidence.Medium, item.Confidence);
    }

    [Fact]
    public void Analyze_TreatsStandaloneRuntimeAsElevatedInsteadOfHigh()
    {
        var observation = new WorkspaceProcessObservation(
            Array.Empty<ProtectedProcessMatch>(),
            new[]
            {
                new ObservedProcessInfo("python.exe", 1)
            });

        var snapshot = WorkspaceRiskAnalyzer.Analyze(observation, DateTimeOffset.Parse("2026-03-11T09:14:00-05:00"));

        var item = Assert.Single(snapshot.RiskItems);
        Assert.Equal(WorkspaceCategory.LocalDevServer, item.Category);
        Assert.Equal(WorkspaceRiskSeverity.Elevated, item.Severity);
        Assert.Equal(WorkspaceConfidence.Medium, item.Confidence);
        Assert.Equal(WorkspaceRiskSeverity.Elevated, snapshot.HighestSeverity);
    }
}
