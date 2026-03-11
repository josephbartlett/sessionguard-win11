using SessionGuard.Core.Services;
using SessionGuard.Infrastructure.Configuration;
using SessionGuard.Infrastructure.Diagnostics;
using SessionGuard.Infrastructure.Environment;
using SessionGuard.Infrastructure.Services;
using SessionGuard.Service;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "SessionGuard Service";
});

builder.Services.AddSingleton(_ => RuntimePaths.Discover(AppContext.BaseDirectory));
builder.Services.AddSingleton<IAppLogger, FileAppLogger>();
builder.Services.AddSingleton<IScanSnapshotStore, JsonScanSnapshotStore>();
builder.Services.AddSingleton<IConfigurationRepository, JsonConfigurationRepository>();
builder.Services.AddSingleton<IMitigationService, WindowsMitigationService>();
builder.Services.AddSingleton<IProtectedWorkspaceDetector, ProcessInventoryService>();
builder.Services.AddSingleton<IRestartSignalProvider, RegistryRestartSignalProvider>();
builder.Services.AddSingleton<IRestartSignalProvider, WindowsUpdateAgentSignalProvider>();
builder.Services.AddSingleton<IRestartSignalProvider, WindowsUpdateUxSettingsSignalProvider>();
builder.Services.AddSingleton<IRestartSignalProvider, WindowsUpdateScheduledTaskSignalProvider>();
builder.Services.AddSingleton<SessionGuardCoordinator>();
builder.Services.AddHostedService<SessionGuardWorker>();

var host = builder.Build();
host.Run();
