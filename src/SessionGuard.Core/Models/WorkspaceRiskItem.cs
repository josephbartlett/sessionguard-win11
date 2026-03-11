namespace SessionGuard.Core.Models;

public sealed record WorkspaceRiskItem(
    string Title,
    WorkspaceCategory Category,
    WorkspaceRiskSeverity Severity,
    WorkspaceConfidence Confidence,
    int InstanceCount,
    string Reason,
    IReadOnlyList<string> Processes)
{
    public string ProcessSummary => string.Join(", ", Processes);
}
