namespace SessionGuard.Core.Models;

public sealed record PolicyEvaluation(
    PolicyDecisionType Decision,
    bool HasBlockingRules,
    bool RequiresApproval,
    bool ApprovalActive,
    DateTimeOffset? ApprovalExpiresAt,
    int RecommendedApprovalWindowMinutes,
    string Summary,
    IReadOnlyList<PolicyRuleMatch> MatchedRules,
    IReadOnlyList<string> EvaluationTrace)
{
    public static PolicyEvaluation None { get; } = new(
        PolicyDecisionType.None,
        HasBlockingRules: false,
        RequiresApproval: false,
        ApprovalActive: false,
        ApprovalExpiresAt: null,
        RecommendedApprovalWindowMinutes: 60,
        "No policy rules are currently constraining restart behavior.",
        Array.Empty<PolicyRuleMatch>(),
        Array.Empty<string>());
}
