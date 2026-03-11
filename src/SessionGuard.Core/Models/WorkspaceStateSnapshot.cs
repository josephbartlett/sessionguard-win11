namespace SessionGuard.Core.Models;

public sealed record WorkspaceStateSnapshot(
    DateTimeOffset Timestamp,
    bool HasRisk,
    WorkspaceRiskSeverity HighestSeverity,
    WorkspaceConfidence Confidence,
    string Summary,
    IReadOnlyList<WorkspaceRiskItem> RiskItems)
{
    public static WorkspaceStateSnapshot None(DateTimeOffset timestamp) => new(
        timestamp,
        HasRisk: false,
        WorkspaceRiskSeverity.None,
        WorkspaceConfidence.Low,
        "No workspace-risk heuristics were triggered during the latest scan.",
        Array.Empty<WorkspaceRiskItem>());
}
