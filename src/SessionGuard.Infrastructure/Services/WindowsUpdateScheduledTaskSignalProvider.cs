using System.Reflection;
using System.Runtime.InteropServices;
using SessionGuard.Core.Models;
using SessionGuard.Core.Services;

namespace SessionGuard.Infrastructure.Services;

public sealed class WindowsUpdateScheduledTaskSignalProvider : IRestartSignalProvider
{
    public string Name => "Windows Update scheduled task";

    public Task<IReadOnlyList<RestartIndicator>> GetIndicatorsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        object? schedulerService = null;
        object? folder = null;
        object? task = null;

        try
        {
            var type = Type.GetTypeFromProgID("Schedule.Service", throwOnError: false);
            if (type is null)
            {
                return Task.FromResult<IReadOnlyList<RestartIndicator>>(
                    new[]
                    {
                        new RestartIndicator(
                            Name,
                            "Scheduled Start task",
                            RestartIndicatorCategory.UpdateOrchestration,
                            false,
                            "The Task Scheduler COM API is not available on this machine.",
                            SignalConfidence.Low,
                            LimitedVisibility: true)
                    });
            }

            schedulerService = Activator.CreateInstance(type);
            type.InvokeMember("Connect", BindingFlags.InvokeMethod, binder: null, target: schedulerService, args: null);
            folder = type.InvokeMember("GetFolder", BindingFlags.InvokeMethod, binder: null, target: schedulerService, args: new object[] { @"\Microsoft\Windows\WindowsUpdate" });
            if (folder is null)
            {
                throw new InvalidOperationException("The Windows Update task folder was not returned by Task Scheduler.");
            }

            task = folder.GetType().InvokeMember("GetTask", BindingFlags.InvokeMethod, binder: null, target: folder, args: new object[] { "Scheduled Start" });
            if (task is null)
            {
                throw new InvalidOperationException("The Scheduled Start task is not available.");
            }

            var nextRunTime = ReadComDateTime(task, "NextRunTime");
            var lastRunTime = ReadComDateTime(task, "LastRunTime");
            var enabled = ReadComBoolean(task, "Enabled") ?? false;
            var state = ReadComInt(task, "State") ?? 0;
            var hasNextRun = nextRunTime.HasValue && nextRunTime.Value.Year >= 2000;

            return Task.FromResult<IReadOnlyList<RestartIndicator>>(
                new[]
                {
                    new RestartIndicator(
                        Name,
                        "Scheduled Start task",
                        RestartIndicatorCategory.UpdateOrchestration,
                        hasNextRun,
                        hasNextRun
                            ? $"Windows Update scheduled task is enabled with next run time {nextRunTime:yyyy-MM-dd HH:mm}; current task state code {state}."
                            : enabled
                                ? $"Windows Update scheduled task exists and is enabled, but no next run time is currently exposed. Last run time: {FormatDate(lastRunTime)}."
                                : "Windows Update scheduled task exists but is disabled.",
                        SignalConfidence.Low)
                });
        }
        catch (Exception exception)
        {
            return Task.FromResult<IReadOnlyList<RestartIndicator>>(
                new[]
                {
                    new RestartIndicator(
                        Name,
                        "Scheduled Start task",
                        RestartIndicatorCategory.UpdateOrchestration,
                        false,
                        $"The Windows Update scheduled task could not be queried: {exception.Message}",
                        SignalConfidence.Low,
                        LimitedVisibility: true)
                });
        }
        finally
        {
            ReleaseComObject(task);
            ReleaseComObject(folder);
            ReleaseComObject(schedulerService);
        }
    }

    private static DateTimeOffset? ReadComDateTime(object comObject, string propertyName)
    {
        var rawValue = comObject.GetType().InvokeMember(propertyName, BindingFlags.GetProperty, null, comObject, null);
        return rawValue switch
        {
            DateTime dateTime => dateTime.Year < 2000 ? null : new DateTimeOffset(dateTime),
            _ => null
        };
    }

    private static int? ReadComInt(object comObject, string propertyName)
    {
        var rawValue = comObject.GetType().InvokeMember(propertyName, BindingFlags.GetProperty, null, comObject, null);
        return rawValue switch
        {
            int intValue => intValue,
            short shortValue => shortValue,
            _ => null
        };
    }

    private static bool? ReadComBoolean(object comObject, string propertyName)
    {
        var rawValue = comObject.GetType().InvokeMember(propertyName, BindingFlags.GetProperty, null, comObject, null);
        return rawValue switch
        {
            bool boolValue => boolValue,
            _ => null
        };
    }

    private static string FormatDate(DateTimeOffset? value)
    {
        return value.HasValue ? value.Value.ToString("yyyy-MM-dd HH:mm") : "not available";
    }

    private static void ReleaseComObject(object? comObject)
    {
        if (comObject is not null && Marshal.IsComObject(comObject))
        {
            Marshal.FinalReleaseComObject(comObject);
        }
    }
}
