using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
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
    private readonly bool _forceStartMinimized;
    private readonly DispatcherTimer _timer;
    private readonly EventHandler _timerHandler;
    private readonly SemaphoreSlim _scanLock = new(1, 1);
    private readonly AsyncRelayCommand _scanNowCommand;
    private readonly AsyncRelayCommand _recommendedActionCommand;
    private readonly AsyncRelayCommand _applyMitigationsCommand;
    private readonly AsyncRelayCommand _resetMitigationsCommand;
    private readonly AsyncRelayCommand _grantRestartApprovalCommand;
    private readonly AsyncRelayCommand _clearRestartApprovalCommand;
    private readonly RelayCommand _openConfigCommand;
    private readonly RelayCommand _openPoliciesCommand;
    private readonly RelayCommand _openLogsCommand;
    private readonly RelayCommand _setSimpleViewCommand;
    private readonly RelayCommand _setTechnicalViewCommand;
    private readonly RelayCommand _openWindowsUpdateSettingsCommand;

    private OperatorAlertContext? _operatorAlertContext;
    private WarningBehaviorOptions _warningBehavior = new();
    private RestartRiskLevel _previousRiskLevel;
    private RecommendedActionKind _recommendedActionKind;
    private bool _disposed;
    private bool _guardModeEnabled;
    private bool _guardModeInitialized;
    private bool _recommendedActionVisible;
    private bool _serviceWriteActionsAvailable;
    private bool _showApplyMitigationsAction;
    private bool _showClearApprovalAction;
    private bool _showGrantApprovalAction;
    private bool _showResetMitigationsAction;
    private bool _suppressGuardModeRefresh;
    private bool _isBusy;
    private bool _showDetailedSignals;
    private bool _shouldStartMinimized;
    private string _currentStatusText = "Unknown / Limited Visibility";
    private string _friendlyStatusHeadline = "SessionGuard is checking your machine.";
    private string _friendlyStatusBody = "The latest scan has not completed yet.";
    private string _activeWorkSummaryText = "Active work: not yet scanned.";
    private string _serviceModeSummaryText = "Service mode: not yet scanned.";
    private string _recommendedActionHeadline = "Checking what to do next.";
    private string _recommendedActionDescription = "SessionGuard will suggest the next useful step after the first scan completes.";
    private string _recommendedActionButtonText = string.Empty;
    private string _recommendedActionSupportText = "Advanced details stay available below if you want the technical explanation.";
    private string _friendlyReasonHeadline = "Waiting for the first scan.";
    private string _friendlyReasonBody = "SessionGuard has not finished reading restart, workspace, and policy state yet.";
    private string _friendlyProtectionSummary = "Restart protection state will appear after the first scan.";
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
    private string _policyTimingText = "Policy timing: not yet scanned";
    private string _serviceActionAvailabilityText = "Managed actions: availability not yet scanned";
    private string _protectedProcessSummary = "Protected processes: not yet scanned";
    private string _workspaceSummaryText = "Workspace safety: not yet scanned";
    private string _workspaceConfidenceText = "Workspace confidence: not yet scanned";
    private string _workspaceSnapshotText = "Workspace snapshot: not yet scanned";
    private string _lastActionMessage = string.Empty;
    private string _configurationDirectoryText = string.Empty;
    private string _policyDirectoryText = string.Empty;
    private string _logDirectoryText = string.Empty;
    private string _trayTooltipText = "SessionGuard";
    private string _trayStatusText = "Status: not yet scanned";
    private string _trayModeText = "Mode: not yet scanned";
    private string _trayPolicyText = "Policy: not yet scanned";
    private string _trayTimingText = "Timing: not yet scanned";
    private string _policiesPath = string.Empty;
    private Brush _statusBrush = CreateBrush("#64748B");
    private Brush _riskBrush = CreateBrush("#64748B");

    public MainWindowViewModel(
        ISessionGuardControlPlane controlPlane,
        IConfigurationRepository configurationRepository,
        IAppLogger logger,
        RuntimePaths runtimePaths,
        bool forceStartMinimized = false)
    {
        _controlPlane = controlPlane;
        _configurationRepository = configurationRepository;
        _logger = logger;
        _runtimePaths = runtimePaths;
        _forceStartMinimized = forceStartMinimized;

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
        _recommendedActionCommand = new AsyncRelayCommand(ExecuteRecommendedActionAsync, () => !IsBusy && RecommendedActionVisible);
        _applyMitigationsCommand = new AsyncRelayCommand(ApplyMitigationsAsync, () => !IsBusy && ServiceWriteActionsAvailable);
        _resetMitigationsCommand = new AsyncRelayCommand(ResetMitigationsAsync, () => !IsBusy && ServiceWriteActionsAvailable);
        _grantRestartApprovalCommand = new AsyncRelayCommand(GrantRestartApprovalAsync, () => !IsBusy && ServiceWriteActionsAvailable);
        _clearRestartApprovalCommand = new AsyncRelayCommand(ClearRestartApprovalAsync, () => !IsBusy && ServiceWriteActionsAvailable);
        _openConfigCommand = new RelayCommand(() => OpenPath(_runtimePaths.ConfigDirectory), () => !IsBusy);
        _openPoliciesCommand = new RelayCommand(() => OpenPath(_policiesPath), () => !IsBusy && !string.IsNullOrWhiteSpace(_policiesPath));
        _openLogsCommand = new RelayCommand(() => OpenPath(_runtimePaths.LogDirectory), () => !IsBusy);
        _setSimpleViewCommand = new RelayCommand(() => SetTechnicalView(false), () => !IsBusy);
        _setTechnicalViewCommand = new RelayCommand(() => SetTechnicalView(true), () => !IsBusy);
        _openWindowsUpdateSettingsCommand = new RelayCommand(OpenWindowsUpdateSettings, () => !IsBusy);
    }

    public event EventHandler? AttentionRequested;

    public event EventHandler<OperatorNotificationEventArgs>? NotificationRequested;

    public ObservableCollection<ProtectedProcessMatch> ProtectedProcesses { get; }

    public ObservableCollection<PolicyRuleMatch> MatchedPolicyRules { get; }

    public ObservableCollection<PolicyValidationIssue> PolicyValidationIssues { get; }

    public ObservableCollection<string> PolicyEvaluationTraceItems { get; }

    public ObservableCollection<WorkspaceRiskItem> WorkspaceRiskItems { get; }

    public ObservableCollection<RestartIndicator> RestartIndicators { get; }

    public ObservableCollection<ManagedMitigationState> ManagedMitigations { get; }

    public ObservableCollection<string> Recommendations { get; }

    public AsyncRelayCommand ScanNowCommand => _scanNowCommand;

    public AsyncRelayCommand RecommendedActionCommand => _recommendedActionCommand;

    public AsyncRelayCommand ApplyMitigationsCommand => _applyMitigationsCommand;

    public AsyncRelayCommand ResetMitigationsCommand => _resetMitigationsCommand;

    public AsyncRelayCommand GrantRestartApprovalCommand => _grantRestartApprovalCommand;

    public AsyncRelayCommand ClearRestartApprovalCommand => _clearRestartApprovalCommand;

    public RelayCommand OpenConfigCommand => _openConfigCommand;

    public RelayCommand OpenPoliciesCommand => _openPoliciesCommand;

    public RelayCommand OpenLogsCommand => _openLogsCommand;

    public RelayCommand SetSimpleViewCommand => _setSimpleViewCommand;

    public RelayCommand SetTechnicalViewCommand => _setTechnicalViewCommand;

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
                _recommendedActionCommand.RaiseCanExecuteChanged();
                _applyMitigationsCommand.RaiseCanExecuteChanged();
                _resetMitigationsCommand.RaiseCanExecuteChanged();
                _grantRestartApprovalCommand.RaiseCanExecuteChanged();
                _clearRestartApprovalCommand.RaiseCanExecuteChanged();
                _openConfigCommand.RaiseCanExecuteChanged();
                _openPoliciesCommand.RaiseCanExecuteChanged();
                _openLogsCommand.RaiseCanExecuteChanged();
                _setSimpleViewCommand.RaiseCanExecuteChanged();
                _setTechnicalViewCommand.RaiseCanExecuteChanged();
                _openWindowsUpdateSettingsCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool ShowDetailedSignals
    {
        get => _showDetailedSignals;
        set
        {
            if (SetProperty(ref _showDetailedSignals, value))
            {
                OnPropertyChanged(nameof(SimpleViewEnabled));
                OnPropertyChanged(nameof(TechnicalViewEnabled));
                OnPropertyChanged(nameof(ViewModeHeadline));
                OnPropertyChanged(nameof(ViewModeDescription));
                _setSimpleViewCommand.RaiseCanExecuteChanged();
                _setTechnicalViewCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool SimpleViewEnabled => !ShowDetailedSignals;

    public bool TechnicalViewEnabled => ShowDetailedSignals;

    public string ViewModeHeadline => TechnicalViewEnabled ? "Technical view is on." : "Simple view is on.";

    public string ViewModeDescription => TechnicalViewEnabled
        ? "You are seeing raw restart signals, rule matches, managed settings, and operator file shortcuts."
        : "Simple view keeps the focus on status, guidance, and plain-language reasons. Switch views when you need the raw signals or advanced controls.";

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

    public string FriendlyStatusHeadline
    {
        get => _friendlyStatusHeadline;
        private set => SetProperty(ref _friendlyStatusHeadline, value);
    }

    public string FriendlyStatusBody
    {
        get => _friendlyStatusBody;
        private set => SetProperty(ref _friendlyStatusBody, value);
    }

    public string ActiveWorkSummaryText
    {
        get => _activeWorkSummaryText;
        private set => SetProperty(ref _activeWorkSummaryText, value);
    }

    public string ServiceModeSummaryText
    {
        get => _serviceModeSummaryText;
        private set => SetProperty(ref _serviceModeSummaryText, value);
    }

    public string RecommendedActionHeadline
    {
        get => _recommendedActionHeadline;
        private set => SetProperty(ref _recommendedActionHeadline, value);
    }

    public string RecommendedActionDescription
    {
        get => _recommendedActionDescription;
        private set => SetProperty(ref _recommendedActionDescription, value);
    }

    public string RecommendedActionButtonText
    {
        get => _recommendedActionButtonText;
        private set => SetProperty(ref _recommendedActionButtonText, value);
    }

    public string RecommendedActionSupportText
    {
        get => _recommendedActionSupportText;
        private set => SetProperty(ref _recommendedActionSupportText, value);
    }

    public bool RecommendedActionVisible
    {
        get => _recommendedActionVisible;
        private set
        {
            if (SetProperty(ref _recommendedActionVisible, value))
            {
                _recommendedActionCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string FriendlyReasonHeadline
    {
        get => _friendlyReasonHeadline;
        private set => SetProperty(ref _friendlyReasonHeadline, value);
    }

    public string FriendlyReasonBody
    {
        get => _friendlyReasonBody;
        private set => SetProperty(ref _friendlyReasonBody, value);
    }

    public string FriendlyProtectionSummary
    {
        get => _friendlyProtectionSummary;
        private set => SetProperty(ref _friendlyProtectionSummary, value);
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

    public string PolicyTimingText
    {
        get => _policyTimingText;
        private set => SetProperty(ref _policyTimingText, value);
    }

    public string ServiceActionAvailabilityText
    {
        get => _serviceActionAvailabilityText;
        private set => SetProperty(ref _serviceActionAvailabilityText, value);
    }

    public bool ShowGrantApprovalAction
    {
        get => _showGrantApprovalAction;
        private set => SetProperty(ref _showGrantApprovalAction, value);
    }

    public bool ShowClearApprovalAction
    {
        get => _showClearApprovalAction;
        private set => SetProperty(ref _showClearApprovalAction, value);
    }

    public bool ShowApplyMitigationsAction
    {
        get => _showApplyMitigationsAction;
        private set => SetProperty(ref _showApplyMitigationsAction, value);
    }

    public bool ShowResetMitigationsAction
    {
        get => _showResetMitigationsAction;
        private set => SetProperty(ref _showResetMitigationsAction, value);
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
        private set
        {
            if (SetProperty(ref _lastActionMessage, value))
            {
                OnPropertyChanged(nameof(HasLastActionMessage));
            }
        }
    }

    public bool HasLastActionMessage => !string.IsNullOrWhiteSpace(_lastActionMessage);

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

    public string TrayTooltipText
    {
        get => _trayTooltipText;
        private set => SetProperty(ref _trayTooltipText, value);
    }

    public string TrayStatusText
    {
        get => _trayStatusText;
        private set => SetProperty(ref _trayStatusText, value);
    }

    public string TrayModeText
    {
        get => _trayModeText;
        private set => SetProperty(ref _trayModeText, value);
    }

    public string TrayPolicyText
    {
        get => _trayPolicyText;
        private set => SetProperty(ref _trayPolicyText, value);
    }

    public string TrayTimingText
    {
        get => _trayTimingText;
        private set => SetProperty(ref _trayTimingText, value);
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
            FriendlyStatusHeadline = "SessionGuard could not refresh right now.";
            FriendlyStatusBody = "Monitoring data is temporarily unavailable, so the dashboard is showing a safe failure state instead of stale information.";
            ActiveWorkSummaryText = "Active work: unavailable while the latest scan is failing.";
            ServiceModeSummaryText = "Service mode: unavailable until the service or local scan path recovers.";
            FriendlyReasonHeadline = "The latest scan failed.";
            FriendlyReasonBody = "SessionGuard could not read restart, workspace, or policy state. Review the logs and configuration before trusting the current machine state.";
            FriendlyProtectionSummary = "Restart protection actions are unavailable until SessionGuard reconnects.";
            _recommendedActionKind = RecommendedActionKind.ScanNow;
            RecommendedActionHeadline = "Retry after checking the service and config.";
            RecommendedActionDescription = "SessionGuard could not complete the latest scan.";
            RecommendedActionButtonText = "Scan now";
            RecommendedActionVisible = true;
            RecommendedActionSupportText = "If the failure repeats, open the logs and configuration paths below before retrying.";
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
            PolicyTimingText = "Policy timing unavailable.";
            ServiceWriteActionsAvailable = false;
            ShowGrantApprovalAction = false;
            ShowClearApprovalAction = false;
            ShowApplyMitigationsAction = false;
            ShowResetMitigationsAction = false;
            ServiceActionAvailabilityText = "Managed actions: unavailable because the background service is unreachable.";
            ProtectedProcessSummary = "Protected apps: unavailable.";
            WorkspaceSummaryText = "Workspace safety detection unavailable.";
            WorkspaceConfidenceText = "Workspace confidence: unavailable";
            WorkspaceSnapshotText = "Workspace snapshot: unavailable";
            TrayTooltipText = "SessionGuard - Unavailable";
            TrayStatusText = "Status: unavailable";
            TrayModeText = "Mode: unavailable";
            TrayPolicyText = "Policy: unavailable";
            TrayTimingText = "Timing: unavailable";
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

    private async Task ExecuteRecommendedActionAsync()
    {
        switch (_recommendedActionKind)
        {
            case RecommendedActionKind.GrantApproval:
                await GrantRestartApprovalAsync();
                break;
            case RecommendedActionKind.ApplyMitigations:
                await ApplyMitigationsAsync();
                break;
            case RecommendedActionKind.OpenWindowsUpdateOptions:
                OpenWindowsUpdateSettings();
                break;
            case RecommendedActionKind.OpenPolicies:
                if (!string.IsNullOrWhiteSpace(_policiesPath))
                {
                    OpenPath(_policiesPath);
                }

                break;
            case RecommendedActionKind.ScanNow:
                await RefreshAsync(initialLoad: false, forceScan: true, explicitGuardMode: null, skipIfBusy: false);
                break;
        }
    }

    private void SetTechnicalView(bool enabled)
    {
        ShowDetailedSignals = enabled;
    }

    private void ApplyConfiguration(RuntimeConfiguration configuration, bool initialLoad)
    {
        var settings = configuration.AppSettings;
        _warningBehavior = settings.WarningBehavior;
        ConfigurationDirectoryText = $"Config: {configuration.ConfigurationDirectory}";
        PolicyDirectoryText = $"Policies: {configuration.PoliciesPath}";
        LogDirectoryText = $"Logs: {_logger.LogDirectory}";
        _policiesPath = configuration.PoliciesPath;
        _openPoliciesCommand.RaiseCanExecuteChanged();

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
            ShowDetailedSignals = settings.UiPreferences.ShowDetailedSignals;
            ShouldStartMinimized = settings.UiPreferences.StartMinimized || _forceStartMinimized;
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

        AdminAccessText = result.IsElevated
            ? "Elevated"
            : status.IsRemote
                ? "Service-backed"
                : "Read-only";
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
        var operatorAlert = OperatorAlertEvaluator.Evaluate(
            _operatorAlertContext,
            status,
            _warningBehavior.ApprovalExpiryWarningLeadMinutes);
        PolicyTimingText = operatorAlert.PolicyTimingText;
        TrayTooltipText = operatorAlert.Tray.TooltipText;
        TrayStatusText = operatorAlert.Tray.StatusLine;
        TrayModeText = operatorAlert.Tray.ModeLine;
        TrayPolicyText = operatorAlert.Tray.PolicyLine;
        TrayTimingText = operatorAlert.Tray.TimingLine;
        _operatorAlertContext = operatorAlert.Context;
        if (_warningBehavior.ShowDesktopNotifications)
        {
            foreach (var notification in operatorAlert.Notifications)
            {
                NotificationRequested?.Invoke(this, new OperatorNotificationEventArgs(notification));
            }
        }
        ProtectedProcessSummary = BuildProtectedProcessSummary(result);
        WorkspaceSummaryText = result.Workspace.Summary;
        WorkspaceConfidenceText = $"Workspace confidence: {FormatWorkspaceConfidence(result.Workspace.Confidence)}";
        WorkspaceSnapshotText = result.Workspace.HasRisk
            ? "Workspace snapshot: advisory metadata written to state/workspace-snapshot.json"
            : "Workspace snapshot: no advisory snapshot written";
        StatusBrush = CreateBrush(GetStatusColor(result.State));
        RiskBrush = CreateBrush(GetRiskColor(result.RiskLevel));
        ApplyFriendlyOverview(status, result);

        ReplaceItems(ProtectedProcesses, result.ProtectedProcesses);
        ReplaceItems(MatchedPolicyRules, result.Policy.MatchedRules);
        ReplaceItems(PolicyValidationIssues, result.Policy.Validation.Issues);
        ReplaceItems(PolicyEvaluationTraceItems, result.Policy.EvaluationTrace);
        ReplaceItems(WorkspaceRiskItems, result.Workspace.RiskItems);
        ReplaceItems(RestartIndicators, result.Indicators);
        ReplaceItems(ManagedMitigations, result.Mitigations);
        ReplaceItems(Recommendations, result.Recommendations);
    }

    private void ApplyFriendlyOverview(SessionControlStatus status, SessionScanResult result)
    {
        FriendlyStatusHeadline = BuildFriendlyStatusHeadline(result);
        FriendlyStatusBody = BuildFriendlyStatusBody(result);
        ActiveWorkSummaryText = BuildActiveWorkSummary(result);
        ServiceModeSummaryText = status.IsRemote
            ? "Service mode: the background service is connected and can change protections when needed."
            : "Service mode: monitoring only. The background service is unavailable, so SessionGuard cannot change protections right now.";
        FriendlyReasonHeadline = BuildFriendlyReasonHeadline(result);
        FriendlyReasonBody = BuildFriendlyReasonBody(result);
        FriendlyProtectionSummary = BuildFriendlyProtectionSummary(result);
        ShowGrantApprovalAction = status.IsRemote &&
                                  result.Policy.RequiresApproval &&
                                  !result.Policy.ApprovalActive &&
                                  !result.Policy.Validation.HasErrors;
        ShowClearApprovalAction = status.IsRemote && result.Policy.ApprovalActive;
        ShowApplyMitigationsAction = status.IsRemote && result.Mitigations.Any(mitigation => !mitigation.IsApplied);
        ShowResetMitigationsAction = status.IsRemote && result.Mitigations.Any(mitigation => mitigation.IsApplied);
        ConfigureRecommendedAction(status, result);
    }

    private void ConfigureRecommendedAction(SessionControlStatus status, SessionScanResult result)
    {
        if (result.Policy.Validation.HasErrors)
        {
            SetRecommendedAction(
                RecommendedActionKind.OpenPolicies,
                "Fix the policy file first.",
                "SessionGuard is still monitoring restart risk, but policy-based protection is paused until the policy file is valid again.",
                "Open the policy file to correct the rules and then rescan.",
                "Open policies");
            return;
        }

        if (!status.IsRemote &&
            (result.RestartPending ||
             result.Policy.RequiresApproval ||
             result.Mitigations.Any(mitigation => !mitigation.IsApplied) ||
             result.RiskLevel != RestartRiskLevel.Low))
        {
            SetRecommendedAction(
                RecommendedActionKind.OpenWindowsUpdateOptions,
                "Monitoring is working, but protection changes are offline.",
                "The background service is unavailable, so SessionGuard can explain the risk but cannot change restart protections or approval state right now.",
                "Use Windows Update options for a manual review while the service is offline.",
                "Windows Update options");
            return;
        }

        if (status.IsRemote && result.Policy.RequiresApproval && !result.Policy.ApprovalActive)
        {
            SetRecommendedAction(
                RecommendedActionKind.GrantApproval,
                "Approve a supervised restart window.",
                $"Your current rules require a temporary approval window before restart. The default approval window is {result.Policy.RecommendedApprovalWindowMinutes} minute(s).",
                "Grant approval only when you are ready to supervise the restart.",
                "Grant approval window");
            return;
        }

        if (status.IsRemote && result.Mitigations.Any(mitigation => !mitigation.IsApplied))
        {
            SetRecommendedAction(
                RecommendedActionKind.ApplyMitigations,
                "Apply the recommended protections.",
                "SessionGuard found supported Windows restart settings that are not applied yet.",
                "This keeps Windows Update enabled while reducing avoidable automatic restart disruption.",
                "Apply protections");
            return;
        }

        if (result.RestartPending)
        {
            SetRecommendedAction(
                RecommendedActionKind.None,
                "Plan a supervised restart soon.",
                "Windows still looks restart-sensitive. Save work before you step away, even if SessionGuard does not currently need more from you.",
                "Advanced details below will show the exact restart signals and any matched rules.");
            return;
        }

        if (result.Workspace.HasRisk)
        {
            SetRecommendedAction(
                RecommendedActionKind.None,
                "Save work before leaving the machine.",
                "SessionGuard sees app groups that usually represent active work. It cannot inspect unsaved state, so it is intentionally cautious.",
                "You do not need to click anything right now unless you want to change protections or inspect the technical details.");
            return;
        }

        if (result.Policy.ApprovalActive)
        {
            SetRecommendedAction(
                RecommendedActionKind.None,
                "Approval is already active.",
                "A temporary approval window is in place, so SessionGuard is not asking you to do anything else right now.",
                "If you are finished, you can clear the approval window from Advanced details.");
            return;
        }

        SetRecommendedAction(
            RecommendedActionKind.None,
            "Nothing needs your attention.",
            "SessionGuard did not find a pending restart or risky active workspace in the latest scan.",
            "Leave guard mode on if you want ongoing monitoring. Advanced details stay available below if you want the technical reason.");
    }

    private void SetRecommendedAction(
        RecommendedActionKind kind,
        string headline,
        string description,
        string supportText,
        string? buttonText = null)
    {
        _recommendedActionKind = kind;
        RecommendedActionHeadline = headline;
        RecommendedActionDescription = description;
        RecommendedActionSupportText = supportText;
        RecommendedActionButtonText = buttonText ?? string.Empty;
        RecommendedActionVisible = kind != RecommendedActionKind.None && !string.IsNullOrWhiteSpace(buttonText);
    }

    private static string BuildFriendlyStatusHeadline(SessionScanResult result)
    {
        return result.State switch
        {
            RestartStateCategory.Safe => "You can keep working.",
            RestartStateCategory.RestartPending => "A restart could interrupt you later.",
            RestartStateCategory.ProtectedSessionActive => "Active work could be disrupted by a restart.",
            RestartStateCategory.MitigatedDeferred => "Restart protections are helping right now.",
            RestartStateCategory.UnknownLimitedVisibility => "SessionGuard is being cautious.",
            _ => "SessionGuard is being cautious."
        };
    }

    private static string BuildFriendlyStatusBody(SessionScanResult result)
    {
        if (result.RestartPending && result.Workspace.HasRisk)
        {
            return "Windows restart signals are active and SessionGuard also sees apps that look expensive to reconstruct after a restart.";
        }

        if (result.RestartPending)
        {
            return "Windows indicates that a restart may still be outstanding. Save work before stepping away from the machine.";
        }

        if (result.Workspace.HasRisk)
        {
            return "SessionGuard sees app groups that usually represent active work. It cannot inspect unsaved state, so the warning stays advisory and cautious.";
        }

        if (result.State == RestartStateCategory.MitigatedDeferred)
        {
            return "A temporary approval window or supported Windows restart protections are already helping reduce surprise restart disruption.";
        }

        if (result.LimitedVisibility || result.HasAmbiguousSignals)
        {
            return "Some restart clues are ambiguous or only partially visible, so SessionGuard is leaning cautious instead of claiming the machine is clear.";
        }

        return "No pending restart or risky active workspace was detected in the latest scan.";
    }

    private static string BuildFriendlyReasonHeadline(SessionScanResult result)
    {
        if (result.Policy.Validation.HasErrors)
        {
            return "Policy rules need attention.";
        }

        if (result.RestartPending && result.Workspace.HasRisk)
        {
            return "Windows restart signals and active work are both present.";
        }

        if (result.Workspace.HasRisk)
        {
            return "Active apps look expensive to lose.";
        }

        if (result.RestartPending)
        {
            return "Windows still looks restart-sensitive.";
        }

        if (result.State == RestartStateCategory.MitigatedDeferred)
        {
            return "Supported protections are already active.";
        }

        if (result.LimitedVisibility || result.HasAmbiguousSignals)
        {
            return "Some restart clues are inconclusive.";
        }

        return "The latest scan looks calm.";
    }

    private static string BuildFriendlyReasonBody(SessionScanResult result)
    {
        if (result.Policy.Validation.HasErrors)
        {
            return "SessionGuard can still monitor restart and workspace state, but it will not trust a broken policy file for policy-driven guidance.";
        }

        if (result.RestartPending && result.Workspace.HasRisk)
        {
            return $"{BuildWorkspaceGroupSummary(result)} are active, and Windows is also reporting restart pressure.";
        }

        if (result.Workspace.HasRisk)
        {
            return $"{BuildWorkspaceGroupSummary(result)} are active. SessionGuard cannot verify unsaved tabs, buffers, or shell history, so it treats these as advisory disruption risks.";
        }

        if (result.RestartPending)
        {
            return "Windows and update-related providers indicate that a restart may still be outstanding.";
        }

        if (result.State == RestartStateCategory.MitigatedDeferred)
        {
            return "Native restart settings or a temporary approval window are already in place.";
        }

        if (result.LimitedVisibility || result.HasAmbiguousSignals)
        {
            return "Some Windows Update clues are advisory or partially visible, so SessionGuard is warning conservatively instead of overstating certainty.";
        }

        return "The latest scan did not find restart pressure or active risky work.";
    }

    private static string BuildFriendlyProtectionSummary(SessionScanResult result)
    {
        if (result.Policy.Validation.HasErrors)
        {
            return "Restart protection rules are paused until the policy file is fixed.";
        }

        if (result.Policy.ApprovalActive && result.Policy.ApprovalExpiresAt.HasValue)
        {
            return $"Restart protection: a temporary approval window is active until {result.Policy.ApprovalExpiresAt.Value.LocalDateTime:G}.";
        }

        if (result.Policy.RequiresApproval)
        {
            return $"Restart protection: supervised approval is required before restart ({result.Policy.RecommendedApprovalWindowMinutes} minute default window).";
        }

        if (result.Policy.HasBlockingRules)
        {
            return "Restart protection: your current rules are actively blocking restart while the current conditions hold.";
        }

        var appliedMitigationCount = result.Mitigations.Count(mitigation => mitigation.IsApplied);
        if (appliedMitigationCount > 0)
        {
            return $"Restart protection: {appliedMitigationCount} managed Windows setting(s) are already applied.";
        }

        return "Restart protection: no extra rule or managed setting is active right now.";
    }

    private static string BuildActiveWorkSummary(SessionScanResult result)
    {
        if (result.Workspace.RiskItems.Count > 0)
        {
            var groups = result.Workspace.RiskItems.Select(item => item.Title).Take(3).ToArray();
            var summary = JoinHumanList(groups);
            var remainingCount = result.Workspace.RiskItems.Count - groups.Length;
            return remainingCount > 0
                ? $"Active work: {summary}, and {remainingCount} more group(s) look active."
                : $"Active work: {summary} look active.";
        }

        if (result.ProtectedProcesses.Count > 0)
        {
            var apps = result.ProtectedProcesses
                .Select(match => NormalizeProcessDisplayName(match.DisplayName))
                .Take(4)
                .ToArray();
            return $"Active work: {JoinHumanList(apps)} detected, but the current heuristics did not classify them as a risky workspace group.";
        }

        return "Active work: SessionGuard did not see risky app groups in the latest scan.";
    }

    private static string BuildProtectedProcessSummary(SessionScanResult result)
    {
        if (result.ProtectedProcesses.Count == 0)
        {
            return "Protected apps: none detected in the latest scan.";
        }

        var apps = result.ProtectedProcesses
            .Select(match => match.InstanceCount > 1
                ? $"{NormalizeProcessDisplayName(match.DisplayName)} x{match.InstanceCount}"
                : NormalizeProcessDisplayName(match.DisplayName))
            .ToArray();
        return $"Protected apps: {string.Join(", ", apps)}.";
    }

    private static string BuildWorkspaceGroupSummary(SessionScanResult result)
    {
        var groups = result.Workspace.RiskItems.Select(item => item.Title).Take(3).ToArray();
        return groups.Length == 0 ? "No flagged workspace groups" : JoinHumanList(groups);
    }

    private static string NormalizeProcessDisplayName(string value)
    {
        var fileName = Path.GetFileNameWithoutExtension(value);
        return string.IsNullOrWhiteSpace(fileName) ? value : fileName;
    }

    private static string JoinHumanList(IReadOnlyList<string> values)
    {
        return values.Count switch
        {
            0 => string.Empty,
            1 => values[0],
            2 => $"{values[0]} and {values[1]}",
            _ => $"{string.Join(", ", values.Take(values.Count - 1))}, and {values[^1]}"
        };
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
            ProtectionMode.MonitorOnly => "Monitoring only",
            ProtectionMode.GuardModeActive => "Guard mode on",
            ProtectionMode.PolicyGuardActive => "Extra protection on",
            ProtectionMode.PolicyApprovalWindow => "Approval active",
            ProtectionMode.ManagedMitigationsApplied => "Windows protections on",
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

    private enum RecommendedActionKind
    {
        None,
        GrantApproval,
        ApplyMitigations,
        OpenWindowsUpdateOptions,
        OpenPolicies,
        ScanNow
    }
}
