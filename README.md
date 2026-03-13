# SessionGuard

SessionGuard is a Windows 11 desktop utility that helps reduce surprise restart disruption related to Windows Update without disabling Windows Update itself.

It is designed for people who keep live work open for long stretches of time: terminals, editors, browser sessions, local dev servers, research tabs, and other tools that are painful to lose at the wrong moment.

## What SessionGuard does

- inspects bounded restart and reboot-required signals that are available from user mode
- detects configured protected apps and derives workspace-risk hints from them
- surfaces restart pressure, policy state, and recommended next steps in a Windows-native desktop UI
- supports a background service for continuous monitoring and service-owned write actions
- applies a small set of reversible native mitigation settings when run with the required permissions

## What SessionGuard does not do

- disable Windows Update
- guarantee prevention of every Windows restart path
- inspect unsaved buffers, tab importance, or app-specific internal state
- restore unsaved workspaces after a restart

## Requirements

- Windows 11
- .NET 9 SDK for source builds and local `dotnet run`
- administrative rights only when you need to install or update the service, or when you want the app to request service-owned guard-mode, mitigation, or approval changes

## Quick Start

### Build the solution

```powershell
dotnet build SessionGuard.sln
```

### Install from the release setup zip

If you downloaded `sessionguard-win11-setup-<version>-win-x64.zip`, extract it and run this from an elevated PowerShell session:

```powershell
powershell -ExecutionPolicy Bypass -File .\Install-SessionGuard.ps1
```

That is the preferred end-user path. It installs the background service, registers the tray app to start at sign-in for the current user, and launches the app minimized to the tray unless you opt out with `-DoNotLaunchApp`.

Install it from the same signed-in Windows account that should get the tray auto-start. The installer writes the startup registration under that user's `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` key.

### Try the desktop app only

```powershell
dotnet run --project src/SessionGuard.App/SessionGuard.App.csproj
```

This is the fastest way to try SessionGuard. In this mode the app can still monitor restart state, but service-owned write actions stay unavailable if the background service is not running.

What to expect:

- the app opens in `Simple view`
- closing the window keeps it running in the tray
- if you launch the same app runtime again at the same privilege level, SessionGuard reuses the existing tray app instead of opening a second copy
- if an installed tray app is already running, a source-built `dotnet run` app now starts separately instead of being swallowed by the installed instance

### Run the app with the local service host

In one PowerShell window:

```powershell
dotnet run --project src/SessionGuard.Service/SessionGuard.Service.csproj -- console
```

In another:

```powershell
dotnet run --project src/SessionGuard.App/SessionGuard.App.csproj
```

When this path is active, the app should report `Control plane: Service`. Monitoring stays available in a normal user session, but guard-mode, mitigation, and approval changes still require running `SessionGuard.App.exe` as administrator. That elevated launch now starts a separate elevated SessionGuard app instead of reusing the normal tray session.

### Install the background service

Preferred source-repo install path:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/install/Install-SessionGuard.ps1 -SelfContained
```

This installs the shared SessionGuard runtime to `Program Files\SessionGuard`, installs the Windows Service, registers the app to start at sign-in for the current user, and launches the app minimized to the tray unless you opt out with `-DoNotLaunchApp`.

Useful install switches:

- `-DoNotLaunchApp`: install everything without opening the tray app immediately
- `-DoNotStartService`: install without starting the Windows Service yet
- `-ValidateOnly -AsJson`: check install readiness without changing the machine

Advanced service-only path:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/service/Publish-SessionGuardService.ps1
powershell -ExecutionPolicy Bypass -File scripts/service/Install-SessionGuardService.ps1
```

### Publish a combined installable bundle

```powershell
powershell -ExecutionPolicy Bypass -File scripts/install/Publish-SessionGuardBundle.ps1 -SelfContained
```

This produces a shared app-plus-service runtime under `artifacts\publish\SessionGuard`, including root-level `Install-SessionGuard.ps1` and `Uninstall-SessionGuard.ps1` entry points for the extracted setup package.

### Publish a distributable desktop build

From a normal PowerShell session:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/app/Publish-SessionGuardApp.ps1
```

The published desktop executable will be written to `artifacts\publish\SessionGuard.App\SessionGuard.App.exe`.

### Publish full release assets locally

```powershell
powershell -ExecutionPolicy Bypass -File scripts/release/Publish-SessionGuardReleaseAssets.ps1 -SelfContained
```

This produces versioned setup, app, service, and source zip assets under `artifacts\releases\<version>\`.

## How SessionGuard Runs

SessionGuard has two processes with different responsibilities:

- **`SessionGuard.Service.exe`**: background engine
  - runs scans continuously
  - owns service-backed mitigation and approval actions
  - auto-starts with Windows when installed as a service
  - does **not** show a tray icon
- **`SessionGuard.App.exe`**: tray and dashboard shell
  - starts as a tray-first shell in installed mode
  - shows the window and the tray icon
  - talks to the service when it is available
  - falls back to local read-only monitoring when the service is unavailable
  - can be registered to auto-start at user sign-in
  - reuses the already-running tray app when launched again from the same installed path and privilege level
  - starts a separate elevated app session when you run it as administrator to request service-owned mitigation or approval changes

The tray icon belongs to the app, not the service.

Typical modes:

- **App only**: useful for inspection and local fallback monitoring
- **App + local service host**: useful for development or manual validation
- **Installed mode**: service auto-starts with Windows, app auto-starts at sign-in for the installing user and starts minimized to the tray
- **Elevated app session**: useful when the service is connected but you need to request service-owned write actions

In normal installed use, the tray menu is the daily path. Open the dashboard when you need a fuller explanation or want the technical tables.

## Common Tasks

### Run tests

```powershell
dotnet test SessionGuard.sln
```

### Run the full local validation flow

```powershell
powershell -ExecutionPolicy Bypass -File scripts/ci/Invoke-WindowsValidation.ps1
```

### Run the UI smoke pass only

```powershell
powershell -ExecutionPolicy Bypass -File scripts/ui/Run-UiSmoke.ps1
```

### Package the tracked source tree

```powershell
powershell -ExecutionPolicy Bypass -File scripts/package-release.ps1
```

## Configuration

SessionGuard keeps operator-editable behavior in JSON files:

- [`config/appsettings.json`](config/appsettings.json): scan interval, warnings, active-hours defaults, and UI preferences
- [`config/protected-processes.json`](config/protected-processes.json): protected process list
- [`config/policies.json`](config/policies.json): restart windows, blocking rules, and approval requirements

Published service layouts preserve live runtime config under `config/` and shipped defaults under `config.defaults/`.

## Admin vs Non-Admin Behavior

- non-elevated mode supports monitoring, scanning, config changes, logs, and dashboard status
- mitigation apply or reset actions require elevation because they write under `HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate`
- guard-mode changes, restart approval changes, and mitigation writes are service-owned and require an elevated app session when connected to the service
- if the app falls back locally, mitigation and approval actions become read-only on purpose
- new installs now default `warningBehavior.raiseWindowOnHighRisk` to `false`, so SessionGuard prefers tray-first attention unless you opt into raising the dashboard

## Logs and State

SessionGuard writes local diagnostics and machine-readable state to:

- `logs/sessionguard-app-YYYYMMDD.log`
- `logs/sessionguard-service-YYYYMMDD.log`
- `state/current-scan.json`
- `state/workspace-snapshot.json`
- `state/policy-approval.json`
- `state/service-health.json`

Published service layouts may also write config migration backups under `state/config-backups/`.

## Install and Uninstall

Install both the service and the tray app startup registration from source:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/install/Install-SessionGuard.ps1 -SelfContained
```

Install from an extracted setup zip:

```powershell
powershell -ExecutionPolicy Bypass -File .\Install-SessionGuard.ps1
```

Remove the combined install:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/install/Uninstall-SessionGuard.ps1 -RemoveFiles
```

Or, from an extracted setup zip:

```powershell
powershell -ExecutionPolicy Bypass -File .\Uninstall-SessionGuard.ps1 -RemoveFiles
```

## Release Automation

Pushing an annotated `vX.Y.Z` tag now triggers the `Release Assets` workflow. That workflow:

- runs the repo-owned Windows validation flow
- publishes self-contained `win-x64` desktop and service binaries
- publishes a setup zip that contains both runtimes plus install scripts
- strips live logs and state from the public app, service, and setup artifacts
- uploads the generated zip assets
- creates or updates the matching GitHub Release

The tag must match the version in [`Directory.Build.props`](Directory.Build.props), and the matching release notes file must exist under [`docs/releases`](docs/releases), for example [`docs/releases/v1.1.1.md`](docs/releases/v1.1.1.md).

## Documentation Map

- [Getting started and common operations](docs/getting-started.md)
- [Runtime model](docs/runtime-model.md)
- [Manual validation checklist](docs/manual-validation.md)
- [Architecture](docs/architecture.md)
- [Product brief](docs/product-brief.md)
- [Limitations](docs/limitations.md)
- [Roadmap](docs/roadmap.md)
- [Future service and shell direction](docs/future-service-architecture.md)
- [Current release notes](docs/releases/v1.1.1.md)

## Practical Positioning

Use SessionGuard as a restart-awareness and mitigation helper. It is most valuable when you want the machine to stay up to date while reducing the chance that Windows Update catches an active workspace at the wrong time.

The current full product is best suited to direct-download distribution. A Microsoft Store edition would likely need a reduced packaging and privilege model.
