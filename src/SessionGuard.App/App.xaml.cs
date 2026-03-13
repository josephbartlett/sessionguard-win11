using SessionGuard.App.Automation;
using SessionGuard.App.Runtime;

namespace SessionGuard.App;

public partial class App : System.Windows.Application
{
    private AppInstanceCoordinator? _instanceCoordinator;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);

        var options = SessionGuardAppOptions.Parse(e.Args);
        if (options.EnableSingleInstance)
        {
            _instanceCoordinator = new AppInstanceCoordinator();
            if (!_instanceCoordinator.IsPrimaryInstance)
            {
                if (!options.ForceStartMinimized)
                {
                    _instanceCoordinator.SignalActivation();
                }

                _instanceCoordinator.Dispose();
                _instanceCoordinator = null;
                Shutdown();
                return;
            }
        }

        var window = SessionGuardAppBootstrapper.CreateMainWindow(options);
        MainWindow = window;
        _instanceCoordinator?.StartListening(() =>
        {
            window.Dispatcher.BeginInvoke(() => window.ShowFromExternalActivation());
        });
        window.Show();
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        _instanceCoordinator?.Dispose();
        _instanceCoordinator = null;
        base.OnExit(e);
    }
}
