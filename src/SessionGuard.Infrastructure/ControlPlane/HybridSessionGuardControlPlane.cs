using SessionGuard.Core.Models;
using SessionGuard.Core.Services;

namespace SessionGuard.Infrastructure.ControlPlane;

public sealed class HybridSessionGuardControlPlane : ISessionGuardControlPlane
{
    private readonly ISessionGuardControlPlane _remote;
    private readonly ISessionGuardControlPlane _local;
    private readonly IAppLogger _logger;

    private bool? _remoteAvailable;

    public HybridSessionGuardControlPlane(
        ISessionGuardControlPlane remote,
        ISessionGuardControlPlane local,
        IAppLogger logger)
    {
        _remote = remote;
        _local = local;
        _logger = logger;
    }

    public Task<SessionControlStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        return ExecuteWithFallbackAsync(
            remote => remote.GetStatusAsync(cancellationToken),
            local => local.GetStatusAsync(cancellationToken));
    }

    public Task<SessionControlStatus> ScanNowAsync(CancellationToken cancellationToken = default)
    {
        return ExecuteWithFallbackAsync(
            remote => remote.ScanNowAsync(cancellationToken),
            local => local.ScanNowAsync(cancellationToken));
    }

    public Task<SessionControlStatus> SetGuardModeAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        return ExecuteWithFallbackAsync(
            remote => remote.SetGuardModeAsync(enabled, cancellationToken),
            local => local.SetGuardModeAsync(enabled, cancellationToken));
    }

    public Task<MitigationCommandResult> ApplyRecommendedAsync(CancellationToken cancellationToken = default)
    {
        return ExecuteWithFallbackAsync(
            remote => remote.ApplyRecommendedAsync(cancellationToken),
            local => local.ApplyRecommendedAsync(cancellationToken));
    }

    public Task<MitigationCommandResult> ResetManagedAsync(CancellationToken cancellationToken = default)
    {
        return ExecuteWithFallbackAsync(
            remote => remote.ResetManagedAsync(cancellationToken),
            local => local.ResetManagedAsync(cancellationToken));
    }

    private async Task<T> ExecuteWithFallbackAsync<T>(
        Func<ISessionGuardControlPlane, Task<T>> remoteAction,
        Func<ISessionGuardControlPlane, Task<T>> localAction)
    {
        try
        {
            var result = await remoteAction(_remote);
            ReportRemoteAvailability(true, null);
            return result;
        }
        catch (Exception exception)
        {
            ReportRemoteAvailability(false, exception);
            return await localAction(_local);
        }
    }

    private void ReportRemoteAvailability(bool available, Exception? exception)
    {
        if (_remoteAvailable == available)
        {
            return;
        }

        _remoteAvailable = available;

        if (available)
        {
            _logger.Info("control_plane.remote.available", new { mode = "service" });
            return;
        }

        _logger.Warn(
            "control_plane.remote.unavailable",
            new
            {
                mode = "local_fallback",
                reason = exception?.Message
            });
    }
}
