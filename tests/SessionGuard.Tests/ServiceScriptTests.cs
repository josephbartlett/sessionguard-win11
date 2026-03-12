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
        Assert.True(root.TryGetProperty("InstalledImagePath", out _));
        Assert.True(root.TryGetProperty("InstalledPublishRoot", out _));
        Assert.True(root.TryGetProperty("InstalledPathMatchesProbeExecutable", out _));
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
        CopyDirectory(GetBuiltServiceOutputRoot(repoRoot), publishRoot);

        var configDirectory = Path.Combine(publishRoot, "config");
        var configDefaultsDirectory = Path.Combine(publishRoot, "config.defaults");
        Directory.CreateDirectory(configDirectory);
        Directory.CreateDirectory(configDefaultsDirectory);
        File.WriteAllText(Path.Combine(configDirectory, "appsettings.json"), "{}");
        File.WriteAllText(Path.Combine(configDirectory, "protected-processes.json"), "{\"processNames\":[]}");
        File.WriteAllText(Path.Combine(configDirectory, "policies.json"), "{\"enabled\":true,\"rules\":[]}");
        File.WriteAllText(Path.Combine(configDefaultsDirectory, "appsettings.json"), "{}");
        File.WriteAllText(Path.Combine(configDefaultsDirectory, "protected-processes.json"), "{\"processNames\":[]}");
        File.WriteAllText(Path.Combine(configDefaultsDirectory, "policies.json"), "{\"enabled\":true,\"rules\":[]}");

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
        Assert.True(root.GetProperty("ConfigDefaultsExists").GetBoolean());
        Assert.True(root.GetProperty("ScExeAvailable").GetBoolean());
        Assert.True(root.GetProperty("RuntimeValidation").GetProperty("CanRun").GetBoolean());
        Assert.Contains(
            root.GetProperty("ConfigUpgrade").GetProperty("Files").EnumerateArray(),
            file => file.GetProperty("RequiresUpgrade").GetBoolean());

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

    [Fact]
    public async Task UpgradeServiceConfig_UpgradesLegacyConfigAndCreatesBackup()
    {
        var repoRoot = GetRepositoryRoot();
        var publishRoot = Path.Combine(Path.GetTempPath(), "SessionGuard.Tests", Guid.NewGuid().ToString("N"));
        CopyDirectory(GetBuiltServiceOutputRoot(repoRoot), publishRoot);

        var configDirectory = Path.Combine(publishRoot, "config");
        var configDefaultsDirectory = Path.Combine(publishRoot, "config.defaults");
        Directory.CreateDirectory(configDirectory);
        Directory.CreateDirectory(configDefaultsDirectory);
        File.WriteAllText(Path.Combine(configDirectory, "appsettings.json"), "{ \"scanIntervalSeconds\": 45 }");
        File.WriteAllText(Path.Combine(configDirectory, "protected-processes.json"), "{ \"processNames\": [\"pwsh.exe\"] }");
        File.WriteAllText(Path.Combine(configDirectory, "policies.json"), "{ \"enabled\": true, \"rules\": [] }");
        File.WriteAllText(Path.Combine(configDefaultsDirectory, "appsettings.json"), "{ \"schemaVersion\": 1, \"scanIntervalSeconds\": 30 }");
        File.WriteAllText(Path.Combine(configDefaultsDirectory, "protected-processes.json"), "{ \"schemaVersion\": 1, \"processNames\": [] }");
        File.WriteAllText(Path.Combine(configDefaultsDirectory, "policies.json"), "{ \"schemaVersion\": 1, \"enabled\": true, \"rules\": [] }");

        var scriptPath = Path.Combine(repoRoot, "scripts", "service", "Upgrade-SessionGuardServiceConfig.ps1");
        var result = await RunPowerShellScriptAsync(
            scriptPath,
            "-PublishRoot",
            publishRoot,
            "-AsJson");

        Assert.True(result.ExitCode == 0, $"PowerShell exited with {result.ExitCode}. stderr: {result.StandardError}");

        using var document = JsonDocument.Parse(result.StandardOutput);
        var root = document.RootElement;

        Assert.True(root.GetProperty("UpgradedAnyFiles").GetBoolean());
        var backupDirectory = root.GetProperty("BackupDirectory").GetString();
        Assert.False(string.IsNullOrWhiteSpace(backupDirectory));
        Assert.True(Directory.Exists(backupDirectory!));

        var appSettings = JsonDocument.Parse(File.ReadAllText(Path.Combine(configDirectory, "appsettings.json")));
        Assert.Equal(1, appSettings.RootElement.GetProperty("schemaVersion").GetInt32());
    }

    [Fact]
    public async Task UpdateServiceDeployment_ValidateOnly_ReportsUpgradePlanForValidLayout()
    {
        var repoRoot = GetRepositoryRoot();
        var publishRoot = Path.Combine(Path.GetTempPath(), "SessionGuard.Tests", Guid.NewGuid().ToString("N"));
        CopyDirectory(GetBuiltServiceOutputRoot(repoRoot), publishRoot);

        var configDirectory = Path.Combine(publishRoot, "config");
        var configDefaultsDirectory = Path.Combine(publishRoot, "config.defaults");
        Directory.CreateDirectory(configDirectory);
        Directory.CreateDirectory(configDefaultsDirectory);
        File.WriteAllText(Path.Combine(configDirectory, "appsettings.json"), "{}");
        File.WriteAllText(Path.Combine(configDirectory, "protected-processes.json"), "{\"processNames\":[]}");
        File.WriteAllText(Path.Combine(configDirectory, "policies.json"), "{\"enabled\":true,\"rules\":[]}");
        File.WriteAllText(Path.Combine(configDefaultsDirectory, "appsettings.json"), "{\"schemaVersion\":1}");
        File.WriteAllText(Path.Combine(configDefaultsDirectory, "protected-processes.json"), "{\"schemaVersion\":1,\"processNames\":[]}");
        File.WriteAllText(Path.Combine(configDefaultsDirectory, "policies.json"), "{\"schemaVersion\":1,\"enabled\":true,\"rules\":[]}");
        File.WriteAllText(
            Path.Combine(publishRoot, "install-manifest.json"),
            "{\"ProductVersion\":\"0.0-test\",\"ProtocolVersion\":\"1.2\"}");

        var scriptPath = Path.Combine(repoRoot, "scripts", "service", "Update-SessionGuardServiceDeployment.ps1");
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

        Assert.Equal(publishRoot, root.GetProperty("TargetPublishRoot").GetString());
        Assert.False(root.GetProperty("WillPublish").GetBoolean());
        Assert.True(root.GetProperty("DotnetAvailable").GetBoolean());
        Assert.True(root.TryGetProperty("WillStopService", out _));
        Assert.True(root.TryGetProperty("InstalledService", out _));
        Assert.True(root.GetProperty("InstallReadiness").GetProperty("ServiceExecutableExists").GetBoolean());
        Assert.True(root.GetProperty("InstallReadiness").GetProperty("RuntimeValidation").GetProperty("CanRun").GetBoolean());

        var elevated = IsElevated();
        Assert.Equal(elevated, root.GetProperty("CanExecuteNow").GetBoolean());
    }

    private static string GetBuiltServiceOutputRoot(string repoRoot)
    {
        return Path.Combine(repoRoot, "src", "SessionGuard.Service", "bin", "Debug", "net9.0-windows");
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);

        foreach (var filePath in Directory.GetFiles(sourceDirectory))
        {
            var destinationPath = Path.Combine(destinationDirectory, Path.GetFileName(filePath));
            File.Copy(filePath, destinationPath, overwrite: true);
        }

        foreach (var directoryPath in Directory.GetDirectories(sourceDirectory))
        {
            var destinationPath = Path.Combine(destinationDirectory, Path.GetFileName(directoryPath));
            CopyDirectory(directoryPath, destinationPath);
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
