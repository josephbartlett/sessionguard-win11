using SessionGuard.Core.Models;

namespace SessionGuard.Service;

public static class SessionGuardPipeCommandAuthorization
{
    public static bool RequiresAdministrativeAccess(SessionControlCommandType commandType)
    {
        return commandType is SessionControlCommandType.ApplyMitigations or
               SessionControlCommandType.ResetMitigations or
               SessionControlCommandType.GrantRestartApproval or
               SessionControlCommandType.ClearRestartApproval;
    }
}
