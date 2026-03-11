# Architecture

## Decision summary

SessionGuard MVP uses a layered `WPF + Core + Infrastructure` structure:

- `SessionGuard.App`: presentation, tray-aware dashboard behavior, and user-facing actions.
- `SessionGuard.Core`: models, process matching, restart status evaluation, scan orchestration, and service contracts.
- `SessionGuard.Infrastructure`: Windows-specific implementations for config loading, file logging, process inventory, registry inspection, named-pipe IPC, and registry-backed mitigation settings.
- `SessionGuard.Service`: service-hostable background worker plus named-pipe server that reuses the same coordinator and shared state output.

WPF was chosen over WinUI for the MVP because the priority is a stable desktop monitor with fast local iteration and direct Windows API access. The architecture keeps system-facing logic out of the UI so the service boundary and future tray shell can evolve without rewriting the core scan logic.

## Runtime flow

1. The app and service resolve runtime paths from the repository root or executable location.
2. The service host owns the authoritative background scan loop and named-pipe control plane when it is running.
3. The desktop app creates a hybrid control plane:
   - prefer the service over named pipes
   - fall back to a local in-process scan path if the service is unavailable
4. The configuration repository loads:
   - [`config/appsettings.json`](/C:/Users/decoy/sessionguard-win11/config/appsettings.json)
   - [`config/protected-processes.json`](/C:/Users/decoy/sessionguard-win11/config/protected-processes.json)
5. The coordinator runs a scan:
   - protected-process detection
   - restart signal inspection across multiple providers
   - mitigation state inspection
6. Core logic aggregates the signals into:
   - `Safe`
   - `Restart Pending`
   - `Protected Session Active`
   - `Mitigated / Deferred`
   - `Unknown / Limited Visibility`
7. The service or local fallback path persists `state/current-scan.json`, and the WPF view model updates the dashboard.
8. When guard mode is enabled, the WPF shell can raise the dashboard on a high-risk transition and otherwise stay minimized in the tray.

## Restart signal inspection

The current implementation uses multiple bounded, user-mode-friendly providers:

- Registry restart signals:
  - `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending`
  - `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\PackagesPending`
  - `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\PostRebootReporting`
  - `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired`
  - `HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\PendingFileRenameOperations`
  - `HKLM\SOFTWARE\Microsoft\Updates\UpdateExeVolatile`
- Windows Update Agent COM:
  - `Microsoft.Update.SystemInfo.RebootRequired`
- Windows Update UX settings:
  - active hours
  - pause-updates expiry
  - restart notifications
  - smart scheduler prediction clues
- Windows Update scheduled task visibility:
  - `\Microsoft\Windows\WindowsUpdate\Scheduled Start`

Core aggregation now distinguishes:

- definitive pending restart signals
- ambiguous orchestration or low-confidence clues
- limited visibility/provider read failures

These signals are useful but incomplete. SessionGuard treats them as best-effort indicators and surfaces limited visibility when reads fail.

## Mitigation model

SessionGuard only manages reversible, native Windows settings:

- `HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU\NoAutoRebootWithLoggedOnUsers`
- `HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\SetActiveHours`
- `HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\ActiveHoursStart`
- `HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\ActiveHoursEnd`

Before writing managed values, the infrastructure layer captures previous values into a local backup file under `state/`. Resetting managed settings restores those saved values when available, otherwise it removes the values SessionGuard added.

## Configuration and state paths

- `config/`: source-controlled default runtime config for protected processes and app behavior.
- `logs/`: local structured logs created on demand.
- `state/`: local backup state used for mitigation reset behavior.
  - `current-scan.json`: latest machine-readable scan snapshot shared by the app and service paths.

The log and state folders are intentionally excluded from source control.

## Service and control plane

`SessionGuard.Service` is now present as a Windows service-hostable worker project with a local named-pipe server. The current split already provides:

- loads the same root `config/*.json`
- runs the same multi-provider scan coordinator
- writes the same `state/current-scan.json`
- owns mitigation commands when reached through the pipe control plane
- uses the same file logger and mitigation/state services

`SessionGuard.App` currently acts as a tray-aware dashboard client layered over a hybrid control plane. That keeps the background path and the desktop path aligned while full service installation, startup, and dedicated tray-shell packaging are still in progress.

## Logging

Logging is lightweight JSON-line output and captures:

- scan start and finish
- protected process counts
- mitigation actions
- provider failures
- UI-level action failures

This keeps the MVP auditable without introducing a full telemetry stack.

## Extension points

- Add more signal providers by implementing `IRestartSignalProvider`.
- Add richer workspace heuristics behind `IProtectedWorkspaceDetector`.
- Harden the named-pipe contract and move more privileged behavior behind the service boundary without changing core status evaluation.
- Replace or complement the current WPF shell with a dedicated tray app while retaining the current core and infrastructure contracts.
