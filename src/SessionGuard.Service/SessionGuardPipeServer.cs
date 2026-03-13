using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using SessionGuard.Core.Models;
using SessionGuard.Infrastructure.Ipc;

namespace SessionGuard.Service;

public sealed class SessionGuardPipeServer : BackgroundService
{
    private readonly SessionGuardServiceRuntime _runtime;
    private readonly SessionGuardServiceHealthReporter _healthReporter;
    private readonly SessionGuard.Core.Services.IAppLogger _logger;

    public SessionGuardPipeServer(
        SessionGuardServiceRuntime runtime,
        SessionGuardServiceHealthReporter healthReporter,
        SessionGuard.Core.Services.IAppLogger logger)
    {
        _runtime = runtime;
        _healthReporter = healthReporter;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _healthReporter.RecordPipeServerStartedAsync(stoppingToken);
        _logger.Info("service.pipe.start", new { pipe = SessionGuardPipeConstants.PipeName });

        while (!stoppingToken.IsCancellationRequested)
        {
            await using var server = CreateServerStream();

            try
            {
                await server.WaitForConnectionAsync(stoppingToken);
                var request = await PipeMessageProtocol.ReadRequestAsync(server, stoppingToken);
                var response = await HandleRequestAsync(server, request, stoppingToken);
                await PipeMessageProtocol.WriteResponseAsync(server, response, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.Error("service.pipe.request.failed", exception);
                await _healthReporter.RecordErrorAsync("pipe", exception, stoppingToken);
            }
        }

        _logger.Info("service.pipe.stop");
    }

    private async Task<SessionControlResponse> HandleRequestAsync(
        NamedPipeServerStream server,
        SessionControlRequest request,
        CancellationToken cancellationToken)
    {
        var callerCanPerformServiceWrites = IsCallerAuthorizedForPrivilegedCommands(server);
        var unauthorizedGuardModeMessage =
            "Guard mode changes require running SessionGuard.App as administrator while connected to the service.";

        return request.CommandType switch
        {
            SessionControlCommandType.Ping => new SessionControlResponse(true, "Service available."),
            SessionControlCommandType.GetStatus => new SessionControlResponse(
                true,
                "Current service status returned.",
                Status: (await _runtime.GetStatusAsync(cancellationToken)) with
                {
                    CanPerformServiceWrites = callerCanPerformServiceWrites
                }),
            SessionControlCommandType.ScanNow => new SessionControlResponse(
                true,
                "Scan completed.",
                Status: (await _runtime.ScanNowAsync(cancellationToken)) with
                {
                    CanPerformServiceWrites = callerCanPerformServiceWrites
                }),
            SessionControlCommandType.SetGuardMode when request.GuardModeEnabled.HasValue && callerCanPerformServiceWrites => new SessionControlResponse(
                true,
                $"Guard mode set to {request.GuardModeEnabled.Value}.",
                Status: (await _runtime.SetGuardModeAsync(request.GuardModeEnabled.Value, cancellationToken)) with
                {
                    CanPerformServiceWrites = callerCanPerformServiceWrites
                }),
            SessionControlCommandType.SetGuardMode when request.GuardModeEnabled.HasValue => new SessionControlResponse(
                false,
                unauthorizedGuardModeMessage,
                Status: (await _runtime.GetStatusAsync(cancellationToken)) with
                {
                    CanPerformServiceWrites = callerCanPerformServiceWrites
                }),
            SessionControlCommandType.ApplyMitigations => new SessionControlResponse(
                callerCanPerformServiceWrites,
                callerCanPerformServiceWrites
                    ? "Mitigation command completed."
                    : "Managed mitigation changes require running SessionGuard.App as administrator while connected to the service.",
                MitigationResult: callerCanPerformServiceWrites
                    ? await _runtime.ApplyMitigationsAsync(cancellationToken)
                    : await BuildUnauthorizedMitigationResultAsync(cancellationToken)),
            SessionControlCommandType.ResetMitigations => new SessionControlResponse(
                callerCanPerformServiceWrites,
                callerCanPerformServiceWrites
                    ? "Mitigation reset completed."
                    : "Managed mitigation changes require running SessionGuard.App as administrator while connected to the service.",
                MitigationResult: callerCanPerformServiceWrites
                    ? await _runtime.ResetMitigationsAsync(cancellationToken)
                    : await BuildUnauthorizedMitigationResultAsync(cancellationToken)),
            SessionControlCommandType.GrantRestartApproval => new SessionControlResponse(
                callerCanPerformServiceWrites,
                callerCanPerformServiceWrites
                    ? "Policy approval granted."
                    : "Restart approval changes require running SessionGuard.App as administrator while connected to the service.",
                PolicyResult: callerCanPerformServiceWrites
                    ? await _runtime.GrantRestartApprovalAsync(cancellationToken)
                    : await BuildUnauthorizedPolicyResultAsync(cancellationToken)),
            SessionControlCommandType.ClearRestartApproval => new SessionControlResponse(
                callerCanPerformServiceWrites,
                callerCanPerformServiceWrites
                    ? "Policy approval cleared."
                    : "Restart approval changes require running SessionGuard.App as administrator while connected to the service.",
                PolicyResult: callerCanPerformServiceWrites
                    ? await _runtime.ClearRestartApprovalAsync(cancellationToken)
                    : await BuildUnauthorizedPolicyResultAsync(cancellationToken)),
            SessionControlCommandType.SetGuardMode => new SessionControlResponse(false, "Guard mode value was not supplied."),
            _ => new SessionControlResponse(false, $"Unsupported command: {request.CommandType}.")
        };
    }

    private async Task<MitigationCommandResult> BuildUnauthorizedMitigationResultAsync(CancellationToken cancellationToken)
    {
        _logger.Warn("service.pipe.request.denied", new { action = "mitigation", requiredRole = "Administrator" });
        var status = await _runtime.GetStatusAsync(cancellationToken);
        return new MitigationCommandResult(
            Success: false,
            RequiresElevation: true,
            RequiresService: false,
            "Managed mitigation changes require running SessionGuard.App as administrator while connected to the service.",
            status.ScanResult.Mitigations);
    }

    private async Task<PolicyApprovalCommandResult> BuildUnauthorizedPolicyResultAsync(CancellationToken cancellationToken)
    {
        _logger.Warn("service.pipe.request.denied", new { action = "policy_approval", requiredRole = "Administrator" });
        var status = await _runtime.GetStatusAsync(cancellationToken);
        return new PolicyApprovalCommandResult(
            Success: false,
            RequiresService: false,
            RequiresElevation: true,
            "Restart approval changes require running SessionGuard.App as administrator while connected to the service.",
            status.ScanResult.Policy);
    }

    private static bool IsCallerAuthorizedForPrivilegedCommands(NamedPipeServerStream server)
    {
        var authorized = false;

        server.RunAsClient(() =>
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            authorized = principal.IsInRole(WindowsBuiltInRole.Administrator);
        });

        return authorized;
    }

    private static NamedPipeServerStream CreateServerStream()
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance,
            AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            SessionGuardPipeConstants.PipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            0,
            0,
            security);
    }
}
