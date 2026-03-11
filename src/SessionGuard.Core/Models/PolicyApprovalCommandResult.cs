namespace SessionGuard.Core.Models;

public sealed record PolicyApprovalCommandResult(
    bool Success,
    string Message,
    PolicyEvaluation Policy);
