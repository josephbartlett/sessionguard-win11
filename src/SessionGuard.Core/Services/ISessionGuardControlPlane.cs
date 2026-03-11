using SessionGuard.Core.Models;

namespace SessionGuard.Core.Services;

public interface ISessionGuardControlPlane
{
    Task<SessionControlStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    Task<SessionControlStatus> ScanNowAsync(CancellationToken cancellationToken = default);

    Task<SessionControlStatus> SetGuardModeAsync(bool enabled, CancellationToken cancellationToken = default);

    Task<MitigationCommandResult> ApplyRecommendedAsync(CancellationToken cancellationToken = default);

    Task<MitigationCommandResult> ResetManagedAsync(CancellationToken cancellationToken = default);
}
