using System.ComponentModel;
using System.Drawing;
using System.Windows;
using SessionGuard.App.ViewModels;
using SessionGuard.Core.Services;
using SessionGuard.Infrastructure.Configuration;
using SessionGuard.Infrastructure.ControlPlane;
using SessionGuard.Infrastructure.Diagnostics;
using SessionGuard.Infrastructure.Environment;
using SessionGuard.Infrastructure.Services;
using Forms = System.Windows.Forms;

namespace SessionGuard.App;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly Forms.NotifyIcon _notifyIcon;
    private bool _exitRequested;

    public MainWindow()
    {
        InitializeComponent();

        var runtimePaths = RuntimePaths.Discover(AppContext.BaseDirectory);
        var logger = new FileAppLogger(runtimePaths, "app");
        var snapshotStore = new JsonScanSnapshotStore(runtimePaths);
        var configurationRepository = new JsonConfigurationRepository(runtimePaths);
        var mitigationService = new WindowsMitigationService(runtimePaths, logger);
        var coordinator = new SessionGuardCoordinator(
            configurationRepository,
            new ProcessInventoryService(),
            new IRestartSignalProvider[]
            {
                new RegistryRestartSignalProvider(),
                new WindowsUpdateAgentSignalProvider(),
                new WindowsUpdateUxSettingsSignalProvider(),
                new WindowsUpdateScheduledTaskSignalProvider()
            },
            mitigationService,
            logger);

        var localControlPlane = new LocalSessionGuardControlPlane(
            coordinator,
            configurationRepository,
            mitigationService,
            snapshotStore);
        var remoteControlPlane = new NamedPipeSessionGuardControlPlane();
        var controlPlane = new HybridSessionGuardControlPlane(
            remoteControlPlane,
            localControlPlane,
            logger);

        _viewModel = new MainWindowViewModel(
            controlPlane,
            configurationRepository,
            logger,
            runtimePaths);
        _notifyIcon = CreateNotifyIcon();

        _viewModel.AttentionRequested += HandleAttentionRequested;
        DataContext = _viewModel;
        Loaded += OnLoaded;
        Closing += OnClosing;
        Closed += OnClosed;
        StateChanged += OnStateChanged;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.InitializeAsync();

        if (_viewModel.ShouldStartMinimized)
        {
            HideToTray(showBalloon: false);
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_exitRequested)
        {
            return;
        }

        e.Cancel = true;
        HideToTray(showBalloon: false);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.AttentionRequested -= HandleAttentionRequested;
        _viewModel.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            HideToTray(showBalloon: false);
        }
    }

    private void HandleAttentionRequested(object? sender, EventArgs e)
    {
        if (!IsVisible || WindowState == WindowState.Minimized)
        {
            _notifyIcon.BalloonTipTitle = "SessionGuard";
            _notifyIcon.BalloonTipText = "High restart risk detected. Open the dashboard to review the current status.";
            _notifyIcon.ShowBalloonTip(4000);
            ShowFromTray();
        }

        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private Forms.NotifyIcon CreateNotifyIcon()
    {
        var contextMenu = new Forms.ContextMenuStrip();
        contextMenu.Items.Add("Open dashboard", null, (_, _) => ShowFromTray());
        contextMenu.Items.Add("Scan now", null, (_, _) => _viewModel.ScanNowCommand.Execute(null));
        contextMenu.Items.Add("Windows Update options", null, (_, _) => _viewModel.OpenWindowsUpdateSettingsCommand.Execute(null));
        contextMenu.Items.Add(new Forms.ToolStripSeparator());
        contextMenu.Items.Add("Exit", null, (_, _) => ExitApplication());

        var notifyIcon = new Forms.NotifyIcon
        {
            Icon = SystemIcons.Shield,
            Text = "SessionGuard",
            Visible = true,
            ContextMenuStrip = contextMenu
        };

        notifyIcon.DoubleClick += (_, _) => ShowFromTray();
        return notifyIcon;
    }

    private void HideToTray(bool showBalloon)
    {
        ShowInTaskbar = false;
        Hide();

        if (showBalloon)
        {
            _notifyIcon.BalloonTipTitle = "SessionGuard";
            _notifyIcon.BalloonTipText = "SessionGuard is still running in the tray.";
            _notifyIcon.ShowBalloonTip(3000);
        }
    }

    private void ShowFromTray()
    {
        Show();
        ShowInTaskbar = true;
        WindowState = WindowState.Normal;
        Activate();
    }

    private void ExitApplication()
    {
        _exitRequested = true;
        Close();
    }
}
