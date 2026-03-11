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
            new RestartIndicator("Windows Update reboot required", true, "Reboot required", SignalConfidence.High)
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
}
