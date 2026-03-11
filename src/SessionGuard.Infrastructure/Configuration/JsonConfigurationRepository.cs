using System.Text.Json;
using SessionGuard.Core.Configuration;
using SessionGuard.Core.Services;
using SessionGuard.Infrastructure.Environment;
using SessionGuard.Infrastructure.Serialization;

namespace SessionGuard.Infrastructure.Configuration;

public sealed class JsonConfigurationRepository : IConfigurationRepository
{
    private readonly RuntimePaths _paths;

    public JsonConfigurationRepository(RuntimePaths paths)
    {
        _paths = paths;
    }

    public string ConfigurationDirectory => _paths.ConfigDirectory;

    public async Task<RuntimeConfiguration> LoadAsync(CancellationToken cancellationToken = default)
    {
        var appSettingsPath = Path.Combine(_paths.ConfigDirectory, "appsettings.json");
        var protectedProcessesPath = Path.Combine(_paths.ConfigDirectory, "protected-processes.json");
        var policiesPath = Path.Combine(_paths.ConfigDirectory, "policies.json");

        if (!File.Exists(appSettingsPath))
        {
            throw new FileNotFoundException("Missing app settings file.", appSettingsPath);
        }

        if (!File.Exists(protectedProcessesPath))
        {
            throw new FileNotFoundException("Missing protected process catalog.", protectedProcessesPath);
        }

        await using var appSettingsStream = File.OpenRead(appSettingsPath);
        var appSettings = await JsonSerializer.DeserializeAsync<AppSettings>(
                              appSettingsStream,
                              SessionGuardJson.Default,
                              cancellationToken) ??
                          new AppSettings();

        await using var protectedProcessStream = File.OpenRead(protectedProcessesPath);
        var protectedProcessCatalog = await JsonSerializer.DeserializeAsync<ProtectedProcessCatalog>(
                                          protectedProcessStream,
                                          SessionGuardJson.Default,
                                          cancellationToken) ??
                                      new ProtectedProcessCatalog();
        PolicyConfiguration policies = new();

        if (File.Exists(policiesPath))
        {
            await using var policyStream = File.OpenRead(policiesPath);
            policies = await JsonSerializer.DeserializeAsync<PolicyConfiguration>(
                           policyStream,
                           SessionGuardJson.Default,
                           cancellationToken) ??
                       new PolicyConfiguration();
        }

        return new RuntimeConfiguration(
            appSettings.Normalize(),
            protectedProcessCatalog.Normalize(),
            policies.Normalize(),
            _paths.ConfigDirectory,
            appSettingsPath,
            protectedProcessesPath,
            policiesPath);
    }
}
