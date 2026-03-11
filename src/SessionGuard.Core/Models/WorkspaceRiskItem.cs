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
    public string CategoryLabel => Category switch
    {
        WorkspaceCategory.TerminalShell => "Terminal / shell",
        WorkspaceCategory.EditorOrIde => "Editor / IDE",
        WorkspaceCategory.Browser => "Browser",
        WorkspaceCategory.LocalDevServer => "Local runtime",
        WorkspaceCategory.ProtectedTool => "Protected tool",
        _ => Category.ToString()
    };

    public string ProcessSummary => string.Join(", ", Processes);
}
