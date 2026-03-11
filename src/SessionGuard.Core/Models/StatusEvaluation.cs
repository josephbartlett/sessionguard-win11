namespace SessionGuard.Core.Models;

public sealed record StatusEvaluation(
    RestartStateCategory State,
    RestartRiskLevel RiskLevel,
    string Summary,
    bool HasAmbiguousSignals);
