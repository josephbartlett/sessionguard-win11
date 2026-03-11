using System.Windows;
using SessionGuard.App.ViewModels;
using SessionGuard.Core.Services;
using SessionGuard.Infrastructure.Configuration;
using SessionGuard.Infrastructure.Diagnostics;
using SessionGuard.Infrastructure.Environment;
using SessionGuard.Infrastructure.Services;

namespace SessionGuard.App;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();

        var runtimePaths = RuntimePaths.Discover(AppContext.BaseDirectory);
        var logger = new FileAppLogger(runtimePaths);
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

        _viewModel = new MainWindowViewModel(
            coordinator,
            configurationRepository,
            mitigationService,
            logger,
            snapshotStore,
            runtimePaths);

        _viewModel.AttentionRequested += HandleAttentionRequested;
        DataContext = _viewModel;
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.InitializeAsync();

        if (_viewModel.ShouldStartMinimized)
        {
            WindowState = WindowState.Minimized;
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.AttentionRequested -= HandleAttentionRequested;
        _viewModel.Dispose();
    }

    private void HandleAttentionRequested(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }
}
