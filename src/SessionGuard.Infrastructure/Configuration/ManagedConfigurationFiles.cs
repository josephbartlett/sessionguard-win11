namespace SessionGuard.Infrastructure.Configuration;

public static class ManagedConfigurationFiles
{
    public const string AppSettings = "appsettings.json";
    public const string ProtectedProcesses = "protected-processes.json";
    public const string Policies = "policies.json";

    public static readonly IReadOnlyList<string> All =
    [
        AppSettings,
        ProtectedProcesses,
        Policies
    ];

    public static readonly IReadOnlySet<string> Required =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            AppSettings,
            ProtectedProcesses
        };
}
