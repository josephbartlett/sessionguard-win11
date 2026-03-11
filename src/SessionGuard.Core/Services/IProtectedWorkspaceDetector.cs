using SessionGuard.Core.Models;

namespace SessionGuard.Core.Services;

public interface IProtectedWorkspaceDetector
{
    Task<WorkspaceProcessObservation> GetWorkspaceObservationAsync(
        IReadOnlyCollection<string> protectedProcesses,
        CancellationToken cancellationToken = default);
}
