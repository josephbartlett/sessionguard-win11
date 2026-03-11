using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Threading;
using SessionGuard.Core.Configuration;
using SessionGuard.Core.Models;
using SessionGuard.Core.Services;
using SessionGuard.Infrastructure.Environment;
using Brush = System.Windows.Media.Brush;
using BrushConverter = System.Windows.Media.BrushConverter;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace SessionGuard.App.ViewModels;

public sealed class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly ISessionGuardControlPlane _controlPlane;
    private readonly IConfigurationRepository _configurationRepository;
    private readonly IAppLogger _logger;
    private readonly RuntimePaths _runtimePaths;
    private readonly DispatcherTimer _timer;
    private readonly EventHandler _timerHandler;
    private readonly SemaphoreSlim _scanLock = new(1, 1);
    private readonly AsyncRelayCommand _scanNowCommand;
    private readonly AsyncRelayCommand _applyMitigationsCommand;
    private readonly AsyncRelayCommand _resetMitigationsCommand;
    private readonly AsyncRelayCommand _grantRestartApprovalCommand;
    private readonly AsyncRelayCommand _clearRestartApprovalCommand;
    private readonly RelayCommand _openConfigCommand;
    private readonly RelayCommand _openPoliciesCommand;
    private readonly RelayCommand _openLogsCommand;
    private readonly RelayCommand _openWindowsUpdateSettingsCommand;

    private WarningBehaviorOptions _warningBehavior = new();
    private RestartRiskLevel _previousRiskLevel;
    private bool _disposed;
    private bool _guardModeEnabled;
    private bool _guardModeInitialized;
    private bool _serviceWriteActionsAvailable;
    private bool _suppressGuardModeRefresh;
    private bool _isBusy;
    private bool _showDetailedSignals = true;
    private bool _shouldStartMinimized;
    private string _currentStatusText = "Unknown / Limited Visibility";
    private string _restartRiskText = "Unknown";
    private string _protectionModeText = "Unavailable";
    private string _pendingRestartText = "Not scanned";
    private string _adminAccessText = "Checking";
    private string _lastScanText = "Last scan: not yet run";
    private string _statusSummary = "Waiting for the first scan.";
    private string _signalOverviewText = "Signal overview: not yet scanned";
    private string _providerCoverageText = "Providers: not yet scanned";
    private string _connectionModeText = "Control plane: not yet determined";
    private string _policyDecisionText = "Policy engine: not yet scanned";
    private string _policySummaryText = "Policy summary: not yet scanned";
    private string _policyApprovalText = "Policy approval: not yet scanned";
    private string _policyDiagnosticsText = "Policy config: not yet scanned";
    private string _serviceActionAvailabilityText = "Managed actions: availability not yet scanned";
    private string _protectedProcessSummary = "Protected processes: not yet scanned";
    private string _workspaceSummaryText = "Workspace safety: not yet scanned";
    private string _workspaceConfidenceText = "Workspace confidence: not yet scanned";
    private string _workspaceSnapshotText = "Workspace snapshot: not yet scanned";
    private string _lastActionMessage = "SessionGuard is ready.";
    private string _configurationDirectoryText = string.Empty;
    private string _policyDirectoryText = string.Empty;
    private string _logDirectoryText = string.Empty;
    private string _policiesPath = string.Empty;
    private Brush _statusBrush = CreateBrush("#64748B");
    private Brush _riskBrush = CreateBrush("#64748B");

    public MainWindowViewModel(
        ISessionGuardControlPlane controlPlane,
        IConfigurationRepository configurationRepository,
        IAppLogger logger,
        RuntimePaths runtimePaths)
    {
        _controlPlane = controlPlane;
        _configurationRepository = configurationRepository;
        _logger = logger;
        _runtimePaths = runtimePaths;

        ProtectedProcesses = new ObservableCollection<ProtectedProcessMatch>();
        MatchedPolicyRules = new ObservableCollection<PolicyRuleMatch>();
        PolicyValidationIssues = new ObservableCollection<PolicyValidationIssue>();
        PolicyEvaluationTraceItems = new ObservableCollection<string>();
        WorkspaceRiskItems = new ObservableCollection<WorkspaceRiskItem>();
        RestartIndicators = new ObservableCollection<RestartIndicator>();
        ManagedMitigations = new ObservableCollection<ManagedMitigationState>();
        Recommendations = new ObservableCollection<string>();

        _timer = new DispatcherTimer();
        _timerHandler = async (_, _) => await RefreshAsync(initialLoad: false, forceScan: false, explicitGuardMode: null, skipIfBusy: true);
        _timer.Tick += _timerHandler;

        _scanNowCommand = new AsyncRelayCommand(
            () => RefreshAsync(initialLoad: false, forceScan: true, explicitGuardMode: null, skipIfBusy: false),
            () => !IsBusy);
        _applyMitigationsCommand = new AsyncRelayCommand(ApplyMitigationsAsync, () => !IsBusy && ServiceWriteActionsAvailable);
        _resetMitigationsCommand = new AsyncRelayCommand(ResetMitigationsAsync, () => !IsBusy && ServiceWriteActionsAvailable);
        _grantRestartApprovalCommand = new AsyncRelayCommand(GrantRestartApprovalAsync, () => !IsBusy && ServiceWriteActionsAvailable);
        _clearRestartApprovalCommand = new AsyncRelayCommand(ClearRestartApprovalAsync, () => !IsBusy && ServiceWriteActionsAvailable);
        _openConfigCommand = new RelayCommand(() => OpenPath(_runtimePaths.ConfigDirectory), () => !IsBusy);
        _openPoliciesCommand = new RelayCommand(() => OpenPath(_policiesPath), () => !IsBusy && !string.IsNullOrWhiteSpace(_policiesPath));
        _openLogsCommand = new RelayCommand(() => OpenPath(_runtimePaths.LogDirectory), () => !IsBusy);
        _openWindowsUpdateSettingsCommand = new RelayCommand(OpenWindowsUpdateSettings, () => !IsBusy);
    }

    public event EventHandler? AttentionRequested;

    public ObservableCollection<ProtectedProcessMatch> ProtectedProcesses { get; }

    public ObservableCollection<PolicyRuleMatch> MatchedPolicyRules { get; }

    public ObservableCollection<PolicyValidationIssue> PolicyValidationIssues { get; }

    public ObservableCollection<string> PolicyEvaluationTraceItems { get; }

    public ObservableCollection<WorkspaceRiskItem> WorkspaceRiskItems { get; }

    public ObservableCollection<RestartIndicator> RestartIndicators { get; }

    public ObservableCollection<ManagedMitigationState> ManagedMitigations { get; }

    public ObservableCollection<string> Recommendations { get; }

    public AsyncRelayCommand ScanNowCommand => _scanNowCommand;

    public AsyncRelayCommand ApplyMitigationsCommand => _applyMitigationsCommand;

    public AsyncRelayCommand ResetMitigationsCommand => _resetMitigationsCommand;

    public AsyncRelayCommand GrantRestartApprovalCommand => _grantRestartApprovalCommand;

    public AsyncRelayCommand ClearRestartApprovalCommand => _clearRestartApprovalCommand;

    public RelayCommand OpenConfigCommand => _openConfigCommand;

    public RelayCommand OpenPoliciesCommand => _openPoliciesCommand;

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
                _ = RefreshAsync(initialLoad: false, forceScan: false, explicitGuardMode: value, skipIfBusy: false);
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
                _grantRestartApprovalCommand.RaiseCanExecuteChanged();
                _clearRestartApprovalCommand.RaiseCanExecuteChanged();
                _openConfigCommand.RaiseCanExecuteChanged();
                _openPoliciesCommand.RaiseCanExecuteChanged();
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

    public bool ServiceWriteActionsAvailable
    {
        get => _serviceWriteActionsAvailable;
        private set
        {
            if (SetProperty(ref _serviceWriteActionsAvailable, value))
            {
                _applyMitigationsCommand.RaiseCanExecuteChanged();
                _resetMitigationsCommand.RaiseCanExecuteChanged();
                _grantRestartApprovalCommand.RaiseCanExecuteChanged();
                _clearRestartApprovalCommand.RaiseCanExecuteChanged();
            }
        }
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

    public string SignalOverviewText
    {
        get => _signalOverviewText;
        private set => SetProperty(ref _signalOverviewText, value);
    }

    public string ProviderCoverageText
    {
        get => _providerCoverageText;
        private set => SetProperty(ref _providerCoverageText, value);
    }

    public string ConnectionModeText
    {
        get => _connectionModeText;
        private set => SetProperty(ref _connectionModeText, value);
    }

    public string PolicyDecisionText
    {
        get => _policyDecisionText;
        private set => SetProperty(ref _policyDecisionText, value);
    }

    public string PolicySummaryText
    {
        get => _policySummaryText;
        private set => SetProperty(ref _policySummaryText, value);
    }

    public string PolicyApprovalText
    {
        get => _policyApprovalText;
        private set => SetProperty(ref _policyApprovalText, value);
    }

    public string PolicyDiagnosticsText
    {
        get => _policyDiagnosticsText;
        private set => SetProperty(ref _policyDiagnosticsText, value);
    }

    public string ServiceActionAvailabilityText
    {
        get => _serviceActionAvailabilityText;
        private set => SetProperty(ref _serviceActionAvailabilityText, value);
    }

    public string ProtectedProcessSummary
    {
        get => _protectedProcessSummary;
        private set => SetProperty(ref _protectedProcessSummary, value);
    }

    public string WorkspaceSummaryText
    {
        get => _workspaceSummaryText;
        private set => SetProperty(ref _workspaceSummaryText, value);
    }

    public string WorkspaceConfidenceText
    {
        get => _workspaceConfidenceText;
        private set => SetProperty(ref _workspaceConfidenceText, value);
    }

    public string WorkspaceSnapshotText
    {
        get => _workspaceSnapshotText;
        private set => SetProperty(ref _workspaceSnapshotText, value);
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

    public string PolicyDirectoryText
    {
        get => _policyDirectoryText;
        private set => SetProperty(ref _policyDirectoryText, value);
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

        await RefreshAsync(initialLoad: true, forceScan: false, explicitGuardMode: null, skipIfBusy: false);
        _timer.Start();
    }

    public async Task RefreshAsync()
    {
        await RefreshAsync(initialLoad: false, forceScan: false, explicitGuardMode: null, skipIfBusy: false);
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

    private async Task RefreshAsync(
        bool initialLoad,
        bool forceScan,
        bool? explicitGuardMode,
        bool skipIfBusy)
    {
        if (skipIfBusy)
        {
            if (!await _scanLock.WaitAsync(0))
            {
                return;
            }
        }
        else
        {
            await _scanLock.WaitAsync();
        }

        try
        {
            IsBusy = true;
            var configuration = await _configurationRepository.LoadAsync();
            ApplyConfiguration(configuration, initialLoad);

            SessionControlStatus status = explicitGuardMode.HasValue
                ? await _controlPlane.SetGuardModeAsync(explicitGuardMode.Value)
                : forceScan
                    ? await _controlPlane.ScanNowAsync()
                    : await _controlPlane.GetStatusAsync();

            ApplyScanResult(status);

            if (_warningBehavior.RaiseWindowOnHighRisk &&
                GuardModeEnabled &&
                status.ScanResult.RiskLevel == RestartRiskLevel.High &&
                _previousRiskLevel != RestartRiskLevel.High)
            {
                AttentionRequested?.Invoke(this, EventArgs.Empty);
            }

            _previousRiskLevel = status.ScanResult.RiskLevel;
        }
        catch (Exception exception)
        {
            _logger.Error("view.refresh.failed", exception);
            CurrentStatusText = "Unknown / Limited Visibility";
            RestartRiskText = "Unknown";
            ProtectionModeText = "Unavailable";
            PendingRestartText = "Scan failed";
            AdminAccessText = "Unavailable";
            LastScanText = $"Last scan failed at {DateTime.Now:t}";
            StatusSummary = "SessionGuard could not complete the scan. Review the configuration files and logs for details.";
            SignalOverviewText = "Signal overview unavailable.";
            ProviderCoverageText = "Providers: scan failed";
            ConnectionModeText = "Control plane: unavailable";
            PolicyDecisionText = "Policy engine: unavailable";
            PolicySummaryText = "Policy summary unavailable.";
            PolicyApprovalText = "Policy approval unavailable.";
            PolicyDiagnosticsText = "Policy config unavailable.";
            ServiceWriteActionsAvailable = false;
            ServiceActionAvailabilityText = "Managed actions: unavailable because the background service is unreachable.";
            ProtectedProcessSummary = "Protected process detection unavailable.";
            WorkspaceSummaryText = "Workspace safety detection unavailable.";
            WorkspaceConfidenceText = "Workspace confidence: unavailable";
            WorkspaceSnapshotText = "Workspace snapshot: unavailable";
            LastActionMessage = $"Scan failed: {exception.Message}";
            StatusBrush = CreateBrush("#64748B");
            RiskBrush = CreateBrush("#64748B");
            ReplaceItems(ProtectedProcesses, Array.Empty<ProtectedProcessMatch>());
            ReplaceItems(MatchedPolicyRules, Array.Empty<PolicyRuleMatch>());
            ReplaceItems(PolicyValidationIssues, Array.Empty<PolicyValidationIssue>());
            ReplaceItems(PolicyEvaluationTraceItems, Array.Empty<string>());
            ReplaceItems(WorkspaceRiskItems, Array.Empty<WorkspaceRiskItem>());
            ReplaceItems(RestartIndicators, Array.Empty<RestartIndicator>());
            ReplaceItems(ManagedMitigations, Array.Empty<ManagedMitigationState>());
            ReplaceItems(
                Recommendations,
                new[]
                {
                    "Verify that the service is running or that config/appsettings.json and config/protected-processes.json are still valid.",
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
            var result = await _controlPlane.ApplyRecommendedAsync();
            LastActionMessage = result.Message;
            ReplaceItems(ManagedMitigations, result.CurrentStates);
            if (result.Success)
            {
                await RefreshAsync(initialLoad: false, forceScan: true, explicitGuardMode: null, skipIfBusy: false);
            }
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
            var result = await _controlPlane.ResetManagedAsync();
            LastActionMessage = result.Message;
            ReplaceItems(ManagedMitigations, result.CurrentStates);
            if (result.Success)
            {
                await RefreshAsync(initialLoad: false, forceScan: true, explicitGuardMode: null, skipIfBusy: false);
            }
        }
        catch (Exception exception)
        {
            _logger.Error("view.reset_mitigation.failed", exception);
            LastActionMessage = $"Failed to reset mitigation: {exception.Message}";
        }
    }

    private async Task GrantRestartApprovalAsync()
    {
        try
        {
            var result = await _controlPlane.GrantRestartApprovalAsync();
            LastActionMessage = result.Message;
            if (result.Success)
            {
                await RefreshAsync(initialLoad: false, forceScan: true, explicitGuardMode: null, skipIfBusy: false);
            }
        }
        catch (Exception exception)
        {
            _logger.Error("view.policy_approval.grant.failed", exception);
            LastActionMessage = $"Failed to grant restart approval: {exception.Message}";
        }
    }

    private async Task ClearRestartApprovalAsync()
    {
        try
        {
            var result = await _controlPlane.ClearRestartApprovalAsync();
            LastActionMessage = result.Message;
            if (result.Success)
            {
                await RefreshAsync(initialLoad: false, forceScan: true, explicitGuardMode: null, skipIfBusy: false);
            }
        }
        catch (Exception exception)
        {
            _logger.Error("view.policy_approval.clear.failed", exception);
            LastActionMessage = $"Failed to clear restart approval: {exception.Message}";
        }
    }

    private void ApplyConfiguration(RuntimeConfiguration configuration, bool initialLoad)
    {
        var settings = configuration.AppSettings;
        _warningBehavior = settings.WarningBehavior;
        ShowDetailedSignals = settings.UiPreferences.ShowDetailedSignals;
        ConfigurationDirectoryText = $"Config: {configuration.ConfigurationDirectory}";
        PolicyDirectoryText = $"Policies: {configuration.PoliciesPath}";
        LogDirectoryText = $"Logs: {_logger.LogDirectory}";
        _policiesPath = configuration.PoliciesPath;

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

    private void ApplyScanResult(SessionControlStatus status)
    {
        _suppressGuardModeRefresh = true;
        GuardModeEnabled = status.GuardModeEnabled;
        _suppressGuardModeRefresh = false;

        var result = status.ScanResult;
        CurrentStatusText = FormatState(result.State);
        RestartRiskText = FormatRisk(result.RiskLevel);
        ProtectionModeText = FormatProtectionMode(result.ProtectionMode);
        PendingRestartText = result.RestartPending ? "Pending" : "Not detected";
        if (!result.RestartPending && result.HasAmbiguousSignals)
        {
            PendingRestartText = "Ambiguous / review signals";
        }

        AdminAccessText = result.IsElevated ? "Elevated" : "Read-only until run as administrator";
        LastScanText = $"Last scan: {result.Timestamp.LocalDateTime:G}";
        StatusSummary = result.Summary;
        SignalOverviewText = result.SignalOverview.Summary;
        ProviderCoverageText = $"Providers: {result.SignalOverview.ProviderCount} total, {result.SignalOverview.ProvidersWithLimitedVisibility} limited, {result.SignalOverview.ActiveIndicators} active signals";
        ConnectionModeText = status.IsRemote
            ? "Control plane: Service (background service is authoritative)"
            : "Control plane: Local fallback (the dashboard is scanning in-process because the service is unavailable)";
        ServiceWriteActionsAvailable = status.IsRemote;
        ServiceActionAvailabilityText = status.IsRemote
            ? "Managed actions: service-backed mitigation and approval changes are available."
            : "Managed actions: mitigation and approval changes are disabled in local fallback until the background service reconnects.";
        PolicyDecisionText = result.Policy.Validation.HasErrors
            ? "Policy decision: Unavailable due to configuration errors"
            : $"Policy decision: {FormatPolicyDecision(result.Policy.Decision)}";
        PolicySummaryText = result.Policy.Summary;
        PolicyApprovalText = BuildPolicyApprovalText(result.Policy);
        PolicyDiagnosticsText = result.Policy.Validation.Summary;
        ProtectedProcessSummary = result.ProtectedProcesses.Count == 0
            ? "Protected processes: none detected"
            : $"Protected processes: {string.Join(", ", result.ProtectedProcesses.Select(match => $"{match.DisplayName} x{match.InstanceCount}"))}";
        WorkspaceSummaryText = result.Workspace.Summary;
        WorkspaceConfidenceText = $"Workspace confidence: {FormatWorkspaceConfidence(result.Workspace.Confidence)}";
        WorkspaceSnapshotText = result.Workspace.HasRisk
            ? "Workspace snapshot: advisory metadata written to state/workspace-snapshot.json"
            : "Workspace snapshot: no advisory snapshot written";
        StatusBrush = CreateBrush(GetStatusColor(result.State));
        RiskBrush = CreateBrush(GetRiskColor(result.RiskLevel));

        ReplaceItems(ProtectedProcesses, result.ProtectedProcesses);
        ReplaceItems(MatchedPolicyRules, result.Policy.MatchedRules);
        ReplaceItems(PolicyValidationIssues, result.Policy.Validation.Issues);
        ReplaceItems(PolicyEvaluationTraceItems, result.Policy.EvaluationTrace);
        ReplaceItems(WorkspaceRiskItems, result.Workspace.RiskItems);
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
            ProtectionMode.PolicyGuardActive => "Policy guard active",
            ProtectionMode.PolicyApprovalWindow => "Policy approval window active",
            ProtectionMode.ManagedMitigationsApplied => "Managed mitigations applied",
            ProtectionMode.LimitedReadOnly => "Read-only",
            _ => "Read-only"
        };
    }

    private static string FormatPolicyDecision(PolicyDecisionType decision)
    {
        return decision switch
        {
            PolicyDecisionType.None => "No active constraints",
            PolicyDecisionType.RestartBlocked => "Restart blocked by policy",
            PolicyDecisionType.ApprovalRequired => "Approval required",
            PolicyDecisionType.ApprovalActive => "Approval window active",
            _ => "Unknown"
        };
    }

    private static string BuildPolicyApprovalText(PolicyEvaluation policy)
    {
        if (policy.Validation.HasErrors)
        {
            return "Policy approval: unavailable until policy configuration errors are fixed";
        }

        if (policy.ApprovalActive && policy.ApprovalExpiresAt.HasValue)
        {
            return $"Policy approval: active until {policy.ApprovalExpiresAt.Value.LocalDateTime:G}";
        }

        if (policy.RequiresApproval)
        {
            return $"Policy approval: required before restart ({policy.RecommendedApprovalWindowMinutes} minute default window)";
        }

        return "Policy approval: not required";
    }

    private static string FormatWorkspaceConfidence(WorkspaceConfidence confidence)
    {
        return confidence switch
        {
            WorkspaceConfidence.Low => "Low",
            WorkspaceConfidence.Medium => "Medium",
            WorkspaceConfidence.High => "High",
            _ => "Unknown"
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
