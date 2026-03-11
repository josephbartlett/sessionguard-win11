using System.Text.Json;
using SessionGuard.Core.Models;
using SessionGuard.Infrastructure.Diagnostics;
using SessionGuard.Infrastructure.Environment;
using SessionGuard.Infrastructure.Serialization;

namespace SessionGuard.Tests;

public sealed class JsonScanSnapshotStoreTests
{
    [Fact]
    public async Task PersistAsync_WritesWorkspaceSnapshot_WhenRiskIsPresent()
    {
        var runtimeRoot = CreateRuntimeRoot();
        var paths = RuntimePaths.Discover(runtimeRoot);
        var store = new JsonScanSnapshotStore(paths);
        var result = CreateScanResult(CreateWorkspaceSnapshot(hasRisk: true));

        await store.PersistAsync(result);

        var workspacePath = Path.Combine(paths.StateDirectory, "workspace-snapshot.json");
        Assert.True(File.Exists(workspacePath));

        await using var stream = File.OpenRead(workspacePath);
        var snapshot = await JsonSerializer.DeserializeAsync<WorkspaceStateSnapshot>(stream, SessionGuardJson.Default);
        Assert.NotNull(snapshot);
        Assert.True(snapshot!.HasRisk);
        Assert.Single(snapshot.RiskItems);
    }

    [Fact]
    public async Task PersistAsync_RemovesStaleWorkspaceSnapshot_WhenNoRiskIsPresent()
    {
        var runtimeRoot = CreateRuntimeRoot();
        var paths = RuntimePaths.Discover(runtimeRoot);
        var store = new JsonScanSnapshotStore(paths);

        await store.PersistAsync(CreateScanResult(CreateWorkspaceSnapshot(hasRisk: true)));
        await store.PersistAsync(CreateScanResult(CreateWorkspaceSnapshot(hasRisk: false)));

        var workspacePath = Path.Combine(paths.StateDirectory, "workspace-snapshot.json");
        Assert.False(File.Exists(workspacePath));
    }

    private static SessionScanResult CreateScanResult(WorkspaceStateSnapshot workspace)
    {
        return new SessionScanResult(
            DateTimeOffset.Parse("2026-03-11T09:15:00-05:00"),
            RestartStateCategory.Safe,
            RestartRiskLevel.Low,
            ProtectionMode.MonitorOnly,
            RestartPending: false,
            HasAmbiguousSignals: false,
            ProtectedSessionActive: workspace.HasRisk,
            LimitedVisibility: false,
            IsElevated: false,
            Summary: "Safe",
            workspace,
            PolicyEvaluation.None,
            new RestartSignalOverview(0, 0, 0, 0, 0, 0, 0, "No signals."),
            Array.Empty<RestartIndicator>(),
            Array.Empty<ProtectedProcessMatch>(),
            Array.Empty<ManagedMitigationState>(),
            new[] { "No action required." });
    }

    private static WorkspaceStateSnapshot CreateWorkspaceSnapshot(bool hasRisk)
    {
        if (!hasRisk)
        {
            return WorkspaceStateSnapshot.None(DateTimeOffset.Parse("2026-03-11T09:15:00-05:00"));
        }

        return new WorkspaceStateSnapshot(
            DateTimeOffset.Parse("2026-03-11T09:15:00-05:00"),
            HasRisk: true,
            WorkspaceRiskSeverity.High,
            WorkspaceConfidence.High,
            "Workspace-risk heuristics flagged high-impact activity: Terminal and shell sessions.",
            new[]
            {
                new WorkspaceRiskItem(
                    "Terminal and shell sessions",
                    WorkspaceCategory.TerminalShell,
                    WorkspaceRiskSeverity.High,
                    WorkspaceConfidence.High,
                    1,
                    "Interactive shell detected.",
                    new[] { "pwsh.exe" })
            });
    }

    private static string CreateRuntimeRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "SessionGuard.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "config"));
        return root;
    }
}
