using System.Text.Json;
using SessionGuard.Core.Models;
using SessionGuard.Core.Services;
using SessionGuard.Infrastructure.Environment;
using SessionGuard.Infrastructure.Serialization;

namespace SessionGuard.Infrastructure.Diagnostics;

public sealed class JsonScanSnapshotStore : IScanSnapshotStore
{
    private readonly string _snapshotPath;

    public JsonScanSnapshotStore(RuntimePaths paths)
    {
        _snapshotPath = Path.Combine(paths.StateDirectory, "current-scan.json");
    }

    public async Task PersistAsync(SessionScanResult result, CancellationToken cancellationToken = default)
    {
        await using var stream = File.Create(_snapshotPath);
        await JsonSerializer.SerializeAsync(stream, result, SessionGuardJson.Indented, cancellationToken);
    }
}
