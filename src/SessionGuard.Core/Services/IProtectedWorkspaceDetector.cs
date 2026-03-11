using SessionGuard.Core.Models;

namespace SessionGuard.Core.Services;

public interface IProtectedWorkspaceDetector
{
    Task<IReadOnlyList<ProtectedProcessMatch>> GetActiveMatchesAsync(
        IReadOnlyCollection<string> protectedProcesses,
        CancellationToken cancellationToken = default);
}
