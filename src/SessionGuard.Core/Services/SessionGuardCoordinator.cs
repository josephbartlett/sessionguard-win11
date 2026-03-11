using SessionGuard.Core.Models;

namespace SessionGuard.Core.Services;

public sealed class SessionGuardCoordinator
{
    private readonly IConfigurationRepository _configurationRepository;
    private readonly IProtectedWorkspaceDetector _workspaceDetector;
    private readonly IReadOnlyList<IRestartSignalProvider> _signalProviders;
    private readonly IMitigationService _mitigationService;
    private readonly IAppLogger _logger;

    public SessionGuardCoordinator(
        IConfigurationRepository configurationRepository,
        IProtectedWorkspaceDetector workspaceDetector,
        IEnumerable<IRestartSignalProvider> signalProviders,
        IMitigationService mitigationService,
        IAppLogger logger)
    {
        _configurationRepository = configurationRepository;
        _workspaceDetector = workspaceDetector;
        _signalProviders = signalProviders.ToArray();
        _mitigationService = mitigationService;
        _logger = logger;
    }

    public async Task<SessionScanResult> ScanAsync(
        bool guardModeEnabled,
        CancellationToken cancellationToken = default)
    {
        _logger.Info("scan.start", new { guardModeEnabled });

        var configuration = await _configurationRepository.LoadAsync(cancellationToken);
        var protectedProcesses = await _workspaceDetector.GetActiveMatchesAsync(
            configuration.ProtectedProcesses.ProcessNames,
            cancellationToken);

        var indicators = new List<RestartIndicator>();
        foreach (var provider in _signalProviders)
        {
            try
            {
                var providerIndicators = await provider.GetIndicatorsAsync(cancellationToken);
                indicators.AddRange(providerIndicators);
            }
            catch (Exception exception)
            {
                _logger.Warn("scan.signal_provider_failed", new { provider = provider.Name, exception = exception.Message });
                indicators.Add(new RestartIndicator(
                    provider.Name,
                    false,
                    $"{provider.Name} failed: {exception.Message}",
                    SignalConfidence.Low,
                    LimitedVisibility: true));
            }
        }

        var mitigationStates = await _mitigationService.GetStatesAsync(configuration, cancellationToken);
        var evaluation = RestartStatusEvaluator.Evaluate(indicators, protectedProcesses, mitigationStates);
        var protectionMode = RestartStatusEvaluator.DetermineProtectionMode(
            guardModeEnabled,
            mitigationStates.Any(state => state.IsApplied),
            _mitigationService.IsElevated);

        var result = new SessionScanResult(
            DateTimeOffset.Now,
            evaluation.State,
            evaluation.RiskLevel,
            protectionMode,
            indicators.Any(indicator => indicator.IsActive),
            protectedProcesses.Count > 0,
            indicators.Any(indicator => indicator.LimitedVisibility),
            _mitigationService.IsElevated,
            evaluation.Summary,
            indicators,
            protectedProcesses,
            mitigationStates,
            BuildRecommendations(indicators, protectedProcesses, mitigationStates, _mitigationService.IsElevated));

        _logger.Info(
            "scan.finish",
            new
            {
                result.State,
                result.RiskLevel,
                result.RestartPending,
                protectedProcessCount = result.ProtectedProcesses.Count
            });

        return result;
    }

    private static IReadOnlyList<string> BuildRecommendations(
        IReadOnlyList<RestartIndicator> indicators,
        IReadOnlyList<ProtectedProcessMatch> protectedProcesses,
        IReadOnlyList<ManagedMitigationState> mitigationStates,
        bool isElevated)
    {
        var recommendations = new List<string>();
        var restartPending = indicators.Any(indicator => indicator.IsActive);
        var protectedSessionActive = protectedProcesses.Count > 0;
        var mitigationsApplied = mitigationStates.Any(state => state.IsApplied);
        var limitedVisibility = indicators.Any(indicator => indicator.LimitedVisibility);

        if (restartPending && protectedSessionActive)
        {
            recommendations.Add("Keep critical terminals, editors, and browsers open only if you can supervise the machine; otherwise save work and schedule a manual restart.");
        }

        if (protectedSessionActive)
        {
            recommendations.Add("Protected processes are active. SessionGuard is surfacing risk only; it does not snapshot or recover workspace state in this MVP.");
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
            recommendations.Add("Some signals had limited visibility. Treat the dashboard as a best-effort monitor, not a guarantee.");
        }

        if (recommendations.Count == 0)
        {
            recommendations.Add("No immediate action is required. Keep guard mode enabled if you want the app to keep surfacing restart risk.");
        }

        return recommendations;
    }
}
