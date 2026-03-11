namespace SessionGuard.Infrastructure.ControlPlane;

public sealed class SessionGuardControlPlaneUnavailableException : Exception
{
    public SessionGuardControlPlaneUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
