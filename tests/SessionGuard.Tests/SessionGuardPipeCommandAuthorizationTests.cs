using SessionGuard.Core.Models;
using SessionGuard.Service;

namespace SessionGuard.Tests;

public sealed class SessionGuardPipeCommandAuthorizationTests
{
    [Theory]
    [InlineData(SessionControlCommandType.Ping, false)]
    [InlineData(SessionControlCommandType.GetStatus, false)]
    [InlineData(SessionControlCommandType.ScanNow, false)]
    [InlineData(SessionControlCommandType.SetGuardMode, false)]
    [InlineData(SessionControlCommandType.ApplyMitigations, true)]
    [InlineData(SessionControlCommandType.ResetMitigations, true)]
    [InlineData(SessionControlCommandType.GrantRestartApproval, true)]
    [InlineData(SessionControlCommandType.ClearRestartApproval, true)]
    public void RequiresAdministrativeAccess_MatchesCommandRisk(
        SessionControlCommandType commandType,
        bool expected)
    {
        Assert.Equal(expected, SessionGuardPipeCommandAuthorization.RequiresAdministrativeAccess(commandType));
    }
}
