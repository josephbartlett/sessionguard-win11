using SessionGuard.Core.Configuration;
using SessionGuard.Core.Models;

namespace SessionGuard.Core.Services;

public interface IMitigationService
{
    bool IsElevated { get; }

    Task<IReadOnlyList<ManagedMitigationState>> GetStatesAsync(
        RuntimeConfiguration configuration,
        CancellationToken cancellationToken = default);

    Task<MitigationCommandResult> ApplyRecommendedAsync(
        RuntimeConfiguration configuration,
        CancellationToken cancellationToken = default);

    Task<MitigationCommandResult> ResetManagedAsync(
        RuntimeConfiguration configuration,
        CancellationToken cancellationToken = default);
}
