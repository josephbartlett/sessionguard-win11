namespace SessionGuard.Core.Models;

public sealed record OperatorAlertEvaluation(
    OperatorAlertContext Context,
    TrayStatusSnapshot Tray,
    string PolicyTimingText,
    IReadOnlyList<OperatorNotification> Notifications);
