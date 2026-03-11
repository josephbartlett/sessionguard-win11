# SessionGuard

SessionGuard is a Windows 11 desktop utility that helps reduce surprise restart disruption related to Windows Update without disabling Windows Update itself.

The MVP is intentionally bounded:

- It inspects plausible restart and reboot-required signals that are accessible from user mode.
- It detects whether a protected workspace is active based on a configurable process list.
- It now derives advisory workspace-risk heuristics for terminals, editors, browsers, and local dev-server style runtimes.
- It now applies a separate JSON-backed policy engine for restart windows, blocking rules, and temporary approval windows.
- It now validates the policy configuration separately so malformed or conflicting policy files show diagnostics instead of breaking the whole dashboard scan.
- It now treats mitigation changes and restart-approval changes as service-owned actions, so local fallback remains read-only for those writes.
- It can apply a small set of reversible native mitigation settings when the app is run with administrative rights.
- It logs what it observed and what it attempted so the behavior stays auditable.

SessionGuard does not guarantee prevention of every OS-driven restart path, and it does not snapshot or recover unsaved user workspaces in this version.

## Why this stack

- `C# + .NET + WPF` was chosen for the fastest stable Windows-native MVP with direct registry/process access and a low-friction local build.
- The codebase is split so a future Windows Service can move into the infrastructure layer without rewriting core logic.
- The provided machine had `dotnet 9.0.312` installed during implementation, so the repo currently targets `net9.0-windows` for deterministic local builds. Microsoft .NET 10 is already stable as of March 11, 2026; upgrading should be low-risk once the local toolchain is updated.

## MVP capabilities

- Dashboard showing current status, restart risk, protection mode, pending restart state, protected process matches, workspace safety signals, mitigation status, and last scan time.
- Configurable protected process detection from [`config/protected-processes.json`](/C:/Users/decoy/sessionguard-win11/config/protected-processes.json).
- Configurable policy rules from [`config/policies.json`](/C:/Users/decoy/sessionguard-win11/config/policies.json) for:
  - restart windows
  - process and workspace restart blocks
  - temporary approval requirements and approval window duration
- Policy diagnostics that surface:
  - malformed `policies.json` handling without crashing the scan path
  - duplicate or conflicting rule warnings
  - deterministic approval-window precedence when multiple approval rules match
- Restart signal inspection using multiple providers:
  - bounded registry checks for CBS, Windows Update, and Session Manager reboot clues
  - Windows Update Agent COM `RebootRequired`
  - Windows Update UX settings for active hours, pause expiry, restart notifications, and scheduler prediction clues
  - Task Scheduler visibility for the Windows Update `Scheduled Start` task
- Aggregated signal coverage that distinguishes:
  - definitive pending restart signals
  - ambiguous restart-orchestration activity
  - limited visibility/provider failures
- Reversible native mitigation actions:
  - `NoAutoRebootWithLoggedOnUsers`
  - policy-managed active hours (`SetActiveHours`, `ActiveHoursStart`, `ActiveHoursEnd`)
- Local JSON-line logging under the runtime `logs/` folder.
- Tray-aware WPF dashboard behavior that minimizes to the notification area and can reopen the dashboard on demand.
- Versioned named-pipe control plane between the desktop app and the service-hostable worker, with local fallback if the service is not reachable.
- Service-owned write boundary for:
  - mitigation apply/reset actions
  - temporary restart approval grant/clear actions
- Machine-readable scan snapshot at `state/current-scan.json` shared by the desktop app and service path.
- Advisory workspace metadata snapshot at `state/workspace-snapshot.json` when workspace-risk heuristics are active.
- Persisted approval state at `state/policy-approval.json` when a temporary restart approval window is active.
- Separate app and service logs under `logs/`.
- Unit tests for process matching, workspace heuristics, status aggregation, policy evaluation, snapshot persistence, control-plane behavior, and IPC compatibility checks.
- Deterministic WPF UI smoke automation with screenshot capture under `artifacts/ui/smoke`.

## Repo layout

- [`src/SessionGuard.App`](/C:/Users/decoy/sessionguard-win11/src/SessionGuard.App) contains the WPF desktop UI and view models.
- [`src/SessionGuard.Core`](/C:/Users/decoy/sessionguard-win11/src/SessionGuard.Core) contains pure models, matching logic, status evaluation, and orchestration contracts.
- [`src/SessionGuard.Infrastructure`](/C:/Users/decoy/sessionguard-win11/src/SessionGuard.Infrastructure) contains Windows-specific config, logging, registry inspection, and mitigation code.
- [`src/SessionGuard.Service`](/C:/Users/decoy/sessionguard-win11/src/SessionGuard.Service) contains the service-hostable background worker foundation.
- [`tests/SessionGuard.Tests`](/C:/Users/decoy/sessionguard-win11/tests/SessionGuard.Tests) contains the unit tests.
- [`tests/SessionGuard.UiSmoke`](/C:/Users/decoy/sessionguard-win11/tests/SessionGuard.UiSmoke) contains the WPF UI smoke runner and screenshot capture tool.
- [`docs`](/C:/Users/decoy/sessionguard-win11/docs) contains product, architecture, limitations, roadmap, and future-service notes.

## Build and run

Build the full solution:

```powershell
dotnet build SessionGuard.sln
```

Run the background service host locally:

```powershell
dotnet run --project src/SessionGuard.Service/SessionGuard.Service.csproj -- console
```

Run the desktop app in non-elevated mode:

```powershell
dotnet run --project src/SessionGuard.App/SessionGuard.App.csproj
```

When the service host is running, the desktop app prefers the named-pipe service control plane and reports `Control plane: Service`. If the service is not reachable, the app falls back to a local in-process scan path and reports `Control plane: Local fallback`.

When the app is in `Control plane: Local fallback`, mitigation writes and restart-approval changes are intentionally disabled. Monitoring stays available, but service-owned write actions require the background service to reconnect.

Run the desktop app in an elevated PowerShell session to test mitigation actions:

```powershell
Start-Process powershell -Verb RunAs
```

Then, in the elevated window:

```powershell
cd C:\Users\decoy\sessionguard-win11
dotnet run --project src/SessionGuard.App/SessionGuard.App.csproj
```

Run tests:

```powershell
dotnet test SessionGuard.sln
```

Run the deterministic UI smoke and screenshot pass:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/ui/Run-UiSmoke.ps1
```

Run the repo-owned Windows validation flow that CI uses:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/ci/Invoke-WindowsValidation.ps1
```

Package the release ZIP:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/package-release.ps1
```

## Service operations

Publish the service executable and copy config defaults next to it:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/service/Publish-SessionGuardService.ps1
```

Install the Windows Service from an elevated PowerShell session:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/service/Install-SessionGuardService.ps1
```

Validate install readiness without modifying the machine:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/service/Install-SessionGuardService.ps1 -ValidateOnly
```

Query service status and control-plane reachability:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/service/Get-SessionGuardServiceStatus.ps1
```

Validate a published service layout outside the repo root:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/service/Validate-SessionGuardPublishedLayout.ps1
```

Print the latest persisted service health snapshot:

```powershell
artifacts\publish\SessionGuard.Service\SessionGuard.Service.exe health
```

Probe the running service directly from the service executable:

```powershell
src\SessionGuard.Service\bin\Debug\net9.0-windows\SessionGuard.Service.exe probe
```

Grant a temporary restart approval window through the running service:

```powershell
src\SessionGuard.Service\bin\Debug\net9.0-windows\SessionGuard.Service.exe approve-restart
```

Clear the temporary restart approval window:

```powershell
src\SessionGuard.Service\bin\Debug\net9.0-windows\SessionGuard.Service.exe clear-approval
```

## Admin vs non-admin behavior

- Non-elevated mode supports full monitoring, dashboard updates, config changes, and logging.
- Applying or resetting native mitigation settings requires elevation because the app writes under `HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate`.
- Applying or resetting native mitigation settings also requires the background service path. The desktop app will not perform those writes itself while in local fallback.
- If the app is not elevated, it stays honest: it surfaces a read-only status and explains why the action could not be completed.

## Config and logs

- Edit [`config/appsettings.json`](/C:/Users/decoy/sessionguard-win11/config/appsettings.json) to change scan interval, warning behavior, active hours defaults, and UI preferences.
- Edit [`config/protected-processes.json`](/C:/Users/decoy/sessionguard-win11/config/protected-processes.json) to add or remove protected processes without rebuilding.
- Edit [`config/policies.json`](/C:/Users/decoy/sessionguard-win11/config/policies.json) to change restart windows, process or workspace blocking rules, and approval requirements without rebuilding.
- If `config/policies.json` is malformed or contains conflicting rules, SessionGuard now keeps scanning and surfaces the policy diagnostics in the dashboard instead of failing the entire refresh path.
- App logs are written to `logs/sessionguard-app-YYYYMMDD.log`.
- Service logs are written to `logs/sessionguard-service-YYYYMMDD.log`.
- Temporary mitigation backups are written to the local `state/` folder so SessionGuard can restore previous values when resetting managed settings.
- The latest scan snapshot is written to `state/current-scan.json` for future background-service or tray-client consumption.
- When workspace-risk heuristics are active, advisory metadata is also written to `state/workspace-snapshot.json`.
- Temporary restart approval state is written to `state/policy-approval.json` so the policy engine can survive app or service restarts until the window expires.
- The service health snapshot is written to `state/service-health.json` so status tooling can show startup, scan, control-plane health, and approval-state recovery without scraping logs.
- UI smoke screenshots and the smoke summary are written to `artifacts/ui/smoke/`.
- CI-oriented validation outputs, including test results and UI smoke artifacts, are written to `artifacts/ci/windows-validation/`.

## Manual review checklist

1. Run `powershell -ExecutionPolicy Bypass -File scripts/ui/Run-UiSmoke.ps1`.
2. Run `powershell -ExecutionPolicy Bypass -File scripts/ci/Invoke-WindowsValidation.ps1` if you want the same validation flow used by GitHub Actions.
3. Inspect the screenshots under `artifacts/ui/smoke/` or `artifacts/ci/windows-validation/ui-smoke/` and confirm the scenarios render without clipped text, broken layout, or missing sections.
4. Build the solution and launch the app in a normal PowerShell session.
5. Confirm the dashboard renders current status, risk, workspace safety signals, restart indicators, protected process matches, and mitigation state.
6. Confirm the restart indicator table shows multiple providers and that the pending-restart field can read `Pending`, `Not detected`, or `Ambiguous / review signals` depending on the signal mix.
7. Start a protected tool such as Windows Terminal or VS Code and confirm the dashboard detects it on the next scan or after pressing `Scan now`.
8. Edit [`config/protected-processes.json`](/C:/Users/decoy/sessionguard-win11/config/protected-processes.json), save the file, and verify the next scan uses the updated list.
9. Launch the app from an elevated shell, apply the recommended mitigation, and confirm the mitigation state changes to applied.
10. Reset managed settings and verify the app reports the reverted state.
11. Review the latest file under `logs/` and confirm scans, detections, mitigation attempts, and failures are recorded.
12. Run the service project, then launch the desktop app and confirm the dashboard reports `Control plane: Service`.
13. Run `src\SessionGuard.Service\bin\Debug\net9.0-windows\SessionGuard.Service.exe probe` and confirm it prints JSON status while the service path is running.
14. Run `powershell -ExecutionPolicy Bypass -File scripts/service/Get-SessionGuardServiceStatus.ps1` and confirm it reports both control-plane reachability and health snapshot details.
15. Run `powershell -ExecutionPolicy Bypass -File scripts/service/Install-SessionGuardService.ps1 -ValidateOnly` and confirm it reports install readiness or a clear elevation requirement without changing the machine.
16. Run `powershell -ExecutionPolicy Bypass -File scripts/service/Validate-SessionGuardPublishedLayout.ps1` and confirm the published layout works outside the repo root.
17. Minimize or close the dashboard window and confirm SessionGuard remains available in the notification area.
18. Trigger a state with restart pressure, then confirm the policy card explains whether restart is blocked, requires approval, or has an active approval window.
19. Stop the service or force local fallback, then confirm the mitigation and approval buttons are disabled and the dashboard explains that those write actions are service-owned.
20. Grant and clear a temporary restart approval window from the dashboard or `SessionGuard.Service.exe approve-restart` while the service path is active and confirm the policy status changes.
21. Start a protected terminal, browser, or editor session and confirm the workspace safety table explains why the session is considered risky.
22. Introduce a temporary mistake into [`config/policies.json`](/C:/Users/decoy/sessionguard-win11/config/policies.json), trigger a scan, and confirm the policy card stays up, reports configuration errors, and leaves the rest of the dashboard functional.
23. Inspect `state/current-scan.json`, `state/workspace-snapshot.json`, `state/policy-approval.json`, and `state/service-health.json` and confirm the latest status is serialized by the service or local fallback path.

## What the MVP does not do

- It does not disable Windows Update.
- It does not promise absolute prevention of every automatic restart.
- It does not inspect unsaved buffers, browser tab counts, or developer session internals.
- It now writes advisory workspace metadata, but that metadata is local only and does not capture enough detail to restore sessions.
- It now includes approval workflows and rule-driven policy state, but those policies are advisory control logic inside SessionGuard rather than a guarantee that Windows itself will honor every desired restart outcome.
- It now validates policy configuration and degrades safely on malformed policy JSON, but it still cannot infer operator intent beyond the local rule set or resolve every ambiguous policy design automatically.
- It now makes mitigation and approval writes service-owned, but the local fallback path still exists for monitoring, so a missing service reduces SessionGuard to read-only restart awareness until the service returns.
- It now includes a service-hostable worker, versioned named-pipe IPC, tray-aware window behavior, and local install/start/stop scripts, but it is not yet a hardened enterprise deployment package or dedicated tray-only shell.
- It does not yet capture full recovery snapshots or restore workspace state.

## Further documentation

- Architecture: [`docs/architecture.md`](/C:/Users/decoy/sessionguard-win11/docs/architecture.md)
- Product brief: [`docs/product-brief.md`](/C:/Users/decoy/sessionguard-win11/docs/product-brief.md)
- Limitations: [`docs/limitations.md`](/C:/Users/decoy/sessionguard-win11/docs/limitations.md)
- Roadmap: [`docs/roadmap.md`](/C:/Users/decoy/sessionguard-win11/docs/roadmap.md)
- Workspace safety plan: [`docs/plans/v0.4.0-workspace-safety-plan.md`](/C:/Users/decoy/sessionguard-win11/docs/plans/v0.4.0-workspace-safety-plan.md)
- Future service design: [`docs/future-service-architecture.md`](/C:/Users/decoy/sessionguard-win11/docs/future-service-architecture.md)
- Release notes: [`docs/releases/v0.5.2.md`](/C:/Users/decoy/sessionguard-win11/docs/releases/v0.5.2.md)
