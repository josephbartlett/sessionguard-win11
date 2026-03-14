# Getting Started

This guide is the fastest way to get SessionGuard running locally and understand the common operator paths.

## Requirements

- Windows 11
- .NET 9 SDK for source builds
- administrative rights only for service installation, service update, and service-backed guard-mode, mitigation, or approval changes

## 1. Install from a downloaded setup zip

If you downloaded `sessionguard-win11-setup-<version>-win-x64.zip` instead of cloning the repo, extract it and run:

```powershell
powershell -ExecutionPolicy Bypass -File .\Install-SessionGuard.ps1
```

That is the preferred operator path for a real machine. It:

- installs the shared runtime under `Program Files\SessionGuard`
- installs the Windows Service
- registers the app to start at user sign-in
- scopes the installed service control plane plus `logs/` and `state/` access to that user, administrators, and `SYSTEM`
- stops a running installed tray app before replacing files during reinstall or upgrade
- attempts to launch the app minimized to the tray unless you opt out with `-DoNotLaunchApp`

Run the installer from the same signed-in Windows account that should get the tray auto-start. The startup registration is written to that user's `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` key.

If Windows blocks the immediate launch, the install still succeeded. SessionGuard setup zips are direct-download unsigned binaries today, so Windows may show a SmartScreen or protection prompt on first launch. Open `C:\Program Files\SessionGuard\SessionGuard.App.exe` manually from your normal desktop session, use `-DoNotLaunchApp`, or wait for the next sign-in.

Useful install switches:

- `-DoNotLaunchApp`: install without opening the tray app right away
- `-DoNotStartService`: install without starting the Windows Service yet
- `-ValidateOnly -AsJson`: check readiness without changing the machine

## 2. Build the solution

```powershell
dotnet build SessionGuard.sln
```

## 3. Try the desktop app without installing anything

```powershell
dotnet run --project src/SessionGuard.App/SessionGuard.App.csproj
```

This is the quickest path to evaluate the UI and restart-awareness features.

What to expect:

- the app opens in `Simple view` by default
- closing the window keeps SessionGuard running in the tray
- it can still scan locally if the service is not running
- if the service is unavailable, the app reports `Control plane: Local fallback`
- mitigation and approval writes are intentionally disabled in local fallback

## 4. Run with the local service host

In one PowerShell window:

```powershell
dotnet run --project src/SessionGuard.Service/SessionGuard.Service.csproj -- console
```

In another:

```powershell
dotnet run --project src/SessionGuard.App/SessionGuard.App.csproj
```

What to expect:

- the app should report `Control plane: Service`
- the tray menu becomes the quickest day-to-day path for state, next step, and the common action
- monitoring becomes service-backed
- guard-mode, mitigation, and approval changes still require an elevated SessionGuard window
- use `Open elevated controls` when the dashboard tells you the service is connected but write access still needs elevation
- that elevated launch starts a separate elevated SessionGuard app instead of reusing the normal tray app
- the service writes `state/service-health.json`
- in installed mode, only the installing user plus administrators and `SYSTEM` can use the service control plane or read the installed `logs/` and `state/` directories

## 5. Install the service and tray app from source

Preferred install path from an elevated PowerShell session:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/install/Install-SessionGuard.ps1 -SelfContained
```

What this does:

- installs the shared runtime under `Program Files\SessionGuard`
- installs the Windows Service
- registers the app to start at user sign-in
- stops a running installed tray app before replacing files during reinstall or upgrade
- attempts to launch the app minimized to the tray unless you opt out with `-DoNotLaunchApp`
- ensures later manual launches of the same installed app and privilege level reuse the running tray app instead of opening a duplicate copy

Advanced service-only path:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/service/Publish-SessionGuardService.ps1
powershell -ExecutionPolicy Bypass -File scripts/service/Install-SessionGuardService.ps1
```

Useful related commands:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/service/Get-SessionGuardServiceStatus.ps1
powershell -ExecutionPolicy Bypass -File scripts/service/Update-SessionGuardServiceDeployment.ps1
powershell -ExecutionPolicy Bypass -File scripts/service/Uninstall-SessionGuardService.ps1
```

## Common Issues

### The app says `Control plane: Local fallback`

That usually means the service is not running, not installed, or not reachable over the local named-pipe control plane.

Check:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/service/Get-SessionGuardServiceStatus.ps1
```

### Mitigation buttons are disabled

That is expected when:

- the app is not elevated
- the background service path is unavailable
- the app is intentionally in read-only local fallback mode

If the service is connected but the UI shows `Needs elevated window` under `Protection changes`, monitoring is working correctly. Use `Open elevated controls` from the dashboard or tray-driven workflow when you need SessionGuard to change guard mode, mitigation, or approval state. For manual troubleshooting, the equivalent advanced launch is `SessionGuard.App.exe --disable-tray --technical-view --disable-single-instance` from an elevated shell.

### Policy diagnostics are visible

If [`config/policies.json`](../config/policies.json) is malformed or conflicting, SessionGuard keeps scanning but surfaces policy configuration problems instead of pretending the rules still applied cleanly.

## Where to go next

- [Runtime model](runtime-model.md)
- [Manual validation checklist](manual-validation.md)
- [Development and release guide](development.md)
- [Architecture](architecture.md)
- [Limitations](limitations.md)
