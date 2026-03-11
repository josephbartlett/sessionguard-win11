using SessionGuard.Core.Services;

namespace SessionGuard.Service;

public sealed class SessionGuardWorker : BackgroundService
{
    private readonly SessionGuardCoordinator _coordinator;
    private readonly IConfigurationRepository _configurationRepository;
    private readonly IScanSnapshotStore _snapshotStore;
    private readonly IAppLogger _logger;

    public SessionGuardWorker(
        SessionGuardCoordinator coordinator,
        IConfigurationRepository configurationRepository,
        IScanSnapshotStore snapshotStore,
        IAppLogger logger)
    {
        _coordinator = coordinator;
        _configurationRepository = configurationRepository;
        _snapshotStore = snapshotStore;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.Info("service.start");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var configuration = await _configurationRepository.LoadAsync(stoppingToken);
                var result = await _coordinator.ScanAsync(
                    configuration.AppSettings.GuardModeEnabledByDefault,
                    stoppingToken);

                await _snapshotStore.PersistAsync(result, stoppingToken);
                _logger.Info(
                    "service.snapshot.updated",
                    new
                    {
                        result.State,
                        result.RiskLevel,
                        result.RestartPending,
                        result.HasAmbiguousSignals
                    });

                await Task.Delay(TimeSpan.FromSeconds(configuration.AppSettings.ScanIntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.Error("service.scan.failed", exception);
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            }
        }

        _logger.Info("service.stop");
    }
}
