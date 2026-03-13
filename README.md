# SessionGuard

SessionGuard is a Windows 11 utility that helps reduce surprise Windows Update restart disruption without disabling Windows Update.

It is designed for people who keep live work open for long stretches of time: terminals, editors, browsers, local dev servers, research tabs, and similar restart-sensitive tools.

## What SessionGuard does

- monitors bounded restart and reboot-required signals that are available from user mode
- detects configured protected apps and derives workspace-risk hints from them
- surfaces restart pressure, policy state, and recommended next steps in a tray-first desktop app
- supports a background service for continuous monitoring and service-owned write actions
- applies a small set of reversible native mitigation settings when run with the required permissions

## Preferred install

The preferred end-user download is:

- `sessionguard-win11-setup-<version>-win-x64.zip`

Extract it, open an elevated PowerShell session in the extracted folder, and run:

```powershell
powershell -ExecutionPolicy Bypass -File .\Install-SessionGuard.ps1
```

That install path:

- installs the Windows Service
- installs the shared runtime under `C:\Program Files\SessionGuard`
- registers the tray app to start at sign-in for the current user
- launches the app minimized to the tray unless you opt out with `-DoNotLaunchApp`

Install it from the same signed-in Windows account that should see the tray icon at sign-in. The startup registration is written to that user's `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` key.

For full install, uninstall, and source-run instructions, use [Getting Started](docs/getting-started.md).

## Runtime at a glance

SessionGuard has two processes:

- **`SessionGuard.Service.exe`**: background engine
  - auto-starts with Windows when installed
  - runs monitoring continuously
  - owns service-backed write actions
- **`SessionGuard.App.exe`**: tray icon and dashboard
  - starts at user sign-in when installed
  - owns the tray icon and visible UI
  - connects to the service when available
  - falls back to local read-only monitoring when the service is unavailable

Important behavior:

- the tray icon belongs to the app, not the service
- service-backed guard-mode, mitigation, and approval actions require both service connectivity and an elevated app session
- in normal installed use, the tray menu is the daily path and the dashboard is opened on demand

For the fuller app-versus-service explanation, see [Runtime Model](docs/runtime-model.md).

## If you cloned the repo

You only need the .NET 9 SDK if you are building from source.

Use these docs:

- [Getting Started](docs/getting-started.md) for source build, local service-host, and install paths
- [Development and Release Guide](docs/development.md) for validation, packaging, and tag-driven release steps

## Limits

SessionGuard does **not**:

- disable Windows Update
- guarantee prevention of every Windows restart path
- inspect unsaved buffers, tab importance, or app-specific internal state
- restore unsaved workspaces after a restart

See [Limitations](docs/limitations.md) for the full platform and permissions boundaries.

## Documentation

- [Getting Started](docs/getting-started.md)
- [Runtime Model](docs/runtime-model.md)
- [Development and Release Guide](docs/development.md)
- [Manual Validation Checklist](docs/manual-validation.md)
- [Architecture](docs/architecture.md)
- [Product Brief](docs/product-brief.md)
- [Limitations](docs/limitations.md)
- [Roadmap](docs/roadmap.md)
- [Future service and shell direction](docs/future-service-architecture.md)
- [Current release notes](docs/releases/v1.1.2.md)

## Practical positioning

SessionGuard is best used as a restart-awareness and mitigation helper for a machine that should stay up to date while reducing the chance that Windows Update catches an active workspace at the wrong time.

The full product is best suited to direct-download distribution. A Microsoft Store edition would likely need a reduced packaging and privilege model.
