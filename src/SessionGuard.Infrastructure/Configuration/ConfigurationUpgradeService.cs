using System.Text.Json;
using System.Text.Json.Nodes;
using SessionGuard.Infrastructure.Environment;

namespace SessionGuard.Infrastructure.Configuration;

public sealed class ConfigurationUpgradeService
{
    private readonly RuntimePaths _paths;

    public ConfigurationUpgradeService(RuntimePaths paths)
    {
        _paths = paths;
    }

    public async Task<ConfigurationUpgradeReport> InspectAsync(CancellationToken cancellationToken = default)
    {
        await ConfigurationRuntimeBootstrapper.EnsureMutableConfigurationFilesAsync(_paths, cancellationToken);
        return await BuildReportAsync(writeChanges: false, cancellationToken);
    }

    public async Task<ConfigurationUpgradeReport> UpgradeAsync(CancellationToken cancellationToken = default)
    {
        await ConfigurationRuntimeBootstrapper.EnsureMutableConfigurationFilesAsync(_paths, cancellationToken);
        return await BuildReportAsync(writeChanges: true, cancellationToken);
    }

    private async Task<ConfigurationUpgradeReport> BuildReportAsync(bool writeChanges, CancellationToken cancellationToken)
    {
        string? backupDirectory = null;
        var reports = new List<ConfigurationFileUpgradeReport>();

        foreach (var fileName in ManagedConfigurationFiles.All)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var filePath = Path.Combine(_paths.ConfigDirectory, fileName);
            var report = await ProcessFileAsync(
                fileName,
                filePath,
                writeChanges,
                () => backupDirectory ??= CreateBackupDirectory(),
                cancellationToken);
            reports.Add(report);
        }

        if (backupDirectory is not null &&
            !reports.Any(report => !string.IsNullOrWhiteSpace(report.BackupPath)))
        {
            Directory.Delete(backupDirectory, recursive: true);
            backupDirectory = null;
        }

        return new ConfigurationUpgradeReport(
            _paths.ConfigDirectory,
            _paths.ConfigDefaultsDirectory,
            backupDirectory,
            ConfigurationSchemaInfo.LatestVersion,
            reports.Any(report => report.Status == ConfigurationUpgradeStatus.Upgraded),
            reports.Any(report => report.Status is ConfigurationUpgradeStatus.UnsupportedFutureVersion or ConfigurationUpgradeStatus.InvalidJson or ConfigurationUpgradeStatus.WriteFailed),
            reports);
    }

    private static async Task<ConfigurationFileUpgradeReport> ProcessFileAsync(
        string fileName,
        string filePath,
        bool writeChanges,
        Func<string> ensureBackupDirectory,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            var message = ManagedConfigurationFiles.Required.Contains(fileName)
                ? $"Missing required config file '{filePath}'."
                : $"Optional config file '{filePath}' is not present.";
            return new ConfigurationFileUpgradeReport(
                fileName,
                filePath,
                Exists: false,
                SchemaVersion: null,
                ConfigurationSchemaInfo.LatestVersion,
                ConfigurationUpgradeStatus.Missing,
                RequiresUpgrade: false,
                message);
        }

        JsonObject? root;
        try
        {
            var rawJson = await File.ReadAllTextAsync(filePath, cancellationToken);
            root = JsonNode.Parse(rawJson) as JsonObject;
            if (root is null)
            {
                return new ConfigurationFileUpgradeReport(
                    fileName,
                    filePath,
                    Exists: true,
                    SchemaVersion: null,
                    ConfigurationSchemaInfo.LatestVersion,
                    ConfigurationUpgradeStatus.InvalidJson,
                    RequiresUpgrade: false,
                    $"Config file '{filePath}' does not contain a JSON object root.");
            }
        }
        catch (JsonException exception)
        {
            return new ConfigurationFileUpgradeReport(
                fileName,
                filePath,
                Exists: true,
                SchemaVersion: null,
                ConfigurationSchemaInfo.LatestVersion,
                ConfigurationUpgradeStatus.InvalidJson,
                RequiresUpgrade: false,
                $"Config file '{filePath}' could not be parsed: {exception.Message}");
        }

        var schemaVersion = TryGetSchemaVersion(root);
        if (schemaVersion.HasValue && schemaVersion.Value > ConfigurationSchemaInfo.LatestVersion)
        {
            return new ConfigurationFileUpgradeReport(
                fileName,
                filePath,
                Exists: true,
                SchemaVersion: schemaVersion,
                ConfigurationSchemaInfo.LatestVersion,
                ConfigurationUpgradeStatus.UnsupportedFutureVersion,
                RequiresUpgrade: false,
                $"Config file '{filePath}' uses unsupported schema version {schemaVersion.Value}. This build supports up to {ConfigurationSchemaInfo.LatestVersion}.");
        }

        var effectiveVersion = schemaVersion ?? ConfigurationSchemaInfo.LegacyVersion;
        if (effectiveVersion >= ConfigurationSchemaInfo.LatestVersion)
        {
            return new ConfigurationFileUpgradeReport(
                fileName,
                filePath,
                Exists: true,
                SchemaVersion: effectiveVersion,
                ConfigurationSchemaInfo.LatestVersion,
                ConfigurationUpgradeStatus.Current,
                RequiresUpgrade: false,
                $"Config file '{filePath}' already uses schema version {effectiveVersion}.");
        }

        var upgraded = UpgradeToLatest(root);
        var upgradeMessage = $"Config file '{filePath}' uses legacy schema version {effectiveVersion} and should be upgraded to {ConfigurationSchemaInfo.LatestVersion}.";
        if (!writeChanges)
        {
            return new ConfigurationFileUpgradeReport(
                fileName,
                filePath,
                Exists: true,
                SchemaVersion: effectiveVersion,
                ConfigurationSchemaInfo.LatestVersion,
                ConfigurationUpgradeStatus.NeedsUpgrade,
                RequiresUpgrade: true,
                upgradeMessage);
        }

        try
        {
            var backupDirectory = ensureBackupDirectory();
            var backupPath = Path.Combine(backupDirectory, fileName);
            File.Copy(filePath, backupPath, overwrite: true);
            await File.WriteAllTextAsync(
                filePath,
                upgraded.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
                cancellationToken);

            return new ConfigurationFileUpgradeReport(
                fileName,
                filePath,
                Exists: true,
                SchemaVersion: effectiveVersion,
                ConfigurationSchemaInfo.LatestVersion,
                ConfigurationUpgradeStatus.Upgraded,
                RequiresUpgrade: false,
                $"Config file '{filePath}' was upgraded from schema version {effectiveVersion} to {ConfigurationSchemaInfo.LatestVersion}.",
                backupPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new ConfigurationFileUpgradeReport(
                fileName,
                filePath,
                Exists: true,
                SchemaVersion: effectiveVersion,
                ConfigurationSchemaInfo.LatestVersion,
                ConfigurationUpgradeStatus.WriteFailed,
                RequiresUpgrade: true,
                $"Config file '{filePath}' could not be upgraded: {exception.Message}");
        }
    }

    private string CreateBackupDirectory()
    {
        var root = Path.Combine(
            _paths.StateDirectory,
            "config-backups",
            $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private static int? TryGetSchemaVersion(JsonObject root)
    {
        if (root.TryGetPropertyValue("schemaVersion", out var value) &&
            value is JsonValue jsonValue &&
            jsonValue.TryGetValue<int>(out var numericVersion))
        {
            return numericVersion;
        }

        return null;
    }

    private static JsonObject UpgradeToLatest(JsonObject root)
    {
        var upgraded = new JsonObject
        {
            ["schemaVersion"] = ConfigurationSchemaInfo.LatestVersion
        };

        foreach (var property in root)
        {
            if (string.Equals(property.Key, "schemaVersion", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            upgraded[property.Key] = property.Value?.DeepClone();
        }

        return upgraded;
    }
}
