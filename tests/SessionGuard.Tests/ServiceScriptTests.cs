using System.Diagnostics;
using System.Security.Principal;
using System.Text.Json;

namespace SessionGuard.Tests;

public sealed class ServiceScriptTests
{
    [Fact]
    public async Task GetServiceStatus_AsJson_ReturnsStructuredPayload()
    {
        var repoRoot = GetRepositoryRoot();
        var fakeProbeExecutable = Path.Combine(Path.GetTempPath(), "SessionGuard.Tests", Guid.NewGuid().ToString("N"), "SessionGuard.Service.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(fakeProbeExecutable)!);
        var scriptPath = Path.Combine(repoRoot, "scripts", "service", "Get-SessionGuardServiceStatus.ps1");

        var result = await RunPowerShellScriptAsync(
            scriptPath,
            "-AsJson",
            "-ProbeExecutable",
            fakeProbeExecutable);

        Assert.True(result.ExitCode == 0, $"PowerShell exited with {result.ExitCode}. stderr: {result.StandardError}");

        using var document = JsonDocument.Parse(result.StandardOutput);
        var root = document.RootElement;

        Assert.Equal("SessionGuardService", root.GetProperty("ServiceName").GetString());
        Assert.True(root.TryGetProperty("ControlPlaneReachable", out _));
        Assert.True(root.TryGetProperty("HealthFilePath", out _));
    }

    [Fact]
    public async Task InstallService_ValidateOnly_ReportsMissingLayout()
    {
        var repoRoot = GetRepositoryRoot();
        var publishRoot = Path.Combine(Path.GetTempPath(), "SessionGuard.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(publishRoot);
        var scriptPath = Path.Combine(repoRoot, "scripts", "service", "Install-SessionGuardService.ps1");

        var result = await RunPowerShellScriptAsync(
            scriptPath,
            "-SkipPublish",
            "-PublishRoot",
            publishRoot,
            "-ValidateOnly",
            "-AsJson");

        Assert.True(result.ExitCode == 0, $"PowerShell exited with {result.ExitCode}. stderr: {result.StandardError}");

        using var document = JsonDocument.Parse(result.StandardOutput);
        var root = document.RootElement;

        Assert.False(root.GetProperty("ServiceExecutableExists").GetBoolean());
        Assert.False(root.GetProperty("AppSettingsExists").GetBoolean());
        Assert.False(root.GetProperty("ProtectedProcessesExists").GetBoolean());
        Assert.True(root.GetProperty("Issues").GetArrayLength() >= 1);
    }

    [Fact]
    public async Task InstallService_ValidateOnly_ReportsReadinessForValidLayout()
    {
        var repoRoot = GetRepositoryRoot();
        var publishRoot = Path.Combine(Path.GetTempPath(), "SessionGuard.Tests", Guid.NewGuid().ToString("N"));
        var configDirectory = Path.Combine(publishRoot, "config");
        Directory.CreateDirectory(configDirectory);
        File.WriteAllText(Path.Combine(publishRoot, "SessionGuard.Service.exe"), string.Empty);
        File.WriteAllText(Path.Combine(configDirectory, "appsettings.json"), "{}");
        File.WriteAllText(Path.Combine(configDirectory, "protected-processes.json"), "{\"processNames\":[]}");

        var scriptPath = Path.Combine(repoRoot, "scripts", "service", "Install-SessionGuardService.ps1");
        var result = await RunPowerShellScriptAsync(
            scriptPath,
            "-SkipPublish",
            "-PublishRoot",
            publishRoot,
            "-ValidateOnly",
            "-AsJson");

        Assert.True(result.ExitCode == 0, $"PowerShell exited with {result.ExitCode}. stderr: {result.StandardError}");

        using var document = JsonDocument.Parse(result.StandardOutput);
        var root = document.RootElement;

        Assert.True(root.GetProperty("ServiceExecutableExists").GetBoolean());
        Assert.True(root.GetProperty("AppSettingsExists").GetBoolean());
        Assert.True(root.GetProperty("ProtectedProcessesExists").GetBoolean());
        Assert.True(root.GetProperty("ScExeAvailable").GetBoolean());

        var elevated = IsElevated();
        Assert.Equal(elevated, root.GetProperty("CanInstallNow").GetBoolean());

        var issues = root.GetProperty("Issues");
        if (elevated)
        {
            Assert.Equal(0, issues.GetArrayLength());
        }
        else
        {
            Assert.Contains(
                issues.EnumerateArray().Select(item => item.GetString()),
                issue => issue is not null &&
                         issue.Contains("elevated PowerShell session", StringComparison.OrdinalIgnoreCase));
        }
    }

    private static string GetRepositoryRoot()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    }

    private static async Task<ScriptResult> RunPowerShellScriptAsync(string scriptPath, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start PowerShell.");
        var standardOutput = await process.StandardOutput.ReadToEndAsync();
        var standardError = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return new ScriptResult(process.ExitCode, standardOutput.Trim(), standardError.Trim());
    }

    private static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private sealed record ScriptResult(int ExitCode, string StandardOutput, string StandardError);
}
