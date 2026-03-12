namespace SessionGuard.Infrastructure.Environment;

public sealed class RuntimePaths
{
    private RuntimePaths(
        string repositoryRoot,
        string configDirectory,
        string configDefaultsDirectory,
        string logDirectory,
        string stateDirectory)
    {
        RepositoryRoot = repositoryRoot;
        ConfigDirectory = configDirectory;
        ConfigDefaultsDirectory = configDefaultsDirectory;
        LogDirectory = logDirectory;
        StateDirectory = stateDirectory;

        Directory.CreateDirectory(ConfigDirectory);
        Directory.CreateDirectory(LogDirectory);
        Directory.CreateDirectory(StateDirectory);
    }

    public string RepositoryRoot { get; }

    public string ConfigDirectory { get; }

    public string ConfigDefaultsDirectory { get; }

    public string LogDirectory { get; }

    public string StateDirectory { get; }

    public static RuntimePaths Discover(string appBaseDirectory)
    {
        if (LooksLikePublishedRuntime(appBaseDirectory))
        {
            var publishedDefaultsDirectory = Directory.Exists(Path.Combine(appBaseDirectory, "config.defaults"))
                ? Path.Combine(appBaseDirectory, "config.defaults")
                : Path.Combine(appBaseDirectory, "config");

            return new RuntimePaths(
                appBaseDirectory,
                Path.Combine(appBaseDirectory, "config"),
                publishedDefaultsDirectory,
                Path.Combine(appBaseDirectory, "logs"),
                Path.Combine(appBaseDirectory, "state"));
        }

        var repositoryRoot = FindRepositoryRoot(appBaseDirectory);
        var resolvedRoot = repositoryRoot ?? appBaseDirectory;
        var configDirectory = repositoryRoot is not null
            ? Path.Combine(resolvedRoot, "config")
            : Path.Combine(appBaseDirectory, "config");
        var configDefaultsDirectory = repositoryRoot is not null
            ? configDirectory
            : Directory.Exists(Path.Combine(appBaseDirectory, "config.defaults"))
                ? Path.Combine(appBaseDirectory, "config.defaults")
                : configDirectory;

        return new RuntimePaths(
            resolvedRoot,
            configDirectory,
            configDefaultsDirectory,
            Path.Combine(resolvedRoot, "logs"),
            Path.Combine(resolvedRoot, "state"));
    }

    private static string? FindRepositoryRoot(string startingDirectory)
    {
        var current = new DirectoryInfo(startingDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "SessionGuard.sln")) ||
                Directory.Exists(Path.Combine(current.FullName, ".git")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }

    private static bool LooksLikePublishedRuntime(string appBaseDirectory)
    {
        return File.Exists(Path.Combine(appBaseDirectory, "install-manifest.json")) ||
               Directory.Exists(Path.Combine(appBaseDirectory, "config.defaults"));
    }
}
