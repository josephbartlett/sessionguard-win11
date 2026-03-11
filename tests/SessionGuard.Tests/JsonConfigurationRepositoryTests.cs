using SessionGuard.Core.Configuration;
using SessionGuard.Infrastructure.Configuration;
using SessionGuard.Infrastructure.Environment;

namespace SessionGuard.Tests;

public sealed class JsonConfigurationRepositoryTests : IDisposable
{
    private readonly string _rootPath;

    public JsonConfigurationRepositoryTests()
    {
        _rootPath = Path.Combine(Path.GetTempPath(), "SessionGuard.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_rootPath, "config"));
    }

    [Fact]
    public async Task LoadAsync_ParsesConfigurationAndNormalizesValues()
    {
        var settings = """
        {
          "scanIntervalSeconds": 5,
          "guardModeEnabledByDefault": true,
          "uiPreferences": {
            "startMinimized": true,
            "showDetailedSignals": false
          },
          "warningBehavior": {
            "raiseWindowOnHighRisk": true,
            "showDesktopNotifications": false
          },
          "recommendedMitigations": {
            "applyActiveHoursPolicy": true,
            "activeHoursStart": 8,
            "activeHoursEnd": 30
          }
        }
        """;

        var processes = """
        {
          "processNames": [ "pwsh", "PWSH.exe", "Code.exe", "" ]
        }
        """;
        var policies = """
        {
          "enabled": true,
          "defaultApprovalWindowMinutes": 90,
          "rules": [
            {
              "id": "block-terminal-sessions",
              "title": "Block terminal sessions",
              "kind": "ProcessBlock",
              "priority": 20,
              "processNames": [ "pwsh", "WindowsTerminal.exe" ],
              "minimumInstances": 1
            },
            {
              "id": "approval-required-restart-pending",
              "kind": "ApprovalRequired",
              "priority": 10,
              "matchWhenRestartPendingOnly": true,
              "minimumRiskLevel": "Elevated",
              "approvalWindowMinutes": 75
            }
          ]
        }
        """;

        await File.WriteAllTextAsync(Path.Combine(_rootPath, "config", "appsettings.json"), settings);
        await File.WriteAllTextAsync(Path.Combine(_rootPath, "config", "protected-processes.json"), processes);
        await File.WriteAllTextAsync(Path.Combine(_rootPath, "config", "policies.json"), policies);

        var runtimePaths = RuntimePaths.Discover(_rootPath);
        var repository = new JsonConfigurationRepository(runtimePaths);

        var configuration = await repository.LoadAsync();

        Assert.Equal(10, configuration.AppSettings.ScanIntervalSeconds);
        Assert.True(configuration.AppSettings.UiPreferences.StartMinimized);
        Assert.False(configuration.AppSettings.UiPreferences.ShowDetailedSignals);
        Assert.Equal(8, configuration.AppSettings.RecommendedMitigations.ActiveHoursStart);
        Assert.Equal(23, configuration.AppSettings.RecommendedMitigations.ActiveHoursEnd);
        Assert.Equal(new[] { "Code.exe", "pwsh.exe" }, configuration.ProtectedProcesses.ProcessNames);
        Assert.Equal(90, configuration.Policies.DefaultApprovalWindowMinutes);
        Assert.Equal(new[] { "approval-required-restart-pending", "block-terminal-sessions" }, configuration.Policies.Rules.Select(rule => rule.Id));
        Assert.Equal("Approval Required Restart Pending", configuration.Policies.Rules[0].Title);
        Assert.Equal("WindowsTerminal.exe", configuration.Policies.Rules[1].ProcessNames[1]);
    }

    [Fact]
    public async Task LoadAsync_HandlesInvalidPolicyJsonWithoutFailingWholeConfiguration()
    {
        var settings = """
        {
          "scanIntervalSeconds": 30
        }
        """;
        var processes = """
        {
          "processNames": [ "pwsh.exe" ]
        }
        """;
        var invalidPolicies = """
        {
          "enabled": true,
          "rules": [
            {
              "id": "broken-rule",
              "kind": "ProcessBlock",
        """;

        await File.WriteAllTextAsync(Path.Combine(_rootPath, "config", "appsettings.json"), settings);
        await File.WriteAllTextAsync(Path.Combine(_rootPath, "config", "protected-processes.json"), processes);
        await File.WriteAllTextAsync(Path.Combine(_rootPath, "config", "policies.json"), invalidPolicies);

        var runtimePaths = RuntimePaths.Discover(_rootPath);
        var repository = new JsonConfigurationRepository(runtimePaths);

        var configuration = await repository.LoadAsync();

        Assert.False(configuration.Policies.Enabled);
        Assert.True(configuration.PolicyValidation.HasErrors);
        Assert.Contains(configuration.PolicyValidation.Issues, issue => issue.Code == "policy-load-failed");
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }
    }
}
