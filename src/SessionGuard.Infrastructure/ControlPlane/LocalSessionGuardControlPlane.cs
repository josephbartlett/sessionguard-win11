using SessionGuard.Core.Models;
using SessionGuard.Core.Services;

namespace SessionGuard.Infrastructure.ControlPlane;

public sealed class LocalSessionGuardControlPlane : ISessionGuardControlPlane
{
    private readonly SessionGuardCoordinator _coordinator;
    private readonly IConfigurationRepository _configurationRepository;
    private readonly IMitigationService _mitigationService;
    private readonly IPolicyApprovalStore _policyApprovalStore;
    private readonly IScanSnapshotStore _snapshotStore;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private bool? _guardModeEnabled;

    public LocalSessionGuardControlPlane(
        SessionGuardCoordinator coordinator,
        IConfigurationRepository configurationRepository,
        IMitigationService mitigationService,
        IPolicyApprovalStore policyApprovalStore,
        IScanSnapshotStore snapshotStore)
    {
        _coordinator = coordinator;
        _configurationRepository = configurationRepository;
        _mitigationService = mitigationService;
        _policyApprovalStore = policyApprovalStore;
        _snapshotStore = snapshotStore;
    }

    public async Task<SessionControlStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        return await ScanInternalAsync(forceReloadGuardMode: false, explicitGuardMode: null, cancellationToken);
    }

    public async Task<SessionControlStatus> ScanNowAsync(CancellationToken cancellationToken = default)
    {
        return await ScanInternalAsync(forceReloadGuardMode: false, explicitGuardMode: null, cancellationToken);
    }

    public async Task<SessionControlStatus> SetGuardModeAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        return await ScanInternalAsync(forceReloadGuardMode: false, explicitGuardMode: enabled, cancellationToken);
    }

    public async Task<MitigationCommandResult> ApplyRecommendedAsync(CancellationToken cancellationToken = default)
    {
        var configuration = await _configurationRepository.LoadAsync(cancellationToken);
        var states = await _mitigationService.GetStatesAsync(configuration, cancellationToken);
        return new MitigationCommandResult(
            Success: false,
            RequiresElevation: false,
            RequiresService: true,
            "Managed mitigation changes are service-owned. Start the SessionGuard service and reconnect before applying settings.",
            states);
    }

    public async Task<MitigationCommandResult> ResetManagedAsync(CancellationToken cancellationToken = default)
    {
        var configuration = await _configurationRepository.LoadAsync(cancellationToken);
        var states = await _mitigationService.GetStatesAsync(configuration, cancellationToken);
        return new MitigationCommandResult(
            Success: false,
            RequiresElevation: false,
            RequiresService: true,
            "Managed mitigation reset is service-owned. Start the SessionGuard service and reconnect before changing settings.",
            states);
    }

    public async Task<PolicyApprovalCommandResult> GrantRestartApprovalAsync(CancellationToken cancellationToken = default)
    {
        var status = await ScanInternalAsync(forceReloadGuardMode: false, explicitGuardMode: null, cancellationToken);
        return new PolicyApprovalCommandResult(
            Success: false,
            RequiresService: true,
            "Restart approval changes are service-owned. Start the SessionGuard service and reconnect before granting approval.",
            status.ScanResult.Policy);
    }

    public async Task<PolicyApprovalCommandResult> ClearRestartApprovalAsync(CancellationToken cancellationToken = default)
    {
        var status = await ScanInternalAsync(forceReloadGuardMode: false, explicitGuardMode: null, cancellationToken);
        return new PolicyApprovalCommandResult(
            Success: false,
            RequiresService: true,
            "Restart approval changes are service-owned. Start the SessionGuard service and reconnect before clearing approval.",
            status.ScanResult.Policy);
    }

    private async Task<SessionControlStatus> ScanInternalAsync(
        bool forceReloadGuardMode,
        bool? explicitGuardMode,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            var configuration = await _configurationRepository.LoadAsync(cancellationToken);
            return await ScanLockedAsync(configuration, forceReloadGuardMode, explicitGuardMode, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<SessionControlStatus> ScanLockedAsync(
        Core.Configuration.RuntimeConfiguration configuration,
        bool forceReloadGuardMode,
        bool? explicitGuardMode,
        CancellationToken cancellationToken)
    {
        if (_guardModeEnabled is null || forceReloadGuardMode)
        {
            _guardModeEnabled = configuration.AppSettings.GuardModeEnabledByDefault;
        }

        if (explicitGuardMode.HasValue)
        {
            _guardModeEnabled = explicitGuardMode.Value;
        }

        var result = await _coordinator.ScanAsync(_guardModeEnabled ?? configuration.AppSettings.GuardModeEnabledByDefault, cancellationToken);
        await _snapshotStore.PersistAsync(result, cancellationToken);

        return new SessionControlStatus(
            result,
            _guardModeEnabled ?? configuration.AppSettings.GuardModeEnabledByDefault,
            "Local fallback",
            IsRemote: false);
    }
}
