using SessionGuard.Core.Models;

namespace SessionGuard.Core.Services;

public interface IRestartSignalProvider
{
    string Name { get; }

    Task<IReadOnlyList<RestartIndicator>> GetIndicatorsAsync(CancellationToken cancellationToken = default);
}
