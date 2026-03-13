using SessionGuard.Core.Automation;
using SessionGuard.Core.Models;
using SessionGuard.Core.Services;

namespace SessionGuard.App.Automation;

internal sealed class ScenarioSessionGuardControlPlane : ISessionGuardControlPlane
{
    private readonly UiSmokeScenario _scenario;
    private bool _guardModeEnabled;

    public ScenarioSessionGuardControlPlane(string scenarioName)
    {
        _scenario = UiSmokeScenarioCatalog.Get(scenarioName);
        _guardModeEnabled = _scenario.Status.GuardModeEnabled;
    }

    public Task<SessionControlStatus> GetStatusAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(BuildStatus());

    public Task<SessionControlStatus> ScanNowAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(BuildStatus());

    public Task<SessionControlStatus> SetGuardModeAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        _guardModeEnabled = enabled;
        return Task.FromResult(BuildStatus());
    }

    public Task<MitigationCommandResult> ApplyRecommendedAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new MitigationCommandResult(
            Success: false,
            RequiresElevation: false,
            RequiresService: false,
            "UI smoke scenarios are read-only. Mitigation changes are disabled in scenario mode.",
            _scenario.Status.ScanResult.Mitigations));
    }

    public Task<MitigationCommandResult> ResetManagedAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new MitigationCommandResult(
            Success: false,
            RequiresElevation: false,
            RequiresService: false,
            "UI smoke scenarios are read-only. Managed setting reset is disabled in scenario mode.",
            _scenario.Status.ScanResult.Mitigations));
    }

    public Task<PolicyApprovalCommandResult> GrantRestartApprovalAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new PolicyApprovalCommandResult(
            Success: false,
            RequiresService: false,
            RequiresElevation: false,
            "UI smoke scenarios are read-only. Policy approval is disabled in scenario mode.",
            _scenario.Status.ScanResult.Policy));
    }

    public Task<PolicyApprovalCommandResult> ClearRestartApprovalAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new PolicyApprovalCommandResult(
            Success: false,
            RequiresService: false,
            RequiresElevation: false,
            "UI smoke scenarios are read-only. Policy approval is disabled in scenario mode.",
            _scenario.Status.ScanResult.Policy));
    }

    private SessionControlStatus BuildStatus()
    {
        return _scenario.Status with { GuardModeEnabled = _guardModeEnabled };
    }
}
