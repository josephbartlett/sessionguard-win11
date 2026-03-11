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
- Restart signal inspection using bounded registry checks for:
  - Component Based Servicing reboot pending
  - Windows Update reboot required
  - pending file rename operations
  - `UpdateExeVolatile`
- Reversible native mitigation actions:
  - `NoAutoRebootWithLoggedOnUsers`
  - policy-managed active hours (`SetActiveHours`, `ActiveHoursStart`, `ActiveHoursEnd`)
- Local JSON-line logging under the runtime `logs/` folder.
- Unit tests for process matching, status aggregation, and configuration parsing.

## Repo layout

- [`src/SessionGuard.App`](/C:/Users/decoy/sessionguard-win11/src/SessionGuard.App) contains the WPF desktop UI and view models.
- [`src/SessionGuard.Core`](/C:/Users/decoy/sessionguard-win11/src/SessionGuard.Core) contains pure models, matching logic, status evaluation, and orchestration contracts.
- [`src/SessionGuard.Infrastructure`](/C:/Users/decoy/sessionguard-win11/src/SessionGuard.Infrastructure) contains Windows-specific config, logging, registry inspection, and mitigation code.
- [`tests/SessionGuard.Tests`](/C:/Users/decoy/sessionguard-win11/tests/SessionGuard.Tests) contains the unit tests.
- [`docs`](/C:/Users/decoy/sessionguard-win11/docs) contains product, architecture, limitations, roadmap, and future-service notes.

## Build and run

Build the full solution:

```powershell
dotnet build SessionGuard.sln
```

Run the desktop app in non-elevated mode:

```powershell
dotnet run --project src/SessionGuard.App/SessionGuard.App.csproj
```

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

## Admin vs non-admin behavior

- Non-elevated mode supports full monitoring, dashboard updates, config changes, and logging.
- Applying or resetting native mitigation settings requires elevation because the app writes under `HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate`.
- If the app is not elevated, it stays honest: it surfaces a read-only status and explains why the action could not be completed.

## Config and logs

- Edit [`config/appsettings.json`](/C:/Users/decoy/sessionguard-win11/config/appsettings.json) to change scan interval, warning behavior, active hours defaults, and UI preferences.
- Edit [`config/protected-processes.json`](/C:/Users/decoy/sessionguard-win11/config/protected-processes.json) to add or remove protected processes without rebuilding.
- Logs are written to `logs/sessionguard-YYYYMMDD.log`.
- Temporary mitigation backups are written to the local `state/` folder so SessionGuard can restore previous values when resetting managed settings.

## Manual review checklist

1. Build the solution and launch the app in a normal PowerShell session.
2. Confirm the dashboard renders current status, risk, restart indicators, protected process matches, and mitigation state.
3. Start a protected tool such as Windows Terminal or VS Code and confirm the dashboard detects it on the next scan or after pressing `Scan now`.
4. Edit [`config/protected-processes.json`](/C:/Users/decoy/sessionguard-win11/config/protected-processes.json), save the file, and verify the next scan uses the updated list.
5. Launch the app from an elevated shell, apply the recommended mitigation, and confirm the mitigation state changes to applied.
6. Reset managed settings and verify the app reports the reverted state.
7. Review the latest file under `logs/` and confirm scans, detections, mitigation attempts, and failures are recorded.

## What the MVP does not do

- It does not disable Windows Update.
- It does not promise absolute prevention of every automatic restart.
- It does not inspect unsaved buffers, browser tab counts, or developer session internals.
- It does not yet run as a background service or tray agent.
- It does not yet capture or restore workspace snapshots.

## Further documentation

- Architecture: [`docs/architecture.md`](/C:/Users/decoy/sessionguard-win11/docs/architecture.md)
- Product brief: [`docs/product-brief.md`](/C:/Users/decoy/sessionguard-win11/docs/product-brief.md)
- Limitations: [`docs/limitations.md`](/C:/Users/decoy/sessionguard-win11/docs/limitations.md)
- Roadmap: [`docs/roadmap.md`](/C:/Users/decoy/sessionguard-win11/docs/roadmap.md)
- Future service design: [`docs/future-service-architecture.md`](/C:/Users/decoy/sessionguard-win11/docs/future-service-architecture.md)
