using System.Text.Json;
using SessionGuard.Core.Models;
using SessionGuard.Core.Services;
using SessionGuard.Infrastructure.Environment;
using SessionGuard.Infrastructure.Serialization;

namespace SessionGuard.Infrastructure.Diagnostics;

public sealed class JsonScanSnapshotStore : IScanSnapshotStore
{
    private readonly string _snapshotPath;
    private readonly string _workspaceSnapshotPath;

    public JsonScanSnapshotStore(RuntimePaths paths)
    {
        _snapshotPath = Path.Combine(paths.StateDirectory, "current-scan.json");
        _workspaceSnapshotPath = Path.Combine(paths.StateDirectory, "workspace-snapshot.json");
    }

    public async Task PersistAsync(SessionScanResult result, CancellationToken cancellationToken = default)
    {
        await using var stream = File.Create(_snapshotPath);
        await JsonSerializer.SerializeAsync(stream, result, SessionGuardJson.Indented, cancellationToken);

        if (result.Workspace.HasRisk)
        {
            await using var workspaceStream = File.Create(_workspaceSnapshotPath);
            await JsonSerializer.SerializeAsync(workspaceStream, result.Workspace, SessionGuardJson.Indented, cancellationToken);
        }
        else if (File.Exists(_workspaceSnapshotPath))
        {
            File.Delete(_workspaceSnapshotPath);
        }
    }
}
