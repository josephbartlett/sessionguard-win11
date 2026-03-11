namespace SessionGuard.Core.Configuration;

public sealed class AppSettings
{
    public int ScanIntervalSeconds { get; init; } = 30;

    public bool GuardModeEnabledByDefault { get; init; } = true;

    public UiPreferences UiPreferences { get; init; } = new();

    public WarningBehaviorOptions WarningBehavior { get; init; } = new();

    public RecommendedMitigationOptions RecommendedMitigations { get; init; } = new();

    public AppSettings Normalize()
    {
        return new AppSettings
        {
            ScanIntervalSeconds = Math.Clamp(ScanIntervalSeconds, 10, 300),
            GuardModeEnabledByDefault = GuardModeEnabledByDefault,
            UiPreferences = UiPreferences.Normalize(),
            WarningBehavior = WarningBehavior.Normalize(),
            RecommendedMitigations = RecommendedMitigations.Normalize()
        };
    }
}

public sealed class UiPreferences
{
    public bool StartMinimized { get; init; }

    public bool ShowDetailedSignals { get; init; } = true;

    public UiPreferences Normalize()
    {
        return new UiPreferences
        {
            StartMinimized = StartMinimized,
            ShowDetailedSignals = ShowDetailedSignals
        };
    }
}

public sealed class WarningBehaviorOptions
{
    public bool RaiseWindowOnHighRisk { get; init; } = true;

    public bool ShowDesktopNotifications { get; init; }

    public WarningBehaviorOptions Normalize()
    {
        return new WarningBehaviorOptions
        {
            RaiseWindowOnHighRisk = RaiseWindowOnHighRisk,
            ShowDesktopNotifications = ShowDesktopNotifications
        };
    }
}

public sealed class RecommendedMitigationOptions
{
    public bool ApplyActiveHoursPolicy { get; init; } = true;

    public int ActiveHoursStart { get; init; } = 8;

    public int ActiveHoursEnd { get; init; } = 23;

    public RecommendedMitigationOptions Normalize()
    {
        var start = Math.Clamp(ActiveHoursStart, 0, 23);
        var end = Math.Clamp(ActiveHoursEnd, 0, 23);
        var duration = CalculateDuration(start, end);

        if (duration < 1)
        {
            end = (start + 1) % 24;
        }
        else if (duration > 18)
        {
            end = (start + 18) % 24;
        }

        return new RecommendedMitigationOptions
        {
            ApplyActiveHoursPolicy = ApplyActiveHoursPolicy,
            ActiveHoursStart = start,
            ActiveHoursEnd = end
        };
    }

    private static int CalculateDuration(int start, int end)
    {
        return end >= start ? end - start : (24 - start) + end;
    }
}
