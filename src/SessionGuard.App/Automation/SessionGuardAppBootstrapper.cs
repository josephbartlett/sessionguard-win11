using SessionGuard.Core.Services;
using SessionGuard.App.ViewModels;
using SessionGuard.Infrastructure.Configuration;
using SessionGuard.Infrastructure.ControlPlane;
using SessionGuard.Infrastructure.Diagnostics;
using SessionGuard.Infrastructure.Environment;
using SessionGuard.Infrastructure.Services;

namespace SessionGuard.App.Automation;

internal static class SessionGuardAppBootstrapper
{
    public static MainWindow CreateMainWindow(SessionGuardAppOptions options)
    {
        var runtimePaths = RuntimePaths.Discover(AppContext.BaseDirectory);
        var logger = new FileAppLogger(runtimePaths, "app");

        IConfigurationRepository configurationRepository;
        ISessionGuardControlPlane controlPlane;

        if (!string.IsNullOrWhiteSpace(options.UiScenarioName))
        {
            configurationRepository = new ScenarioConfigurationRepository(
                runtimePaths.ConfigDirectory,
                options.UiScenarioName);
            controlPlane = new ScenarioSessionGuardControlPlane(options.UiScenarioName);
        }
        else
        {
            configurationRepository = new JsonConfigurationRepository(runtimePaths);
            var snapshotStore = new JsonScanSnapshotStore(runtimePaths);
            var mitigationService = new WindowsMitigationService(runtimePaths, logger);
            var policyApprovalStore = new FilePolicyApprovalStore(runtimePaths, logger);
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
                policyApprovalStore,
                logger);

            var localControlPlane = new LocalSessionGuardControlPlane(
                coordinator,
                configurationRepository,
                mitigationService,
                policyApprovalStore,
                snapshotStore);
            var remoteControlPlane = new NamedPipeSessionGuardControlPlane();
            controlPlane = new HybridSessionGuardControlPlane(
                remoteControlPlane,
                localControlPlane,
                logger);
        }

        var viewModel = new MainWindowViewModel(
            controlPlane,
            configurationRepository,
            logger,
            runtimePaths,
            options.ForceStartMinimized,
            options.ForceTechnicalView,
            options.UseTrayIcon);

        return new MainWindow(
            viewModel,
            options.UseTrayIcon,
            options.ForceStartMinimized && options.UseTrayIcon);
    }
}
