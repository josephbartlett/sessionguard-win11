namespace SessionGuard.Core.Models;

public sealed record SessionControlResponse(
    bool Success,
    string Message,
    SessionControlStatus? Status = null,
    MitigationCommandResult? MitigationResult = null);
