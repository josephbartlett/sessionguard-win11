namespace SessionGuard.Core.Models;

public sealed record ManagedMitigationState(
    string Id,
    string DisplayName,
    string Description,
    bool IsApplied,
    bool RequiresElevation,
    string CurrentValue,
    string RecommendedValue,
    string RegistryPath);
