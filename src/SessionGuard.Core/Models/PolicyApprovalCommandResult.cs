namespace SessionGuard.Core.Models;

public sealed record PolicyApprovalCommandResult(
    bool Success,
    bool RequiresService,
    bool RequiresElevation,
    string Message,
    PolicyEvaluation Policy);
