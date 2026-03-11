using System.IO.Pipes;
using SessionGuard.Core.Models;
using SessionGuard.Core.Services;
using SessionGuard.Infrastructure.Ipc;

namespace SessionGuard.Infrastructure.ControlPlane;

public sealed class NamedPipeSessionGuardControlPlane : ISessionGuardControlPlane
{
    private readonly TimeSpan _connectTimeout;

    public NamedPipeSessionGuardControlPlane(TimeSpan? connectTimeout = null)
    {
        _connectTimeout = connectTimeout ?? TimeSpan.FromMilliseconds(600);
    }

    public Task<SessionControlStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        return ExecuteForStatusAsync(
            new SessionControlRequest(SessionControlCommandType.GetStatus),
            cancellationToken);
    }

    public Task<SessionControlStatus> ScanNowAsync(CancellationToken cancellationToken = default)
    {
        return ExecuteForStatusAsync(
            new SessionControlRequest(SessionControlCommandType.ScanNow),
            cancellationToken);
    }

    public Task<SessionControlStatus> SetGuardModeAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        return ExecuteForStatusAsync(
            new SessionControlRequest(SessionControlCommandType.SetGuardMode, enabled),
            cancellationToken);
    }

    public Task<MitigationCommandResult> ApplyRecommendedAsync(CancellationToken cancellationToken = default)
    {
        return ExecuteForMitigationAsync(
            new SessionControlRequest(SessionControlCommandType.ApplyMitigations),
            cancellationToken);
    }

    public Task<MitigationCommandResult> ResetManagedAsync(CancellationToken cancellationToken = default)
    {
        return ExecuteForMitigationAsync(
            new SessionControlRequest(SessionControlCommandType.ResetMitigations),
            cancellationToken);
    }

    public Task<PolicyApprovalCommandResult> GrantRestartApprovalAsync(CancellationToken cancellationToken = default)
    {
        return ExecuteForPolicyAsync(
            new SessionControlRequest(SessionControlCommandType.GrantRestartApproval),
            cancellationToken);
    }

    public Task<PolicyApprovalCommandResult> ClearRestartApprovalAsync(CancellationToken cancellationToken = default)
    {
        return ExecuteForPolicyAsync(
            new SessionControlRequest(SessionControlCommandType.ClearRestartApproval),
            cancellationToken);
    }

    private async Task<SessionControlStatus> ExecuteForStatusAsync(
        SessionControlRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync(request, cancellationToken);
        if (!response.Success || response.Status is null)
        {
            throw new InvalidOperationException(response.Message);
        }

        return response.Status;
    }

    private async Task<MitigationCommandResult> ExecuteForMitigationAsync(
        SessionControlRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync(request, cancellationToken);
        if (!response.Success || response.MitigationResult is null)
        {
            throw new InvalidOperationException(response.Message);
        }

        return response.MitigationResult;
    }

    private async Task<PolicyApprovalCommandResult> ExecuteForPolicyAsync(
        SessionControlRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync(request, cancellationToken);
        if (!response.Success || response.PolicyResult is null)
        {
            throw new InvalidOperationException(response.Message);
        }

        return response.PolicyResult;
    }

    private async Task<SessionControlResponse> SendAsync(
        SessionControlRequest request,
        CancellationToken cancellationToken)
    {
        using var client = new NamedPipeClientStream(
            ".",
            SessionGuardPipeConstants.PipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_connectTimeout);

        await client.ConnectAsync(timeoutCts.Token);
        await PipeMessageProtocol.WriteRequestAsync(client, request, cancellationToken);
        return await PipeMessageProtocol.ReadResponseAsync(client, cancellationToken);
    }
}
