using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Media;
using System.Windows.Threading;
using SessionGuard.Core.Configuration;
using SessionGuard.Core.Models;
using SessionGuard.Core.Services;
using SessionGuard.Infrastructure.Environment;

namespace SessionGuard.App.ViewModels;

public sealed class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly SessionGuardCoordinator _coordinator;
    private readonly IConfigurationRepository _configurationRepository;
    private readonly IMitigationService _mitigationService;
    private readonly IAppLogger _logger;
    private readonly RuntimePaths _runtimePaths;
    private readonly DispatcherTimer _timer;
    private readonly EventHandler _timerHandler;
    private readonly SemaphoreSlim _scanLock = new(1, 1);
    private readonly AsyncRelayCommand _scanNowCommand;
    private readonly AsyncRelayCommand _applyMitigationsCommand;
    private readonly AsyncRelayCommand _resetMitigationsCommand;
    private readonly RelayCommand _openConfigCommand;
    private readonly RelayCommand _openLogsCommand;
    private readonly RelayCommand _openWindowsUpdateSettingsCommand;

    private WarningBehaviorOptions _warningBehavior = new();
    private RestartRiskLevel _previousRiskLevel;
    private bool _disposed;
    private bool _guardModeEnabled;
    private bool _guardModeInitialized;
    private bool _suppressGuardModeRefresh;
    private bool _isBusy;
    private bool _showDetailedSignals = true;
    private bool _shouldStartMinimized;
    private string _currentStatusText = "Unknown / Limited Visibility";
    private string _restartRiskText = "Unknown";
    private string _protectionModeText = "Read-only";
    private string _pendingRestartText = "Not scanned";
    private string _adminAccessText = "Checking";
    private string _lastScanText = "Last scan: not yet run";
    private string _statusSummary = "Waiting for the first scan.";
    private string _protectedProcessSummary = "Protected processes: not yet scanned";
    private string _lastActionMessage = "SessionGuard is ready.";
    private string _configurationDirectoryText = string.Empty;
    private string _logDirectoryText = string.Empty;
    private Brush _statusBrush = CreateBrush("#64748B");
    private Brush _riskBrush = CreateBrush("#64748B");

    public MainWindowViewModel(
        SessionGuardCoordinator coordinator,
        IConfigurationRepository configurationRepository,
        IMitigationService mitigationService,
        IAppLogger logger,
        RuntimePaths runtimePaths)
    {
        _coordinator = coordinator;
        _configurationRepository = configurationRepository;
        _mitigationService = mitigationService;
        _logger = logger;
        _runtimePaths = runtimePaths;

        ProtectedProcesses = new ObservableCollection<ProtectedProcessMatch>();
        RestartIndicators = new ObservableCollection<RestartIndicator>();
        ManagedMitigations = new ObservableCollection<ManagedMitigationState>();
        Recommendations = new ObservableCollection<string>();

        _timer = new DispatcherTimer();
        _timerHandler = async (_, _) => await RefreshAsync();
        _timer.Tick += _timerHandler;

        _scanNowCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        _applyMitigationsCommand = new AsyncRelayCommand(ApplyMitigationsAsync, () => !IsBusy);
        _resetMitigationsCommand = new AsyncRelayCommand(ResetMitigationsAsync, () => !IsBusy);
        _openConfigCommand = new RelayCommand(() => OpenPath(_runtimePaths.ConfigDirectory), () => !IsBusy);
        _openLogsCommand = new RelayCommand(() => OpenPath(_runtimePaths.LogDirectory), () => !IsBusy);
        _openWindowsUpdateSettingsCommand = new RelayCommand(OpenWindowsUpdateSettings, () => !IsBusy);
    }

    public event EventHandler? AttentionRequested;

    public ObservableCollection<ProtectedProcessMatch> ProtectedProcesses { get; }

    public ObservableCollection<RestartIndicator> RestartIndicators { get; }

    public ObservableCollection<ManagedMitigationState> ManagedMitigations { get; }

    public ObservableCollection<string> Recommendations { get; }

    public AsyncRelayCommand ScanNowCommand => _scanNowCommand;

    public AsyncRelayCommand ApplyMitigationsCommand => _applyMitigationsCommand;

    public AsyncRelayCommand ResetMitigationsCommand => _resetMitigationsCommand;

    public RelayCommand OpenConfigCommand => _openConfigCommand;

    public RelayCommand OpenLogsCommand => _openLogsCommand;

    public RelayCommand OpenWindowsUpdateSettingsCommand => _openWindowsUpdateSettingsCommand;

    public bool GuardModeEnabled
    {
        get => _guardModeEnabled;
        set
        {
            if (!SetProperty(ref _guardModeEnabled, value))
            {
                return;
            }

            if (_guardModeInitialized)
            {
                _logger.Info("guard_mode.changed", new { enabled = value });
            }

            if (_guardModeInitialized && !_suppressGuardModeRefresh)
            {
                _ = RefreshAsync();
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                _scanNowCommand.RaiseCanExecuteChanged();
                _applyMitigationsCommand.RaiseCanExecuteChanged();
                _resetMitigationsCommand.RaiseCanExecuteChanged();
                _openConfigCommand.RaiseCanExecuteChanged();
                _openLogsCommand.RaiseCanExecuteChanged();
                _openWindowsUpdateSettingsCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool ShowDetailedSignals
    {
        get => _showDetailedSignals;
        private set => SetProperty(ref _showDetailedSignals, value);
    }

    public bool ShouldStartMinimized
    {
        get => _shouldStartMinimized;
        private set => SetProperty(ref _shouldStartMinimized, value);
    }

    public string CurrentStatusText
    {
        get => _currentStatusText;
        private set => SetProperty(ref _currentStatusText, value);
    }

    public string RestartRiskText
    {
        get => _restartRiskText;
        private set => SetProperty(ref _restartRiskText, value);
    }

    public string ProtectionModeText
    {
        get => _protectionModeText;
        private set => SetProperty(ref _protectionModeText, value);
    }

    public string PendingRestartText
    {
        get => _pendingRestartText;
        private set => SetProperty(ref _pendingRestartText, value);
    }

    public string AdminAccessText
    {
        get => _adminAccessText;
        private set => SetProperty(ref _adminAccessText, value);
    }

    public string LastScanText
    {
        get => _lastScanText;
        private set => SetProperty(ref _lastScanText, value);
    }

    public string StatusSummary
    {
        get => _statusSummary;
        private set => SetProperty(ref _statusSummary, value);
    }

    public string ProtectedProcessSummary
    {
        get => _protectedProcessSummary;
        private set => SetProperty(ref _protectedProcessSummary, value);
    }

    public string LastActionMessage
    {
        get => _lastActionMessage;
        private set => SetProperty(ref _lastActionMessage, value);
    }

    public string ConfigurationDirectoryText
    {
        get => _configurationDirectoryText;
        private set => SetProperty(ref _configurationDirectoryText, value);
    }

    public string LogDirectoryText
    {
        get => _logDirectoryText;
        private set => SetProperty(ref _logDirectoryText, value);
    }

    public Brush StatusBrush
    {
        get => _statusBrush;
        private set => SetProperty(ref _statusBrush, value);
    }

    public Brush RiskBrush
    {
        get => _riskBrush;
        private set => SetProperty(ref _riskBrush, value);
    }

    public async Task InitializeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await RefreshAsync(initialLoad: true);
        _timer.Start();
    }

    public async Task RefreshAsync()
    {
        await RefreshAsync(initialLoad: false);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Stop();
        _timer.Tick -= _timerHandler;
        _scanLock.Dispose();
    }

    private async Task RefreshAsync(bool initialLoad)
    {
        if (!await _scanLock.WaitAsync(0))
        {
            return;
        }

        try
        {
            IsBusy = true;
            var configuration = await _configurationRepository.LoadAsync();
            ApplyConfiguration(configuration, initialLoad);

            var result = await _coordinator.ScanAsync(GuardModeEnabled);
            ApplyScanResult(result);

            if (_warningBehavior.RaiseWindowOnHighRisk &&
                GuardModeEnabled &&
                result.RiskLevel == RestartRiskLevel.High &&
                _previousRiskLevel != RestartRiskLevel.High)
            {
                AttentionRequested?.Invoke(this, EventArgs.Empty);
            }

            _previousRiskLevel = result.RiskLevel;
        }
        catch (Exception exception)
        {
            _logger.Error("view.refresh.failed", exception);
            CurrentStatusText = "Unknown / Limited Visibility";
            RestartRiskText = "Unknown";
            ProtectionModeText = "Read-only";
            PendingRestartText = "Scan failed";
            AdminAccessText = _mitigationService.IsElevated ? "Elevated" : "Read-only";
            LastScanText = $"Last scan failed at {DateTime.Now:t}";
            StatusSummary = "SessionGuard could not complete the scan. Review the configuration files and logs for details.";
            ProtectedProcessSummary = "Protected process detection unavailable.";
            LastActionMessage = $"Scan failed: {exception.Message}";
            StatusBrush = CreateBrush("#64748B");
            RiskBrush = CreateBrush("#64748B");
            ReplaceItems(ProtectedProcesses, Array.Empty<ProtectedProcessMatch>());
            ReplaceItems(RestartIndicators, Array.Empty<RestartIndicator>());
            ReplaceItems(ManagedMitigations, Array.Empty<ManagedMitigationState>());
            ReplaceItems(
                Recommendations,
                new[]
                {
                    "Verify that config/appsettings.json and config/protected-processes.json exist and contain valid JSON.",
                    "Review the latest log file for the underlying exception before retrying."
                });
        }
        finally
        {
            IsBusy = false;
            _scanLock.Release();
        }
    }

    private async Task ApplyMitigationsAsync()
    {
        try
        {
            var configuration = await _configurationRepository.LoadAsync();
            var result = await _mitigationService.ApplyRecommendedAsync(configuration);
            LastActionMessage = result.Message;
            ReplaceItems(ManagedMitigations, result.CurrentStates);
            await RefreshAsync();
        }
        catch (Exception exception)
        {
            _logger.Error("view.apply_mitigation.failed", exception);
            LastActionMessage = $"Failed to apply mitigation: {exception.Message}";
        }
    }

    private async Task ResetMitigationsAsync()
    {
        try
        {
            var configuration = await _configurationRepository.LoadAsync();
            var result = await _mitigationService.ResetManagedAsync(configuration);
            LastActionMessage = result.Message;
            ReplaceItems(ManagedMitigations, result.CurrentStates);
            await RefreshAsync();
        }
        catch (Exception exception)
        {
            _logger.Error("view.reset_mitigation.failed", exception);
            LastActionMessage = $"Failed to reset mitigation: {exception.Message}";
        }
    }

    private void ApplyConfiguration(RuntimeConfiguration configuration, bool initialLoad)
    {
        var settings = configuration.AppSettings;
        _warningBehavior = settings.WarningBehavior;
        ShowDetailedSignals = settings.UiPreferences.ShowDetailedSignals;
        ConfigurationDirectoryText = $"Config: {configuration.ConfigurationDirectory}";
        LogDirectoryText = $"Logs: {_logger.LogDirectory}";

        var nextInterval = TimeSpan.FromSeconds(settings.ScanIntervalSeconds);
        if (_timer.Interval != nextInterval)
        {
            _timer.Interval = nextInterval;
        }

        if (!_guardModeInitialized)
        {
            _suppressGuardModeRefresh = true;
            GuardModeEnabled = settings.GuardModeEnabledByDefault;
            _suppressGuardModeRefresh = false;
            _guardModeInitialized = true;
        }

        if (initialLoad)
        {
            ShouldStartMinimized = settings.UiPreferences.StartMinimized;
        }
    }

    private void ApplyScanResult(SessionScanResult result)
    {
        CurrentStatusText = FormatState(result.State);
        RestartRiskText = FormatRisk(result.RiskLevel);
        ProtectionModeText = FormatProtectionMode(result.ProtectionMode);
        PendingRestartText = result.RestartPending ? "Pending" : "Not detected";
        AdminAccessText = result.IsElevated ? "Elevated" : "Read-only until run as administrator";
        LastScanText = $"Last scan: {result.Timestamp.LocalDateTime:G}";
        StatusSummary = result.Summary;
        ProtectedProcessSummary = result.ProtectedProcesses.Count == 0
            ? "Protected processes: none detected"
            : $"Protected processes: {string.Join(", ", result.ProtectedProcesses.Select(match => $"{match.DisplayName} x{match.InstanceCount}"))}";
        StatusBrush = CreateBrush(GetStatusColor(result.State));
        RiskBrush = CreateBrush(GetRiskColor(result.RiskLevel));

        ReplaceItems(ProtectedProcesses, result.ProtectedProcesses);
        ReplaceItems(RestartIndicators, result.Indicators);
        ReplaceItems(ManagedMitigations, result.Mitigations);
        ReplaceItems(Recommendations, result.Recommendations);
    }

    private void OpenPath(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            _logger.Error("view.open_path.failed", exception, new { path });
            LastActionMessage = $"Could not open path: {path}";
        }
    }

    private void OpenWindowsUpdateSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "ms-settings:windowsupdate-options",
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            _logger.Error("view.open_windows_update_settings.failed", exception);
            LastActionMessage = "Could not open Windows Update options.";
        }
    }

    private static void ReplaceItems<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }

    private static string FormatState(RestartStateCategory state)
    {
        return state switch
        {
            RestartStateCategory.Safe => "Safe",
            RestartStateCategory.RestartPending => "Restart Pending",
            RestartStateCategory.ProtectedSessionActive => "Protected Session Active",
            RestartStateCategory.MitigatedDeferred => "Mitigated / Deferred",
            RestartStateCategory.UnknownLimitedVisibility => "Unknown / Limited Visibility",
            _ => "Unknown / Limited Visibility"
        };
    }

    private static string FormatRisk(RestartRiskLevel riskLevel)
    {
        return riskLevel switch
        {
            RestartRiskLevel.Low => "Low",
            RestartRiskLevel.Elevated => "Elevated",
            RestartRiskLevel.High => "High",
            RestartRiskLevel.Unknown => "Unknown",
            _ => "Unknown"
        };
    }

    private static string FormatProtectionMode(ProtectionMode protectionMode)
    {
        return protectionMode switch
        {
            ProtectionMode.MonitorOnly => "Monitor only",
            ProtectionMode.GuardModeActive => "Guard mode active",
            ProtectionMode.ManagedMitigationsApplied => "Managed mitigations applied",
            ProtectionMode.LimitedReadOnly => "Read-only",
            _ => "Read-only"
        };
    }

    private static string GetStatusColor(RestartStateCategory state)
    {
        return state switch
        {
            RestartStateCategory.Safe => "#15803D",
            RestartStateCategory.RestartPending => "#C2410C",
            RestartStateCategory.ProtectedSessionActive => "#B91C1C",
            RestartStateCategory.MitigatedDeferred => "#1D4ED8",
            RestartStateCategory.UnknownLimitedVisibility => "#64748B",
            _ => "#64748B"
        };
    }

    private static string GetRiskColor(RestartRiskLevel riskLevel)
    {
        return riskLevel switch
        {
            RestartRiskLevel.Low => "#15803D",
            RestartRiskLevel.Elevated => "#C2410C",
            RestartRiskLevel.High => "#B91C1C",
            RestartRiskLevel.Unknown => "#64748B",
            _ => "#64748B"
        };
    }

    private static Brush CreateBrush(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFrom(hex)!;
        brush.Freeze();
        return brush;
    }
}
