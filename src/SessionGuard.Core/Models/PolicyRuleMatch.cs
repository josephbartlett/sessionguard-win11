namespace SessionGuard.Core.Models;

public sealed record PolicyRuleMatch(
    string RuleId,
    string Title,
    PolicyRuleKind Kind,
    PolicyRuleOutcome Outcome,
    int Priority,
    string Reason)
{
    public string KindLabel => Kind switch
    {
        PolicyRuleKind.RestartWindow => "Restart window",
        PolicyRuleKind.ProcessBlock => "Process block",
        PolicyRuleKind.WorkspaceCategoryBlock => "Workspace block",
        PolicyRuleKind.ApprovalRequired => "Approval required",
        _ => Kind.ToString()
    };

    public string OutcomeLabel => Outcome switch
    {
        PolicyRuleOutcome.Blocked => "Blocked",
        PolicyRuleOutcome.ApprovalRequired => "Approval required",
        PolicyRuleOutcome.Approved => "Approved",
        _ => Outcome.ToString()
    };
}
