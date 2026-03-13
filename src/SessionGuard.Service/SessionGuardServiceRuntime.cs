using SessionGuard.Core.Models;
using SessionGuard.Core.Services;

namespace SessionGuard.Service;

public sealed class SessionGuardServiceRuntime
{
    private readonly SessionGuardCoordinator _coordinator;
    private readonly IConfigurationRepository _configurationRepository;
    private readonly IMitigationService _mitigationService;
    private readonly IPolicyApprovalStore _policyApprovalStore;
    private readonly IScanSnapshotStore _snapshotStore;
    private readonly SessionGuardServiceHealthReporter _healthReporter;
    private readonly IAppLogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private SessionControlStatus? _currentStatus;
    private bool? _guardModeEnabled;
    private bool _initialized;

    public SessionGuardServiceRuntime(
        SessionGuardCoordinator coordinator,
        IConfigurationRepository configurationRepository,
        IMitigationService mitigationService,
        IPolicyApprovalStore policyApprovalStore,
        IScanSnapshotStore snapshotStore,
        SessionGuardServiceHealthReporter healthReporter,
        IAppLogger logger)
    {
        _coordinator = coordinator;
        _configurationRepository = configurationRepository;
        _mitigationService = mitigationService;
        _policyApprovalStore = policyApprovalStore;
        _snapshotStore = snapshotStore;
        _healthReporter = healthReporter;
        _logger = logger;
    }

    public async Task<SessionControlStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            if (_currentStatus is not null)
            {
                return _currentStatus;
            }

            var configuration = await _configurationRepository.LoadAsync(cancellationToken);
            await EnsureInitializedLockedAsync(configuration, cancellationToken);
            return await ScanLockedAsync(configuration, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<SessionControlStatus> ScanNowAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            var configuration = await _configurationRepository.LoadAsync(cancellationToken);
            await EnsureInitializedLockedAsync(configuration, cancellationToken);
            return await ScanLockedAsync(configuration, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<SessionControlStatus> SetGuardModeAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            var configuration = await _configurationRepository.LoadAsync(cancellationToken);
            await EnsureInitializedLockedAsync(configuration, cancellationToken);
            _guardModeEnabled = enabled;
            _logger.Info("service.guard_mode.changed", new { enabled });
            return await ScanLockedAsync(configuration, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<MitigationCommandResult> ApplyMitigationsAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            var configuration = await _configurationRepository.LoadAsync(cancellationToken);
            await EnsureInitializedLockedAsync(configuration, cancellationToken);
            var result = await _mitigationService.ApplyRecommendedAsync(configuration, cancellationToken);
            await ScanLockedAsync(configuration, cancellationToken);
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<MitigationCommandResult> ResetMitigationsAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            var configuration = await _configurationRepository.LoadAsync(cancellationToken);
            await EnsureInitializedLockedAsync(configuration, cancellationToken);
            var result = await _mitigationService.ResetManagedAsync(configuration, cancellationToken);
            await ScanLockedAsync(configuration, cancellationToken);
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<PolicyApprovalCommandResult> GrantRestartApprovalAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            var configuration = await _configurationRepository.LoadAsync(cancellationToken);
            await EnsureInitializedLockedAsync(configuration, cancellationToken);
            var baselineStatus = await ScanLockedAsync(configuration, cancellationToken);
            var windowMinutes = baselineStatus.ScanResult.Policy.RecommendedApprovalWindowMinutes > 0
                ? baselineStatus.ScanResult.Policy.RecommendedApprovalWindowMinutes
                : configuration.Policies.DefaultApprovalWindowMinutes;
            await _policyApprovalStore.GrantAsync(
                DateTimeOffset.Now,
                TimeSpan.FromMinutes(windowMinutes),
                cancellationToken);
            var status = await ScanLockedAsync(configuration, cancellationToken);
            return new PolicyApprovalCommandResult(
                true,
                false,
                false,
                $"Granted a temporary restart approval window for {windowMinutes} minute(s).",
                status.ScanResult.Policy);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<PolicyApprovalCommandResult> ClearRestartApprovalAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            var configuration = await _configurationRepository.LoadAsync(cancellationToken);
            await EnsureInitializedLockedAsync(configuration, cancellationToken);
            await _policyApprovalStore.ClearAsync(cancellationToken);
            var status = await ScanLockedAsync(configuration, cancellationToken);
            return new PolicyApprovalCommandResult(
                true,
                false,
                false,
                "Cleared the temporary restart approval window.",
                status.ScanResult.Policy);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            var configuration = await _configurationRepository.LoadAsync(cancellationToken);
            await EnsureInitializedLockedAsync(configuration, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureInitializedLockedAsync(
        Core.Configuration.RuntimeConfiguration configuration,
        CancellationToken cancellationToken)
    {
        _guardModeEnabled ??= configuration.AppSettings.GuardModeEnabledByDefault;

        if (_initialized)
        {
            return;
        }

        var approvalState = await _policyApprovalStore.GetCurrentAsync(DateTimeOffset.Now, cancellationToken);
        _logger.Info(
            "service.policy_approval.recovered",
            new
            {
                approvalState.IsActive,
                approvalState.ExpiresAt,
                approvalState.WindowMinutes
            });
        await _healthReporter.RecordApprovalRecoveryAsync(approvalState, cancellationToken);
        _initialized = true;
    }

    private async Task<SessionControlStatus> ScanLockedAsync(
        Core.Configuration.RuntimeConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var result = await _coordinator.ScanAsync(
            _guardModeEnabled ?? configuration.AppSettings.GuardModeEnabledByDefault,
            cancellationToken);

        await _snapshotStore.PersistAsync(result, cancellationToken);

        _currentStatus = new SessionControlStatus(
            result,
            _guardModeEnabled ?? configuration.AppSettings.GuardModeEnabledByDefault,
            "Service",
            IsRemote: true);

        _logger.Info(
            "service.status.updated",
            new
            {
                result.State,
                result.RiskLevel,
                result.RestartPending,
                result.HasAmbiguousSignals,
                guardModeEnabled = _currentStatus.GuardModeEnabled
            });

        await _healthReporter.RecordScanAsync(
            _currentStatus,
            configuration.AppSettings.ScanIntervalSeconds,
            cancellationToken);

        return _currentStatus;
    }
}
