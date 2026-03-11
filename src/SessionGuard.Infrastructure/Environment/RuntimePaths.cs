namespace SessionGuard.Infrastructure.Environment;

public sealed class RuntimePaths
{
    private RuntimePaths(string repositoryRoot, string configDirectory, string logDirectory, string stateDirectory)
    {
        RepositoryRoot = repositoryRoot;
        ConfigDirectory = configDirectory;
        LogDirectory = logDirectory;
        StateDirectory = stateDirectory;

        Directory.CreateDirectory(LogDirectory);
        Directory.CreateDirectory(StateDirectory);
    }

    public string RepositoryRoot { get; }

    public string ConfigDirectory { get; }

    public string LogDirectory { get; }

    public string StateDirectory { get; }

    public static RuntimePaths Discover(string appBaseDirectory)
    {
        var resolvedRoot = FindRepositoryRoot(appBaseDirectory) ?? appBaseDirectory;
        var configDirectory = Directory.Exists(Path.Combine(resolvedRoot, "config"))
            ? Path.Combine(resolvedRoot, "config")
            : Path.Combine(appBaseDirectory, "config");

        return new RuntimePaths(
            resolvedRoot,
            configDirectory,
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
}
