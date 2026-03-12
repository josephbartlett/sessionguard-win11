using SessionGuard.App.Automation;

namespace SessionGuard.Tests;

public sealed class SessionGuardAppOptionsTests
{
    [Fact]
    public void Parse_StartMinimizedFlag_EnablesForcedTrayStartup()
    {
        var options = SessionGuardAppOptions.Parse(new[] { "--start-minimized" });

        Assert.True(options.UseTrayIcon);
        Assert.True(options.ForceStartMinimized);
    }

    [Fact]
    public void Parse_UiSmoke_DisablesTrayWithoutClearingMinimizedOverride()
    {
        var options = SessionGuardAppOptions.Parse(new[] { "--ui-smoke", "--start-minimized" });

        Assert.False(options.UseTrayIcon);
        Assert.True(options.ForceStartMinimized);
    }
}
