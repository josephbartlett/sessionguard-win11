using SessionGuard.Core.Models;

namespace SessionGuard.Core.Services;

public interface IScanSnapshotStore
{
    Task PersistAsync(SessionScanResult result, CancellationToken cancellationToken = default);
}
