# Architecture

## Decision summary

SessionGuard MVP uses a layered `WPF + Core + Infrastructure` structure:

- `SessionGuard.App`: presentation, commands, timer-driven refresh, and user-facing actions.
- `SessionGuard.Core`: models, process matching, restart status evaluation, scan orchestration, and service contracts.
- `SessionGuard.Infrastructure`: Windows-specific implementations for config loading, file logging, process inventory, registry inspection, and registry-backed mitigation settings.

WPF was chosen over WinUI for the MVP because the priority is a stable desktop monitor with fast local iteration and direct Windows API access. The architecture keeps system-facing logic out of the UI so a service/tray split can be added later.

## Runtime flow

1. The app resolves runtime paths from the repository root or executable location.
2. The configuration repository loads:
   - [`config/appsettings.json`](/C:/Users/decoy/sessionguard-win11/config/appsettings.json)
   - [`config/protected-processes.json`](/C:/Users/decoy/sessionguard-win11/config/protected-processes.json)
3. The coordinator runs a scan:
   - protected-process detection
   - restart signal inspection across multiple providers
   - mitigation state inspection
4. Core logic aggregates the signals into:
   - `Safe`
   - `Restart Pending`
   - `Protected Session Active`
   - `Mitigated / Deferred`
   - `Unknown / Limited Visibility`
5. The WPF view model updates the dashboard and, when guard mode is enabled, can raise the window when risk transitions to high.

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
  - `current-scan.json`: latest machine-readable scan snapshot for future tray/service work.

The log and state folders are intentionally excluded from source control.

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
- Move mitigation logic into a service-hosted implementation without changing core status evaluation.
- Replace or complement the WPF front end with a tray app while retaining the current core and infrastructure contracts.
