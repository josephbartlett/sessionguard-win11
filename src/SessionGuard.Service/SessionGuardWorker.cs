using Microsoft.Extensions.Hosting.WindowsServices;
using SessionGuard.Core.Services;

namespace SessionGuard.Service;

public sealed class SessionGuardWorker : BackgroundService
{
    private readonly IConfigurationRepository _configurationRepository;
    private readonly SessionGuardServiceRuntime _runtime;
    private readonly SessionGuardServiceHealthReporter _healthReporter;
    private readonly IAppLogger _logger;

    public SessionGuardWorker(
        IConfigurationRepository configurationRepository,
        SessionGuardServiceRuntime runtime,
        SessionGuardServiceHealthReporter healthReporter,
        IAppLogger logger)
    {
        _configurationRepository = configurationRepository;
        _runtime = runtime;
        _healthReporter = healthReporter;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var hostMode = WindowsServiceHelpers.IsWindowsService() ? "WindowsService" : "Console";
        await _healthReporter.InitializeAsync(hostMode, stoppingToken);
        await _runtime.InitializeAsync(stoppingToken);
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
                await _healthReporter.RecordErrorAsync("scan", exception, stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            }
        }

        await _healthReporter.RecordStoppedAsync(CancellationToken.None);
        _logger.Info("service.stop");
    }
}
