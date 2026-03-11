using SessionGuard.App.Automation;

namespace SessionGuard.App;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);

        var options = SessionGuardAppOptions.Parse(e.Args);
        var window = SessionGuardAppBootstrapper.CreateMainWindow(options);
        MainWindow = window;
        window.Show();
    }
}
