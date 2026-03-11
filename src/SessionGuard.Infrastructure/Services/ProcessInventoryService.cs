using System.Diagnostics;
using SessionGuard.Core.Models;
using SessionGuard.Core.Services;

namespace SessionGuard.Infrastructure.Services;

public sealed class ProcessInventoryService : IProtectedWorkspaceDetector
{
    public Task<IReadOnlyList<ProtectedProcessMatch>> GetActiveMatchesAsync(
        IReadOnlyCollection<string> protectedProcesses,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var runningProcessNames = Process
            .GetProcesses()
            .Select(process => process.ProcessName)
            .ToArray();

        return Task.FromResult(ProcessMatcher.MatchProcesses(protectedProcesses, runningProcessNames));
    }
}
