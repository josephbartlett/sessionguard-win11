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
                var response = await HandleRequestAsync(request, stoppingToken);
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
        SessionControlRequest request,
        CancellationToken cancellationToken)
    {
        return request.CommandType switch
        {
            SessionControlCommandType.Ping => new SessionControlResponse(true, "Service available."),
            SessionControlCommandType.GetStatus => new SessionControlResponse(
                true,
                "Current service status returned.",
                Status: await _runtime.GetStatusAsync(cancellationToken)),
            SessionControlCommandType.ScanNow => new SessionControlResponse(
                true,
                "Scan completed.",
                Status: await _runtime.ScanNowAsync(cancellationToken)),
            SessionControlCommandType.SetGuardMode when request.GuardModeEnabled.HasValue => new SessionControlResponse(
                true,
                $"Guard mode set to {request.GuardModeEnabled.Value}.",
                Status: await _runtime.SetGuardModeAsync(request.GuardModeEnabled.Value, cancellationToken)),
            SessionControlCommandType.ApplyMitigations => new SessionControlResponse(
                true,
                "Mitigation command completed.",
                MitigationResult: await _runtime.ApplyMitigationsAsync(cancellationToken)),
            SessionControlCommandType.ResetMitigations => new SessionControlResponse(
                true,
                "Mitigation reset completed.",
                MitigationResult: await _runtime.ResetMitigationsAsync(cancellationToken)),
            SessionControlCommandType.GrantRestartApproval => new SessionControlResponse(
                true,
                "Policy approval granted.",
                PolicyResult: await _runtime.GrantRestartApprovalAsync(cancellationToken)),
            SessionControlCommandType.ClearRestartApproval => new SessionControlResponse(
                true,
                "Policy approval cleared.",
                PolicyResult: await _runtime.ClearRestartApprovalAsync(cancellationToken)),
            SessionControlCommandType.SetGuardMode => new SessionControlResponse(false, "Guard mode value was not supplied."),
            _ => new SessionControlResponse(false, $"Unsupported command: {request.CommandType}.")
        };
    }

    private static NamedPipeServerStream CreateServerStream()
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
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
