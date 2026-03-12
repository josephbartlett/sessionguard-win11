using System.ComponentModel;
using System.Drawing;
using System.Windows;
using SessionGuard.App.Automation;
using SessionGuard.App.ViewModels;
using SessionGuard.Core.Services;
using SessionGuard.Infrastructure.Environment;
using Forms = System.Windows.Forms;

namespace SessionGuard.App;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly bool _useTrayIcon;
    private readonly Forms.NotifyIcon? _notifyIcon;
    private readonly Forms.ToolStripMenuItem? _trayStatusMenuItem;
    private readonly Forms.ToolStripMenuItem? _trayModeMenuItem;
    private readonly Forms.ToolStripMenuItem? _trayPolicyMenuItem;
    private readonly Forms.ToolStripMenuItem? _trayTimingMenuItem;
    private bool _exitRequested;

    internal MainWindow(
        MainWindowViewModel viewModel,
        bool useTrayIcon)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _useTrayIcon = useTrayIcon;
        if (_useTrayIcon)
        {
            (_notifyIcon, _trayStatusMenuItem, _trayModeMenuItem, _trayPolicyMenuItem, _trayTimingMenuItem) = CreateNotifyIcon();
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

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.InitializeAsync();
        UpdateTrayPresentation();

        if (_viewModel.ShouldStartMinimized)
        {
            HideToTray(showBalloon: false);
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

    private void HandleAttentionRequested(object? sender, EventArgs e)
    {
        if (_useTrayIcon && _notifyIcon is not null && (!IsVisible || WindowState == WindowState.Minimized))
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
            e.PropertyName == nameof(MainWindowViewModel.TrayStatusText) ||
            e.PropertyName == nameof(MainWindowViewModel.TrayModeText) ||
            e.PropertyName == nameof(MainWindowViewModel.TrayPolicyText) ||
            e.PropertyName == nameof(MainWindowViewModel.TrayTimingText))
        {
            UpdateTrayPresentation();
        }
    }

    private (Forms.NotifyIcon NotifyIcon, Forms.ToolStripMenuItem Status, Forms.ToolStripMenuItem Mode, Forms.ToolStripMenuItem Policy, Forms.ToolStripMenuItem Timing) CreateNotifyIcon()
    {
        var contextMenu = new Forms.ContextMenuStrip();
        var statusItem = new Forms.ToolStripMenuItem("Status: waiting for first scan") { Enabled = false };
        var modeItem = new Forms.ToolStripMenuItem("Mode: waiting for first scan") { Enabled = false };
        var policyItem = new Forms.ToolStripMenuItem("Policy: waiting for first scan") { Enabled = false };
        var timingItem = new Forms.ToolStripMenuItem("Timing: waiting for first scan") { Enabled = false };
        contextMenu.Items.Add(statusItem);
        contextMenu.Items.Add(modeItem);
        contextMenu.Items.Add(policyItem);
        contextMenu.Items.Add(timingItem);
        contextMenu.Items.Add(new Forms.ToolStripSeparator());
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
        return (notifyIcon, statusItem, modeItem, policyItem, timingItem);
    }

    private void UpdateTrayPresentation()
    {
        if (!_useTrayIcon || _notifyIcon is null || _trayStatusMenuItem is null || _trayModeMenuItem is null || _trayPolicyMenuItem is null || _trayTimingMenuItem is null)
        {
            return;
        }

        _notifyIcon.Text = ClampNotifyIconText(_viewModel.TrayTooltipText);
        _trayStatusMenuItem.Text = _viewModel.TrayStatusText;
        _trayModeMenuItem.Text = _viewModel.TrayModeText;
        _trayPolicyMenuItem.Text = _viewModel.TrayPolicyText;
        _trayTimingMenuItem.Text = _viewModel.TrayTimingText;
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

    private void ShowFromTray()
    {
        if (!_useTrayIcon)
        {
            return;
        }

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
