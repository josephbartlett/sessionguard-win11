using SessionGuard.Core.Models;
using SessionGuard.Service;

namespace SessionGuard.Tests;

public sealed class SessionGuardPipeCommandAuthorizationTests
{
    [Theory]
    [InlineData(SessionControlCommandType.SetGuardMode)]
    [InlineData(SessionControlCommandType.ApplyMitigations)]
    [InlineData(SessionControlCommandType.ResetMitigations)]
    [InlineData(SessionControlCommandType.GrantRestartApproval)]
    [InlineData(SessionControlCommandType.ClearRestartApproval)]
    public void RequiresAdministrativeAccess_ReturnsTrueForPrivilegedCommands(SessionControlCommandType commandType)
    {
        Assert.True(SessionGuardPipeCommandAuthorization.RequiresAdministrativeAccess(commandType));
    }

    [Theory]
    [InlineData(SessionControlCommandType.Ping)]
    [InlineData(SessionControlCommandType.GetStatus)]
    [InlineData(SessionControlCommandType.ScanNow)]
    public void RequiresAdministrativeAccess_ReturnsFalseForReadOnlyCommands(SessionControlCommandType commandType)
    {
        Assert.False(SessionGuardPipeCommandAuthorization.RequiresAdministrativeAccess(commandType));
    }
}
