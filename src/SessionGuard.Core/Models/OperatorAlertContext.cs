namespace SessionGuard.Core.Models;

public sealed record OperatorAlertContext(
    DateTimeOffset Timestamp,
    bool IsRemote,
    bool CanPerformServiceWrites,
    RestartStateCategory State,
    RestartRiskLevel RiskLevel,
    PolicyDecisionType PolicyDecision,
    bool ApprovalActive,
    DateTimeOffset? ApprovalExpiresAt,
    bool PolicyValidationErrors);
