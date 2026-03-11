namespace SessionGuard.Core.Models;

public sealed record SessionControlStatus(
    SessionScanResult ScanResult,
    bool GuardModeEnabled,
    string ConnectionMode,
    bool IsRemote);
