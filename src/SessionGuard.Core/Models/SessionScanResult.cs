namespace SessionGuard.Core.Models;

public sealed record SessionScanResult(
    DateTimeOffset Timestamp,
    RestartStateCategory State,
    RestartRiskLevel RiskLevel,
    ProtectionMode ProtectionMode,
    bool RestartPending,
    bool ProtectedSessionActive,
    bool LimitedVisibility,
    bool IsElevated,
    string Summary,
    IReadOnlyList<RestartIndicator> Indicators,
    IReadOnlyList<ProtectedProcessMatch> ProtectedProcesses,
    IReadOnlyList<ManagedMitigationState> Mitigations,
    IReadOnlyList<string> Recommendations);
