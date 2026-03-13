# Development and Release Guide

Use this guide when you are working from the source tree, validating changes locally, or preparing a release.

## Requirements

- Windows 11
- .NET 9 SDK
- administrative rights only when testing install, service update, or service-backed write behavior

## Build and test

Build the solution:

```powershell
dotnet build SessionGuard.sln
```

Run the test suite:

```powershell
dotnet test SessionGuard.sln
```

## Run from source

Run the desktop app only:

```powershell
dotnet run --project src/SessionGuard.App/SessionGuard.App.csproj
```

Run with the local service host:

In one PowerShell window:

```powershell
dotnet run --project src/SessionGuard.Service/SessionGuard.Service.csproj -- console
```

In another:

```powershell
dotnet run --project src/SessionGuard.App/SessionGuard.App.csproj
```

The app should report `Control plane: Service` when the local service host is active.

## Validation

Run UI smoke only:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/ui/Run-UiSmoke.ps1
```

Run the full local validation flow:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/ci/Invoke-WindowsValidation.ps1
```

Use the manual checklist when you want a structured release-readiness pass:

- [Manual Validation Checklist](manual-validation.md)

## Install from source

Preferred combined install path:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/install/Install-SessionGuard.ps1 -SelfContained
```

Useful switches:

- `-DoNotLaunchApp`
- `-DoNotStartService`
- `-ValidateOnly -AsJson`

Service-only path:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/service/Publish-SessionGuardService.ps1
powershell -ExecutionPolicy Bypass -File scripts/service/Install-SessionGuardService.ps1
```

## Publish local binaries

Publish the desktop app:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/app/Publish-SessionGuardApp.ps1
```

Publish the combined setup layout:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/install/Publish-SessionGuardBundle.ps1 -SelfContained
```

Publish full release assets locally:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/release/Publish-SessionGuardReleaseAssets.ps1 -SelfContained
```

That produces:

- `sessionguard-win11-setup-<version>-win-x64.zip`
- `sessionguard-win11-app-<version>-win-x64.zip`
- `sessionguard-win11-service-<version>-win-x64.zip`
- `sessionguard-win11-source-<version>.zip`

## Tag-driven release flow

GitHub Actions will publish release assets when you push an annotated `vX.Y.Z` tag.

Requirements:

- the tag must match [`Directory.Build.props`](../Directory.Build.props)
- the matching release notes file must exist under [`docs/releases`](releases)

Example:

```powershell
git tag -a v1.1.4 -m "SessionGuard 1.1.4"
git push origin main
git push origin v1.1.4
```

The release workflow:

- runs the repo-owned Windows validation flow
- publishes self-contained `win-x64` binaries
- creates the setup, app, service, and source zip assets
- uploads those assets to the matching GitHub Release
