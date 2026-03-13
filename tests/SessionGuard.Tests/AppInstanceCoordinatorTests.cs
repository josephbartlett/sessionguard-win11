using SessionGuard.App.Runtime;

namespace SessionGuard.Tests;

public sealed class AppInstanceCoordinatorTests
{
    [Fact]
    public async Task SecondCoordinator_IsNotPrimary_AndCanSignalActivation()
    {
        var instanceKey = Guid.NewGuid().ToString("N");
        using var primary = new AppInstanceCoordinator(instanceKey);
        using var secondary = new AppInstanceCoordinator(instanceKey);
        var activation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        primary.StartListening(() => activation.TrySetResult());

        Assert.True(primary.IsPrimaryInstance);
        Assert.False(secondary.IsPrimaryInstance);

        secondary.SignalActivation();

        var completed = await Task.WhenAny(activation.Task, Task.Delay(TimeSpan.FromSeconds(3)));
        Assert.Same(activation.Task, completed);
    }

    [Fact]
    public void BuildInstanceScopeKey_SeparatesDifferentExecutablePaths()
    {
        var installedScope = AppInstanceCoordinator.BuildInstanceScopeKey(
            "S-1-5-21-test",
            @"C:\Program Files\SessionGuard\SessionGuard.App.exe",
            isElevated: false);
        var sourceScope = AppInstanceCoordinator.BuildInstanceScopeKey(
            "S-1-5-21-test",
            @"C:\src\sessionguard-win11\src\SessionGuard.App\bin\Debug\net9.0-windows\SessionGuard.App.exe",
            isElevated: false);

        Assert.NotEqual(installedScope, sourceScope);
    }

    [Fact]
    public void BuildInstanceScopeKey_SeparatesElevationLevels()
    {
        var standardScope = AppInstanceCoordinator.BuildInstanceScopeKey(
            "S-1-5-21-test",
            @"C:\Program Files\SessionGuard\SessionGuard.App.exe",
            isElevated: false);
        var elevatedScope = AppInstanceCoordinator.BuildInstanceScopeKey(
            "S-1-5-21-test",
            @"C:\Program Files\SessionGuard\SessionGuard.App.exe",
            isElevated: true);

        Assert.NotEqual(standardScope, elevatedScope);
    }

    [Fact]
    public void BuildInstanceScopeKey_NormalizesPathCasingAndTrailingSeparators()
    {
        var firstScope = AppInstanceCoordinator.BuildInstanceScopeKey(
            "S-1-5-21-test",
            @"C:\Program Files\SessionGuard\SessionGuard.App.exe",
            isElevated: false);
        var secondScope = AppInstanceCoordinator.BuildInstanceScopeKey(
            "S-1-5-21-test",
            @"c:\program files\sessionguard\SessionGuard.App.exe\",
            isElevated: false);

        Assert.Equal(firstScope, secondScope);
    }
}
