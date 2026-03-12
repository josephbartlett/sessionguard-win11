using System.Text.Json;
using SessionGuard.Core.Configuration;
using SessionGuard.Core.Models;
using SessionGuard.Core.Services;
using SessionGuard.Infrastructure.Environment;
using SessionGuard.Infrastructure.Serialization;

namespace SessionGuard.Infrastructure.Configuration;

public sealed class JsonConfigurationRepository : IConfigurationRepository
{
    private static readonly string[] ManagedConfigFiles =
    {
        "appsettings.json",
        "protected-processes.json",
        "policies.json"
    };

    private readonly RuntimePaths _paths;

    public JsonConfigurationRepository(RuntimePaths paths)
    {
        _paths = paths;
    }

    public string ConfigurationDirectory => _paths.ConfigDirectory;

    public async Task<RuntimeConfiguration> LoadAsync(CancellationToken cancellationToken = default)
    {
        await EnsureMutableConfigurationFilesAsync(cancellationToken);

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
        PolicyValidationReport policyValidation = PolicyValidationReport.None;

        if (File.Exists(policiesPath))
        {
            try
            {
                await using var policyStream = File.OpenRead(policiesPath);
                policies = await JsonSerializer.DeserializeAsync<PolicyConfiguration>(
                               policyStream,
                               SessionGuardJson.Default,
                               cancellationToken) ??
                           new PolicyConfiguration();
                policies = policies.Normalize();
                policyValidation = PolicyConfigurationValidator.Validate(policies, policiesPath);
            }
            catch (JsonException exception)
            {
                policies = new PolicyConfiguration
                {
                    Enabled = false
                }.Normalize();
                policyValidation = PolicyConfigurationValidator.BuildLoadFailure(policiesPath, exception.Message);
            }
        }
        else
        {
            policies = policies.Normalize();
        }

        return new RuntimeConfiguration(
            appSettings.Normalize(),
            protectedProcessCatalog.Normalize(),
            policies,
            _paths.ConfigDirectory,
            _paths.ConfigDefaultsDirectory,
            appSettingsPath,
            protectedProcessesPath,
            policiesPath)
        {
            PolicyValidation = policyValidation
        };
    }

    private Task EnsureMutableConfigurationFilesAsync(CancellationToken cancellationToken)
    {
        if (string.Equals(_paths.ConfigDirectory, _paths.ConfigDefaultsDirectory, StringComparison.OrdinalIgnoreCase) ||
            !Directory.Exists(_paths.ConfigDefaultsDirectory))
        {
            return Task.CompletedTask;
        }

        Directory.CreateDirectory(_paths.ConfigDirectory);

        foreach (var fileName in ManagedConfigFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var livePath = Path.Combine(_paths.ConfigDirectory, fileName);
            if (File.Exists(livePath))
            {
                continue;
            }

            var defaultsPath = Path.Combine(_paths.ConfigDefaultsDirectory, fileName);
            if (!File.Exists(defaultsPath))
            {
                continue;
            }

            File.Copy(defaultsPath, livePath, overwrite: false);
        }

        return Task.CompletedTask;
    }
}
