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

        await File.WriteAllTextAsync(Path.Combine(_rootPath, "config", "appsettings.json"), settings);
        await File.WriteAllTextAsync(Path.Combine(_rootPath, "config", "protected-processes.json"), processes);

        var runtimePaths = RuntimePaths.Discover(_rootPath);
        var repository = new JsonConfigurationRepository(runtimePaths);

        var configuration = await repository.LoadAsync();

        Assert.Equal(10, configuration.AppSettings.ScanIntervalSeconds);
        Assert.True(configuration.AppSettings.UiPreferences.StartMinimized);
        Assert.False(configuration.AppSettings.UiPreferences.ShowDetailedSignals);
        Assert.Equal(8, configuration.AppSettings.RecommendedMitigations.ActiveHoursStart);
        Assert.Equal(23, configuration.AppSettings.RecommendedMitigations.ActiveHoursEnd);
        Assert.Equal(new[] { "Code.exe", "pwsh.exe" }, configuration.ProtectedProcesses.ProcessNames);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }
    }
}
