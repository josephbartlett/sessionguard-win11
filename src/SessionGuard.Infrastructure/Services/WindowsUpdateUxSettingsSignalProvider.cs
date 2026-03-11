using Microsoft.Win32;
using SessionGuard.Core.Models;
using SessionGuard.Core.Services;

namespace SessionGuard.Infrastructure.Services;

public sealed class WindowsUpdateUxSettingsSignalProvider : IRestartSignalProvider
{
    private const string SettingsPath = @"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings";
    private const string StateVariablesPath = @"SOFTWARE\Microsoft\WindowsUpdate\UX\StateVariables";

    public string Name => "Windows Update UX settings";

    public Task<IReadOnlyList<RestartIndicator>> GetIndicatorsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var indicators = new List<RestartIndicator>();

        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var settingsKey = baseKey.OpenSubKey(SettingsPath);
            using var stateKey = baseKey.OpenSubKey(StateVariablesPath);

            indicators.Add(BuildActiveHoursIndicator(settingsKey));
            indicators.Add(BuildRestartNotificationIndicator(settingsKey));
            indicators.Add(BuildPauseUpdatesIndicator(settingsKey));
            indicators.Add(BuildPredictedWindowIndicator(stateKey));
        }
        catch (Exception exception)
        {
            indicators.Add(new RestartIndicator(
                Name,
                "UX settings visibility",
                RestartIndicatorCategory.UpdateOrchestration,
                false,
                $"Windows Update UX settings could not be read: {exception.Message}",
                SignalConfidence.Low,
                LimitedVisibility: true));
        }

        return Task.FromResult<IReadOnlyList<RestartIndicator>>(indicators);
    }

    private RestartIndicator BuildActiveHoursIndicator(RegistryKey? settingsKey)
    {
        if (settingsKey is null)
        {
            return MissingIndicator("Active hours", "Windows Update UX settings are not available.");
        }

        var start = ReadInt(settingsKey, "ActiveHoursStart");
        var end = ReadInt(settingsKey, "ActiveHoursEnd");

        if (start is null || end is null)
        {
            return new RestartIndicator(
                Name,
                "Active hours",
                RestartIndicatorCategory.MitigationVisibility,
                false,
                "Active hours are not exposed through the Windows Update UX settings key.",
                SignalConfidence.Low,
                LimitedVisibility: true);
        }

        return new RestartIndicator(
            Name,
            "Active hours",
            RestartIndicatorCategory.MitigationVisibility,
            true,
            $"Windows Update UX settings show active hours from {start:00}:00 to {end:00}:00.",
            SignalConfidence.Medium);
    }

    private RestartIndicator BuildRestartNotificationIndicator(RegistryKey? settingsKey)
    {
        if (settingsKey is null)
        {
            return MissingIndicator("Restart notifications allowed", "Windows Update UX settings are not available.");
        }

        var allowed = ReadInt(settingsKey, "RestartNotificationsAllowed2");
        if (allowed is null)
        {
            return new RestartIndicator(
                Name,
                "Restart notifications allowed",
                RestartIndicatorCategory.MitigationVisibility,
                false,
                "Restart notification visibility is not exposed through the UX settings key.",
                SignalConfidence.Low,
                LimitedVisibility: true);
        }

        var isAllowed = allowed.Value != 0;
        return new RestartIndicator(
            Name,
            "Restart notifications allowed",
            RestartIndicatorCategory.MitigationVisibility,
            isAllowed,
            isAllowed
                ? "Windows Update UX settings allow restart notifications for the current device."
                : "Windows Update UX settings indicate restart notifications are not enabled.",
            SignalConfidence.Medium);
    }

    private RestartIndicator BuildPauseUpdatesIndicator(RegistryKey? settingsKey)
    {
        if (settingsKey is null)
        {
            return MissingIndicator("Pause updates expiry", "Windows Update UX settings are not available.");
        }

        var expiry = ReadDateTimeOffset(settingsKey, "PauseUpdatesExpiryTime");
        var isActive = expiry.HasValue && expiry.Value > DateTimeOffset.Now;

        return new RestartIndicator(
            Name,
            "Pause updates expiry",
            RestartIndicatorCategory.UpdateOrchestration,
            isActive,
            isActive
                ? $"Updates appear paused until {expiry:yyyy-MM-dd HH:mm zzz}."
                : "No active pause-updates expiry was detected in the UX settings key.",
            SignalConfidence.Low);
    }

    private RestartIndicator BuildPredictedWindowIndicator(RegistryKey? stateKey)
    {
        if (stateKey is null)
        {
            return MissingIndicator("Smart scheduler prediction", "Windows Update state variables are not available.");
        }

        var predictedStart = ReadUnixMilliseconds(stateKey, "SmartSchedulerPredictedStartTimePoint");
        var predictedEnd = ReadUnixMilliseconds(stateKey, "SmartSchedulerPredictedEndTimePoint");
        var predictedConfidence = ReadInt(stateKey, "SmartSchedulerPredictedConfidence");
        var hasWindow = predictedStart.HasValue && predictedEnd.HasValue && predictedStart.Value > DateTimeOffset.Now;

        return new RestartIndicator(
            Name,
            "Smart scheduler prediction",
            RestartIndicatorCategory.UpdateOrchestration,
            hasWindow,
            hasWindow
                ? $"Windows Update predicts maintenance activity between {predictedStart:yyyy-MM-dd HH:mm} and {predictedEnd:yyyy-MM-dd HH:mm} (confidence {predictedConfidence ?? 0}%)."
                : "No future smart scheduler prediction was exposed through Windows Update state variables.",
            predictedConfidence.GetValueOrDefault() >= 70 ? SignalConfidence.Medium : SignalConfidence.Low);
    }

    private RestartIndicator MissingIndicator(string source, string summary)
    {
        return new RestartIndicator(
            Name,
            source,
            RestartIndicatorCategory.UpdateOrchestration,
            false,
            summary,
            SignalConfidence.Low,
            LimitedVisibility: true);
    }

    private static int? ReadInt(RegistryKey key, string valueName)
    {
        var rawValue = key.GetValue(valueName);
        return rawValue switch
        {
            int intValue => intValue,
            long longValue => Convert.ToInt32(longValue),
            string stringValue when int.TryParse(stringValue, out var parsedValue) => parsedValue,
            _ => null
        };
    }

    private static DateTimeOffset? ReadDateTimeOffset(RegistryKey key, string valueName)
    {
        var rawValue = key.GetValue(valueName);
        return rawValue switch
        {
            string stringValue when DateTimeOffset.TryParse(stringValue, out var parsedValue) => parsedValue,
            long longValue when longValue > 0 => DateTimeOffset.FromFileTime(longValue),
            _ => null
        };
    }

    private static DateTimeOffset? ReadUnixMilliseconds(RegistryKey key, string valueName)
    {
        var rawValue = key.GetValue(valueName);
        return rawValue switch
        {
            int intValue when intValue > 0 => DateTimeOffset.FromUnixTimeMilliseconds(intValue),
            long longValue when longValue > 0 => DateTimeOffset.FromUnixTimeMilliseconds(longValue),
            string stringValue when long.TryParse(stringValue, out var parsedValue) && parsedValue > 0
                => DateTimeOffset.FromUnixTimeMilliseconds(parsedValue),
            _ => null
        };
    }
}
