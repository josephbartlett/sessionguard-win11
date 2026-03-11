namespace SessionGuard.Core.Models;

public enum RestartStateCategory
{
    Safe,
    RestartPending,
    ProtectedSessionActive,
    MitigatedDeferred,
    UnknownLimitedVisibility
}
