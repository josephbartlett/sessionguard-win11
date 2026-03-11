using SessionGuard.Core.Configuration;
using SessionGuard.Core.Models;
using SessionGuard.Core.Services;
using SessionGuard.Infrastructure.ControlPlane;
using SessionGuard.Service;

namespace SessionGuard.Tests;

public sealed class ControlPlaneTests
{
    [Fact]
    public async Task HybridControlPlane_FallsBackToLocal_WhenRemoteFails()
    {
        var scanResult = CreateScanResult();
        var remote = new ThrowingControlPlane();
        var local = new StubControlPlane(
            new SessionControlStatus(scanResult, GuardModeEnabled: true, "Local fallback", IsRemote: false));
        var logger = new RecordingLogger();
        var hybrid = new HybridSessionGuardControlPlane(remote, local, logger);

        var status = await hybrid.GetStatusAsync();

        Assert.Equal("Local fallback", status.ConnectionMode);
        Assert.False(status.IsRemote);
        Assert.Contains(logger.Warnings, warning => warning.Contains("control_plane.remote.unavailable", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task HybridControlPlane_PrefersRemote_WhenRemoteSucceeds()
    {
        var remoteStatus = new SessionControlStatus(
            CreateScanResult() with
            {
                ProtectionMode = ProtectionMode.ManagedMitigationsApplied
            },
            GuardModeEnabled: true,
            "Service",
            IsRemote: true);
        var remote = new StubControlPlane(remoteStatus);
        var local = new StubControlPlane(
            new SessionControlStatus(CreateScanResult(), GuardModeEnabled: true, "Local fallback", IsRemote: false));
        var logger = new RecordingLogger();
        var hybrid = new HybridSessionGuardControlPlane(remote, local, logger);

        var status = await hybrid.GetStatusAsync();

        Assert.Equal("Service", status.ConnectionMode);
        Assert.True(status.IsRemote);
        Assert.DoesNotContain(logger.Warnings, warning => warning.Contains("control_plane.remote.unavailable", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ServiceRuntime_GetStatusInitializesScanAndPersistsSnapshot()
    {
        var snapshotStore = new RecordingSnapshotStore();
        var runtime = CreateRuntime(snapshotStore);

        var status = await runtime.GetStatusAsync();

        Assert.True(status.GuardModeEnabled);
        Assert.Equal(RestartStateCategory.ProtectedSessionActive, status.ScanResult.State);
        Assert.Single(snapshotStore.Persisted);
    }

    [Fact]
    public async Task ServiceRuntime_SetGuardMode_UpdatesStatusAndPersistsSnapshot()
    {
        var snapshotStore = new RecordingSnapshotStore();
        var runtime = CreateRuntime(snapshotStore);

        await runtime.GetStatusAsync();
        var status = await runtime.SetGuardModeAsync(false);

        Assert.False(status.GuardModeEnabled);
        Assert.Equal(2, snapshotStore.Persisted.Count);
    }

    private static SessionGuardServiceRuntime CreateRuntime(RecordingSnapshotStore snapshotStore)
    {
        var configuration = new RuntimeConfiguration(
            new AppSettings
            {
                GuardModeEnabledByDefault = true,
                ScanIntervalSeconds = 30
            }.Normalize(),
            new ProtectedProcessCatalog
            {
                ProcessNames = new[] { "pwsh.exe" }
            }.Normalize(),
            "config",
            "config\\appsettings.json",
            "config\\protected-processes.json");
        var logger = new RecordingLogger();
        var configurationRepository = new StubConfigurationRepository(configuration);
        var mitigationService = new StubMitigationService();

        return new SessionGuardServiceRuntime(
            new SessionGuardCoordinator(
                configurationRepository,
                new StubWorkspaceDetector(new[] { new ProtectedProcessMatch("pwsh.exe", 1) }),
                new IRestartSignalProvider[]
                {
                    new StubRestartSignalProvider(
                        "stub",
                        new[]
                        {
                            new RestartIndicator(
                                "stub",
                                "Pending reboot",
                                RestartIndicatorCategory.PendingRestart,
                                true,
                                "Pending reboot detected.",
                                SignalConfidence.High)
                        })
                },
                mitigationService,
                logger),
            configurationRepository,
            mitigationService,
            snapshotStore,
            logger);
    }

    private static SessionScanResult CreateScanResult()
    {
        return new SessionScanResult(
            DateTimeOffset.Now,
            RestartStateCategory.Safe,
            RestartRiskLevel.Low,
            ProtectionMode.GuardModeActive,
            RestartPending: false,
            HasAmbiguousSignals: false,
            ProtectedSessionActive: false,
            LimitedVisibility: false,
            IsElevated: false,
            Summary: "Safe",
            new RestartSignalOverview(0, 0, 0, 0, 0, 0, 0, "No signals."),
            Array.Empty<RestartIndicator>(),
            Array.Empty<ProtectedProcessMatch>(),
            Array.Empty<ManagedMitigationState>(),
            new[] { "No action required." });
    }

    private sealed class ThrowingControlPlane : ISessionGuardControlPlane
    {
        public Task<SessionControlStatus> GetStatusAsync(CancellationToken cancellationToken = default)
            => Task.FromException<SessionControlStatus>(new InvalidOperationException("Service unavailable"));

        public Task<SessionControlStatus> ScanNowAsync(CancellationToken cancellationToken = default)
            => GetStatusAsync(cancellationToken);

        public Task<SessionControlStatus> SetGuardModeAsync(bool enabled, CancellationToken cancellationToken = default)
            => GetStatusAsync(cancellationToken);

        public Task<MitigationCommandResult> ApplyRecommendedAsync(CancellationToken cancellationToken = default)
            => Task.FromException<MitigationCommandResult>(new InvalidOperationException("Service unavailable"));

        public Task<MitigationCommandResult> ResetManagedAsync(CancellationToken cancellationToken = default)
            => Task.FromException<MitigationCommandResult>(new InvalidOperationException("Service unavailable"));
    }

    private sealed class StubControlPlane : ISessionGuardControlPlane
    {
        private readonly SessionControlStatus _status;

        public StubControlPlane(SessionControlStatus status)
        {
            _status = status;
        }

        public Task<SessionControlStatus> GetStatusAsync(CancellationToken cancellationToken = default) => Task.FromResult(_status);

        public Task<SessionControlStatus> ScanNowAsync(CancellationToken cancellationToken = default) => Task.FromResult(_status);

        public Task<SessionControlStatus> SetGuardModeAsync(bool enabled, CancellationToken cancellationToken = default)
            => Task.FromResult(_status with { GuardModeEnabled = enabled });

        public Task<MitigationCommandResult> ApplyRecommendedAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new MitigationCommandResult(true, false, "ok", Array.Empty<ManagedMitigationState>()));

        public Task<MitigationCommandResult> ResetManagedAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new MitigationCommandResult(true, false, "ok", Array.Empty<ManagedMitigationState>()));
    }

    private sealed class StubConfigurationRepository : IConfigurationRepository
    {
        private readonly RuntimeConfiguration _configuration;

        public StubConfigurationRepository(RuntimeConfiguration configuration)
        {
            _configuration = configuration;
        }

        public string ConfigurationDirectory => _configuration.ConfigurationDirectory;

        public Task<RuntimeConfiguration> LoadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_configuration);
    }

    private sealed class StubWorkspaceDetector : IProtectedWorkspaceDetector
    {
        private readonly IReadOnlyList<ProtectedProcessMatch> _matches;

        public StubWorkspaceDetector(IReadOnlyList<ProtectedProcessMatch> matches)
        {
            _matches = matches;
        }

        public Task<IReadOnlyList<ProtectedProcessMatch>> GetActiveMatchesAsync(
            IReadOnlyCollection<string> protectedProcesses,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_matches);
    }

    private sealed class StubRestartSignalProvider : IRestartSignalProvider
    {
        private readonly IReadOnlyList<RestartIndicator> _indicators;

        public StubRestartSignalProvider(string name, IReadOnlyList<RestartIndicator> indicators)
        {
            Name = name;
            _indicators = indicators;
        }

        public string Name { get; }

        public Task<IReadOnlyList<RestartIndicator>> GetIndicatorsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_indicators);
    }

    private sealed class StubMitigationService : IMitigationService
    {
        public bool IsElevated => false;

        public Task<IReadOnlyList<ManagedMitigationState>> GetStatesAsync(RuntimeConfiguration configuration, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ManagedMitigationState>>(Array.Empty<ManagedMitigationState>());

        public Task<MitigationCommandResult> ApplyRecommendedAsync(RuntimeConfiguration configuration, CancellationToken cancellationToken = default)
            => Task.FromResult(new MitigationCommandResult(true, false, "Applied", Array.Empty<ManagedMitigationState>()));

        public Task<MitigationCommandResult> ResetManagedAsync(RuntimeConfiguration configuration, CancellationToken cancellationToken = default)
            => Task.FromResult(new MitigationCommandResult(true, false, "Reset", Array.Empty<ManagedMitigationState>()));
    }

    private sealed class RecordingSnapshotStore : IScanSnapshotStore
    {
        public List<SessionScanResult> Persisted { get; } = new();

        public Task PersistAsync(SessionScanResult result, CancellationToken cancellationToken = default)
        {
            Persisted.Add(result);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingLogger : IAppLogger
    {
        public string LogDirectory => "logs";

        public List<string> Warnings { get; } = new();

        public void Info(string message, object? context = null)
        {
        }

        public void Warn(string message, object? context = null)
        {
            Warnings.Add(message);
        }

        public void Error(string message, Exception exception, object? context = null)
        {
            Warnings.Add(message);
        }
    }
}
