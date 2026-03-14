using SessionGuard.App.ViewModels;
using SessionGuard.Core.Configuration;
using SessionGuard.Core.Models;
using SessionGuard.Core.Services;
using SessionGuard.Infrastructure.Environment;

namespace SessionGuard.Tests;

public sealed class MainWindowViewModelWorkflowTests
{
    [Fact]
    public async Task RefreshAsync_UsesOpenElevatedControls_WhenServiceNeedsWriteAccess()
    {
        var status = CreateStatus(
            DateTimeOffset.Parse("2026-03-14T10:00:00-04:00"),
            isRemote: true,
            canPerformServiceWrites: false,
            state: RestartStateCategory.RestartPending,
            riskLevel: RestartRiskLevel.Elevated,
            policy: new PolicyEvaluation(
                PolicyDecisionType.ApprovalRequired,
                HasBlockingRules: false,
                RequiresApproval: true,
                ApprovalActive: false,
                ApprovalExpiresAt: null,
                RecommendedApprovalWindowMinutes: 45,
                "Approval is required before restart.",
                Array.Empty<PolicyRuleMatch>(),
                Array.Empty<string>()));

        using var viewModel = CreateViewModel(status);

        await viewModel.RefreshAsync();

        Assert.Equal("Needs elevated window", viewModel.AdminAccessText);
        Assert.True(viewModel.ShowOpenElevatedSessionAction);
        Assert.True(viewModel.RecommendedActionVisible);
        Assert.Equal("Open elevated controls", viewModel.RecommendedActionButtonText);
        Assert.Equal("Open elevated controls", viewModel.TrayPrimaryActionText);
        Assert.True(viewModel.TrayPrimaryActionVisible);
        Assert.Contains("elevated SessionGuard window", viewModel.ServiceModeSummaryText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefreshAsync_UsesManualReviewAction_WhenServiceIsOffline()
    {
        var status = CreateStatus(
            DateTimeOffset.Parse("2026-03-14T10:05:00-04:00"),
            isRemote: false,
            canPerformServiceWrites: false,
            state: RestartStateCategory.UnknownLimitedVisibility,
            riskLevel: RestartRiskLevel.Elevated,
            policy: new PolicyEvaluation(
                PolicyDecisionType.RestartBlocked,
                HasBlockingRules: true,
                RequiresApproval: false,
                ApprovalActive: false,
                ApprovalExpiresAt: null,
                RecommendedApprovalWindowMinutes: 45,
                "Restart is blocked while the service is offline.",
                Array.Empty<PolicyRuleMatch>(),
                Array.Empty<string>()),
            hasAmbiguousSignals: true,
            limitedVisibility: true);

        using var viewModel = CreateViewModel(status);

        await viewModel.RefreshAsync();

        Assert.Equal("Service unavailable", viewModel.AdminAccessText);
        Assert.False(viewModel.ShowOpenElevatedSessionAction);
        Assert.True(viewModel.RecommendedActionVisible);
        Assert.Equal("Windows Update options", viewModel.RecommendedActionButtonText);
        Assert.Equal("Windows Update options", viewModel.TrayPrimaryActionText);
        Assert.True(viewModel.TrayPrimaryActionVisible);
    }

    [Fact]
    public async Task RefreshAsync_KeepsTrayPrimaryActionHidden_ForCalmReadOnlyServiceSession()
    {
        var status = CreateStatus(
            DateTimeOffset.Parse("2026-03-14T10:10:00-04:00"),
            isRemote: true,
            canPerformServiceWrites: false,
            state: RestartStateCategory.Safe,
            riskLevel: RestartRiskLevel.Low,
            policy: PolicyEvaluation.None);

        using var viewModel = CreateViewModel(status);

        await viewModel.RefreshAsync();

        Assert.Equal("Needs elevated window", viewModel.AdminAccessText);
        Assert.False(viewModel.RecommendedActionVisible);
        Assert.True(viewModel.ShowOpenElevatedSessionAction);
        Assert.False(viewModel.TrayPrimaryActionVisible);
        Assert.Equal(string.Empty, viewModel.TrayPrimaryActionText);
        Assert.Equal("Next: keep working. No action is needed.", viewModel.TrayNextStepText);
    }

    [Fact]
    public async Task RefreshAsync_ShowsAmbiguousRestartText_WhenProtectedStateDoesNotHaveDefinitiveRestart()
    {
        var timestamp = DateTimeOffset.Parse("2026-03-14T10:12:00-04:00");
        var status = new SessionControlStatus(
            new SessionScanResult(
                timestamp,
                RestartStateCategory.ProtectedSessionActive,
                RestartRiskLevel.High,
                ProtectionMode.PolicyGuardActive,
                RestartPending: false,
                HasAmbiguousSignals: true,
                ProtectedSessionActive: true,
                LimitedVisibility: false,
                IsElevated: false,
                Summary: "Active work is present while restart clues still need interpretation.",
                new WorkspaceStateSnapshot(
                    timestamp,
                    HasRisk: true,
                    WorkspaceRiskSeverity.High,
                    WorkspaceConfidence.High,
                    "Workspace-risk heuristics flagged active work.",
                    new[]
                    {
                        new WorkspaceRiskItem(
                            "Terminal and shell sessions",
                            WorkspaceCategory.TerminalShell,
                            WorkspaceRiskSeverity.High,
                            WorkspaceConfidence.High,
                            1,
                            "Interactive shell detected.",
                            new[] { "WindowsTerminal.exe" })
                    }),
                PolicyEvaluation.None,
                new RestartSignalOverview(1, 1, 0, 1, 0, 1, 0, "1 restart-related signal needs interpretation, but no definitive pending reboot was confirmed."),
                new[]
                {
                    new RestartIndicator(
                        "Registry restart signals",
                        "Pending file rename operations",
                        RestartIndicatorCategory.PendingRestart,
                        true,
                        "Pending file rename operations were detected. This can be noisy and does not confirm a restart requirement by itself.",
                        SignalConfidence.Low)
                },
                Array.Empty<ProtectedProcessMatch>(),
                Array.Empty<ManagedMitigationState>(),
                new[] { "Review the technical details before treating this as a confirmed restart." }),
            GuardModeEnabled: true,
            "Service",
            IsRemote: true,
            CanPerformServiceWrites: false);

        using var viewModel = CreateViewModel(status);

        await viewModel.RefreshAsync();

        Assert.Equal("Ambiguous / review signals", viewModel.PendingRestartText);
        Assert.Equal("Protected Session Active", viewModel.CurrentStatusText);
        Assert.False(viewModel.TrayPrimaryActionVisible);
        Assert.Equal(string.Empty, viewModel.TrayPrimaryActionText);
    }

    private static MainWindowViewModel CreateViewModel(SessionControlStatus status)
    {
        return new MainWindowViewModel(
            new FakeControlPlane(status),
            new FakeConfigurationRepository(),
            new FakeLogger(),
            RuntimePaths.Discover(AppContext.BaseDirectory),
            forceStartMinimized: false,
            forceTechnicalView: false,
            trayShellEnabled: true);
    }

    private static SessionControlStatus CreateStatus(
        DateTimeOffset timestamp,
        bool isRemote,
        bool canPerformServiceWrites,
        RestartStateCategory state,
        RestartRiskLevel riskLevel,
        PolicyEvaluation policy,
        bool hasAmbiguousSignals = false,
        bool limitedVisibility = false)
    {
        return new SessionControlStatus(
            new SessionScanResult(
                timestamp,
                state,
                riskLevel,
                state == RestartStateCategory.Safe ? ProtectionMode.GuardModeActive : ProtectionMode.PolicyGuardActive,
                RestartPending: state is RestartStateCategory.RestartPending or RestartStateCategory.ProtectedSessionActive,
                HasAmbiguousSignals: hasAmbiguousSignals,
                ProtectedSessionActive: state == RestartStateCategory.ProtectedSessionActive,
                LimitedVisibility: limitedVisibility,
                IsElevated: false,
                Summary: "Test summary",
                WorkspaceStateSnapshot.None(timestamp),
                policy,
                new RestartSignalOverview(1, 0, 0, 0, 0, 1, 0, "No restart pending."),
                Array.Empty<RestartIndicator>(),
                Array.Empty<ProtectedProcessMatch>(),
                Array.Empty<ManagedMitigationState>(),
                new[] { "Test recommendation." }),
            GuardModeEnabled: true,
            isRemote ? "Service" : "Local fallback",
            IsRemote: isRemote,
            CanPerformServiceWrites: canPerformServiceWrites);
    }

    private sealed class FakeControlPlane(SessionControlStatus status) : ISessionGuardControlPlane
    {
        public Task<SessionControlStatus> GetStatusAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(status);

        public Task<SessionControlStatus> ScanNowAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(status);

        public Task<SessionControlStatus> SetGuardModeAsync(bool enabled, CancellationToken cancellationToken = default)
            => Task.FromResult(status with { GuardModeEnabled = enabled });

        public Task<MitigationCommandResult> ApplyRecommendedAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new MitigationCommandResult(true, false, false, "Applied.", Array.Empty<ManagedMitigationState>()));

        public Task<MitigationCommandResult> ResetManagedAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new MitigationCommandResult(true, false, false, "Reset.", Array.Empty<ManagedMitigationState>()));

        public Task<PolicyApprovalCommandResult> GrantRestartApprovalAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new PolicyApprovalCommandResult(true, false, false, "Approved.", status.ScanResult.Policy));

        public Task<PolicyApprovalCommandResult> ClearRestartApprovalAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new PolicyApprovalCommandResult(true, false, false, "Cleared.", status.ScanResult.Policy));
    }

    private sealed class FakeConfigurationRepository : IConfigurationRepository
    {
        private readonly RuntimeConfiguration _configuration = new(
            new AppSettings
            {
                ScanIntervalSeconds = 300,
                GuardModeEnabledByDefault = true,
                UiPreferences = new UiPreferences
                {
                    StartMinimized = false,
                    ShowDetailedSignals = false
                },
                WarningBehavior = new WarningBehaviorOptions
                {
                    RaiseWindowOnHighRisk = false,
                    ShowDesktopNotifications = false,
                    ApprovalExpiryWarningLeadMinutes = 5
                }
            }.Normalize(),
            new ProtectedProcessCatalog
            {
                ProcessNames = Array.Empty<string>()
            }.Normalize(),
            new PolicyConfiguration
            {
                Rules = Array.Empty<PolicyRuleDefinition>()
            }.Normalize(),
            AppContext.BaseDirectory,
            AppContext.BaseDirectory,
            Path.Combine(AppContext.BaseDirectory, "appsettings.json"),
            Path.Combine(AppContext.BaseDirectory, "protected-processes.json"),
            Path.Combine(AppContext.BaseDirectory, "policies.json"));

        public string ConfigurationDirectory => _configuration.ConfigurationDirectory;

        public Task<RuntimeConfiguration> LoadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_configuration);
    }

    private sealed class FakeLogger : IAppLogger
    {
        public string LogDirectory => AppContext.BaseDirectory;

        public void Info(string message, object? context = null)
        {
        }

        public void Warn(string message, object? context = null)
        {
        }

        public void Error(string message, Exception exception, object? context = null)
        {
        }
    }
}
