using SessionGuard.Core.Models;

namespace SessionGuard.Core.Services;

public static class RestartStatusEvaluator
{
    public static StatusEvaluation Evaluate(
        IReadOnlyList<RestartIndicator> indicators,
        IReadOnlyList<ProtectedProcessMatch> protectedProcesses,
        IReadOnlyList<ManagedMitigationState> mitigations)
    {
        var restartPending = indicators.Any(indicator => indicator.IsActive);
        var protectedSessionActive = protectedProcesses.Count > 0;
        var limitedVisibility = indicators.Any(indicator => indicator.LimitedVisibility);
        var mitigated = mitigations.Any(mitigation => mitigation.IsApplied);

        if (restartPending && protectedSessionActive)
        {
            return new StatusEvaluation(
                RestartStateCategory.ProtectedSessionActive,
                RestartRiskLevel.High,
                "Pending restart indicators are active while protected tools are running.");
        }

        if (protectedSessionActive)
        {
            return new StatusEvaluation(
                RestartStateCategory.ProtectedSessionActive,
                RestartRiskLevel.Elevated,
                "Protected tools are active. A restart would still be disruptive even without a confirmed pending reboot.");
        }

        if (restartPending)
        {
            return new StatusEvaluation(
                RestartStateCategory.RestartPending,
                RestartRiskLevel.Elevated,
                "Windows restart indicators were detected. Review mitigation settings before leaving the machine unattended.");
        }

        if (mitigated)
        {
            return new StatusEvaluation(
                RestartStateCategory.MitigatedDeferred,
                RestartRiskLevel.Low,
                "Recommended native restart mitigations are already applied.");
        }

        if (limitedVisibility)
        {
            return new StatusEvaluation(
                RestartStateCategory.UnknownLimitedVisibility,
                RestartRiskLevel.Unknown,
                "Signal coverage is incomplete. Treat the result as advisory rather than definitive.");
        }

        return new StatusEvaluation(
            RestartStateCategory.Safe,
            RestartRiskLevel.Low,
            "No protected processes or pending restart indicators were detected during the latest scan.");
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
}
