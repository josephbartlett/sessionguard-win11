namespace SessionGuard.Core.Models;

public sealed record OperatorNotification(
    string Title,
    string Message,
    OperatorNotificationSeverity Severity);
