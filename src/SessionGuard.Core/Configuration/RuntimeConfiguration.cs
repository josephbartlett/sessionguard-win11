using SessionGuard.Core.Models;

namespace SessionGuard.Core.Configuration;

public sealed record RuntimeConfiguration(
    AppSettings AppSettings,
    ProtectedProcessCatalog ProtectedProcesses,
    PolicyConfiguration Policies,
    string ConfigurationDirectory,
    string AppSettingsPath,
    string ProtectedProcessesPath,
    string PoliciesPath)
{
    public PolicyValidationReport PolicyValidation { get; init; } = PolicyValidationReport.None;
}
