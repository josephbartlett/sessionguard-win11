using System.Reflection;

namespace SessionGuard.Service;

internal static class ServiceVersionInfo
{
    public static string ResolveProductVersion()
    {
        var assembly = typeof(ServiceVersionInfo).Assembly;
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            return informationalVersion;
        }

        return assembly.GetName().Version?.ToString() ?? "0.0.0";
    }
}
