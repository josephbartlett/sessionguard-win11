using Microsoft.Win32;
using SessionGuard.Core.Models;
using SessionGuard.Core.Services;

namespace SessionGuard.Infrastructure.Services;

public sealed class RegistryRestartSignalProvider : IRestartSignalProvider
{
    public string Name => "Registry restart signals";

    public Task<IReadOnlyList<RestartIndicator>> GetIndicatorsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var indicators = new[]
        {
            CheckSubKeyExists(
                RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending",
                "CBS reboot pending",
                "Component Based Servicing reports a pending reboot.",
                "Component Based Servicing does not report a pending reboot."),
            CheckSubKeyExists(
                RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired",
                "Windows Update reboot required",
                "Windows Update reports that a reboot is required.",
                "Windows Update does not report a required reboot."),
            CheckMultiStringValue(
                RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Session Manager",
                "PendingFileRenameOperations",
                "Pending file rename operations",
                "Pending file rename operations were detected.",
                "No pending file rename operations were detected."),
            CheckDwordValue(
                RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Updates",
                "UpdateExeVolatile",
                "UpdateExeVolatile",
                "UpdateExeVolatile indicates update work is still in progress.",
                "UpdateExeVolatile is not set.")
        };

        return Task.FromResult<IReadOnlyList<RestartIndicator>>(indicators);
    }

    private static RestartIndicator CheckSubKeyExists(
        RegistryHive hive,
        string subKeyPath,
        string source,
        string activeSummary,
        string inactiveSummary)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var subKey = baseKey.OpenSubKey(subKeyPath);

            return new RestartIndicator(
                source,
                subKey is not null,
                subKey is not null ? activeSummary : inactiveSummary,
                SignalConfidence.High);
        }
        catch (Exception exception)
        {
            return new RestartIndicator(
                source,
                false,
                $"{source} could not be read: {exception.Message}",
                SignalConfidence.Low,
                LimitedVisibility: true);
        }
    }

    private static RestartIndicator CheckMultiStringValue(
        RegistryHive hive,
        string subKeyPath,
        string valueName,
        string source,
        string activeSummary,
        string inactiveSummary)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var subKey = baseKey.OpenSubKey(subKeyPath);
            var value = subKey?.GetValue(valueName) as string[];
            var isActive = value is { Length: > 0 };

            return new RestartIndicator(
                source,
                isActive,
                isActive ? activeSummary : inactiveSummary,
                SignalConfidence.Medium);
        }
        catch (Exception exception)
        {
            return new RestartIndicator(
                source,
                false,
                $"{source} could not be read: {exception.Message}",
                SignalConfidence.Low,
                LimitedVisibility: true);
        }
    }

    private static RestartIndicator CheckDwordValue(
        RegistryHive hive,
        string subKeyPath,
        string valueName,
        string source,
        string activeSummary,
        string inactiveSummary)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var subKey = baseKey.OpenSubKey(subKeyPath);
            var rawValue = subKey?.GetValue(valueName);
            var numericValue = rawValue switch
            {
                int intValue => intValue,
                string stringValue when int.TryParse(stringValue, out var parsedValue) => parsedValue,
                _ => 0
            };
            var isActive = numericValue > 0;

            return new RestartIndicator(
                source,
                isActive,
                isActive ? activeSummary : inactiveSummary,
                SignalConfidence.Medium);
        }
        catch (Exception exception)
        {
            return new RestartIndicator(
                source,
                false,
                $"{source} could not be read: {exception.Message}",
                SignalConfidence.Low,
                LimitedVisibility: true);
        }
    }
}
