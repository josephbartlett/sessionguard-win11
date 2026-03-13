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
}
