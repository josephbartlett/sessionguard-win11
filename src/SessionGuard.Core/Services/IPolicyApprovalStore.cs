using SessionGuard.Core.Models;

namespace SessionGuard.Core.Services;

public interface IPolicyApprovalStore
{
    Task<PolicyApprovalState> GetCurrentAsync(DateTimeOffset now, CancellationToken cancellationToken = default);

    Task<PolicyApprovalState> GrantAsync(
        DateTimeOffset now,
        TimeSpan duration,
        CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);
}
