using System.ComponentModel;
using System.Diagnostics;
using SessionGuard.Core.Models;
using SessionGuard.Core.Services;

namespace SessionGuard.Infrastructure.Services;

public sealed class ProcessInventoryService : IProtectedWorkspaceDetector
{
    public Task<WorkspaceProcessObservation> GetWorkspaceObservationAsync(
        IReadOnlyCollection<string> protectedProcesses,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var runningProcessNames = new List<string>();

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    runningProcessNames.Add(process.ProcessName);
                }
                catch (Exception exception) when (
                    exception is InvalidOperationException or Win32Exception or NotSupportedException)
                {
                    // Some transient or protected processes cannot be queried reliably.
                }
            }
        }

        return Task.FromResult(new WorkspaceProcessObservation(
            ProcessMatcher.MatchProcesses(protectedProcesses, runningProcessNames),
            ProcessMatcher.SummarizeProcesses(runningProcessNames)));
    }
}
