using SessionGuard.Core.Services;

namespace SessionGuard.Service;

public sealed class SessionGuardWorker : BackgroundService
{
    private readonly IConfigurationRepository _configurationRepository;
    private readonly SessionGuardServiceRuntime _runtime;
    private readonly IAppLogger _logger;

    public SessionGuardWorker(
        IConfigurationRepository configurationRepository,
        SessionGuardServiceRuntime runtime,
        IAppLogger logger)
    {
        _configurationRepository = configurationRepository;
        _runtime = runtime;
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
                await _runtime.ScanNowAsync(stoppingToken);

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
