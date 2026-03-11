using SessionGuard.Core.Models;

namespace SessionGuard.Core.Automation;

public static class UiSmokeScenarioCatalog
{
    private static readonly Lazy<IReadOnlyList<UiSmokeScenario>> ScenarioCache = new(CreateScenarios);

    public static IReadOnlyList<UiSmokeScenario> All => ScenarioCache.Value;

    public static UiSmokeScenario Get(string name)
    {
        if (TryGet(name, out var scenario) && scenario is not null)
        {
            return scenario;
        }

        throw new InvalidOperationException(
            $"Unknown UI smoke scenario '{name}'. Available scenarios: {string.Join(", ", All.Select(candidate => candidate.Name))}.");
    }

    public static bool TryGet(string name, out UiSmokeScenario? scenario)
    {
        scenario = All.FirstOrDefault(
            candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
        return scenario is not null;
    }

    private static IReadOnlyList<UiSmokeScenario> CreateScenarios()
    {
        return new[]
        {
            CreateSafeServiceScenario(),
            CreateRestartPendingScenario(),
            CreateProtectedWorkspaceScenario(),
            CreateLocalFallbackScenario(),
            CreateMitigatedScenario()
        };
    }

    private static UiSmokeScenario CreateSafeServiceScenario()
    {
        var timestamp = ParseTimestamp("2026-03-11T15:00:00-04:00");
        var workspace = WorkspaceStateSnapshot.None(timestamp);
        var scanResult = new SessionScanResult(
            timestamp,
            RestartStateCategory.Safe,
            RestartRiskLevel.Low,
            ProtectionMode.GuardModeActive,
            RestartPending: false,
            HasAmbiguousSignals: false,
            ProtectedSessionActive: false,
            LimitedVisibility: false,
            IsElevated: false,
            Summary: "No protected processes or pending restart indicators were detected during the latest scan.",
            workspace,
            new RestartSignalOverview(1, 0, 0, 0, 0, 1, 0, "No restart or orchestration activity was detected by the configured providers."),
            new[]
            {
                new RestartIndicator(
                    "Windows Update Agent",
                    "Windows Update reboot required",
                    RestartIndicatorCategory.PendingRestart,
                    false,
                    "Windows Update Agent did not report a pending reboot.",
                    SignalConfidence.High)
            },
            Array.Empty<ProtectedProcessMatch>(),
            Array.Empty<ManagedMitigationState>(),
            new[]
            {
                "No immediate action is required. Keep guard mode enabled if you want the app to keep surfacing restart risk."
            });

        return new UiSmokeScenario(
            "safe-service",
            "Low-risk service-backed dashboard state with no active workspace risk.",
            new SessionControlStatus(scanResult, GuardModeEnabled: true, "Service", IsRemote: true),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [UiSmokeAutomationIds.CurrentStatusText] = "Safe",
                [UiSmokeAutomationIds.RestartRiskText] = "Low",
                [UiSmokeAutomationIds.PendingRestartText] = "Not detected",
                [UiSmokeAutomationIds.ConnectionModeText] = "Control plane: Service (background service is authoritative)",
                [UiSmokeAutomationIds.WorkspaceSummaryText] = workspace.Summary
            });
    }

    private static UiSmokeScenario CreateRestartPendingScenario()
    {
        var timestamp = ParseTimestamp("2026-03-11T15:05:00-04:00");
        var workspace = WorkspaceStateSnapshot.None(timestamp);
        var scanResult = new SessionScanResult(
            timestamp,
            RestartStateCategory.RestartPending,
            RestartRiskLevel.Elevated,
            ProtectionMode.MonitorOnly,
            RestartPending: true,
            HasAmbiguousSignals: false,
            ProtectedSessionActive: false,
            LimitedVisibility: false,
            IsElevated: false,
            Summary: "Windows restart indicators were detected. Review mitigation settings before leaving the machine unattended.",
            workspace,
            new RestartSignalOverview(2, 2, 2, 0, 0, 2, 0, "2 definitive pending-restart signal(s) detected across 2 provider(s)."),
            new[]
            {
                new RestartIndicator(
                    "Windows Update Agent",
                    "Windows Update reboot required",
                    RestartIndicatorCategory.PendingRestart,
                    true,
                    "Windows Update Agent reports that a reboot is required.",
                    SignalConfidence.High),
                new RestartIndicator(
                    "Registry restart signals",
                    "RebootRequired registry key",
                    RestartIndicatorCategory.PendingRestart,
                    true,
                    "Registry signals indicate a pending reboot.",
                    SignalConfidence.High)
            },
            Array.Empty<ProtectedProcessMatch>(),
            Array.Empty<ManagedMitigationState>(),
            new[]
            {
                "Apply the recommended native mitigations or review Windows Update options to reduce surprise restart behavior without disabling updates."
            });

        return new UiSmokeScenario(
            "restart-pending",
            "Pending-reboot state without active protected workspace processes.",
            new SessionControlStatus(scanResult, GuardModeEnabled: false, "Service", IsRemote: true),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [UiSmokeAutomationIds.CurrentStatusText] = "Restart Pending",
                [UiSmokeAutomationIds.RestartRiskText] = "Elevated",
                [UiSmokeAutomationIds.PendingRestartText] = "Pending",
                [UiSmokeAutomationIds.ConnectionModeText] = "Control plane: Service (background service is authoritative)",
                [UiSmokeAutomationIds.StatusSummaryText] = scanResult.Summary
            });
    }

    private static UiSmokeScenario CreateProtectedWorkspaceScenario()
    {
        var timestamp = ParseTimestamp("2026-03-11T15:10:00-04:00");
        var workspace = new WorkspaceStateSnapshot(
            timestamp,
            HasRisk: true,
            WorkspaceRiskSeverity.High,
            WorkspaceConfidence.High,
            "Workspace-risk heuristics flagged high-impact activity: Editor and IDE sessions, Terminal and shell sessions, Browser sessions.",
            new[]
            {
                new WorkspaceRiskItem(
                    "Terminal and shell sessions",
                    WorkspaceCategory.TerminalShell,
                    WorkspaceRiskSeverity.High,
                    WorkspaceConfidence.High,
                    2,
                    "Interactive shells often hold live commands, remote sessions, or transient console context that would be hard to reconstruct after a restart.",
                    new[] { "pwsh.exe", "WindowsTerminal.exe" }),
                new WorkspaceRiskItem(
                    "Editor and IDE sessions",
                    WorkspaceCategory.EditorOrIde,
                    WorkspaceRiskSeverity.Elevated,
                    WorkspaceConfidence.High,
                    1,
                    "Editor and IDE processes suggest active development context. SessionGuard cannot verify unsaved buffers, so this remains an advisory risk signal.",
                    new[] { "Code.exe" }),
                new WorkspaceRiskItem(
                    "Browser sessions",
                    WorkspaceCategory.Browser,
                    WorkspaceRiskSeverity.Elevated,
                    WorkspaceConfidence.Medium,
                    1,
                    "Browser processes are running. SessionGuard cannot count tabs or confirm session persistence, so the disruption risk is inferred from process presence only.",
                    new[] { "chrome.exe" })
            });
        var scanResult = new SessionScanResult(
            timestamp,
            RestartStateCategory.ProtectedSessionActive,
            RestartRiskLevel.High,
            ProtectionMode.GuardModeActive,
            RestartPending: true,
            HasAmbiguousSignals: true,
            ProtectedSessionActive: true,
            LimitedVisibility: false,
            IsElevated: false,
            Summary: "Definitive pending reboot signals are active while workspace-risk heuristics report active sessions. Workspace-risk heuristics flagged high-impact activity: Editor and IDE sessions, Terminal and shell sessions, Browser sessions.",
            workspace,
            new RestartSignalOverview(3, 3, 1, 2, 0, 3, 0, "1 definitive pending-restart signal(s) detected across 3 provider(s)."),
            new[]
            {
                new RestartIndicator(
                    "Windows Update Agent",
                    "Windows Update reboot required",
                    RestartIndicatorCategory.PendingRestart,
                    true,
                    "Windows Update Agent reports that a reboot is required.",
                    SignalConfidence.High),
                new RestartIndicator(
                    "Windows Update UX settings",
                    "Smart scheduler prediction",
                    RestartIndicatorCategory.UpdateOrchestration,
                    true,
                    "Windows Update predicts a maintenance restart window.",
                    SignalConfidence.Low),
                new RestartIndicator(
                    "Windows Update scheduled task",
                    "Scheduled Start",
                    RestartIndicatorCategory.UpdateOrchestration,
                    true,
                    "The Scheduled Start task is enabled and recently updated.",
                    SignalConfidence.Low)
            },
            new[]
            {
                new ProtectedProcessMatch("Code.exe", 1),
                new ProtectedProcessMatch("WindowsTerminal.exe", 1),
                new ProtectedProcessMatch("chrome.exe", 1),
                new ProtectedProcessMatch("pwsh.exe", 1)
            },
            new[]
            {
                new ManagedMitigationState(
                    "no-auto-reboot",
                    "No auto restart with signed-in users",
                    "Native policy to avoid automatic restart while a user is signed in.",
                    false,
                    true,
                    "0",
                    "1",
                    @"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU\NoAutoRebootWithLoggedOnUsers")
            },
            new[]
            {
                "Keep critical terminals, editors, and browsers open only if you can supervise the machine; otherwise save work and schedule a manual restart.",
                "Workspace-risk heuristics are active. SessionGuard is surfacing advisory risk and lightweight metadata only; it does not snapshot unsaved buffers or recover workspace state.",
                "Browser risk is inferred from running processes only. Review your own session persistence settings before relying on restart recovery."
            });

        return new UiSmokeScenario(
            "protected-workspace",
            "High-risk service-backed scenario with multiple workspace groups and visible recommendations.",
            new SessionControlStatus(scanResult, GuardModeEnabled: true, "Service", IsRemote: true),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [UiSmokeAutomationIds.CurrentStatusText] = "Protected Session Active",
                [UiSmokeAutomationIds.RestartRiskText] = "High",
                [UiSmokeAutomationIds.ConnectionModeText] = "Control plane: Service (background service is authoritative)",
                [UiSmokeAutomationIds.WorkspaceSummaryText] = workspace.Summary,
                [UiSmokeAutomationIds.WorkspaceConfidenceText] = "Workspace confidence: High"
            });
    }

    private static UiSmokeScenario CreateLocalFallbackScenario()
    {
        var timestamp = ParseTimestamp("2026-03-11T15:15:00-04:00");
        var workspace = new WorkspaceStateSnapshot(
            timestamp,
            HasRisk: true,
            WorkspaceRiskSeverity.Elevated,
            WorkspaceConfidence.Medium,
            "Workspace-risk heuristics flagged elevated disruption risk: Browser sessions.",
            new[]
            {
                new WorkspaceRiskItem(
                    "Browser sessions",
                    WorkspaceCategory.Browser,
                    WorkspaceRiskSeverity.Elevated,
                    WorkspaceConfidence.Medium,
                    2,
                    "Browser processes are running. SessionGuard cannot count tabs or confirm session persistence, so the disruption risk is inferred from process presence only.",
                    new[] { "chrome.exe", "msedge.exe" })
            });
        var scanResult = new SessionScanResult(
            timestamp,
            RestartStateCategory.UnknownLimitedVisibility,
            RestartRiskLevel.Elevated,
            ProtectionMode.LimitedReadOnly,
            RestartPending: false,
            HasAmbiguousSignals: true,
            ProtectedSessionActive: true,
            LimitedVisibility: true,
            IsElevated: false,
            Summary: "Windows Update orchestration activity or low-confidence restart clues were detected, but a definitive pending reboot was not confirmed.",
            workspace,
            new RestartSignalOverview(2, 2, 0, 1, 1, 2, 1, "1 restart-related signal(s) need interpretation, but no definitive pending reboot was confirmed."),
            new[]
            {
                new RestartIndicator(
                    "Windows Update UX settings",
                    "Smart scheduler prediction",
                    RestartIndicatorCategory.UpdateOrchestration,
                    true,
                    "Windows Update predicts a maintenance window, but this is still advisory.",
                    SignalConfidence.Low),
                new RestartIndicator(
                    "Registry restart signals",
                    "PendingFileRenameOperations",
                    RestartIndicatorCategory.PendingRestart,
                    false,
                    "Registry visibility was limited for the pending-file-rename signal.",
                    SignalConfidence.Low,
                    LimitedVisibility: true)
            },
            new[]
            {
                new ProtectedProcessMatch("chrome.exe", 1),
                new ProtectedProcessMatch("msedge.exe", 1)
            },
            Array.Empty<ManagedMitigationState>(),
            new[]
            {
                "SessionGuard detected Windows Update orchestration activity or low-confidence restart clues. Review the indicator table before assuming the machine is clear.",
                "Some providers had limited visibility. Treat the dashboard as a best-effort monitor, not a guarantee."
            });

        return new UiSmokeScenario(
            "local-fallback-limited",
            "Local fallback path with ambiguous signals and limited-visibility messaging.",
            new SessionControlStatus(scanResult, GuardModeEnabled: true, "Local fallback", IsRemote: false),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [UiSmokeAutomationIds.CurrentStatusText] = "Unknown / Limited Visibility",
                [UiSmokeAutomationIds.PendingRestartText] = "Ambiguous / review signals",
                [UiSmokeAutomationIds.ConnectionModeText] = "Control plane: Local fallback (the dashboard is scanning in-process because the service is unavailable)",
                [UiSmokeAutomationIds.WorkspaceConfidenceText] = "Workspace confidence: Medium"
            });
    }

    private static UiSmokeScenario CreateMitigatedScenario()
    {
        var timestamp = ParseTimestamp("2026-03-11T15:20:00-04:00");
        var workspace = WorkspaceStateSnapshot.None(timestamp);
        var mitigations = new[]
        {
            new ManagedMitigationState(
                "no-auto-reboot",
                "No auto restart with signed-in users",
                "Native policy to avoid automatic restart while a user is signed in.",
                true,
                true,
                "1",
                "1",
                @"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU\NoAutoRebootWithLoggedOnUsers"),
            new ManagedMitigationState(
                "active-hours",
                "Active hours policy",
                "Policy-managed active hours to keep restart timing predictable.",
                true,
                true,
                "8-23",
                "8-23",
                @"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate")
        };
        var scanResult = new SessionScanResult(
            timestamp,
            RestartStateCategory.MitigatedDeferred,
            RestartRiskLevel.Low,
            ProtectionMode.ManagedMitigationsApplied,
            RestartPending: false,
            HasAmbiguousSignals: false,
            ProtectedSessionActive: false,
            LimitedVisibility: false,
            IsElevated: true,
            Summary: "Recommended native restart mitigations are already applied.",
            workspace,
            new RestartSignalOverview(1, 1, 0, 0, 0, 1, 0, "No restart pending."),
            new[]
            {
                new RestartIndicator(
                    "Windows Update mitigation visibility",
                    "Managed policy state",
                    RestartIndicatorCategory.MitigationVisibility,
                    true,
                    "Managed restart mitigations are visible in policy-backed settings.",
                    SignalConfidence.Medium)
            },
            Array.Empty<ProtectedProcessMatch>(),
            mitigations,
            new[]
            {
                "No immediate action is required. Keep guard mode enabled if you want the app to keep surfacing restart risk."
            });

        return new UiSmokeScenario(
            "mitigated-deferred",
            "Applied-mitigation state with low risk and elevated access.",
            new SessionControlStatus(scanResult, GuardModeEnabled: true, "Service", IsRemote: true),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [UiSmokeAutomationIds.CurrentStatusText] = "Mitigated / Deferred",
                [UiSmokeAutomationIds.ProtectionModeText] = "Managed mitigations applied",
                [UiSmokeAutomationIds.AdminAccessText] = "Elevated",
                [UiSmokeAutomationIds.ConnectionModeText] = "Control plane: Service (background service is authoritative)"
            });
    }

    private static DateTimeOffset ParseTimestamp(string timestamp)
    {
        return DateTimeOffset.Parse(
            timestamp,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind);
    }
}
