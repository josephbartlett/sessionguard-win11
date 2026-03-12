# Getting Started

This guide is the fastest way to get SessionGuard running locally and understand the common operator paths.

## Requirements

- Windows 11
- .NET 9 SDK for source builds
- administrative rights only for service installation, service update, and native mitigation writes

## 1. Build the solution

```powershell
dotnet build SessionGuard.sln
```

## 2. Try the desktop app without installing anything

```powershell
dotnet run --project src/SessionGuard.App/SessionGuard.App.csproj
```

This is the quickest path to evaluate the UI and restart-awareness features.

What to expect:

- the app opens in `Simple view` by default
- it can still scan locally if the service is not running
- if the service is unavailable, the app reports `Control plane: Local fallback`
- mitigation and approval writes are intentionally disabled in local fallback

## 3. Run with the local service host

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
- service-owned actions become available
- the service writes `state/service-health.json`

## 4. Install the Windows Service

From an elevated PowerShell session:

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

## 5. Publish a desktop executable

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

- `artifacts\releases\<version>\sessionguard-win11-app-<version>-win-x64.zip`
- `artifacts\releases\<version>\sessionguard-win11-service-<version>-win-x64.zip`
- `artifacts\releases\<version>\sessionguard-win11-source-<version>.zip`

## 6. Trigger the GitHub release flow

The repo now supports tag-driven binary publishing.

Requirements:

- the tag must match [`Directory.Build.props`](../Directory.Build.props)
- the matching release notes file must exist under [`docs/releases`](releases)

Example:

```powershell
git tag -a v1.0.1 -m "SessionGuard 1.0.1"
git push origin main
git push origin v1.0.1
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

### Policy diagnostics are visible

If [`config/policies.json`](../config/policies.json) is malformed or conflicting, SessionGuard keeps scanning but surfaces policy configuration problems instead of pretending the rules still applied cleanly.

## Where to go next

- [Manual validation checklist](manual-validation.md)
- [Architecture](architecture.md)
- [Limitations](limitations.md)
