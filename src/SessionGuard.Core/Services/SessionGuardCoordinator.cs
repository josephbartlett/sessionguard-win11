using SessionGuard.Core.Models;

namespace SessionGuard.Core.Services;

public sealed class SessionGuardCoordinator
{
    private readonly IConfigurationRepository _configurationRepository;
    private readonly IProtectedWorkspaceDetector _workspaceDetector;
    private readonly IReadOnlyList<IRestartSignalProvider> _signalProviders;
    private readonly IMitigationService _mitigationService;
    private readonly IPolicyApprovalStore _policyApprovalStore;
    private readonly IAppLogger _logger;

    public SessionGuardCoordinator(
        IConfigurationRepository configurationRepository,
        IProtectedWorkspaceDetector workspaceDetector,
        IEnumerable<IRestartSignalProvider> signalProviders,
        IMitigationService mitigationService,
        IPolicyApprovalStore policyApprovalStore,
        IAppLogger logger)
    {
        _configurationRepository = configurationRepository;
        _workspaceDetector = workspaceDetector;
        _signalProviders = signalProviders.ToArray();
        _mitigationService = mitigationService;
        _policyApprovalStore = policyApprovalStore;
        _logger = logger;
    }

    public async Task<SessionScanResult> ScanAsync(
        bool guardModeEnabled,
        CancellationToken cancellationToken = default)
    {
        _logger.Info("scan.start", new { guardModeEnabled });

        var configuration = await _configurationRepository.LoadAsync(cancellationToken);
        var timestamp = DateTimeOffset.Now;
        var approvalState = await _policyApprovalStore.GetCurrentAsync(timestamp, cancellationToken);
        var workspaceObservation = await _workspaceDetector.GetWorkspaceObservationAsync(
            configuration.ProtectedProcesses.ProcessNames,
            cancellationToken);
        var workspace = WorkspaceRiskAnalyzer.Analyze(workspaceObservation, timestamp);
        var protectedProcesses = workspaceObservation.ProtectedProcesses;

        var indicators = new List<RestartIndicator>();
        foreach (var provider in _signalProviders)
        {
            try
            {
                var providerIndicators = await provider.GetIndicatorsAsync(cancellationToken);
                indicators.AddRange(providerIndicators);
                _logger.Info(
                    "scan.signal_provider.finish",
                    new
                    {
                        provider = provider.Name,
                        totalSignals = providerIndicators.Count,
                        activeSignals = providerIndicators.Count(indicator => indicator.IsActive),
                        limitedVisibilitySignals = providerIndicators.Count(indicator => indicator.LimitedVisibility)
                    });
            }
            catch (Exception exception)
            {
                _logger.Warn("scan.signal_provider_failed", new { provider = provider.Name, exception = exception.Message });
                indicators.Add(new RestartIndicator(
                    provider.Name,
                    provider.Name,
                    RestartIndicatorCategory.UpdateOrchestration,
                    false,
                    $"{provider.Name} failed: {exception.Message}",
                    SignalConfidence.Low,
                    LimitedVisibility: true));
            }
        }

        var mitigationStates = await _mitigationService.GetStatesAsync(configuration, cancellationToken);
        var signalOverview = RestartStatusEvaluator.BuildOverview(indicators);
        var evaluation = RestartStatusEvaluator.Evaluate(indicators, workspace, mitigationStates);
        var policy = PolicyEvaluator.Evaluate(
            configuration.Policies,
            approvalState,
            new PolicyEvaluationContext(
                timestamp,
                evaluation.State,
                evaluation.RiskLevel,
                signalOverview.DefinitivePendingSignals > 0,
                workspace,
                protectedProcesses,
                workspaceObservation.RunningProcesses),
            configuration.PolicyValidation);
        var protectionMode = RestartStatusEvaluator.DetermineProtectionMode(
            guardModeEnabled,
            mitigationStates.Any(state => state.IsApplied),
            _mitigationService.IsElevated,
            policy);
        var summary = policy.Decision == PolicyDecisionType.None
            ? evaluation.Summary
            : $"{evaluation.Summary} {policy.Summary}";

        var result = new SessionScanResult(
            timestamp,
            evaluation.State,
            evaluation.RiskLevel,
            protectionMode,
            signalOverview.DefinitivePendingSignals > 0,
            evaluation.HasAmbiguousSignals,
            workspace.HasRisk,
            indicators.Any(indicator => indicator.LimitedVisibility),
            _mitigationService.IsElevated,
            summary,
            workspace,
            policy,
            signalOverview,
            indicators,
            protectedProcesses,
            mitigationStates,
            BuildRecommendations(signalOverview, workspace, mitigationStates, _mitigationService.IsElevated, policy));

        _logger.Info(
            "scan.finish",
            new
            {
                result.State,
                result.RiskLevel,
                result.RestartPending,
                result.HasAmbiguousSignals,
                signalOverview.DefinitivePendingSignals,
                signalOverview.AmbiguousSignals,
                protectedProcessCount = result.ProtectedProcesses.Count,
                workspaceRiskItems = result.Workspace.RiskItems.Count,
                workspaceHighestSeverity = result.Workspace.HighestSeverity,
                policyDecision = result.Policy.Decision,
                policyMatchedRules = result.Policy.MatchedRules.Count
            });

        return result;
    }

    private static IReadOnlyList<string> BuildRecommendations(
        RestartSignalOverview signalOverview,
        WorkspaceStateSnapshot workspace,
        IReadOnlyList<ManagedMitigationState> mitigationStates,
        bool isElevated,
        PolicyEvaluation policy)
    {
        var recommendations = new List<string>();
        var restartPending = signalOverview.DefinitivePendingSignals > 0;
        var hasAmbiguousSignals = signalOverview.AmbiguousSignals > 0;
        var protectedSessionActive = workspace.HasRisk;
        var mitigationsApplied = mitigationStates.Any(state => state.IsApplied);
        var limitedVisibility = signalOverview.LimitedVisibilityIndicators > 0;

        if (restartPending && protectedSessionActive)
        {
            recommendations.Add("Keep critical terminals, editors, and browsers open only if you can supervise the machine; otherwise save work and schedule a manual restart.");
        }

        if (hasAmbiguousSignals)
        {
            recommendations.Add("SessionGuard detected Windows Update orchestration activity or low-confidence restart clues. Review the indicator table before assuming the machine is clear.");
        }

        if (protectedSessionActive)
        {
            recommendations.Add("Workspace-risk heuristics are active. SessionGuard is surfacing advisory risk and lightweight metadata only; it does not snapshot unsaved buffers or recover workspace state.");
        }

        if (workspace.RiskItems.Any(item => item.Category == WorkspaceCategory.LocalDevServer))
        {
            recommendations.Add("Local runtime processes suggest a dev server or long-running task may be active. Confirm whether you need to keep the machine supervised before stepping away.");
        }

        if (workspace.RiskItems.Any(item => item.Category == WorkspaceCategory.Browser))
        {
            recommendations.Add("Browser risk is inferred from running processes only. Review your own session persistence settings before relying on restart recovery.");
        }

        if (policy.HasBlockingRules)
        {
            recommendations.Add("Policy rules are actively blocking restart right now. Review the matched rules before approving or scheduling any restart.");
        }

        if (policy.Validation.HasErrors)
        {
            recommendations.Add("Policy configuration has errors and no policy rules are being enforced. Review config/policies.json before relying on approval or blocking behavior.");
        }
        else if (policy.Validation.HasWarnings)
        {
            recommendations.Add("Policy configuration loaded with warnings. Review the policy diagnostics section before relying on the current rule set.");
        }

        if (policy.RequiresApproval && !policy.ApprovalActive)
        {
            recommendations.Add($"Policy requires a supervised approval window before restart. Grant one from the dashboard to allow a manual restart for {policy.RecommendedApprovalWindowMinutes} minute(s).");
        }

        if (policy.ApprovalActive && policy.ApprovalExpiresAt.HasValue)
        {
            recommendations.Add($"A temporary restart approval window is active until {policy.ApprovalExpiresAt.Value.LocalDateTime:G}. Blocking rules still win if they are active.");
        }

        if (!mitigationsApplied)
        {
            recommendations.Add("Apply the recommended native mitigations or review Windows Update options to reduce surprise restart behavior without disabling updates.");
        }

        if (!isElevated)
        {
            recommendations.Add("Run SessionGuard as administrator to apply or reset native mitigation settings. Non-elevated mode stays read-only.");
        }

        if (limitedVisibility)
        {
            recommendations.Add("Some providers had limited visibility. Treat the dashboard as a best-effort monitor, not a guarantee.");
        }

        if (recommendations.Count == 0)
        {
            recommendations.Add("No immediate action is required. Keep guard mode enabled if you want the app to keep surfacing restart risk.");
        }

        return recommendations;
    }
}
