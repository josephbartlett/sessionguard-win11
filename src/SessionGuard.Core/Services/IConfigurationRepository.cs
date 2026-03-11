using SessionGuard.Core.Configuration;

namespace SessionGuard.Core.Services;

public interface IConfigurationRepository
{
    string ConfigurationDirectory { get; }

    Task<RuntimeConfiguration> LoadAsync(CancellationToken cancellationToken = default);
}
