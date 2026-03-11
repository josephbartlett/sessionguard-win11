namespace SessionGuard.Core.Models;

public enum ProtectionMode
{
    MonitorOnly,
    GuardModeActive,
    PolicyGuardActive,
    PolicyApprovalWindow,
    ManagedMitigationsApplied,
    LimitedReadOnly
}
