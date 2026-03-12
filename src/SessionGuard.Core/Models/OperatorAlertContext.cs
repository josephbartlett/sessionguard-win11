namespace SessionGuard.Core.Models;

public sealed record OperatorAlertContext(
    DateTimeOffset Timestamp,
    bool IsRemote,
    RestartStateCategory State,
    RestartRiskLevel RiskLevel,
    PolicyDecisionType PolicyDecision,
    bool ApprovalActive,
    DateTimeOffset? ApprovalExpiresAt,
    bool PolicyValidationErrors);
