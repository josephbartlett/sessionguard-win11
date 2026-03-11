namespace SessionGuard.Core.Configuration;

public sealed record RuntimeConfiguration(
    AppSettings AppSettings,
    ProtectedProcessCatalog ProtectedProcesses,
    string ConfigurationDirectory,
    string AppSettingsPath,
    string ProtectedProcessesPath);
