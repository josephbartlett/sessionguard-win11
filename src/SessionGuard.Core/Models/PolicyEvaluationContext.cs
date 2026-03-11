namespace SessionGuard.Core.Models;

public sealed record PolicyEvaluationContext(
    DateTimeOffset Timestamp,
    RestartStateCategory State,
    RestartRiskLevel RiskLevel,
    bool RestartPending,
    WorkspaceStateSnapshot Workspace,
    IReadOnlyList<ProtectedProcessMatch> ProtectedProcesses,
    IReadOnlyList<ObservedProcessInfo> ObservedProcesses);
