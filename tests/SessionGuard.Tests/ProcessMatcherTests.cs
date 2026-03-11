using SessionGuard.Core.Services;

namespace SessionGuard.Tests;

public sealed class ProcessMatcherTests
{
    [Fact]
    public void MatchProcesses_MatchesCaseInsensitiveNames_WithOrWithoutExeExtension()
    {
        var matches = ProcessMatcher.MatchProcesses(
            new[] { "WindowsTerminal.exe", "Code" },
            new[] { "windowsterminal", "CODE", "code", "pwsh" });

        Assert.Equal(2, matches.Count);
        Assert.Equal("Code.exe", matches[0].DisplayName);
        Assert.Equal(2, matches[0].InstanceCount);
        Assert.Equal("WindowsTerminal.exe", matches[1].DisplayName);
        Assert.Equal(1, matches[1].InstanceCount);
    }

    [Fact]
    public void MatchProcesses_IgnoresDuplicateConfiguredEntries()
    {
        var matches = ProcessMatcher.MatchProcesses(
            new[] { "pwsh.exe", "PWSH", "pwsh" },
            new[] { "pwsh", "pwsh" });

        var match = Assert.Single(matches);
        Assert.Equal("pwsh.exe", match.DisplayName);
        Assert.Equal(2, match.InstanceCount);
    }
}
