# SessionGuard

SessionGuard is a Windows 11 desktop utility that helps reduce surprise restart disruption related to Windows Update without disabling Windows Update itself.

The MVP is intentionally bounded:

- It inspects plausible restart and reboot-required signals that are accessible from user mode.
- It detects whether a protected workspace is active based on a configurable process list.
- It can apply a small set of reversible native mitigation settings when the app is run with administrative rights.
- It logs what it observed and what it attempted so the behavior stays auditable.

SessionGuard does not guarantee prevention of every OS-driven restart path, and it does not snapshot or recover user workspaces in this version.

## Why this stack

- `C# + .NET + WPF` was chosen for the fastest stable Windows-native MVP with direct registry/process access and a low-friction local build.
- The codebase is split so a future Windows Service can move into the infrastructure layer without rewriting core logic.
- The provided machine had `dotnet 9.0.312` installed during implementation, so the repo currently targets `net9.0-windows` for deterministic local builds. Microsoft .NET 10 is already stable as of March 11, 2026; upgrading should be low-risk once the local toolchain is updated.

## MVP capabilities

- Dashboard showing current status, restart risk, protection mode, pending restart state, protected process matches, mitigation status, and last scan time.
- Configurable protected process detection from [`config/protected-processes.json`](/C:/Users/decoy/sessionguard-win11/config/protected-processes.json).
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
- Machine-readable scan snapshot at `state/current-scan.json` shared by the desktop app and service path.
- Separate app and service logs under `logs/`.
- Unit tests for process matching, status aggregation, configuration parsing, control-plane behavior, and IPC compatibility checks.

## Repo layout

- [`src/SessionGuard.App`](/C:/Users/decoy/sessionguard-win11/src/SessionGuard.App) contains the WPF desktop UI and view models.
- [`src/SessionGuard.Core`](/C:/Users/decoy/sessionguard-win11/src/SessionGuard.Core) contains pure models, matching logic, status evaluation, and orchestration contracts.
- [`src/SessionGuard.Infrastructure`](/C:/Users/decoy/sessionguard-win11/src/SessionGuard.Infrastructure) contains Windows-specific config, logging, registry inspection, and mitigation code.
- [`src/SessionGuard.Service`](/C:/Users/decoy/sessionguard-win11/src/SessionGuard.Service) contains the service-hostable background worker foundation.
- [`tests/SessionGuard.Tests`](/C:/Users/decoy/sessionguard-win11/tests/SessionGuard.Tests) contains the unit tests.
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

Query service status and control-plane reachability:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/service/Get-SessionGuardServiceStatus.ps1
```

Print the latest persisted service health snapshot:

```powershell
artifacts\publish\SessionGuard.Service\SessionGuard.Service.exe health
```

Probe the running service directly from the service executable:

```powershell
src\SessionGuard.Service\bin\Debug\net9.0-windows\SessionGuard.Service.exe probe
```

## Admin vs non-admin behavior

- Non-elevated mode supports full monitoring, dashboard updates, config changes, and logging.
- Applying or resetting native mitigation settings requires elevation because the app writes under `HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate`.
- If the app is not elevated, it stays honest: it surfaces a read-only status and explains why the action could not be completed.

## Config and logs

- Edit [`config/appsettings.json`](/C:/Users/decoy/sessionguard-win11/config/appsettings.json) to change scan interval, warning behavior, active hours defaults, and UI preferences.
- Edit [`config/protected-processes.json`](/C:/Users/decoy/sessionguard-win11/config/protected-processes.json) to add or remove protected processes without rebuilding.
- App logs are written to `logs/sessionguard-app-YYYYMMDD.log`.
- Service logs are written to `logs/sessionguard-service-YYYYMMDD.log`.
- Temporary mitigation backups are written to the local `state/` folder so SessionGuard can restore previous values when resetting managed settings.
- The latest scan snapshot is written to `state/current-scan.json` for future background-service or tray-client consumption.
- The service health snapshot is written to `state/service-health.json` so status tooling can show startup, scan, and control-plane health without scraping logs.

## Manual review checklist

1. Build the solution and launch the app in a normal PowerShell session.
2. Confirm the dashboard renders current status, risk, restart indicators, protected process matches, and mitigation state.
3. Confirm the restart indicator table shows multiple providers and that the pending-restart field can read `Pending`, `Not detected`, or `Ambiguous / review signals` depending on the signal mix.
4. Start a protected tool such as Windows Terminal or VS Code and confirm the dashboard detects it on the next scan or after pressing `Scan now`.
5. Edit [`config/protected-processes.json`](/C:/Users/decoy/sessionguard-win11/config/protected-processes.json), save the file, and verify the next scan uses the updated list.
6. Launch the app from an elevated shell, apply the recommended mitigation, and confirm the mitigation state changes to applied.
7. Reset managed settings and verify the app reports the reverted state.
8. Review the latest file under `logs/` and confirm scans, detections, mitigation attempts, and failures are recorded.
9. Run the service project, then launch the desktop app and confirm the dashboard reports `Control plane: Service`.
10. Run `src\SessionGuard.Service\bin\Debug\net9.0-windows\SessionGuard.Service.exe probe` and confirm it prints JSON status while the service path is running.
11. Run `powershell -ExecutionPolicy Bypass -File scripts/service/Get-SessionGuardServiceStatus.ps1` and confirm it reports both control-plane reachability and health snapshot details.
12. Minimize or close the dashboard window and confirm SessionGuard remains available in the notification area.
13. Inspect `state/current-scan.json` and `state/service-health.json` and confirm the latest status is serialized by the service or local fallback path.

## What the MVP does not do

- It does not disable Windows Update.
- It does not promise absolute prevention of every automatic restart.
- It does not inspect unsaved buffers, browser tab counts, or developer session internals.
- It now includes a service-hostable worker, versioned named-pipe IPC, tray-aware window behavior, and local install/start/stop scripts, but it is not yet a hardened enterprise deployment package or dedicated tray-only shell.
- It does not yet capture or restore workspace snapshots.

## Further documentation

- Architecture: [`docs/architecture.md`](/C:/Users/decoy/sessionguard-win11/docs/architecture.md)
- Product brief: [`docs/product-brief.md`](/C:/Users/decoy/sessionguard-win11/docs/product-brief.md)
- Limitations: [`docs/limitations.md`](/C:/Users/decoy/sessionguard-win11/docs/limitations.md)
- Roadmap: [`docs/roadmap.md`](/C:/Users/decoy/sessionguard-win11/docs/roadmap.md)
- Workspace safety plan: [`docs/plans/v0.4.0-workspace-safety-plan.md`](/C:/Users/decoy/sessionguard-win11/docs/plans/v0.4.0-workspace-safety-plan.md)
- Future service design: [`docs/future-service-architecture.md`](/C:/Users/decoy/sessionguard-win11/docs/future-service-architecture.md)
- Release notes: [`docs/releases/v0.3.0.md`](/C:/Users/decoy/sessionguard-win11/docs/releases/v0.3.0.md)
