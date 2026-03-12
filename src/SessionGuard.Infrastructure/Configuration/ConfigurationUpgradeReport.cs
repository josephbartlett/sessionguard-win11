namespace SessionGuard.Infrastructure.Configuration;

public sealed record ConfigurationUpgradeReport(
    string ConfigurationDirectory,
    string ConfigurationDefaultsDirectory,
    string? BackupDirectory,
    int LatestSchemaVersion,
    bool UpgradedAnyFiles,
    bool HasErrors,
    IReadOnlyList<ConfigurationFileUpgradeReport> Files)
{
    public IReadOnlyList<string> Issues =>
        Files
            .Where(file => file.Status is ConfigurationUpgradeStatus.UnsupportedFutureVersion or ConfigurationUpgradeStatus.InvalidJson or ConfigurationUpgradeStatus.WriteFailed)
            .Select(file => file.Message)
            .ToArray();

    public IReadOnlyList<string> Warnings =>
        Files
            .Where(file => file.Status == ConfigurationUpgradeStatus.NeedsUpgrade)
            .Select(file => file.Message)
            .ToArray();
}
