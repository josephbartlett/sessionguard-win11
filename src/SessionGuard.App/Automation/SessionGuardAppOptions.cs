namespace SessionGuard.App.Automation;

public sealed record SessionGuardAppOptions(
    string? UiScenarioName,
    bool DisableTrayIcon,
    bool ForceStartMinimized,
    bool DisableSingleInstance)
{
    public bool UseTrayIcon => !DisableTrayIcon && string.IsNullOrWhiteSpace(UiScenarioName);

    public bool EnableSingleInstance => !DisableSingleInstance && string.IsNullOrWhiteSpace(UiScenarioName);

    public static SessionGuardAppOptions Parse(IEnumerable<string> args)
    {
        string? uiScenarioName = Environment.GetEnvironmentVariable("SESSIONGUARD_UI_SCENARIO");
        var disableTrayIcon = string.Equals(
            Environment.GetEnvironmentVariable("SESSIONGUARD_DISABLE_TRAY"),
            "1",
            StringComparison.OrdinalIgnoreCase);
        var forceStartMinimized = string.Equals(
            Environment.GetEnvironmentVariable("SESSIONGUARD_START_MINIMIZED"),
            "1",
            StringComparison.OrdinalIgnoreCase);
        var disableSingleInstance = string.Equals(
            Environment.GetEnvironmentVariable("SESSIONGUARD_DISABLE_SINGLE_INSTANCE"),
            "1",
            StringComparison.OrdinalIgnoreCase);

        using var enumerator = args.GetEnumerator();
        while (enumerator.MoveNext())
        {
            var argument = enumerator.Current;
            if (string.IsNullOrWhiteSpace(argument))
            {
                continue;
            }

            if (argument.StartsWith("--ui-scenario=", StringComparison.OrdinalIgnoreCase))
            {
                uiScenarioName = argument.Split('=', 2)[1];
                continue;
            }

            if (string.Equals(argument, "--ui-scenario", StringComparison.OrdinalIgnoreCase))
            {
                if (!enumerator.MoveNext())
                {
                    throw new InvalidOperationException("Missing value after --ui-scenario.");
                }

                uiScenarioName = enumerator.Current;
                continue;
            }

            if (string.Equals(argument, "--disable-tray", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(argument, "--ui-smoke", StringComparison.OrdinalIgnoreCase))
            {
                disableTrayIcon = true;
                if (string.Equals(argument, "--ui-smoke", StringComparison.OrdinalIgnoreCase))
                {
                    disableSingleInstance = true;
                }
                continue;
            }

            if (string.Equals(argument, "--start-minimized", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(argument, "--tray", StringComparison.OrdinalIgnoreCase))
            {
                forceStartMinimized = true;
            }
        }

        return new SessionGuardAppOptions(
            string.IsNullOrWhiteSpace(uiScenarioName) ? null : uiScenarioName.Trim(),
            disableTrayIcon,
            forceStartMinimized,
            disableSingleInstance);
    }
}
