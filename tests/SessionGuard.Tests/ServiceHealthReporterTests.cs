using System.Text.Json;
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
