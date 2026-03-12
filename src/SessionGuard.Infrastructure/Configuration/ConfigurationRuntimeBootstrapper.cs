using SessionGuard.Infrastructure.Environment;

namespace SessionGuard.Infrastructure.Configuration;

public static class ConfigurationRuntimeBootstrapper
{
    public static Task EnsureMutableConfigurationFilesAsync(
        RuntimePaths paths,
        CancellationToken cancellationToken = default)
    {
        if (string.Equals(paths.ConfigDirectory, paths.ConfigDefaultsDirectory, StringComparison.OrdinalIgnoreCase) ||
            !Directory.Exists(paths.ConfigDefaultsDirectory))
        {
            return Task.CompletedTask;
        }

        Directory.CreateDirectory(paths.ConfigDirectory);

        foreach (var fileName in ManagedConfigurationFiles.All)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var livePath = Path.Combine(paths.ConfigDirectory, fileName);
            if (File.Exists(livePath))
            {
                continue;
            }

            var defaultsPath = Path.Combine(paths.ConfigDefaultsDirectory, fileName);
            if (!File.Exists(defaultsPath))
            {
                continue;
            }

            File.Copy(defaultsPath, livePath, overwrite: false);
        }

        return Task.CompletedTask;
    }
}
