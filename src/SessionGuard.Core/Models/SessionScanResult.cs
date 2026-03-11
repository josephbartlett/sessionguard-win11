namespace SessionGuard.Core.Models;

public sealed record SessionScanResult(
    DateTimeOffset Timestamp,
    RestartStateCategory State,
    RestartRiskLevel RiskLevel,
    ProtectionMode ProtectionMode,
    bool RestartPending,
    bool HasAmbiguousSignals,
    bool ProtectedSessionActive,
    bool LimitedVisibility,
    bool IsElevated,
    string Summary,
    WorkspaceStateSnapshot Workspace,
    RestartSignalOverview SignalOverview,
    IReadOnlyList<RestartIndicator> Indicators,
    IReadOnlyList<ProtectedProcessMatch> ProtectedProcesses,
    IReadOnlyList<ManagedMitigationState> Mitigations,
    IReadOnlyList<string> Recommendations);
