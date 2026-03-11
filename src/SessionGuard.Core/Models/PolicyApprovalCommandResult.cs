namespace SessionGuard.Core.Models;

public sealed record PolicyApprovalCommandResult(
    bool Success,
    bool RequiresService,
    string Message,
    PolicyEvaluation Policy);
