using SessionGuard.Core.Configuration;
using SessionGuard.Core.Models;
using SessionGuard.Core.Services;
using SessionGuard.Infrastructure.ControlPlane;
using SessionGuard.Infrastructure.Environment;
using SessionGuard.Service;

namespace SessionGuard.Tests;

public sealed class ControlPlaneTests
{
    [Fact]
    public async Task HybridControlPlane_FallsBackToLocal_WhenRemoteFails()
    {
        var scanResult = CreateScanResult();
        var remote = new UnavailableControlPlane();
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
    public async Task HybridControlPlane_DoesNotFallbackToLocal_WhenRemoteReturnsApplicationFailure()
    {
        var remote = new ApplicationFailureControlPlane();
        var local = new RecordingControlPlane(new SessionControlStatus(CreateScanResult(), GuardModeEnabled: true, "Local fallback", IsRemote: false));
        var logger = new RecordingLogger();
        var hybrid = new HybridSessionGuardControlPlane(remote, local, logger);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => hybrid.ApplyRecommendedAsync());

        Assert.Contains("service denied", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(local.ApplyRecommendedCalled);
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

    [Fact]
    public async Task ServiceRuntime_GrantRestartApproval_UpdatesPolicyState()
    {
        var snapshotStore = new RecordingSnapshotStore();
        var runtime = CreateRuntime(snapshotStore);

        var result = await runtime.GrantRestartApprovalAsync();
        var status = await runtime.GetStatusAsync();

        Assert.True(result.Success);
        Assert.True(status.ScanResult.Policy.ApprovalActive);
        Assert.Equal(PolicyDecisionType.ApprovalActive, status.ScanResult.Policy.Decision);
    }

    [Fact]
    public async Task LocalControlPlane_ServiceOwnedActionsReturnRequiresService()
    {
        var local = CreateLocalControlPlane();

        var mitigation = await local.ApplyRecommendedAsync();
        var approval = await local.GrantRestartApprovalAsync();

        Assert.False(mitigation.Success);
        Assert.True(mitigation.RequiresService);
        Assert.False(approval.Success);
        Assert.True(approval.RequiresService);
    }

    [Fact]
    public async Task ServiceRuntime_InitializeAsync_RecordsRecoveredApprovalState()
    {
        var snapshotStore = new RecordingSnapshotStore();
        var now = DateTimeOffset.Now;
        var approvalStore = new InMemoryPolicyApprovalStore
        {
            Current = new PolicyApprovalState(
                IsActive: true,
                GrantedAt: now,
                ExpiresAt: now.AddMinutes(45),
                WindowMinutes: 45)
        };
        var runtimeRoot = Path.Combine(Path.GetTempPath(), "SessionGuard.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(runtimeRoot);
        var logger = new RecordingLogger();
        var healthReporter = new SessionGuardServiceHealthReporter(
            RuntimePaths.Discover(runtimeRoot),
            logger);
        var runtime = CreateRuntime(snapshotStore, approvalStore, logger, healthReporter);

        await runtime.InitializeAsync();

        await using var stream = File.OpenRead(healthReporter.HealthPath);
        var snapshot = await System.Text.Json.JsonSerializer.DeserializeAsync<ServiceHealthSnapshot>(
            stream,
            Infrastructure.Serialization.SessionGuardJson.Default);

        Assert.NotNull(snapshot);
        Assert.True(snapshot!.ApprovalWindowActive);
        Assert.Equal(45, snapshot.ApprovalWindowMinutes);
        Assert.NotNull(snapshot.ApprovalStateRecoveredAt);
    }

    private static SessionGuardServiceRuntime CreateRuntime(
        RecordingSnapshotStore snapshotStore,
        InMemoryPolicyApprovalStore? approvalStore = null,
        RecordingLogger? logger = null,
        SessionGuardServiceHealthReporter? healthReporter = null)
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
            new PolicyConfiguration
            {
                DefaultApprovalWindowMinutes = 45,
                Rules = new[]
                {
                    new PolicyRuleDefinition
                    {
                        Id = "approval-required-restart-pending",
                        Title = "Approval required for restart-pending states",
                        Kind = PolicyRuleKind.ApprovalRequired,
                        MatchWhenRestartPendingOnly = true,
                        MinimumRiskLevel = RestartRiskLevel.Elevated,
                        ApprovalWindowMinutes = 45
                    }
                }
            }.Normalize(),
            "config",
            "config",
            "config\\appsettings.json",
            "config\\protected-processes.json",
            "config\\policies.json");
        logger ??= new RecordingLogger();
        var configurationRepository = new StubConfigurationRepository(configuration);
        var mitigationService = new StubMitigationService();
        approvalStore ??= new InMemoryPolicyApprovalStore();

        if (healthReporter is null)
        {
            var runtimeRoot = Path.Combine(Path.GetTempPath(), "SessionGuard.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(runtimeRoot);
            healthReporter = new SessionGuardServiceHealthReporter(
                RuntimePaths.Discover(runtimeRoot),
                logger);
        }

        return new SessionGuardServiceRuntime(
            new SessionGuardCoordinator(
                configurationRepository,
                new StubWorkspaceDetector(
                    new WorkspaceProcessObservation(
                        new[] { new ProtectedProcessMatch("pwsh.exe", 1) },
                        new[]
                        {
                            new ObservedProcessInfo("pwsh.exe", 1),
                            new ObservedProcessInfo("node.exe", 1)
                        })),
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
                approvalStore,
                logger),
            configurationRepository,
            mitigationService,
            approvalStore,
            snapshotStore,
            healthReporter,
            logger);
    }

    private static LocalSessionGuardControlPlane CreateLocalControlPlane()
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
            new PolicyConfiguration
            {
                DefaultApprovalWindowMinutes = 45,
                Rules = new[]
                {
                    new PolicyRuleDefinition
                    {
                        Id = "approval-required-restart-pending",
                        Title = "Approval required for restart-pending states",
                        Kind = PolicyRuleKind.ApprovalRequired,
                        MatchWhenRestartPendingOnly = true,
                        MinimumRiskLevel = RestartRiskLevel.Elevated,
                        ApprovalWindowMinutes = 45
                    }
                }
            }.Normalize(),
            "config",
            "config",
            "config\\appsettings.json",
            "config\\protected-processes.json",
            "config\\policies.json");
        var configurationRepository = new StubConfigurationRepository(configuration);
        var mitigationService = new StubMitigationService();
        var approvalStore = new InMemoryPolicyApprovalStore();
        var snapshotStore = new RecordingSnapshotStore();

        return new LocalSessionGuardControlPlane(
            new SessionGuardCoordinator(
                configurationRepository,
                new StubWorkspaceDetector(
                    new WorkspaceProcessObservation(
                        new[] { new ProtectedProcessMatch("pwsh.exe", 1) },
                        new[] { new ObservedProcessInfo("pwsh.exe", 1) })),
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
                approvalStore,
                new RecordingLogger()),
            configurationRepository,
            mitigationService,
            approvalStore,
            snapshotStore);
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
            WorkspaceStateSnapshot.None(DateTimeOffset.Parse("2026-03-11T09:45:00-05:00")),
            PolicyEvaluation.None,
            new RestartSignalOverview(0, 0, 0, 0, 0, 0, 0, "No signals."),
            Array.Empty<RestartIndicator>(),
            Array.Empty<ProtectedProcessMatch>(),
            Array.Empty<ManagedMitigationState>(),
            new[] { "No action required." });
    }

    private sealed class UnavailableControlPlane : ISessionGuardControlPlane
    {
        public Task<SessionControlStatus> GetStatusAsync(CancellationToken cancellationToken = default)
            => Task.FromException<SessionControlStatus>(new SessionGuardControlPlaneUnavailableException("Service unavailable"));

        public Task<SessionControlStatus> ScanNowAsync(CancellationToken cancellationToken = default)
            => GetStatusAsync(cancellationToken);

        public Task<SessionControlStatus> SetGuardModeAsync(bool enabled, CancellationToken cancellationToken = default)
            => GetStatusAsync(cancellationToken);

        public Task<MitigationCommandResult> ApplyRecommendedAsync(CancellationToken cancellationToken = default)
            => Task.FromException<MitigationCommandResult>(new SessionGuardControlPlaneUnavailableException("Service unavailable"));

        public Task<MitigationCommandResult> ResetManagedAsync(CancellationToken cancellationToken = default)
            => Task.FromException<MitigationCommandResult>(new SessionGuardControlPlaneUnavailableException("Service unavailable"));

        public Task<PolicyApprovalCommandResult> GrantRestartApprovalAsync(CancellationToken cancellationToken = default)
            => Task.FromException<PolicyApprovalCommandResult>(new SessionGuardControlPlaneUnavailableException("Service unavailable"));

        public Task<PolicyApprovalCommandResult> ClearRestartApprovalAsync(CancellationToken cancellationToken = default)
            => Task.FromException<PolicyApprovalCommandResult>(new SessionGuardControlPlaneUnavailableException("Service unavailable"));
    }

    private sealed class ApplicationFailureControlPlane : ISessionGuardControlPlane
    {
        public Task<SessionControlStatus> GetStatusAsync(CancellationToken cancellationToken = default)
            => Task.FromException<SessionControlStatus>(new InvalidOperationException("Service denied status request."));

        public Task<SessionControlStatus> ScanNowAsync(CancellationToken cancellationToken = default)
            => Task.FromException<SessionControlStatus>(new InvalidOperationException("Service denied scan request."));

        public Task<SessionControlStatus> SetGuardModeAsync(bool enabled, CancellationToken cancellationToken = default)
            => Task.FromException<SessionControlStatus>(new InvalidOperationException("Service denied guard mode request."));

        public Task<MitigationCommandResult> ApplyRecommendedAsync(CancellationToken cancellationToken = default)
            => Task.FromException<MitigationCommandResult>(new InvalidOperationException("Service denied mitigation request."));

        public Task<MitigationCommandResult> ResetManagedAsync(CancellationToken cancellationToken = default)
            => Task.FromException<MitigationCommandResult>(new InvalidOperationException("Service denied mitigation reset request."));

        public Task<PolicyApprovalCommandResult> GrantRestartApprovalAsync(CancellationToken cancellationToken = default)
            => Task.FromException<PolicyApprovalCommandResult>(new InvalidOperationException("Service denied approval request."));

        public Task<PolicyApprovalCommandResult> ClearRestartApprovalAsync(CancellationToken cancellationToken = default)
            => Task.FromException<PolicyApprovalCommandResult>(new InvalidOperationException("Service denied approval-clear request."));
    }

    private class StubControlPlane : ISessionGuardControlPlane
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

        public virtual Task<MitigationCommandResult> ApplyRecommendedAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new MitigationCommandResult(true, false, false, "ok", Array.Empty<ManagedMitigationState>()));

        public Task<MitigationCommandResult> ResetManagedAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new MitigationCommandResult(true, false, false, "ok", Array.Empty<ManagedMitigationState>()));

        public Task<PolicyApprovalCommandResult> GrantRestartApprovalAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new PolicyApprovalCommandResult(true, false, "ok", _status.ScanResult.Policy));

        public Task<PolicyApprovalCommandResult> ClearRestartApprovalAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new PolicyApprovalCommandResult(true, false, "ok", _status.ScanResult.Policy));
    }

    private sealed class RecordingControlPlane : StubControlPlane
    {
        public RecordingControlPlane(SessionControlStatus status)
            : base(status)
        {
        }

        public bool ApplyRecommendedCalled { get; private set; }

        public override Task<MitigationCommandResult> ApplyRecommendedAsync(CancellationToken cancellationToken = default)
        {
            ApplyRecommendedCalled = true;
            return base.ApplyRecommendedAsync(cancellationToken);
        }
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
        private readonly WorkspaceProcessObservation _observation;

        public StubWorkspaceDetector(WorkspaceProcessObservation observation)
        {
            _observation = observation;
        }

        public Task<WorkspaceProcessObservation> GetWorkspaceObservationAsync(
            IReadOnlyCollection<string> protectedProcesses,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_observation);
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
            => Task.FromResult(new MitigationCommandResult(true, false, false, "Applied", Array.Empty<ManagedMitigationState>()));

        public Task<MitigationCommandResult> ResetManagedAsync(RuntimeConfiguration configuration, CancellationToken cancellationToken = default)
            => Task.FromResult(new MitigationCommandResult(true, false, false, "Reset", Array.Empty<ManagedMitigationState>()));
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

    private sealed class InMemoryPolicyApprovalStore : IPolicyApprovalStore
    {
        public PolicyApprovalState Current { get; set; } = PolicyApprovalState.None;

        public Task<PolicyApprovalState> GetCurrentAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
        {
            if (Current.IsActive && Current.ExpiresAt.HasValue && Current.ExpiresAt.Value <= now)
            {
                Current = PolicyApprovalState.None;
            }

            return Task.FromResult(Current);
        }

        public Task<PolicyApprovalState> GrantAsync(DateTimeOffset now, TimeSpan duration, CancellationToken cancellationToken = default)
        {
            Current = new PolicyApprovalState(true, now, now.Add(duration), (int)duration.TotalMinutes);
            return Task.FromResult(Current);
        }

        public Task ClearAsync(CancellationToken cancellationToken = default)
        {
            Current = PolicyApprovalState.None;
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
