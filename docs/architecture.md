# Architecture

## Decision summary

SessionGuard MVP uses a layered `WPF + Core + Infrastructure` structure:

- `SessionGuard.App`: presentation, tray-aware dashboard behavior, and user-facing actions.
- `SessionGuard.Core`: models, process matching, restart status evaluation, scan orchestration, and service contracts.
- `SessionGuard.Infrastructure`: Windows-specific implementations for config loading, file logging, process inventory, registry inspection, versioned named-pipe IPC, and registry-backed mitigation settings.
- `SessionGuard.Service`: service-hostable background worker plus named-pipe server that reuses the same coordinator and shared state output.

WPF was chosen over WinUI for the MVP because the priority is a stable desktop monitor with fast local iteration and direct Windows API access. The architecture keeps system-facing logic out of the UI so the service boundary and future tray shell can evolve without rewriting the core scan logic.

## Runtime flow

1. The app and service resolve runtime paths from the repository root or executable location.
   - published layouts that contain `install-manifest.json` or `config.defaults/` are treated as self-contained runtimes even if they live under the repo tree
   - source-tree runs still resolve back to the repo root for shared local development config
2. The desktop app can also boot in a deterministic `--ui-scenario <name>` mode for screenshot automation and UI smoke validation.
3. The service host owns the authoritative background scan loop and named-pipe control plane when it is running.
4. The desktop app creates a hybrid control plane:
   - prefer the service over named pipes
   - fall back to a local in-process scan path if the service is unavailable
5. The configuration repository loads:
   - [`config/appsettings.json`](/C:/Users/decoy/sessionguard-win11/config/appsettings.json)
   - [`config/protected-processes.json`](/C:/Users/decoy/sessionguard-win11/config/protected-processes.json)
   - [`config/policies.json`](/C:/Users/decoy/sessionguard-win11/config/policies.json)
   - when a published runtime has `config.defaults/`, missing live config files are seeded into `config/` before load
6. The coordinator runs a scan:
   - protected-process detection
   - workspace-risk heuristic analysis using protected-tool matches plus bounded runtime-process clues
   - restart signal inspection across multiple providers
   - mitigation state inspection
   - policy evaluation against restart windows, process or workspace blocking rules, and any active temporary approval window
7. Core logic aggregates the signals into:
   - `Safe`
   - `Restart Pending`
   - `Protected Session Active`
   - `Mitigated / Deferred`
   - `Unknown / Limited Visibility`
8. The service or local fallback path persists `state/current-scan.json`; when workspace risk is present it also writes `state/workspace-snapshot.json`, and the WPF view model updates the dashboard.
9. Core operator-alert evaluation turns scan transitions into:
   - policy-timing summary text for the dashboard
   - compact tray status text for the notify-icon context menu
   - desktop notification events for service fallback, policy transitions, and approval timing
10. When guard mode is enabled, the WPF shell can raise the dashboard on a high-risk transition and otherwise stay minimized in the tray.

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

## Workspace safety model

`SessionGuard.Core` now has a distinct workspace-risk layer separate from restart-state inspection.

The current heuristics are intentionally bounded:

- terminals and shells are treated as high-risk interactive context
- editors and IDEs are treated as elevated disruption risk
- browsers are treated as elevated disruption risk with medium confidence because tab or session importance is not knowable from process names alone
- local dev-server style runtimes are treated as advisory-to-high disruption risk depending on whether interactive tools are also present
- other configured protected tools are still honored as operator-defined protected context even when SessionGuard cannot infer richer semantics

The output is a `WorkspaceStateSnapshot` that includes:

- summary text
- highest severity
- confidence
- grouped risk items with process lists and explicit reasons

This keeps the new behavior explainable and testable without claiming recovery capabilities the product does not yet have.

## Policy engine

`SessionGuard.Core` now includes a distinct policy-evaluation layer that is separate from raw restart-state detection and separate from mitigation writes.

The current policy schema supports:

- restart-window rules
- process-block rules
- workspace-category block rules
- approval-required rules

The policy configuration is stored in `config/policies.json` instead of inside `appsettings.json`. That keeps static app behavior, protected-process defaults, and operator policy rules separate.

At scan time, the coordinator evaluates policy rules against:

- the aggregated restart state and risk level
- the current workspace snapshot
- the currently observed running-process summary
- the persisted approval window state, if one exists

The output is a `PolicyEvaluation` that includes:

- current policy decision
- whether restart is blocked right now
- whether a temporary approval window is required
- whether a temporary approval window is active
- matched rules with deterministic priority ordering
- policy validation diagnostics for malformed or conflicting config
- evaluation trace text explaining why each matched rule fired and which approval rule won when overlaps exist

Temporary approval windows are persisted locally in `state/policy-approval.json`. This allows the service or the local fallback path to survive process restarts without losing the operator's active approval window immediately.

`JsonConfigurationRepository` now treats `config/policies.json` differently from the other config files: if policy JSON is malformed, SessionGuard keeps the rest of the scan pipeline alive, disables policy evaluation for that scan, and surfaces explicit diagnostics in the dashboard. That keeps restart-awareness and workspace detection available while still staying honest that policy enforcement is temporarily unavailable.

As of `0.5.2`, SessionGuard also tightens write ownership at the control-plane boundary:

- mitigation apply/reset actions are service-owned
- restart approval grant/clear actions are service-owned
- local fallback remains available for status and scanning, but it returns explicit read-only results for those write commands instead of performing them in-process

`NamedPipeSessionGuardControlPlane` now distinguishes transport or protocol unavailability from application-level service failures. The hybrid client only falls back on true control-plane unavailability, which prevents accidental local execution after a real service-side failure.

## Mitigation model

SessionGuard only manages reversible, native Windows settings:

- `HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU\NoAutoRebootWithLoggedOnUsers`
- `HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\SetActiveHours`
- `HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\ActiveHoursStart`
- `HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\ActiveHoursEnd`

Before writing managed values, the infrastructure layer captures previous values into a local backup file under `state/`. Resetting managed settings restores those saved values when available, otherwise it removes the values SessionGuard added.

## Configuration and state paths

- `config/`: mutable runtime config used by the app and service.
- `config.defaults/`: shipped default config for published service layouts, used to seed missing live config files without overwriting operator edits.
- `config/policies.json`: source-controlled default rule definitions for restart windows, blocking rules, and approval requirements.
- `logs/`: local structured logs created on demand.
- `state/`: local backup state used for mitigation reset behavior.
  - `current-scan.json`: latest machine-readable scan snapshot shared by the app and service paths.
  - `workspace-snapshot.json`: advisory workspace-risk metadata written only when heuristics detect risky activity.
  - `policy-approval.json`: temporary approval-window state used by the policy engine.
  - `service-health.json`: service lifecycle and diagnostics snapshot for startup, scan, and pipe health.

The log and state folders are intentionally excluded from source control.

Published service layouts now also include `install-manifest.json` with version, protocol, path, and validation metadata so install scripts can verify they are starting the expected runtime.

## Service and control plane

`SessionGuard.Service` is now present as a Windows service-hostable worker project with a local named-pipe server and operator scripts. The current split already provides:

- loads the same root `config/*.json`
- runs the same multi-provider scan coordinator
- writes the same `state/current-scan.json`
- owns mitigation commands when reached through the pipe control plane
- uses the same file logger and mitigation/state services
- exposes `probe` and `scan-now` console commands for local validation
- exposes `validate-runtime` so scripts can verify that the published layout is runnable before installation

`SessionGuard.App` currently acts as a tray-aware dashboard client layered over a hybrid control plane. That keeps the background path and the desktop path aligned while full service installation, startup, and dedicated tray-shell packaging are still in progress.

As of `0.5.3`, the desktop shell also derives an operator-alert layer from shared scan status:

- the tray menu shows compact status, mode, policy, and timing lines
- the view model emits notification events instead of letting the window shell infer policy transitions itself
- approval timing is surfaced explicitly so operators can see whether an approval is active, expiring soon, expired, or was cleared early

## Logging

Logging is lightweight JSON-line output and captures:

- scan start and finish
- protected process counts
- mitigation actions
- provider failures
- UI-level action failures

The desktop app and service now write to separate files:

- `logs/sessionguard-app-YYYYMMDD.log`
- `logs/sessionguard-service-YYYYMMDD.log`

The service also persists `state/service-health.json` so operator tooling can read current startup and error state directly.

The health snapshot now also records approval-window recovery state so startup diagnostics can show whether the service recovered a still-active temporary approval window or started with none.

This keeps the MVP auditable without introducing a full telemetry stack.

## UI smoke automation

The repo now includes a deterministic UI smoke path for WPF review:

- `SessionGuard.App` can boot in scenario mode with tray behavior disabled
- `tests/SessionGuard.UiSmoke` launches the real app executable, validates named UI elements through Windows UI Automation, and captures screenshots
- `scripts/ui/Run-UiSmoke.ps1` builds the solution and writes screenshots to `artifacts/ui/smoke`
- the scenario catalog now covers policy decision states and policy-timing text so UI smoke can catch policy-card regressions as well as the older restart and workspace views

This gives the repo a repeatable way to catch layout regressions, missing controls, and obviously bad UI states before a human manual pass.

## Extension points

- Add more signal providers by implementing `IRestartSignalProvider`.
- Add richer workspace heuristics behind `IProtectedWorkspaceDetector`.
- Extend the current workspace heuristic model into richer snapshot metadata and future recovery hints without changing restart-signal providers.
- Harden the named-pipe contract and move more privileged behavior behind the service boundary without changing core status evaluation.
- Replace or complement the current WPF shell with a dedicated tray app while retaining the current core and infrastructure contracts.
