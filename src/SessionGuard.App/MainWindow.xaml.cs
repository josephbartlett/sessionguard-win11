using System.ComponentModel;
using System.Drawing;
using System.Windows;
using SessionGuard.App.Automation;
using SessionGuard.App.ViewModels;
using SessionGuard.Core.Services;
using Forms = System.Windows.Forms;

namespace SessionGuard.App;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly bool _useTrayIcon;
    private readonly bool _startHiddenInTray;
    private readonly Forms.NotifyIcon? _notifyIcon;
    private readonly Forms.ToolStripMenuItem? _traySummaryMenuItem;
    private readonly Forms.ToolStripMenuItem? _trayNextStepMenuItem;
    private readonly Forms.ToolStripMenuItem? _trayContextMenuItem;
    private readonly Forms.ToolStripMenuItem? _trayPrimaryActionMenuItem;
    private readonly Forms.ToolStripMenuItem? _trayStatusMenuItem;
    private readonly Forms.ToolStripMenuItem? _trayModeMenuItem;
    private readonly Forms.ToolStripMenuItem? _trayPolicyMenuItem;
    private readonly Forms.ToolStripMenuItem? _trayTimingMenuItem;
    private bool _exitRequested;

    internal MainWindow(
        MainWindowViewModel viewModel,
        bool useTrayIcon,
        bool startHiddenInTray = false)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _useTrayIcon = useTrayIcon;
        _startHiddenInTray = startHiddenInTray;
        if (_useTrayIcon)
        {
            (_notifyIcon,
                _traySummaryMenuItem,
                _trayNextStepMenuItem,
                _trayContextMenuItem,
                _trayPrimaryActionMenuItem,
                _trayStatusMenuItem,
                _trayModeMenuItem,
                _trayPolicyMenuItem,
                _trayTimingMenuItem) = CreateNotifyIcon();
        }

        if (_startHiddenInTray)
        {
            ShowInTaskbar = false;
            WindowState = WindowState.Minimized;
            ShowActivated = false;
            Opacity = 0;
        }

        _viewModel.AttentionRequested += HandleAttentionRequested;
        _viewModel.NotificationRequested += HandleNotificationRequested;
        _viewModel.PropertyChanged += HandleViewModelPropertyChanged;
        DataContext = _viewModel;
        Loaded += OnLoaded;
        Closing += OnClosing;
        Closed += OnClosed;
        StateChanged += OnStateChanged;
    }

    internal void ShowFromExternalActivation()
    {
        ShowDashboardFromTray(openTechnicalView: false);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        FitToWorkArea();
        await _viewModel.InitializeAsync();
        UpdateTrayPresentation();

        if (_startHiddenInTray || _viewModel.ShouldStartMinimized)
        {
            HideToTray(showBalloon: false);
            Opacity = 1;
            ShowActivated = true;
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_exitRequested || !_useTrayIcon)
        {
            return;
        }

        e.Cancel = true;
        HideToTray(showBalloon: false);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.AttentionRequested -= HandleAttentionRequested;
        _viewModel.NotificationRequested -= HandleNotificationRequested;
        _viewModel.PropertyChanged -= HandleViewModelPropertyChanged;
        _viewModel.Dispose();
        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (_useTrayIcon && WindowState == WindowState.Minimized)
        {
            HideToTray(showBalloon: false);
        }
    }

    private void OnHideToTrayClick(object sender, RoutedEventArgs e)
    {
        HideToTray(showBalloon: false);
    }

    private void HandleAttentionRequested(object? sender, EventArgs e)
    {
        if (_useTrayIcon && _notifyIcon is not null && (!IsVisible || WindowState == WindowState.Minimized))
        {
            _notifyIcon.BalloonTipTitle = "SessionGuard";
            _notifyIcon.BalloonTipText = "High restart risk detected. Open the dashboard if you want the full explanation.";
            _notifyIcon.ShowBalloonTip(4000);
            ShowDashboardFromTray(openTechnicalView: false);
        }

        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private void HandleNotificationRequested(object? sender, OperatorNotificationEventArgs e)
    {
        if (!_useTrayIcon || _notifyIcon is null || (IsVisible && WindowState != WindowState.Minimized))
        {
            return;
        }

        _notifyIcon.BalloonTipTitle = e.Notification.Title;
        _notifyIcon.BalloonTipText = e.Notification.Message;
        _notifyIcon.BalloonTipIcon = e.Notification.Severity == SessionGuard.Core.Models.OperatorNotificationSeverity.Warning
            ? Forms.ToolTipIcon.Warning
            : Forms.ToolTipIcon.Info;
        _notifyIcon.ShowBalloonTip(4000);
    }

    private void HandleViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_useTrayIcon)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(e.PropertyName) ||
            e.PropertyName == nameof(MainWindowViewModel.TrayTooltipText) ||
            e.PropertyName == nameof(MainWindowViewModel.TraySummaryText) ||
            e.PropertyName == nameof(MainWindowViewModel.TrayNextStepText) ||
            e.PropertyName == nameof(MainWindowViewModel.TrayContextText) ||
            e.PropertyName == nameof(MainWindowViewModel.TrayPrimaryActionText) ||
            e.PropertyName == nameof(MainWindowViewModel.TrayPrimaryActionVisible) ||
            e.PropertyName == nameof(MainWindowViewModel.TrayStatusText) ||
            e.PropertyName == nameof(MainWindowViewModel.TrayModeText) ||
            e.PropertyName == nameof(MainWindowViewModel.TrayPolicyText) ||
            e.PropertyName == nameof(MainWindowViewModel.TrayTimingText))
        {
            UpdateTrayPresentation();
        }
    }

    private (
        Forms.NotifyIcon NotifyIcon,
        Forms.ToolStripMenuItem Summary,
        Forms.ToolStripMenuItem NextStep,
        Forms.ToolStripMenuItem Context,
        Forms.ToolStripMenuItem PrimaryAction,
        Forms.ToolStripMenuItem Status,
        Forms.ToolStripMenuItem Mode,
        Forms.ToolStripMenuItem Policy,
        Forms.ToolStripMenuItem Timing) CreateNotifyIcon()
    {
        var contextMenu = new Forms.ContextMenuStrip();
        var summaryItem = new Forms.ToolStripMenuItem("Summary: waiting for first scan") { Enabled = false };
        var nextStepItem = new Forms.ToolStripMenuItem("Next: waiting for first scan") { Enabled = false };
        var contextItem = new Forms.ToolStripMenuItem("Context: waiting for first scan") { Enabled = false };
        var primaryActionItem = new Forms.ToolStripMenuItem("Open dashboard", null, (_, _) => ExecuteTrayPrimaryAction())
        {
            Visible = false
        };
        var statusItem = new Forms.ToolStripMenuItem("Status: waiting for first scan") { Enabled = false };
        var modeItem = new Forms.ToolStripMenuItem("Mode: waiting for first scan") { Enabled = false };
        var policyItem = new Forms.ToolStripMenuItem("Policy: waiting for first scan") { Enabled = false };
        var timingItem = new Forms.ToolStripMenuItem("Timing: waiting for first scan") { Enabled = false };

        var detailsMenuItem = new Forms.ToolStripMenuItem("Details");
        detailsMenuItem.DropDownItems.Add(statusItem);
        detailsMenuItem.DropDownItems.Add(modeItem);
        detailsMenuItem.DropDownItems.Add(policyItem);
        detailsMenuItem.DropDownItems.Add(timingItem);

        contextMenu.Items.Add(summaryItem);
        contextMenu.Items.Add(nextStepItem);
        contextMenu.Items.Add(contextItem);
        contextMenu.Items.Add(new Forms.ToolStripSeparator());
        contextMenu.Items.Add(primaryActionItem);
        contextMenu.Items.Add("Open dashboard", null, (_, _) => ShowDashboardFromTray(openTechnicalView: false));
        contextMenu.Items.Add("Open technical view", null, (_, _) => ShowDashboardFromTray(openTechnicalView: true));
        contextMenu.Items.Add("Scan now", null, (_, _) => _viewModel.ScanNowCommand.Execute(null));
        contextMenu.Items.Add(detailsMenuItem);
        contextMenu.Items.Add(new Forms.ToolStripSeparator());
        contextMenu.Items.Add("Windows Update options", null, (_, _) => _viewModel.OpenWindowsUpdateSettingsCommand.Execute(null));
        contextMenu.Items.Add("Exit", null, (_, _) => ExitApplication());

        var notifyIcon = new Forms.NotifyIcon
        {
            Icon = SystemIcons.Shield,
            Text = "SessionGuard",
            Visible = true,
            ContextMenuStrip = contextMenu
        };

        notifyIcon.DoubleClick += (_, _) => ShowDashboardFromTray(openTechnicalView: false);
        return (notifyIcon, summaryItem, nextStepItem, contextItem, primaryActionItem, statusItem, modeItem, policyItem, timingItem);
    }

    private void UpdateTrayPresentation()
    {
        if (!_useTrayIcon || _notifyIcon is null || _traySummaryMenuItem is null || _trayNextStepMenuItem is null ||
            _trayContextMenuItem is null || _trayPrimaryActionMenuItem is null || _trayStatusMenuItem is null ||
            _trayModeMenuItem is null || _trayPolicyMenuItem is null || _trayTimingMenuItem is null)
        {
            return;
        }

        _notifyIcon.Text = ClampNotifyIconText(_viewModel.TrayTooltipText);
        _traySummaryMenuItem.Text = _viewModel.TraySummaryText;
        _trayNextStepMenuItem.Text = _viewModel.TrayNextStepText;
        _trayContextMenuItem.Text = _viewModel.TrayContextText;
        _trayPrimaryActionMenuItem.Text = _viewModel.TrayPrimaryActionText;
        _trayPrimaryActionMenuItem.Visible = _viewModel.TrayPrimaryActionVisible;
        _trayStatusMenuItem.Text = _viewModel.TrayStatusText;
        _trayModeMenuItem.Text = _viewModel.TrayModeText;
        _trayPolicyMenuItem.Text = _viewModel.TrayPolicyText;
        _trayTimingMenuItem.Text = _viewModel.TrayTimingText;
    }

    private void ExecuteTrayPrimaryAction()
    {
        switch (_viewModel.TrayPrimaryActionKind)
        {
            case TrayPrimaryActionKind.RecommendedAction:
                _viewModel.RecommendedActionCommand.Execute(null);
                break;
            case TrayPrimaryActionKind.OpenDashboard:
                ShowDashboardFromTray(openTechnicalView: false);
                break;
        }
    }

    private void FitToWorkArea()
    {
        var workArea = SystemParameters.WorkArea;
        var availableWidth = Math.Max(MinWidth, workArea.Width - 48);
        var availableHeight = Math.Max(MinHeight, workArea.Height - 48);

        if (Width > availableWidth)
        {
            Width = availableWidth;
        }

        if (Height > availableHeight)
        {
            Height = availableHeight;
        }

        Left = Math.Max(workArea.Left + 24, workArea.Left + ((workArea.Width - Width) / 2));
        Top = Math.Max(workArea.Top + 24, workArea.Top + ((workArea.Height - Height) / 2));
    }

    private static string ClampNotifyIconText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "SessionGuard";
        }

        return text.Length <= 63
            ? text
            : text[..63];
    }

    private void HideToTray(bool showBalloon)
    {
        if (!_useTrayIcon || _notifyIcon is null)
        {
            return;
        }

        ShowInTaskbar = false;
        Hide();

        if (showBalloon)
        {
            _notifyIcon.BalloonTipTitle = "SessionGuard";
            _notifyIcon.BalloonTipText = "SessionGuard is still running in the tray.";
            _notifyIcon.ShowBalloonTip(3000);
        }
    }

    private void ShowDashboardFromTray(bool openTechnicalView)
    {
        if (!_useTrayIcon)
        {
            return;
        }

        if (openTechnicalView)
        {
            _viewModel.ShowTechnicalView();
        }
        else
        {
            _viewModel.ShowSimpleView();
        }

        Show();
        ShowInTaskbar = true;
        WindowState = WindowState.Normal;
        Opacity = 1;
        ShowActivated = true;
        Activate();
    }

    private void ExitApplication()
    {
        _exitRequested = true;
        Close();
    }
}
