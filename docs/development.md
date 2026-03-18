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
- `-SkipBundleVerification`
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

Verify the published bundle layout before install:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/install/Verify-SessionGuardBundle.ps1 -BundleRoot artifacts/publish/SessionGuard
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
- `sessionguard-win11-sha256-<version>.txt`

For direct-download release prep, verify the generated setup zip hash against that checksum file and keep the checksum asset with the published release.

## Signing and trust

SessionGuard uses standard Authenticode signing for official releases. EV signing is still optional outside the repo and is mainly a trust and SmartScreen reputation choice, not a code requirement.

Supported signing inputs:

- `SESSIONGUARD_SIGN_CERT_BASE64`: base64-encoded `.pfx`
- `SESSIONGUARD_SIGN_CERT_FILE`: local path to a `.pfx`
- `SESSIONGUARD_SIGN_CERT_PASSWORD`: `.pfx` password
- `SESSIONGUARD_SIGN_TIMESTAMP_URL`: optional RFC3161 timestamp URL
- `SESSIONGUARD_SIGNTOOL_PATH`: optional explicit `signtool.exe` path

Official tag releases should use the release workflow with signing required. Local builds can stay unsigned unless you explicitly supply a certificate.
For GitHub Actions, configure `SESSIONGUARD_SIGN_CERT_BASE64` and `SESSIONGUARD_SIGN_CERT_PASSWORD` as repository secrets. Optional timestamp and `signtool.exe` overrides can be supplied through repository variables.

Local signed release example:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/release/Publish-SessionGuardReleaseAssets.ps1 -Version 1.4.0 -Configuration Release -Runtime win-x64 -SelfContained -RequireSigning
powershell -ExecutionPolicy Bypass -File scripts/signing/Verify-SessionGuardReleaseSignatures.ps1 -OutputRoot artifacts/releases/1.4.0 -RequireSigned
```

## Tag-driven release flow

GitHub Actions will publish release assets when you push an annotated `vX.Y.Z` tag.

Requirements:

- the tag must match [`Directory.Build.props`](../Directory.Build.props)
- the matching release notes file must exist under [`docs/releases`](releases)

Example:

```powershell
git tag -a v1.4.0 -m "SessionGuard 1.4.0"
git push origin main
git push origin v1.4.0
```

The release workflow:

- runs the repo-owned Windows validation flow
- publishes self-contained `win-x64` binaries
- requires configured signing secrets and verifies official release signatures
- creates the setup, app, service, source, and checksum assets
- uploads those assets to the matching GitHub Release
