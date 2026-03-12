using SessionGuard.Core.Models;

namespace SessionGuard.App.ViewModels;

public sealed class OperatorNotificationEventArgs : EventArgs
{
    public OperatorNotificationEventArgs(OperatorNotification notification)
    {
        Notification = notification;
    }

    public OperatorNotification Notification { get; }
}
