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
- administrative rights only when you need to install the service or apply native mitigation settings

## Quick Start

### Build the solution

```powershell
dotnet build SessionGuard.sln
```

### Try the desktop app only

```powershell
dotnet run --project src/SessionGuard.App/SessionGuard.App.csproj
```

This is the fastest way to try SessionGuard. In this mode the app can still monitor restart state, but service-owned write actions stay unavailable if the background service is not running.

### Run the app with the local service host

In one PowerShell window:

```powershell
dotnet run --project src/SessionGuard.Service/SessionGuard.Service.csproj -- console
```

In another:

```powershell
dotnet run --project src/SessionGuard.App/SessionGuard.App.csproj
```

When this path is active, the app should report `Control plane: Service`.

### Install the background service

From an elevated PowerShell session:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/service/Publish-SessionGuardService.ps1
powershell -ExecutionPolicy Bypass -File scripts/service/Install-SessionGuardService.ps1
```

### Publish a distributable desktop build

```powershell
powershell -ExecutionPolicy Bypass -File scripts/app/Publish-SessionGuardApp.ps1
```

The published desktop executable will be written to `artifacts\publish\SessionGuard.App\SessionGuard.App.exe`.

### Publish full release assets locally

```powershell
powershell -ExecutionPolicy Bypass -File scripts/release/Publish-SessionGuardReleaseAssets.ps1 -SelfContained
```

This produces versioned app, service, and source zip assets under `artifacts\releases\<version>\`.

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
- mitigation and approval writes are service-owned; if the app falls back locally, those actions become read-only on purpose

## Logs and State

SessionGuard writes local diagnostics and machine-readable state to:

- `logs/sessionguard-app-YYYYMMDD.log`
- `logs/sessionguard-service-YYYYMMDD.log`
- `state/current-scan.json`
- `state/workspace-snapshot.json`
- `state/policy-approval.json`
- `state/service-health.json`

Published service layouts may also write config migration backups under `state/config-backups/`.

## Release Automation

Pushing an annotated `vX.Y.Z` tag now triggers the `Release Assets` workflow. That workflow:

- runs the repo-owned Windows validation flow
- publishes self-contained `win-x64` desktop and service binaries
- uploads the generated zip assets
- creates or updates the matching GitHub Release

The tag must match the version in [`Directory.Build.props`](Directory.Build.props), and the matching release notes file must exist under [`docs/releases`](docs/releases), for example [`docs/releases/v1.0.0.md`](docs/releases/v1.0.0.md).

## Documentation Map

- [Getting started and common operations](docs/getting-started.md)
- [Manual validation checklist](docs/manual-validation.md)
- [Architecture](docs/architecture.md)
- [Product brief](docs/product-brief.md)
- [Limitations](docs/limitations.md)
- [Roadmap](docs/roadmap.md)
- [Future service and shell direction](docs/future-service-architecture.md)
- [Current release notes](docs/releases/v1.0.0.md)

## Practical Positioning

Use SessionGuard as a restart-awareness and mitigation helper. It is most valuable when you want the machine to stay up to date while reducing the chance that Windows Update catches an active workspace at the wrong time.

The current full product is best suited to direct-download distribution. A Microsoft Store edition would likely need a reduced packaging and privilege model.
