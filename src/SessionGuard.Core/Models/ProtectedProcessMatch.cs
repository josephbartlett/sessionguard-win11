namespace SessionGuard.Core.Models;

public sealed record ProtectedProcessMatch(
    string DisplayName,
    int InstanceCount);
