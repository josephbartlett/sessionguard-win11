namespace SessionGuard.Core.Models;

public sealed record RestartIndicator(
    string Source,
    bool IsActive,
    string Summary,
    SignalConfidence Confidence,
    bool LimitedVisibility = false);
