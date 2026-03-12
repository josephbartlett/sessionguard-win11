namespace SessionGuard.Infrastructure.Configuration;

public sealed record ConfigurationFileUpgradeReport(
    string FileName,
    string FilePath,
    bool Exists,
    int? SchemaVersion,
    int LatestSchemaVersion,
    ConfigurationUpgradeStatus Status,
    bool RequiresUpgrade,
    string Message,
    string? BackupPath = null);
