using SessionGuard.Infrastructure.Configuration;
using SessionGuard.Infrastructure.Environment;
using SessionGuard.Infrastructure.Ipc;

namespace SessionGuard.Service;

public static class ServiceRuntimeValidator
{
    public static async Task<ServiceRuntimeValidationReport> ValidateAsync(
        string baseDirectory,
        CancellationToken cancellationToken = default)
    {
        var paths = RuntimePaths.Discover(baseDirectory);
        var upgradeService = new ConfigurationUpgradeService(paths);
        var configUpgrade = await upgradeService.InspectAsync(cancellationToken);
        var appSettingsPath = Path.Combine(paths.ConfigDirectory, "appsettings.json");
        var protectedProcessesPath = Path.Combine(paths.ConfigDirectory, "protected-processes.json");
        var policiesPath = Path.Combine(paths.ConfigDirectory, "policies.json");

        var appSettingsExistsBefore = File.Exists(appSettingsPath);
        var protectedProcessesExistsBefore = File.Exists(protectedProcessesPath);
        var policiesExistsBefore = File.Exists(policiesPath);
        var seededFiles = new List<string>();
        var issues = new List<string>();
        var warnings = new List<string>();
        var policyValidationHasErrors = false;
        var policyValidationSummary = "Policy config: not evaluated.";

        if (configUpgrade.HasErrors)
        {
            foreach (var issue in configUpgrade.Issues)
            {
                issues.Add(issue);
            }
        }
        else
        {
            foreach (var warning in configUpgrade.Warnings)
            {
                warnings.Add(warning);
            }
        }

        try
        {
            var repository = new JsonConfigurationRepository(paths);
            var configuration = await repository.LoadAsync(cancellationToken);
            policyValidationHasErrors = configuration.PolicyValidation.HasErrors;
            policyValidationSummary = configuration.PolicyValidation.Summary;

            if (configuration.PolicyValidation.HasErrors)
            {
                warnings.Add(configuration.PolicyValidation.Summary);
            }
            else if (configuration.PolicyValidation.HasWarnings)
            {
                warnings.Add(configuration.PolicyValidation.Summary);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
        {
            issues.Add(exception.Message);
        }

        var appSettingsExistsAfter = File.Exists(appSettingsPath);
        var protectedProcessesExistsAfter = File.Exists(protectedProcessesPath);
        var policiesExistsAfter = File.Exists(policiesPath);

        AddSeededFileIfNeeded("appsettings.json", appSettingsExistsBefore, appSettingsExistsAfter, seededFiles);
        AddSeededFileIfNeeded("protected-processes.json", protectedProcessesExistsBefore, protectedProcessesExistsAfter, seededFiles);
        AddSeededFileIfNeeded("policies.json", policiesExistsBefore, policiesExistsAfter, seededFiles);

        if (!appSettingsExistsAfter)
        {
            issues.Add($"Missing app settings file at '{appSettingsPath}'.");
        }

        if (!protectedProcessesExistsAfter)
        {
            issues.Add($"Missing protected process catalog at '{protectedProcessesPath}'.");
        }

        return new ServiceRuntimeValidationReport(
            ServiceVersionInfo.ResolveProductVersion(),
            SessionControlProtocol.Version,
            AppContext.BaseDirectory,
            paths.RepositoryRoot,
            paths.ConfigDirectory,
            paths.ConfigDefaultsDirectory,
            paths.LogDirectory,
            paths.StateDirectory,
            appSettingsPath,
            protectedProcessesPath,
            policiesPath,
            appSettingsExistsBefore,
            protectedProcessesExistsBefore,
            policiesExistsBefore,
            appSettingsExistsAfter,
            protectedProcessesExistsAfter,
            policiesExistsAfter,
            CanRun: issues.Count == 0,
            ConfigUpgrade: configUpgrade,
            PolicyValidationHasErrors: policyValidationHasErrors,
            PolicyValidationSummary: policyValidationSummary,
            SeededFiles: seededFiles,
            Issues: issues,
            Warnings: warnings);
    }

    private static void AddSeededFileIfNeeded(
        string fileName,
        bool existedBefore,
        bool existsAfter,
        ICollection<string> seededFiles)
    {
        if (!existedBefore && existsAfter)
        {
            seededFiles.Add(fileName);
        }
    }
}
