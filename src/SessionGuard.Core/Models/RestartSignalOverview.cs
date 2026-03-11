namespace SessionGuard.Core.Models;

public sealed record RestartSignalOverview(
    int TotalIndicators,
    int ActiveIndicators,
    int DefinitivePendingSignals,
    int AmbiguousSignals,
    int LimitedVisibilityIndicators,
    int ProviderCount,
    int ProvidersWithLimitedVisibility,
    string Summary);
