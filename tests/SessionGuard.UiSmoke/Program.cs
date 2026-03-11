using System.Diagnostics;
using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using SessionGuard.Core.Automation;
using FlaUIApp = FlaUI.Core.Application;

namespace SessionGuard.UiSmoke;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var options = SmokeOptions.Parse(args);
            Directory.CreateDirectory(options.OutputDirectory);

            var scenarios = options.Scenarios.Count > 0
                ? options.Scenarios.Select(UiSmokeScenarioCatalog.Get).ToArray()
                : UiSmokeScenarioCatalog.All.ToArray();
            var results = new List<SmokeResult>();

            foreach (var scenario in scenarios)
            {
                results.Add(await RunScenarioAsync(options.AppPath, options.OutputDirectory, scenario));
            }

            var summaryPath = Path.Combine(options.OutputDirectory, "summary.json");
            await File.WriteAllTextAsync(
                summaryPath,
                JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));

            Console.WriteLine($"UI smoke passed for {results.Count} scenario(s). Summary: {summaryPath}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"UI smoke failed: {exception.Message}");
            return 1;
        }
    }

    private static async Task<SmokeResult> RunScenarioAsync(
        string appPath,
        string outputDirectory,
        UiSmokeScenario scenario)
    {
        Console.WriteLine($"Running UI smoke scenario '{scenario.Name}'");
        using var process = StartScenarioProcess(appPath, scenario.Name);

        try
        {
            using var automation = new UIA3Automation();
            using var app = FlaUIApp.Attach(process.Id);
            var window = WaitForMainWindow(app, automation);

            VerifyRequiredElement(window, UiSmokeAutomationIds.ScanNowButton);
            VerifyRequiredElement(window, UiSmokeAutomationIds.WindowsUpdateOptionsButton);
            VerifyRequiredElement(window, UiSmokeAutomationIds.PolicyDecisionText);
            VerifyRequiredElement(window, UiSmokeAutomationIds.PolicyDiagnosticsText);
            VerifyRequiredElement(window, UiSmokeAutomationIds.PolicyRulesGrid);
            VerifyRequiredElement(window, UiSmokeAutomationIds.ProtectedProcessesGrid);
            VerifyRequiredElement(window, UiSmokeAutomationIds.WorkspaceRiskGrid);
            VerifyRequiredElement(window, UiSmokeAutomationIds.RestartIndicatorsGrid);
            VerifyRequiredElement(window, UiSmokeAutomationIds.ManagedMitigationsGrid);

            foreach (var expectedText in scenario.ExpectedTexts)
            {
                WaitForText(window, expectedText.Key, expectedText.Value);
            }

            var screenshotPath = Path.Combine(outputDirectory, $"{scenario.Name}.png");
            WindowCapture.SaveToFile(process.MainWindowHandle, screenshotPath);

            return new SmokeResult(
                scenario.Name,
                screenshotPath,
                process.MainWindowTitle,
                process.MainWindowHandle != IntPtr.Zero,
                scenario.ExpectedTexts.Keys.ToArray());
        }
        finally
        {
            await StopProcessAsync(process);
        }
    }

    private static Process StartScenarioProcess(string appPath, string scenarioName)
    {
        if (!File.Exists(appPath))
        {
            throw new FileNotFoundException($"SessionGuard app executable was not found at '{appPath}'.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = appPath,
            Arguments = $"--ui-scenario {scenarioName} --disable-tray",
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(appPath) ?? Directory.GetCurrentDirectory()
        };

        var process = Process.Start(startInfo) ??
                      throw new InvalidOperationException($"Failed to start SessionGuard for scenario '{scenarioName}'.");

        process.WaitForInputIdle(10000);
        return process;
    }

    private static Window WaitForMainWindow(FlaUIApp app, UIA3Automation automation)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            var window = app.GetMainWindow(automation);
            if (window is not null)
            {
                return window;
            }

            Thread.Sleep(250);
        }

        throw new TimeoutException("SessionGuard main window did not appear within 20 seconds.");
    }

    private static AutomationElement VerifyRequiredElement(Window window, string automationId)
    {
        var element = WaitForElement(window, automationId);
        if (element is null)
        {
            throw new InvalidOperationException($"Required automation element '{automationId}' was not found.");
        }

        return element;
    }

    private static void WaitForText(Window window, string automationId, string expectedText)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            var element = WaitForElement(window, automationId, TimeSpan.FromMilliseconds(250));
            if (element is not null && string.Equals(element.Name, expectedText, StringComparison.Ordinal))
            {
                return;
            }

            Thread.Sleep(200);
        }

        var currentValue = WaitForElement(window, automationId, TimeSpan.Zero)?.Name ?? "<missing>";
        throw new InvalidOperationException(
            $"Automation element '{automationId}' did not reach the expected text. Expected '{expectedText}', actual '{currentValue}'.");
    }

    private static AutomationElement? WaitForElement(Window window, string automationId, TimeSpan? timeout = null)
    {
        var wait = timeout ?? TimeSpan.FromSeconds(10);
        var deadline = DateTime.UtcNow.Add(wait);
        while (DateTime.UtcNow < deadline)
        {
            var element = window.FindFirstDescendant(cf => cf.ByAutomationId(automationId));
            if (element is not null)
            {
                return element;
            }

            Thread.Sleep(150);
        }

        return window.FindFirstDescendant(cf => cf.ByAutomationId(automationId));
    }

    private static async Task StopProcessAsync(Process process)
    {
        if (process.HasExited)
        {
            return;
        }

        process.CloseMainWindow();
        if (await Task.Run(() => process.WaitForExit(5000)))
        {
            return;
        }

        process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync();
    }

    private sealed record SmokeOptions(
        string AppPath,
        string OutputDirectory,
        IReadOnlyList<string> Scenarios)
    {
        public static SmokeOptions Parse(IReadOnlyList<string> args)
        {
            var repoRoot = FindRepositoryRoot(AppContext.BaseDirectory);
            var appPath = Path.Combine(repoRoot, "src", "SessionGuard.App", "bin", "Debug", "net9.0-windows", "SessionGuard.App.exe");
            var outputDirectory = Path.Combine(repoRoot, "artifacts", "ui", "smoke");
            var scenarios = new List<string>();

            for (var index = 0; index < args.Count; index++)
            {
                var argument = args[index];
                if (string.Equals(argument, "--app", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Count)
                {
                    appPath = args[++index];
                    continue;
                }

                if (string.Equals(argument, "--output-dir", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Count)
                {
                    outputDirectory = args[++index];
                    continue;
                }

                if (string.Equals(argument, "--scenario", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Count)
                {
                    scenarios.Add(args[++index]);
                }
            }

            return new SmokeOptions(appPath, outputDirectory, scenarios);
        }

        private static string FindRepositoryRoot(string startingDirectory)
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

            throw new InvalidOperationException("Could not locate the SessionGuard repository root for UI smoke execution.");
        }
    }

    private sealed record SmokeResult(
        string ScenarioName,
        string ScreenshotPath,
        string WindowTitle,
        bool WindowHandleAvailable,
        IReadOnlyList<string> VerifiedAutomationIds);
}
