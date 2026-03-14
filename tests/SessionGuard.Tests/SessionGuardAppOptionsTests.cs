using SessionGuard.App.Automation;

namespace SessionGuard.Tests;

public sealed class SessionGuardAppOptionsTests
{
    [Fact]
    public void Parse_StartMinimizedFlag_EnablesForcedTrayStartup()
    {
        var options = SessionGuardAppOptions.Parse(new[] { "--start-minimized" });

        Assert.True(options.UseTrayIcon);
        Assert.True(options.EnableSingleInstance);
        Assert.True(options.ForceStartMinimized);
        Assert.False(options.ForceTechnicalView);
    }

    [Fact]
    public void Parse_UiSmoke_DisablesTrayWithoutClearingMinimizedOverride()
    {
        var options = SessionGuardAppOptions.Parse(new[] { "--ui-smoke", "--start-minimized" });

        Assert.False(options.UseTrayIcon);
        Assert.False(options.EnableSingleInstance);
        Assert.True(options.ForceStartMinimized);
        Assert.False(options.ForceTechnicalView);
    }

    [Fact]
    public void Parse_TechnicalViewFlag_EnablesForcedTechnicalStartup()
    {
        var options = SessionGuardAppOptions.Parse(new[] { "--technical-view" });

        Assert.True(options.UseTrayIcon);
        Assert.True(options.EnableSingleInstance);
        Assert.False(options.ForceStartMinimized);
        Assert.True(options.ForceTechnicalView);
    }
}
