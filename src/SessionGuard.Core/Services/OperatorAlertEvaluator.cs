using SessionGuard.Core.Models;

namespace SessionGuard.Core.Services;

public static class OperatorAlertEvaluator
{
    public static OperatorAlertEvaluation Evaluate(
        OperatorAlertContext? previous,
        SessionControlStatus status,
        int approvalExpiryWarningLeadMinutes)
    {
        var current = CreateContext(status);
        var leadMinutes = Math.Clamp(approvalExpiryWarningLeadMinutes, 1, 60);
        var policyTimingText = BuildPolicyTimingText(previous, status.ScanResult.Policy, current.Timestamp, leadMinutes);
        var notifications = previous is null
            ? Array.Empty<OperatorNotification>()
            : BuildNotifications(previous, current, leadMinutes).ToArray();

        return new OperatorAlertEvaluation(
            current,
            BuildTraySnapshot(status, policyTimingText),
            policyTimingText,
            notifications);
    }

    private static OperatorAlertContext CreateContext(SessionControlStatus status)
    {
        return new OperatorAlertContext(
            status.ScanResult.Timestamp,
            status.IsRemote,
            status.ScanResult.State,
            status.ScanResult.RiskLevel,
            status.ScanResult.Policy.Decision,
            status.ScanResult.Policy.ApprovalActive,
            status.ScanResult.Policy.ApprovalExpiresAt,
            status.ScanResult.Policy.Validation.HasErrors);
    }

    private static IEnumerable<OperatorNotification> BuildNotifications(
        OperatorAlertContext previous,
        OperatorAlertContext current,
        int leadMinutes)
    {
        if (previous.IsRemote && !current.IsRemote)
        {
            yield return new OperatorNotification(
                "Service unavailable",
                "SessionGuard switched to local fallback. Monitoring stays available, but mitigation and approval changes are now read-only.",
                OperatorNotificationSeverity.Warning);
        }
        else if (!previous.IsRemote && current.IsRemote)
        {
            yield return new OperatorNotification(
                "Service reconnected",
                "SessionGuard reconnected to the background service. Managed mitigation and approval actions are available again.",
                OperatorNotificationSeverity.Information);
        }

        if (previous.ApprovalActive && !current.ApprovalActive && previous.ApprovalExpiresAt.HasValue)
        {
            if (previous.ApprovalExpiresAt.Value <= current.Timestamp)
            {
                yield return new OperatorNotification(
                    "Approval window expired",
                    $"The temporary restart approval window expired at {previous.ApprovalExpiresAt.Value.LocalDateTime:G}.",
                    OperatorNotificationSeverity.Warning);
            }
            else
            {
                yield return new OperatorNotification(
                    "Approval window cleared",
                    $"The temporary restart approval window ended before its scheduled expiry at {previous.ApprovalExpiresAt.Value.LocalDateTime:G}.",
                    OperatorNotificationSeverity.Information);
            }
        }

        var isApprovalSoon = IsApprovalExpiringSoon(current, leadMinutes);
        var wasApprovalSoon = IsApprovalExpiringSoon(previous, leadMinutes);

        if (current.ApprovalActive && current.ApprovalExpiresAt.HasValue)
        {
            if (!previous.ApprovalActive)
            {
                yield return new OperatorNotification(
                    "Approval window active",
                    isApprovalSoon
                        ? $"A temporary restart approval window is active and expires soon at {current.ApprovalExpiresAt.Value.LocalDateTime:G}."
                        : $"A temporary restart approval window is active until {current.ApprovalExpiresAt.Value.LocalDateTime:G}.",
                    isApprovalSoon ? OperatorNotificationSeverity.Warning : OperatorNotificationSeverity.Information);
            }
            else if (isApprovalSoon && !wasApprovalSoon)
            {
                yield return new OperatorNotification(
                    "Approval expires soon",
                    $"The temporary restart approval window expires at {current.ApprovalExpiresAt.Value.LocalDateTime:G}.",
                    OperatorNotificationSeverity.Warning);
            }
        }

        if (!previous.PolicyValidationErrors && current.PolicyValidationErrors)
        {
            yield return new OperatorNotification(
                "Policy configuration issue",
                "Policy configuration errors are preventing policy evaluation until config/policies.json is fixed.",
                OperatorNotificationSeverity.Warning);
        }
        else if (previous.PolicyValidationErrors && !current.PolicyValidationErrors)
        {
            yield return new OperatorNotification(
                "Policy configuration restored",
                "Policy configuration errors were cleared and policy evaluation is active again.",
                OperatorNotificationSeverity.Information);
        }

        if (previous.PolicyDecision != current.PolicyDecision && !current.PolicyValidationErrors)
        {
            if (current.PolicyDecision == PolicyDecisionType.RestartBlocked)
            {
                yield return new OperatorNotification(
                    "Restart blocked by policy",
                    "Policy rules are now blocking restart. Review the dashboard before approving or scheduling a restart.",
                    OperatorNotificationSeverity.Warning);
            }
            else if (current.PolicyDecision == PolicyDecisionType.ApprovalRequired)
            {
                yield return new OperatorNotification(
                    "Approval required",
                    "Policy rules now require a temporary approval window before a supervised restart.",
                    OperatorNotificationSeverity.Information);
            }
        }
    }

    private static string BuildPolicyTimingText(
        OperatorAlertContext? previous,
        PolicyEvaluation policy,
        DateTimeOffset now,
        int leadMinutes)
    {
        if (policy.Validation.HasErrors)
        {
            return "Policy timing: unavailable until policy configuration errors are fixed.";
        }

        if (policy.ApprovalActive && policy.ApprovalExpiresAt.HasValue)
        {
            var remaining = policy.ApprovalExpiresAt.Value - now;
            var remainingMinutes = Math.Max(0, (int)Math.Ceiling(remaining.TotalMinutes));
            if (policy.ApprovalExpiresAt.Value <= now.AddMinutes(leadMinutes))
            {
                return $"Policy timing: approval expires in {remainingMinutes} minute(s) at {policy.ApprovalExpiresAt.Value.LocalDateTime:G}.";
            }

            return $"Policy timing: approval window active until {policy.ApprovalExpiresAt.Value.LocalDateTime:G} ({remainingMinutes} minute(s) remaining).";
        }

        if (previous is not null && previous.ApprovalActive && previous.ApprovalExpiresAt.HasValue)
        {
            return previous.ApprovalExpiresAt.Value <= now
                ? $"Policy timing: the previous approval window expired at {previous.ApprovalExpiresAt.Value.LocalDateTime:G}."
                : $"Policy timing: the previous approval window ended before its scheduled expiry at {previous.ApprovalExpiresAt.Value.LocalDateTime:G}.";
        }

        if (policy.RequiresApproval)
        {
            return "Policy timing: no approval window is active. Restart still requires supervised approval.";
        }

        return "Policy timing: no approval window is active.";
    }

    private static TrayStatusSnapshot BuildTraySnapshot(
        SessionControlStatus status,
        string policyTimingText)
    {
        var mode = status.IsRemote ? "Service" : "Fallback";
        var policy = status.ScanResult.Policy.Validation.HasErrors
            ? "Config issue"
            : status.ScanResult.Policy.Decision switch
            {
                PolicyDecisionType.RestartBlocked => "Blocked",
                PolicyDecisionType.ApprovalRequired => "Needs approval",
                PolicyDecisionType.ApprovalActive => "Approval active",
                _ => "Clear"
            };
        var state = status.ScanResult.State switch
        {
            RestartStateCategory.RestartPending => "Restart pending",
            RestartStateCategory.ProtectedSessionActive => "Protected session",
            RestartStateCategory.MitigatedDeferred => "Mitigated",
            RestartStateCategory.UnknownLimitedVisibility => "Limited view",
            _ => "Safe"
        };
        var tooltip = $"SessionGuard - {mode} - {policy}";
        if (tooltip.Length > 63)
        {
            tooltip = tooltip[..63];
        }

        return new TrayStatusSnapshot(
            tooltip,
            $"Status: {state} ({FormatRisk(status.ScanResult.RiskLevel)})",
            $"Mode: {(status.IsRemote ? "Service connected" : "Local fallback read-only")}",
            $"Policy: {policy}",
            policyTimingText.Replace("Policy timing: ", "Timing: "));
    }

    private static bool IsApprovalExpiringSoon(OperatorAlertContext context, int leadMinutes)
    {
        return context.ApprovalActive &&
               context.ApprovalExpiresAt.HasValue &&
               context.ApprovalExpiresAt.Value <= context.Timestamp.AddMinutes(leadMinutes);
    }

    private static string FormatRisk(RestartRiskLevel riskLevel)
    {
        return riskLevel switch
        {
            RestartRiskLevel.Low => "Low",
            RestartRiskLevel.Elevated => "Elevated",
            RestartRiskLevel.High => "High",
            _ => "Unknown"
        };
    }
}
