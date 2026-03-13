using SessionGuard.Core.Models;
using SessionGuard.Core.Services;

namespace SessionGuard.Tests;

public sealed class OperatorAlertEvaluatorTests
{
    [Fact]
    public void Evaluate_ReturnsFallbackNotification_WhenServiceConnectionDrops()
    {
        var previous = new OperatorAlertContext(
            DateTimeOffset.Parse("2026-03-11T15:00:00-04:00"),
            IsRemote: true,
            RestartStateCategory.Safe,
            RestartRiskLevel.Low,
            PolicyDecisionType.None,
            ApprovalActive: false,
            ApprovalExpiresAt: null,
            PolicyValidationErrors: false);
        var current = CreateStatus(
            DateTimeOffset.Parse("2026-03-11T15:05:00-04:00"),
            isRemote: false,
            decision: PolicyDecisionType.RestartBlocked,
            approvalActive: false,
            approvalExpiresAt: null);

        var evaluation = OperatorAlertEvaluator.Evaluate(previous, current, approvalExpiryWarningLeadMinutes: 5);

        Assert.Contains(evaluation.Notifications, alert => alert.Title == "Service unavailable");
        Assert.Equal("Summary: Windows still looks restart-sensitive.", evaluation.Tray.SummaryLine);
        Assert.Equal("Next: reconnect the service or review Windows Update manually.", evaluation.Tray.NextStepLine);
        Assert.Equal("Mode: Local fallback read-only", evaluation.Tray.ModeLine);
    }

    [Fact]
    public void Evaluate_ReturnsElevatedNextStep_WhenServiceWritesNeedAdmin()
    {
        var current = CreateStatus(
            DateTimeOffset.Parse("2026-03-11T15:05:00-04:00"),
            isRemote: true,
            decision: PolicyDecisionType.ApprovalRequired,
            approvalActive: false,
            approvalExpiresAt: null,
            canPerformServiceWrites: false);

        var evaluation = OperatorAlertEvaluator.Evaluate(previous: null, status: current, approvalExpiryWarningLeadMinutes: 5);

        Assert.Equal("Next: reopen SessionGuard as administrator to change protections.", evaluation.Tray.NextStepLine);
        Assert.Equal("Context: service connected for monitoring, but write actions still need elevation.", evaluation.Tray.ContextLine);
    }

    [Fact]
    public void Evaluate_ReturnsApprovalNextStep_WhenWritesAreAvailable()
    {
        var current = CreateStatus(
            DateTimeOffset.Parse("2026-03-11T15:05:00-04:00"),
            isRemote: true,
            decision: PolicyDecisionType.ApprovalRequired,
            approvalActive: false,
            approvalExpiresAt: null,
            canPerformServiceWrites: true);

        var evaluation = OperatorAlertEvaluator.Evaluate(previous: null, status: current, approvalExpiryWarningLeadMinutes: 5);

        Assert.Equal("Next: grant a supervised approval window when ready.", evaluation.Tray.NextStepLine);
        Assert.Equal("Context: service connected and write actions are available.", evaluation.Tray.ContextLine);
    }

    [Fact]
    public void Evaluate_ReturnsApprovalExpiringSoonNotification()
    {
        var previous = new OperatorAlertContext(
            DateTimeOffset.Parse("2026-03-11T15:00:00-04:00"),
            IsRemote: true,
            RestartStateCategory.MitigatedDeferred,
            RestartRiskLevel.Low,
            PolicyDecisionType.ApprovalActive,
            ApprovalActive: true,
            ApprovalExpiresAt: DateTimeOffset.Parse("2026-03-11T15:20:00-04:00"),
            PolicyValidationErrors: false);
        var current = CreateStatus(
            DateTimeOffset.Parse("2026-03-11T15:16:00-04:00"),
            isRemote: true,
            decision: PolicyDecisionType.ApprovalActive,
            approvalActive: true,
            approvalExpiresAt: DateTimeOffset.Parse("2026-03-11T15:20:00-04:00"));

        var evaluation = OperatorAlertEvaluator.Evaluate(previous, current, approvalExpiryWarningLeadMinutes: 5);

        Assert.Contains(evaluation.Notifications, alert => alert.Title == "Approval expires soon");
        Assert.Contains("expires in 4 minute(s)", evaluation.PolicyTimingText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_ReturnsApprovalExpiredNotification_WhenApprovalEnds()
    {
        var previous = new OperatorAlertContext(
            DateTimeOffset.Parse("2026-03-11T15:15:00-04:00"),
            IsRemote: true,
            RestartStateCategory.MitigatedDeferred,
            RestartRiskLevel.Low,
            PolicyDecisionType.ApprovalActive,
            ApprovalActive: true,
            ApprovalExpiresAt: DateTimeOffset.Parse("2026-03-11T15:20:00-04:00"),
            PolicyValidationErrors: false);
        var current = CreateStatus(
            DateTimeOffset.Parse("2026-03-11T15:21:00-04:00"),
            isRemote: true,
            decision: PolicyDecisionType.ApprovalRequired,
            approvalActive: false,
            approvalExpiresAt: null);

        var evaluation = OperatorAlertEvaluator.Evaluate(previous, current, approvalExpiryWarningLeadMinutes: 5);

        Assert.Contains(evaluation.Notifications, alert => alert.Title == "Approval window expired");
        Assert.Contains("expired at", evaluation.PolicyTimingText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_ReturnsApprovalClearedNotification_WhenApprovalEndsEarly()
    {
        var previous = new OperatorAlertContext(
            DateTimeOffset.Parse("2026-03-11T15:15:00-04:00"),
            IsRemote: true,
            RestartStateCategory.MitigatedDeferred,
            RestartRiskLevel.Low,
            PolicyDecisionType.ApprovalActive,
            ApprovalActive: true,
            ApprovalExpiresAt: DateTimeOffset.Parse("2026-03-11T15:45:00-04:00"),
            PolicyValidationErrors: false);
        var current = CreateStatus(
            DateTimeOffset.Parse("2026-03-11T15:20:00-04:00"),
            isRemote: true,
            decision: PolicyDecisionType.ApprovalRequired,
            approvalActive: false,
            approvalExpiresAt: null);

        var evaluation = OperatorAlertEvaluator.Evaluate(previous, current, approvalExpiryWarningLeadMinutes: 5);

        Assert.Contains(evaluation.Notifications, alert => alert.Title == "Approval window cleared");
        Assert.DoesNotContain(evaluation.Notifications, alert => alert.Title == "Approval window expired");
        Assert.Contains("ended before its scheduled expiry", evaluation.PolicyTimingText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_ReturnsPolicyBlockedNotification_WhenDecisionChanges()
    {
        var previous = new OperatorAlertContext(
            DateTimeOffset.Parse("2026-03-11T15:00:00-04:00"),
            IsRemote: true,
            RestartStateCategory.Safe,
            RestartRiskLevel.Low,
            PolicyDecisionType.None,
            ApprovalActive: false,
            ApprovalExpiresAt: null,
            PolicyValidationErrors: false);
        var current = CreateStatus(
            DateTimeOffset.Parse("2026-03-11T15:02:00-04:00"),
            isRemote: true,
            decision: PolicyDecisionType.RestartBlocked,
            approvalActive: false,
            approvalExpiresAt: null);

        var evaluation = OperatorAlertEvaluator.Evaluate(previous, current, approvalExpiryWarningLeadMinutes: 5);

        Assert.Contains(evaluation.Notifications, alert => alert.Title == "Restart blocked by policy");
    }

    [Fact]
    public void Evaluate_ReturnsPolicyConfigNotification_WhenValidationErrorsAppearWithoutDecisionChange()
    {
        var previous = new OperatorAlertContext(
            DateTimeOffset.Parse("2026-03-11T15:00:00-04:00"),
            IsRemote: true,
            RestartStateCategory.Safe,
            RestartRiskLevel.Low,
            PolicyDecisionType.None,
            ApprovalActive: false,
            ApprovalExpiresAt: null,
            PolicyValidationErrors: false);
        var current = CreateStatus(
            DateTimeOffset.Parse("2026-03-11T15:02:00-04:00"),
            isRemote: true,
            decision: PolicyDecisionType.None,
            approvalActive: false,
            approvalExpiresAt: null,
            validation: PolicyValidationReport.Create(
                new[]
                {
                    new PolicyValidationIssue(
                        "duplicate-rule",
                        PolicyValidationSeverity.Error,
                        "Duplicate rule identifiers were found.")
                }));

        var evaluation = OperatorAlertEvaluator.Evaluate(previous, current, approvalExpiryWarningLeadMinutes: 5);

        Assert.Contains(evaluation.Notifications, alert => alert.Title == "Policy configuration issue");
        Assert.Contains("unavailable until policy configuration errors are fixed", evaluation.PolicyTimingText, StringComparison.OrdinalIgnoreCase);
    }

    private static SessionControlStatus CreateStatus(
        DateTimeOffset timestamp,
        bool isRemote,
        PolicyDecisionType decision,
        bool approvalActive,
        DateTimeOffset? approvalExpiresAt,
        PolicyValidationReport? validation = null,
        bool canPerformServiceWrites = false)
    {
        var policy = new PolicyEvaluation(
            decision,
            HasBlockingRules: decision == PolicyDecisionType.RestartBlocked,
            RequiresApproval: decision == PolicyDecisionType.ApprovalRequired,
            ApprovalActive: approvalActive,
            ApprovalExpiresAt: approvalExpiresAt,
            RecommendedApprovalWindowMinutes: 45,
            Summary: "Policy summary",
            Array.Empty<PolicyRuleMatch>(),
            Array.Empty<string>())
        {
            Validation = validation ?? PolicyValidationReport.None
        };

        return new SessionControlStatus(
            new SessionScanResult(
                timestamp,
                decision == PolicyDecisionType.RestartBlocked
                    ? RestartStateCategory.ProtectedSessionActive
                    : RestartStateCategory.MitigatedDeferred,
                RestartRiskLevel.Elevated,
                approvalActive ? ProtectionMode.PolicyApprovalWindow : ProtectionMode.PolicyGuardActive,
                RestartPending: decision != PolicyDecisionType.None,
                HasAmbiguousSignals: false,
                ProtectedSessionActive: false,
                LimitedVisibility: false,
                IsElevated: false,
                Summary: "Scan summary",
                WorkspaceStateSnapshot.None(timestamp),
                policy,
                new RestartSignalOverview(0, 0, 0, 0, 0, 0, 0, "No signals."),
                Array.Empty<RestartIndicator>(),
                Array.Empty<ProtectedProcessMatch>(),
                Array.Empty<ManagedMitigationState>(),
                new[] { "No action required." }),
            GuardModeEnabled: true,
            isRemote ? "Service" : "Local fallback",
            IsRemote: isRemote,
            CanPerformServiceWrites: canPerformServiceWrites);
    }
}
