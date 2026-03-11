using System.Text.Json;
using SessionGuard.Core.Services;
using SessionGuard.Infrastructure.Configuration;
using SessionGuard.Infrastructure.ControlPlane;
using SessionGuard.Infrastructure.Diagnostics;
using SessionGuard.Infrastructure.Environment;
using SessionGuard.Infrastructure.Serialization;
using SessionGuard.Infrastructure.Services;
using SessionGuard.Service;

if (await TryHandleUtilityCommandAsync(args))
{
    return;
}

var host = BuildHost(GetHostArguments(args));
await host.RunAsync();

static async Task<bool> TryHandleUtilityCommandAsync(string[] args)
{
    if (args.Length == 0)
    {
        return false;
    }

    var command = args[0].Trim().ToLowerInvariant();
    switch (command)
    {
        case "run":
        case "console":
        case "--console":
            return false;
        case "probe":
        case "status":
            Environment.ExitCode = await ExecuteControlPlaneCommandAsync(forceScan: false);
            return true;
        case "scan-now":
            Environment.ExitCode = await ExecuteControlPlaneCommandAsync(forceScan: true);
            return true;
        case "health":
            Environment.ExitCode = await ExecuteHealthCommandAsync();
            return true;
        case "help":
        case "--help":
        case "-h":
            PrintHelp();
            Environment.ExitCode = 0;
            return true;
        default:
            Console.Error.WriteLine($"Unsupported SessionGuard.Service command '{args[0]}'.");
            PrintHelp();
            Environment.ExitCode = 1;
            return true;
    }
}

static async Task<int> ExecuteControlPlaneCommandAsync(bool forceScan)
{
    var controlPlane = new NamedPipeSessionGuardControlPlane(TimeSpan.FromSeconds(2));

    try
    {
        var status = forceScan
            ? await controlPlane.ScanNowAsync()
            : await controlPlane.GetStatusAsync();

        Console.WriteLine(JsonSerializer.Serialize(status, SessionGuardJson.Indented));
        return 0;
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"SessionGuard service control plane is unavailable: {exception.Message}");
        return 2;
    }
}

static async Task<int> ExecuteHealthCommandAsync()
{
    var paths = RuntimePaths.Discover(AppContext.BaseDirectory);
    var healthPath = Path.Combine(paths.StateDirectory, "service-health.json");

    if (!File.Exists(healthPath))
    {
        Console.Error.WriteLine($"SessionGuard service health file was not found at '{healthPath}'.");
        return 3;
    }

    Console.WriteLine(await File.ReadAllTextAsync(healthPath));
    return 0;
}

static string[] GetHostArguments(string[] args)
{
    return args.Length > 0 && IsHostRunCommand(args[0])
        ? args[1..]
        : args;
}

static bool IsHostRunCommand(string argument)
{
    return argument.Equals("run", StringComparison.OrdinalIgnoreCase) ||
           argument.Equals("console", StringComparison.OrdinalIgnoreCase) ||
           argument.Equals("--console", StringComparison.OrdinalIgnoreCase);
}

static IHost BuildHost(string[] args)
{
    var builder = Host.CreateApplicationBuilder(args);

    builder.Services.AddWindowsService(options =>
    {
        options.ServiceName = SessionGuardServiceMetadata.ServiceName;
    });

    builder.Services.AddSingleton(_ => RuntimePaths.Discover(AppContext.BaseDirectory));
    builder.Services.AddSingleton<IAppLogger>(provider =>
        new FileAppLogger(provider.GetRequiredService<RuntimePaths>(), "service"));
    builder.Services.AddSingleton<IScanSnapshotStore, JsonScanSnapshotStore>();
    builder.Services.AddSingleton<IConfigurationRepository, JsonConfigurationRepository>();
    builder.Services.AddSingleton<IMitigationService, WindowsMitigationService>();
    builder.Services.AddSingleton<SessionGuardServiceHealthReporter>();
    builder.Services.AddSingleton<IProtectedWorkspaceDetector, ProcessInventoryService>();
    builder.Services.AddSingleton<IRestartSignalProvider, RegistryRestartSignalProvider>();
    builder.Services.AddSingleton<IRestartSignalProvider, WindowsUpdateAgentSignalProvider>();
    builder.Services.AddSingleton<IRestartSignalProvider, WindowsUpdateUxSettingsSignalProvider>();
    builder.Services.AddSingleton<IRestartSignalProvider, WindowsUpdateScheduledTaskSignalProvider>();
    builder.Services.AddSingleton<SessionGuardCoordinator>();
    builder.Services.AddSingleton<SessionGuardServiceRuntime>();
    builder.Services.AddHostedService<SessionGuardWorker>();
    builder.Services.AddHostedService<SessionGuardPipeServer>();

    return builder.Build();
}

static void PrintHelp()
{
    Console.WriteLine("SessionGuard.Service commands:");
    Console.WriteLine("  run | console   Run the host in console mode.");
    Console.WriteLine("  probe | status  Query the service control plane and print the current status JSON.");
    Console.WriteLine("  scan-now        Ask the running service to scan immediately and print the resulting status JSON.");
    Console.WriteLine("  health          Print the latest persisted service health snapshot JSON.");
}
