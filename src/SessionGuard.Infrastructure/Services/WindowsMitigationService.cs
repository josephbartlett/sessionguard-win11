using System.Security.Principal;
using System.Text.Json;
using Microsoft.Win32;
using SessionGuard.Core.Configuration;
using SessionGuard.Core.Models;
using SessionGuard.Core.Services;
using SessionGuard.Infrastructure.Environment;

namespace SessionGuard.Infrastructure.Services;

public sealed class WindowsMitigationService : IMitigationService
{
    private const string WindowsUpdatePolicyPath = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate";
    private const string WindowsUpdateAuPolicyPath = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU";

    private readonly RuntimePaths _paths;
    private readonly IAppLogger _logger;
    private readonly string _backupPath;

    public WindowsMitigationService(RuntimePaths paths, IAppLogger logger)
    {
        _paths = paths;
        _logger = logger;
        _backupPath = Path.Combine(_paths.StateDirectory, "managed-mitigation-backup.json");
    }

    public bool IsElevated => IsRunningElevated();

    public Task<IReadOnlyList<ManagedMitigationState>> GetStatesAsync(
        RuntimeConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var options = configuration.AppSettings.RecommendedMitigations;
        var states = new List<ManagedMitigationState>
        {
            BuildState(
                "no-auto-reboot-logged-on-users",
                "No auto-restart with signed-in users",
                "Sets the Windows Update AU policy that avoids scheduled automatic restarts while users are signed in.",
                WindowsUpdateAuPolicyPath,
                "NoAutoRebootWithLoggedOnUsers",
                1),
            BuildState(
                "active-hours-enabled",
                "Active hours policy enabled",
                "Turns on a policy-managed active hours window so Windows Update defers automatic restarts inside that range.",
                WindowsUpdatePolicyPath,
                "SetActiveHours",
                options.ApplyActiveHoursPolicy ? 1 : 0),
            BuildState(
                "active-hours-start",
                "Active hours start",
                "Policy start hour for the recommended active hours window.",
                WindowsUpdatePolicyPath,
                "ActiveHoursStart",
                options.ActiveHoursStart),
            BuildState(
                "active-hours-end",
                "Active hours end",
                "Policy end hour for the recommended active hours window.",
                WindowsUpdatePolicyPath,
                "ActiveHoursEnd",
                options.ActiveHoursEnd)
        };

        if (!options.ApplyActiveHoursPolicy)
        {
            states = states
                .Where(state => !state.Id.StartsWith("active-hours", StringComparison.OrdinalIgnoreCase) ||
                                state.Id == "active-hours-enabled")
                .Select(state => state.Id == "active-hours-enabled"
                    ? state with
                    {
                        Description = "Active hours policy management is disabled in config for this MVP instance.",
                        RecommendedValue = "0",
                        IsApplied = state.CurrentValue == "0"
                    }
                    : state)
                .ToList();
        }

        return Task.FromResult<IReadOnlyList<ManagedMitigationState>>(states);
    }

    public async Task<MitigationCommandResult> ApplyRecommendedAsync(
        RuntimeConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger.Info("mitigation.apply.start");

        if (!IsElevated)
        {
            var readOnlyStates = await GetStatesAsync(configuration, cancellationToken);
            _logger.Warn("mitigation.apply.requires_admin");
            return new MitigationCommandResult(
                false,
                true,
                "Administrative access is required to apply native Windows Update mitigation settings.",
                readOnlyStates);
        }

        try
        {
            var definitions = GetDefinitions(configuration.AppSettings.RecommendedMitigations);
            var backup = LoadBackup();

            foreach (var definition in definitions)
            {
                CaptureBackupIfMissing(backup, definition);
                WriteDwordValue(definition.SubKeyPath, definition.ValueName, definition.RecommendedValue);
            }

            SaveBackup(backup);
            var states = await GetStatesAsync(configuration, cancellationToken);
            _logger.Info("mitigation.apply.finish", new { applied = states.Count(state => state.IsApplied) });

            return new MitigationCommandResult(
                true,
                false,
                "Applied recommended native restart mitigation settings.",
                states);
        }
        catch (Exception exception)
        {
            _logger.Error("mitigation.apply.failed", exception);
            var states = await GetStatesAsync(configuration, cancellationToken);
            return new MitigationCommandResult(
                false,
                false,
                $"Failed to apply native restart mitigation settings: {exception.Message}",
                states);
        }
    }

    public async Task<MitigationCommandResult> ResetManagedAsync(
        RuntimeConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger.Info("mitigation.reset.start");

        if (!IsElevated)
        {
            var readOnlyStates = await GetStatesAsync(configuration, cancellationToken);
            _logger.Warn("mitigation.reset.requires_admin");
            return new MitigationCommandResult(
                false,
                true,
                "Administrative access is required to reset managed mitigation settings.",
                readOnlyStates);
        }

        try
        {
            var backup = LoadBackup();
            foreach (var definition in GetDefinitions(configuration.AppSettings.RecommendedMitigations))
            {
                if (backup.Values.TryGetValue(GetBackupKey(definition), out var valueBackup) &&
                    valueBackup.Existed &&
                    valueBackup.Value is not null)
                {
                    WriteDwordValue(definition.SubKeyPath, definition.ValueName, valueBackup.Value.Value);
                }
                else
                {
                    DeleteValue(definition.SubKeyPath, definition.ValueName);
                }
            }

            if (File.Exists(_backupPath))
            {
                File.Delete(_backupPath);
            }

            var states = await GetStatesAsync(configuration, cancellationToken);
            _logger.Info("mitigation.reset.finish");

            return new MitigationCommandResult(
                true,
                false,
                "Restored or removed the mitigation values managed by SessionGuard.",
                states);
        }
        catch (Exception exception)
        {
            _logger.Error("mitigation.reset.failed", exception);
            var states = await GetStatesAsync(configuration, cancellationToken);
            return new MitigationCommandResult(
                false,
                false,
                $"Failed to reset managed mitigation settings: {exception.Message}",
                states);
        }
    }

    private static IReadOnlyList<RegistryMitigationDefinition> GetDefinitions(RecommendedMitigationOptions options)
    {
        var definitions = new List<RegistryMitigationDefinition>
        {
            new(
                "no-auto-reboot-logged-on-users",
                WindowsUpdateAuPolicyPath,
                "NoAutoRebootWithLoggedOnUsers",
                1)
        };

        if (options.ApplyActiveHoursPolicy)
        {
            definitions.Add(new RegistryMitigationDefinition("active-hours-enabled", WindowsUpdatePolicyPath, "SetActiveHours", 1));
            definitions.Add(new RegistryMitigationDefinition("active-hours-start", WindowsUpdatePolicyPath, "ActiveHoursStart", options.ActiveHoursStart));
            definitions.Add(new RegistryMitigationDefinition("active-hours-end", WindowsUpdatePolicyPath, "ActiveHoursEnd", options.ActiveHoursEnd));
        }

        return definitions;
    }

    private static bool IsRunningElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private ManagedMitigationState BuildState(
        string id,
        string displayName,
        string description,
        string registryPath,
        string valueName,
        int recommendedValue)
    {
        var currentValue = ReadDwordValue(registryPath, valueName);
        return new ManagedMitigationState(
            id,
            displayName,
            description,
            currentValue == recommendedValue,
            RequiresElevation: true,
            CurrentValue: currentValue?.ToString() ?? "<not set>",
            RecommendedValue: recommendedValue.ToString(),
            RegistryPath: $@"HKLM\{registryPath}\{valueName}");
    }

    private int? ReadDwordValue(string subKeyPath, string valueName)
    {
        using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var subKey = baseKey.OpenSubKey(subKeyPath);
        var rawValue = subKey?.GetValue(valueName);

        return rawValue switch
        {
            int intValue => intValue,
            string stringValue when int.TryParse(stringValue, out var parsedValue) => parsedValue,
            _ => null
        };
    }

    private void WriteDwordValue(string subKeyPath, string valueName, int value)
    {
        using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var subKey = baseKey.CreateSubKey(subKeyPath, writable: true);
        subKey?.SetValue(valueName, value, RegistryValueKind.DWord);
    }

    private void DeleteValue(string subKeyPath, string valueName)
    {
        using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var subKey = baseKey.CreateSubKey(subKeyPath, writable: true);
        subKey?.DeleteValue(valueName, throwOnMissingValue: false);
    }

    private ManagedMitigationBackup LoadBackup()
    {
        if (!File.Exists(_backupPath))
        {
            return new ManagedMitigationBackup();
        }

        var content = File.ReadAllText(_backupPath);
        return JsonSerializer.Deserialize<ManagedMitigationBackup>(content) ?? new ManagedMitigationBackup();
    }

    private void SaveBackup(ManagedMitigationBackup backup)
    {
        var json = JsonSerializer.Serialize(backup, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_backupPath, json);
    }

    private void CaptureBackupIfMissing(ManagedMitigationBackup backup, RegistryMitigationDefinition definition)
    {
        var key = GetBackupKey(definition);
        if (backup.Values.ContainsKey(key))
        {
            return;
        }

        var currentValue = ReadDwordValue(definition.SubKeyPath, definition.ValueName);
        backup.Values[key] = new RegistryValueBackup
        {
            Existed = currentValue.HasValue,
            Value = currentValue
        };
    }

    private static string GetBackupKey(RegistryMitigationDefinition definition)
    {
        return $"{definition.SubKeyPath}|{definition.ValueName}";
    }

    private sealed record RegistryMitigationDefinition(string Id, string SubKeyPath, string ValueName, int RecommendedValue);

    private sealed class ManagedMitigationBackup
    {
        public Dictionary<string, RegistryValueBackup> Values { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class RegistryValueBackup
    {
        public bool Existed { get; init; }

        public int? Value { get; init; }
    }
}
