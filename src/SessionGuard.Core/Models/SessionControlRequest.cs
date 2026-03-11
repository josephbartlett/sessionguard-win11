namespace SessionGuard.Core.Models;

public sealed record SessionControlRequest(
    SessionControlCommandType CommandType,
    bool? GuardModeEnabled = null);
