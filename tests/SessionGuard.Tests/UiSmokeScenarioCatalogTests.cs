using SessionGuard.Core.Automation;

namespace SessionGuard.Tests;

public sealed class UiSmokeScenarioCatalogTests
{
    [Fact]
    public void All_ReturnsExpectedNamedScenarios()
    {
        var scenarios = UiSmokeScenarioCatalog.All;

        Assert.Equal(5, scenarios.Count);
        Assert.Contains(scenarios, scenario => scenario.Name == "safe-service");
        Assert.Contains(scenarios, scenario => scenario.Name == "restart-pending");
        Assert.Contains(scenarios, scenario => scenario.Name == "protected-workspace");
        Assert.Contains(scenarios, scenario => scenario.Name == "local-fallback-limited");
        Assert.Contains(scenarios, scenario => scenario.Name == "mitigated-deferred");
    }

    [Fact]
    public void EachScenario_DeclaresStableExpectedTexts()
    {
        foreach (var scenario in UiSmokeScenarioCatalog.All)
        {
            Assert.NotEmpty(scenario.ExpectedTexts);
            Assert.All(
                scenario.ExpectedTexts,
                entry =>
                {
                    Assert.False(string.IsNullOrWhiteSpace(entry.Key));
                    Assert.False(string.IsNullOrWhiteSpace(entry.Value));
                });
        }
    }
}
