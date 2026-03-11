using SessionGuard.Infrastructure.Environment;

namespace SessionGuard.Tests;

public sealed class RuntimePathsTests
{
    [Fact]
    public void Discover_UsesRepositoryRootWhenSolutionFileIsPresentAboveBaseDirectory()
    {
        var root = CreateTempRoot();
        File.WriteAllText(Path.Combine(root, "SessionGuard.sln"), string.Empty);
        Directory.CreateDirectory(Path.Combine(root, "config"));
        var appBaseDirectory = Path.Combine(root, "artifacts", "publish", "SessionGuard.Service");
        Directory.CreateDirectory(appBaseDirectory);

        var paths = RuntimePaths.Discover(appBaseDirectory);

        Assert.Equal(root, paths.RepositoryRoot);
        Assert.Equal(Path.Combine(root, "config"), paths.ConfigDirectory);
        Assert.Equal(Path.Combine(root, "logs"), paths.LogDirectory);
        Assert.Equal(Path.Combine(root, "state"), paths.StateDirectory);
    }

    [Fact]
    public void Discover_UsesAppBaseDirectoryWhenNoRepositoryRootExists()
    {
        var appBaseDirectory = CreateTempRoot();
        Directory.CreateDirectory(Path.Combine(appBaseDirectory, "config"));

        var paths = RuntimePaths.Discover(appBaseDirectory);

        Assert.Equal(appBaseDirectory, paths.RepositoryRoot);
        Assert.Equal(Path.Combine(appBaseDirectory, "config"), paths.ConfigDirectory);
        Assert.True(Directory.Exists(paths.LogDirectory));
        Assert.True(Directory.Exists(paths.StateDirectory));
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "SessionGuard.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
