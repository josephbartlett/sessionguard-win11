namespace SessionGuard.Core.Models;

public sealed record PolicyApprovalState(
    bool IsActive,
    DateTimeOffset? GrantedAt,
    DateTimeOffset? ExpiresAt,
    int? WindowMinutes)
{
    public static PolicyApprovalState None { get; } = new(
        IsActive: false,
        GrantedAt: null,
        ExpiresAt: null,
        WindowMinutes: null);
}
