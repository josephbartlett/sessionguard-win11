using SessionGuard.Infrastructure.Configuration;

namespace SessionGuard.Service;

public sealed record ServiceRuntimeValidationReport(
    string ProductVersion,
    string ProtocolVersion,
    string BaseDirectory,
    string RepositoryRoot,
    string ConfigDirectory,
    string ConfigDefaultsDirectory,
    string LogDirectory,
    string StateDirectory,
    string AppSettingsPath,
    string ProtectedProcessesPath,
    string PoliciesPath,
    bool AppSettingsExistsBeforeLoad,
    bool ProtectedProcessesExistsBeforeLoad,
    bool PoliciesExistsBeforeLoad,
    bool AppSettingsExistsAfterLoad,
    bool ProtectedProcessesExistsAfterLoad,
    bool PoliciesExistsAfterLoad,
    bool CanRun,
    ConfigurationUpgradeReport ConfigUpgrade,
    bool PolicyValidationHasErrors,
    string PolicyValidationSummary,
    IReadOnlyList<string> SeededFiles,
    IReadOnlyList<string> Issues,
    IReadOnlyList<string> Warnings);
