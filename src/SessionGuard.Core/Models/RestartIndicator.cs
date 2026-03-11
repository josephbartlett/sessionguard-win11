namespace SessionGuard.Core.Models;

public sealed record RestartIndicator(
    string Provider,
    string Source,
    RestartIndicatorCategory Category,
    bool IsActive,
    string Summary,
    SignalConfidence Confidence,
    bool LimitedVisibility = false);
