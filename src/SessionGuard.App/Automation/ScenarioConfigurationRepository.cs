using System.IO;
using SessionGuard.Core.Automation;
using SessionGuard.Core.Configuration;
using SessionGuard.Core.Services;

namespace SessionGuard.App.Automation;

internal sealed class ScenarioConfigurationRepository : IConfigurationRepository
{
    private readonly RuntimeConfiguration _configuration;

    public ScenarioConfigurationRepository(string configurationDirectory, string scenarioName)
    {
        var scenario = UiSmokeScenarioCatalog.Get(scenarioName);
        var protectedProcesses = scenario.Status.ScanResult.ProtectedProcesses
            .Select(match => match.DisplayName)
            .ToArray();

        _configuration = new RuntimeConfiguration(
            new AppSettings
            {
                GuardModeEnabledByDefault = scenario.Status.GuardModeEnabled,
                ScanIntervalSeconds = 300,
                UiPreferences = new UiPreferences
                {
                    StartMinimized = false,
                    ShowDetailedSignals = false
                },
                WarningBehavior = new WarningBehaviorOptions
                {
                    RaiseWindowOnHighRisk = false,
                    ShowDesktopNotifications = false
                }
            }.Normalize(),
            new ProtectedProcessCatalog
            {
                ProcessNames = protectedProcesses
            }.Normalize(),
            new PolicyConfiguration
            {
                Rules = Array.Empty<PolicyRuleDefinition>()
            }.Normalize(),
            configurationDirectory,
            configurationDirectory,
            Path.Combine(configurationDirectory, $"ui-smoke-{scenario.Name}-appsettings.json"),
            Path.Combine(configurationDirectory, $"ui-smoke-{scenario.Name}-protected-processes.json"),
            Path.Combine(configurationDirectory, $"ui-smoke-{scenario.Name}-policies.json"));
    }

    public string ConfigurationDirectory => _configuration.ConfigurationDirectory;

    public Task<RuntimeConfiguration> LoadAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(_configuration);
}
