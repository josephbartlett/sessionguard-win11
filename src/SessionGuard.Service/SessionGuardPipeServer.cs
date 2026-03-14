using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using SessionGuard.Core.Models;
using SessionGuard.Infrastructure.Ipc;

namespace SessionGuard.Service;

public sealed class SessionGuardPipeServer : BackgroundService
{
    private static readonly TimeSpan RequestReadTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ResponseWriteTimeout = TimeSpan.FromSeconds(10);

    private readonly SessionGuardServiceRuntime _runtime;
    private readonly SessionGuardRuntimeAccessPolicy _accessPolicy;
    private readonly SessionGuardServiceHealthReporter _healthReporter;
    private readonly SessionGuard.Core.Services.IAppLogger _logger;

    public SessionGuardPipeServer(
        SessionGuardServiceRuntime runtime,
        SessionGuardRuntimeAccessPolicy accessPolicy,
        SessionGuardServiceHealthReporter healthReporter,
        SessionGuard.Core.Services.IAppLogger logger)
    {
        _runtime = runtime;
        _accessPolicy = accessPolicy;
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
                using var requestTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                requestTimeoutCts.CancelAfter(RequestReadTimeout);
                var request = await PipeMessageProtocol.ReadRequestAsync(server, requestTimeoutCts.Token);
                var response = await HandleRequestAsync(server, request, stoppingToken);
                using var responseTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                responseTimeoutCts.CancelAfter(ResponseWriteTimeout);
                await PipeMessageProtocol.WriteResponseAsync(server, response, responseTimeoutCts.Token);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (OperationCanceledException)
            {
                _logger.Warn("service.pipe.request.timeout", new { timeoutSeconds = RequestReadTimeout.TotalSeconds });
            }
            catch (InvalidDataException exception)
            {
                _logger.Warn("service.pipe.request.invalid", new { exception.Message });
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
            "Guard mode changes require an elevated SessionGuard window while connected to the service.";

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
                    : "Managed mitigation changes require an elevated SessionGuard window while connected to the service.",
                MitigationResult: callerCanPerformServiceWrites
                    ? await _runtime.ApplyMitigationsAsync(cancellationToken)
                    : await BuildUnauthorizedMitigationResultAsync(cancellationToken)),
            SessionControlCommandType.ResetMitigations => new SessionControlResponse(
                callerCanPerformServiceWrites,
                callerCanPerformServiceWrites
                    ? "Mitigation reset completed."
                    : "Managed mitigation changes require an elevated SessionGuard window while connected to the service.",
                MitigationResult: callerCanPerformServiceWrites
                    ? await _runtime.ResetMitigationsAsync(cancellationToken)
                    : await BuildUnauthorizedMitigationResultAsync(cancellationToken)),
            SessionControlCommandType.GrantRestartApproval => new SessionControlResponse(
                callerCanPerformServiceWrites,
                callerCanPerformServiceWrites
                    ? "Policy approval granted."
                    : "Restart approval changes require an elevated SessionGuard window while connected to the service.",
                PolicyResult: callerCanPerformServiceWrites
                    ? await _runtime.GrantRestartApprovalAsync(cancellationToken)
                    : await BuildUnauthorizedPolicyResultAsync(cancellationToken)),
            SessionControlCommandType.ClearRestartApproval => new SessionControlResponse(
                callerCanPerformServiceWrites,
                callerCanPerformServiceWrites
                    ? "Policy approval cleared."
                    : "Restart approval changes require an elevated SessionGuard window while connected to the service.",
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
            "Managed mitigation changes require an elevated SessionGuard window while connected to the service.",
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
            "Restart approval changes require an elevated SessionGuard window while connected to the service.",
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

    private NamedPipeServerStream CreateServerStream()
    {
        var security = _accessPolicy.CreatePipeSecurity();

        return NamedPipeServerStreamAcl.Create(
            SessionGuardPipeConstants.PipeName,
            PipeDirection.InOut,
            4,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            0,
            0,
            security);
    }
}
