# Getting Started

This guide is the fastest way to get SessionGuard running locally and understand the common operator paths.

## Requirements

- Windows 11
- .NET 9 SDK for source builds
- administrative rights only for service installation, service update, and service-backed guard-mode, mitigation, or approval changes

## 1. Build the solution

```powershell
dotnet build SessionGuard.sln
```

## 2. Install from a downloaded release bundle

If you downloaded the combined release zip instead of cloning the repo, extract it and run:

```powershell
powershell -ExecutionPolicy Bypass -File .\Install-SessionGuard.ps1
```

That is the preferred operator path for a real machine. It:

- installs the shared runtime under `Program Files\SessionGuard`
- installs the Windows Service
- registers the app to start at user sign-in
- launches the app minimized to the tray unless you opt out

Run the installer from the same signed-in Windows account that should get the tray auto-start. The startup registration is written to that user's `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` key.

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
- the tray menu becomes the quickest day-to-day path
- monitoring becomes service-backed
- guard-mode, mitigation, and approval changes still require running `SessionGuard.App.exe` as administrator
- the service writes `state/service-health.json`

## 5. Install the service and tray app from source

Preferred install path from an elevated PowerShell session:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/install/Install-SessionGuard.ps1 -SelfContained
```

What this does:

- installs the shared runtime under `Program Files\SessionGuard`
- installs the Windows Service
- registers the app to start at user sign-in
- launches the app minimized to the tray unless you opt out
- ensures later manual launches reuse the running tray app instead of opening a duplicate copy

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

## 6. Publish a desktop executable

If you want the app as a local distributable folder:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/app/Publish-SessionGuardApp.ps1
```

Output:

- `artifacts\publish\SessionGuard.App\SessionGuard.App.exe`

If you want app, service, and source zip assets together:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/release/Publish-SessionGuardReleaseAssets.ps1 -SelfContained
```

Output:

- `artifacts\releases\<version>\sessionguard-win11-bundle-<version>-win-x64.zip`
- `artifacts\releases\<version>\sessionguard-win11-app-<version>-win-x64.zip`
- `artifacts\releases\<version>\sessionguard-win11-service-<version>-win-x64.zip`
- `artifacts\releases\<version>\sessionguard-win11-source-<version>.zip`

The preferred end-user asset is the combined bundle zip because it includes both runtimes plus the top-level install and uninstall scripts.

## 7. Trigger the GitHub release flow

The repo now supports tag-driven binary publishing.

Requirements:

- the tag must match [`Directory.Build.props`](../Directory.Build.props)
- the matching release notes file must exist under [`docs/releases`](releases)

Example:

```powershell
git tag -a v1.0.4 -m "SessionGuard 1.0.4"
git push origin main
git push origin v1.0.4
```

The release workflow will validate the build, publish binaries, and attach the versioned zip assets to the GitHub Release.

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

If the service is connected but the UI says `Action access: Requires elevated app`, monitoring is working correctly. Launch `SessionGuard.App.exe` from an elevated shell when you need SessionGuard to change guard mode, mitigation, or approval state.

### Policy diagnostics are visible

If [`config/policies.json`](../config/policies.json) is malformed or conflicting, SessionGuard keeps scanning but surfaces policy configuration problems instead of pretending the rules still applied cleanly.

## Where to go next

- [Runtime model](runtime-model.md)
- [Manual validation checklist](manual-validation.md)
- [Architecture](architecture.md)
- [Limitations](limitations.md)
