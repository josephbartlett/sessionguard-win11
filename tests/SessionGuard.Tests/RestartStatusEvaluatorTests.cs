using SessionGuard.Core.Models;
using SessionGuard.Core.Services;

namespace SessionGuard.Tests;

public sealed class RestartStatusEvaluatorTests
{
    [Fact]
    public void Evaluate_ReturnsProtectedSessionActive_WhenPendingRestartAndProtectedProcessesExist()
    {
        var indicators = new[]
        {
            new RestartIndicator(
                "Windows Update Agent",
                "Windows Update reboot required",
                RestartIndicatorCategory.PendingRestart,
                true,
                "Reboot required",
                SignalConfidence.High)
        };
        var protectedProcesses = new[]
        {
            new ProtectedProcessMatch("WindowsTerminal.exe", 1)
        };

        var evaluation = RestartStatusEvaluator.Evaluate(indicators, protectedProcesses, Array.Empty<ManagedMitigationState>());

        Assert.Equal(RestartStateCategory.ProtectedSessionActive, evaluation.State);
        Assert.Equal(RestartRiskLevel.High, evaluation.RiskLevel);
    }

    [Fact]
    public void Evaluate_ReturnsMitigatedDeferred_WhenMitigationsAppliedAndNoRiskSignals()
    {
        var mitigations = new[]
        {
            new ManagedMitigationState(
                "no-auto-reboot-logged-on-users",
                "No auto-restart with signed-in users",
                "desc",
                true,
                true,
                "1",
                "1",
                @"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU\NoAutoRebootWithLoggedOnUsers")
        };

        var evaluation = RestartStatusEvaluator.Evaluate(
            Array.Empty<RestartIndicator>(),
            Array.Empty<ProtectedProcessMatch>(),
            mitigations);

        Assert.Equal(RestartStateCategory.MitigatedDeferred, evaluation.State);
        Assert.Equal(RestartRiskLevel.Low, evaluation.RiskLevel);
    }

    [Fact]
    public void Evaluate_ReturnsUnknownLimitedVisibility_WhenOnlyAmbiguousSignalsExist()
    {
        var indicators = new[]
        {
            new RestartIndicator(
                "Windows Update UX settings",
                "Smart scheduler prediction",
                RestartIndicatorCategory.UpdateOrchestration,
                true,
                "Predicted maintenance window",
                SignalConfidence.Low)
        };

        var evaluation = RestartStatusEvaluator.Evaluate(
            indicators,
            Array.Empty<ProtectedProcessMatch>(),
            Array.Empty<ManagedMitigationState>());

        Assert.Equal(RestartStateCategory.UnknownLimitedVisibility, evaluation.State);
        Assert.Equal(RestartRiskLevel.Elevated, evaluation.RiskLevel);
        Assert.True(evaluation.HasAmbiguousSignals);
    }

    [Fact]
    public void BuildOverview_CountsProviderCoverageAndAmbiguousSignals()
    {
        var indicators = new[]
        {
            new RestartIndicator(
                "Windows Update Agent",
                "WUA reboot required",
                RestartIndicatorCategory.PendingRestart,
                false,
                "No reboot required",
                SignalConfidence.High),
            new RestartIndicator(
                "Windows Update UX settings",
                "Smart scheduler prediction",
                RestartIndicatorCategory.UpdateOrchestration,
                true,
                "Predicted maintenance window",
                SignalConfidence.Low),
            new RestartIndicator(
                "Registry restart signals",
                "CBS reboot pending",
                RestartIndicatorCategory.PendingRestart,
                false,
                "Could not read",
                SignalConfidence.Low,
                LimitedVisibility: true)
        };

        var overview = RestartStatusEvaluator.BuildOverview(indicators);

        Assert.Equal(3, overview.TotalIndicators);
        Assert.Equal(1, overview.ActiveIndicators);
        Assert.Equal(0, overview.DefinitivePendingSignals);
        Assert.Equal(1, overview.AmbiguousSignals);
        Assert.Equal(1, overview.LimitedVisibilityIndicators);
        Assert.Equal(3, overview.ProviderCount);
        Assert.Equal(1, overview.ProvidersWithLimitedVisibility);
    }
}
