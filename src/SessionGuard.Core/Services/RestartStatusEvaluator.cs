using SessionGuard.Core.Models;

namespace SessionGuard.Core.Services;

public static class RestartStatusEvaluator
{
    public static RestartSignalOverview BuildOverview(IReadOnlyList<RestartIndicator> indicators)
    {
        var definitivePendingSignals = indicators.Count(IsDefinitivePendingSignal);
        var ambiguousSignals = indicators.Count(IsAmbiguousSignal);
        var limitedVisibilityIndicators = indicators.Count(indicator => indicator.LimitedVisibility);
        var providersWithLimitedVisibility = indicators
            .Where(indicator => indicator.LimitedVisibility)
            .Select(indicator => indicator.Provider)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        var summary = definitivePendingSignals > 0
            ? $"{definitivePendingSignals} definitive pending-restart signal(s) detected across {indicators.Select(indicator => indicator.Provider).Distinct(StringComparer.OrdinalIgnoreCase).Count()} provider(s)."
            : ambiguousSignals > 0
                ? $"{ambiguousSignals} restart-related signal(s) need interpretation, but no definitive pending reboot was confirmed."
                : limitedVisibilityIndicators > 0
                    ? $"No restart indicators were confirmed, but {limitedVisibilityIndicators} signal(s) had limited visibility."
                    : "No restart or orchestration activity was detected by the configured providers.";

        return new RestartSignalOverview(
            indicators.Count,
            indicators.Count(indicator => indicator.IsActive),
            definitivePendingSignals,
            ambiguousSignals,
            limitedVisibilityIndicators,
            indicators.Select(indicator => indicator.Provider).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            providersWithLimitedVisibility,
            summary);
    }

    public static StatusEvaluation Evaluate(
        IReadOnlyList<RestartIndicator> indicators,
        WorkspaceStateSnapshot workspace,
        IReadOnlyList<ManagedMitigationState> mitigations)
    {
        var restartPending = indicators.Any(IsDefinitivePendingSignal);
        var hasAmbiguousSignals = indicators.Any(IsAmbiguousSignal);
        var protectedSessionActive = workspace.HasRisk;
        var limitedVisibility = indicators.Any(indicator => indicator.LimitedVisibility);
        var mitigated = mitigations.Any(mitigation => mitigation.IsApplied);

        if (restartPending && protectedSessionActive)
        {
            return new StatusEvaluation(
                RestartStateCategory.ProtectedSessionActive,
                RestartRiskLevel.High,
                $"Definitive pending reboot signals are active while workspace-risk heuristics report active sessions. {workspace.Summary}",
                hasAmbiguousSignals);
        }

        if (hasAmbiguousSignals && protectedSessionActive)
        {
            return new StatusEvaluation(
                RestartStateCategory.ProtectedSessionActive,
                RestartRiskLevel.High,
                $"Workspace-risk heuristics report active sessions while Windows Update orchestration activity is visible, but the app cannot confirm a definitive pending reboot yet. {workspace.Summary}",
                hasAmbiguousSignals);
        }

        if (protectedSessionActive)
        {
            return new StatusEvaluation(
                RestartStateCategory.ProtectedSessionActive,
                RestartRiskLevel.Elevated,
                $"Workspace-risk heuristics report active sessions. A restart would still be disruptive even without a confirmed pending reboot. {workspace.Summary}",
                hasAmbiguousSignals);
        }

        if (restartPending)
        {
            return new StatusEvaluation(
                RestartStateCategory.RestartPending,
                RestartRiskLevel.Elevated,
                "Windows restart indicators were detected. Review mitigation settings before leaving the machine unattended.",
                hasAmbiguousSignals);
        }

        if (hasAmbiguousSignals)
        {
            return new StatusEvaluation(
                RestartStateCategory.UnknownLimitedVisibility,
                RestartRiskLevel.Elevated,
                "Windows Update orchestration activity or low-confidence restart clues were detected, but a definitive pending reboot was not confirmed.",
                hasAmbiguousSignals);
        }

        if (mitigated)
        {
            return new StatusEvaluation(
                RestartStateCategory.MitigatedDeferred,
                RestartRiskLevel.Low,
                "Recommended native restart mitigations are already applied.",
                hasAmbiguousSignals);
        }

        if (limitedVisibility)
        {
            return new StatusEvaluation(
                RestartStateCategory.UnknownLimitedVisibility,
                RestartRiskLevel.Unknown,
                "Signal coverage is incomplete. Treat the result as advisory rather than definitive.",
                hasAmbiguousSignals);
        }

        return new StatusEvaluation(
            RestartStateCategory.Safe,
            RestartRiskLevel.Low,
            "No protected processes or pending restart indicators were detected during the latest scan.",
            hasAmbiguousSignals);
    }

    public static ProtectionMode DetermineProtectionMode(
        bool guardModeEnabled,
        bool mitigationsApplied,
        bool isElevated)
    {
        if (mitigationsApplied)
        {
            return ProtectionMode.ManagedMitigationsApplied;
        }

        if (guardModeEnabled)
        {
            return ProtectionMode.GuardModeActive;
        }

        return isElevated ? ProtectionMode.MonitorOnly : ProtectionMode.LimitedReadOnly;
    }

    private static bool IsDefinitivePendingSignal(RestartIndicator indicator)
    {
        return indicator.IsActive &&
               indicator.Category == RestartIndicatorCategory.PendingRestart &&
               indicator.Confidence is SignalConfidence.Medium or SignalConfidence.High;
    }

    private static bool IsAmbiguousSignal(RestartIndicator indicator)
    {
        if (!indicator.IsActive)
        {
            return false;
        }

        return indicator.Category == RestartIndicatorCategory.UpdateOrchestration ||
               (indicator.Category == RestartIndicatorCategory.PendingRestart &&
                indicator.Confidence == SignalConfidence.Low);
    }
}
