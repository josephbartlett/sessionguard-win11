using System.Text.Json;
using System.Reflection;
using SessionGuard.Core.Models;
using SessionGuard.Core.Services;
using SessionGuard.Infrastructure.Environment;
using SessionGuard.Infrastructure.Serialization;
using SessionGuard.Service;

namespace SessionGuard.Tests;

public sealed class ServiceHealthReporterTests
{
    [Fact]
    public async Task InitializeAndRecordScan_PersistsRunningHealthSnapshot()
    {
        var runtimeRoot = CreateRuntimeRoot();
        var logger = new RecordingLogger();
        var reporter = new SessionGuardServiceHealthReporter(
            RuntimePaths.Discover(runtimeRoot),
            logger);

        await reporter.InitializeAsync("Console");
        await reporter.RecordPipeServerStartedAsync();
        await reporter.RecordScanAsync(CreateStatus(), scanIntervalSeconds: 45);

        var snapshot = await ReadSnapshotAsync(reporter.HealthPath);

        Assert.Equal("Console", snapshot.HostMode);
        Assert.Equal("Running", snapshot.HealthState);
        Assert.True(snapshot.PipeServerListening);
        Assert.Equal(45, snapshot.ScanIntervalSeconds);
        Assert.Equal(RestartStateCategory.ProtectedSessionActive, snapshot.LastScanState);
        Assert.Equal(RestartRiskLevel.High, snapshot.LastScanRiskLevel);
        Assert.False(snapshot.ApprovalWindowActive);
        Assert.False(string.IsNullOrWhiteSpace(snapshot.ProductVersion));
    }

    [Fact]
    public async Task RecordErrorAndStop_PersistsDegradedThenStoppedState()
    {
        var runtimeRoot = CreateRuntimeRoot();
        var logger = new RecordingLogger();
        var reporter = new SessionGuardServiceHealthReporter(
            RuntimePaths.Discover(runtimeRoot),
            logger);

        await reporter.InitializeAsync("WindowsService");
        await reporter.RecordErrorAsync("pipe", new InvalidOperationException("Pipe failure"));
        await reporter.RecordStoppedAsync();

        var snapshot = await ReadSnapshotAsync(reporter.HealthPath);

        Assert.Equal("Stopped", snapshot.HealthState);
        Assert.False(snapshot.PipeServerListening);
        Assert.Equal("pipe", snapshot.LastErrorStage);
        Assert.Equal("Pipe failure", snapshot.LastErrorMessage);
        Assert.NotNull(snapshot.LastStoppedAt);
    }

    [Fact]
    public async Task RecordApprovalRecovery_PersistsApprovalRecoveryDetails()
    {
        var runtimeRoot = CreateRuntimeRoot();
        var logger = new RecordingLogger();
        var reporter = new SessionGuardServiceHealthReporter(
            RuntimePaths.Discover(runtimeRoot),
            logger);

        await reporter.InitializeAsync("Console");
        await reporter.RecordApprovalRecoveryAsync(
            new PolicyApprovalState(
                IsActive: true,
                GrantedAt: DateTimeOffset.Parse("2026-03-11T16:00:00-04:00"),
                ExpiresAt: DateTimeOffset.Parse("2026-03-11T16:45:00-04:00"),
                WindowMinutes: 45));

        var snapshot = await ReadSnapshotAsync(reporter.HealthPath);

        Assert.True(snapshot.ApprovalWindowActive);
        Assert.Equal(45, snapshot.ApprovalWindowMinutes);
        Assert.NotNull(snapshot.ApprovalStateRecoveredAt);
    }

    [Fact]
    public async Task InitializeAsync_RefreshesPersistedVersionAndRuntimeMetadata()
    {
        var runtimeRoot = CreateRuntimeRoot();
        var logger = new RecordingLogger();
        var reporter = new SessionGuardServiceHealthReporter(
            RuntimePaths.Discover(runtimeRoot),
            logger);

        var staleSnapshot = new ServiceHealthSnapshot(
            "SessionGuardService",
            "SessionGuard Service",
            "1.1.3-old",
            "WindowsService",
            "Running",
            "1.2",
            @"C:\Old\SessionGuard.Service.exe",
            @"C:\Old\",
            @"C:\Old\",
            @"C:\Old\config",
            @"C:\Old\logs",
            @"C:\Old\state",
            @"C:\Old\state\service-health.json",
            PipeServerListening: true,
            LastUpdatedAt: DateTimeOffset.Parse("2026-03-13T15:00:00-04:00"),
            LastStartedAt: DateTimeOffset.Parse("2026-03-13T15:00:00-04:00"));

        Directory.CreateDirectory(Path.GetDirectoryName(reporter.HealthPath)!);
        await File.WriteAllTextAsync(
            reporter.HealthPath,
            JsonSerializer.Serialize(staleSnapshot, SessionGuardJson.Indented));

        await reporter.InitializeAsync("WindowsService");

        var snapshot = await ReadSnapshotAsync(reporter.HealthPath);

        var expectedVersion = typeof(SessionGuardServiceHealthReporter)
            .Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        Assert.False(string.IsNullOrWhiteSpace(expectedVersion));
        Assert.Equal(expectedVersion, snapshot.ProductVersion);
        Assert.Equal(AppContext.BaseDirectory, snapshot.BaseDirectory);
        Assert.Equal(reporter.HealthPath, snapshot.HealthFilePath);
        Assert.Equal(Path.Combine(runtimeRoot, "config"), snapshot.ConfigDirectory);
        Assert.Equal(Path.Combine(runtimeRoot, "state"), snapshot.StateDirectory);
    }

    private static SessionControlStatus CreateStatus()
    {
        return new SessionControlStatus(
            new SessionScanResult(
                DateTimeOffset.Now,
                RestartStateCategory.ProtectedSessionActive,
                RestartRiskLevel.High,
                ProtectionMode.GuardModeActive,
                RestartPending: true,
                HasAmbiguousSignals: false,
                ProtectedSessionActive: true,
                LimitedVisibility: false,
                IsElevated: false,
                Summary: "Protected tools are active while restart indicators are present.",
                new WorkspaceStateSnapshot(
                    DateTimeOffset.Parse("2026-03-11T09:40:00-05:00"),
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
                    }),
                new PolicyEvaluation(
                    PolicyDecisionType.RestartBlocked,
                    HasBlockingRules: true,
                    RequiresApproval: false,
                    ApprovalActive: false,
                    ApprovalExpiresAt: null,
                    RecommendedApprovalWindowMinutes: 60,
                    "Policy rules are blocking restart right now: Never restart while terminals are running.",
                    new[]
                    {
                        new PolicyRuleMatch(
                            "block-terminal-sessions",
                            "Never restart while terminals are running",
                            PolicyRuleKind.ProcessBlock,
                            PolicyRuleOutcome.Blocked,
                            10,
                            "1 matching process instance(s) detected: pwsh.exe x1.")
                    },
                    new[]
                    {
                        "Never restart while terminals are running: 1 matching process instance(s) detected: pwsh.exe x1."
                    }),
                new RestartSignalOverview(1, 1, 1, 0, 0, 1, 0, "1 definitive pending-restart signal detected."),
                new[]
                {
                    new RestartIndicator(
                        "stub",
                        "Pending reboot",
                        RestartIndicatorCategory.PendingRestart,
                        true,
                        "Pending reboot detected.",
                        SignalConfidence.High)
                },
                new[]
                {
                    new ProtectedProcessMatch("pwsh.exe", 1)
                },
                Array.Empty<ManagedMitigationState>(),
                new[] { "Save work." }),
            GuardModeEnabled: true,
            "Service",
            IsRemote: true);
    }

    private static async Task<ServiceHealthSnapshot> ReadSnapshotAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<ServiceHealthSnapshot>(stream, SessionGuardJson.Default) ??
               throw new InvalidOperationException("Failed to deserialize the persisted health snapshot.");
    }

    private static string CreateRuntimeRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "SessionGuard.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "config"));
        return root;
    }

    private sealed class RecordingLogger : IAppLogger
    {
        public string LogDirectory => "logs";

        public void Info(string message, object? context = null)
        {
        }

        public void Warn(string message, object? context = null)
        {
        }

        public void Error(string message, Exception exception, object? context = null)
        {
        }
    }
}
