using System.Text.Json.Nodes;
using SessionGuard.Infrastructure.Configuration;
using SessionGuard.Infrastructure.Environment;

namespace SessionGuard.Tests;

public sealed class ConfigurationUpgradeServiceTests : IDisposable
{
    private readonly string _rootPath;

    public ConfigurationUpgradeServiceTests()
    {
        _rootPath = Path.Combine(Path.GetTempPath(), "SessionGuard.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_rootPath, "config"));
        Directory.CreateDirectory(Path.Combine(_rootPath, "state"));
    }

    [Fact]
    public async Task InspectAsync_ReportsLegacyFilesAsNeedsUpgrade()
    {
        await WriteLegacyConfigAsync();
        var service = new ConfigurationUpgradeService(RuntimePaths.Discover(_rootPath));

        var report = await service.InspectAsync();

        Assert.False(report.UpgradedAnyFiles);
        Assert.False(report.HasErrors);
        Assert.All(
            report.Files.Where(file => file.Exists),
            file => Assert.True(
                file.Status is ConfigurationUpgradeStatus.NeedsUpgrade or ConfigurationUpgradeStatus.Missing,
                $"Unexpected status for {file.FileName}: {file.Status}"));
    }

    [Fact]
    public async Task UpgradeAsync_AddsSchemaVersionAndCreatesBackup()
    {
        await WriteLegacyConfigAsync();
        var service = new ConfigurationUpgradeService(RuntimePaths.Discover(_rootPath));

        var report = await service.UpgradeAsync();

        Assert.True(report.UpgradedAnyFiles);
        Assert.False(report.HasErrors);
        Assert.NotNull(report.BackupDirectory);
        Assert.True(Directory.Exists(report.BackupDirectory!));

        foreach (var fileName in ManagedConfigurationFiles.All)
        {
            var liveJson = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(_rootPath, "config", fileName)))!.AsObject();
            Assert.Equal(ConfigurationSchemaInfo.LatestVersion, liveJson["schemaVersion"]!.GetValue<int>());
            Assert.True(File.Exists(Path.Combine(report.BackupDirectory!, fileName)));
        }
    }

    [Fact]
    public async Task InspectAsync_RejectsFutureSchemaVersion()
    {
        await WriteLegacyConfigAsync();
        var path = Path.Combine(_rootPath, "config", ManagedConfigurationFiles.AppSettings);
        var json = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        json["schemaVersion"] = ConfigurationSchemaInfo.LatestVersion + 1;
        await File.WriteAllTextAsync(path, json.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        var service = new ConfigurationUpgradeService(RuntimePaths.Discover(_rootPath));

        var report = await service.InspectAsync();

        Assert.True(report.HasErrors);
        Assert.Contains(
            report.Files,
            file => file.FileName == ManagedConfigurationFiles.AppSettings &&
                    file.Status == ConfigurationUpgradeStatus.UnsupportedFutureVersion);
    }

    private async Task WriteLegacyConfigAsync()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_rootPath, "config", ManagedConfigurationFiles.AppSettings),
            """
            {
              "scanIntervalSeconds": 30
            }
            """);
        await File.WriteAllTextAsync(
            Path.Combine(_rootPath, "config", ManagedConfigurationFiles.ProtectedProcesses),
            """
            {
              "processNames": [ "pwsh.exe", "Code.exe" ]
            }
            """);
        await File.WriteAllTextAsync(
            Path.Combine(_rootPath, "config", ManagedConfigurationFiles.Policies),
            """
            {
              "enabled": true,
              "rules": []
            }
            """);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }
    }
}
