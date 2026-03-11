namespace SessionGuard.Core.Models;

public enum SessionControlCommandType
{
    Ping,
    GetStatus,
    ScanNow,
    SetGuardMode,
    ApplyMitigations,
    ResetMitigations
}
