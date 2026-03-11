using System.Reflection;
using System.Runtime.InteropServices;
using SessionGuard.Core.Models;
using SessionGuard.Core.Services;

namespace SessionGuard.Infrastructure.Services;

public sealed class WindowsUpdateAgentSignalProvider : IRestartSignalProvider
{
    public string Name => "Windows Update Agent";

    public Task<IReadOnlyList<RestartIndicator>> GetIndicatorsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        object? systemInfo = null;

        try
        {
            var type = Type.GetTypeFromProgID("Microsoft.Update.SystemInfo", throwOnError: false);
            if (type is null)
            {
                return Task.FromResult<IReadOnlyList<RestartIndicator>>(
                    new[]
                    {
                        new RestartIndicator(
                            Name,
                            "WUA reboot required",
                            RestartIndicatorCategory.PendingRestart,
                            false,
                            "The Windows Update Agent COM API is not available on this machine.",
                            SignalConfidence.Low,
                            LimitedVisibility: true)
                    });
            }

            systemInfo = Activator.CreateInstance(type);
            var rebootRequired = (bool)(type.InvokeMember(
                "RebootRequired",
                BindingFlags.GetProperty,
                binder: null,
                target: systemInfo,
                args: null) ?? false);

            return Task.FromResult<IReadOnlyList<RestartIndicator>>(
                new[]
                {
                    new RestartIndicator(
                        Name,
                        "WUA reboot required",
                        RestartIndicatorCategory.PendingRestart,
                        rebootRequired,
                        rebootRequired
                            ? "The Windows Update Agent reports that a reboot is required."
                            : "The Windows Update Agent does not currently report a required reboot.",
                        SignalConfidence.High)
                });
        }
        catch (Exception exception)
        {
            return Task.FromResult<IReadOnlyList<RestartIndicator>>(
                new[]
                {
                    new RestartIndicator(
                        Name,
                        "WUA reboot required",
                        RestartIndicatorCategory.PendingRestart,
                        false,
                        $"The Windows Update Agent COM API could not be queried: {exception.Message}",
                        SignalConfidence.Low,
                        LimitedVisibility: true)
                });
        }
        finally
        {
            ReleaseComObject(systemInfo);
        }
    }

    private static void ReleaseComObject(object? comObject)
    {
        if (comObject is not null && Marshal.IsComObject(comObject))
        {
            Marshal.FinalReleaseComObject(comObject);
        }
    }
}
