# Manual Validation Checklist

Use this when you want a structured release-readiness pass without digging through the README.

## Build and smoke

1. Run `dotnet build SessionGuard.sln`.
2. Run `dotnet test SessionGuard.sln`.
3. Run `powershell -ExecutionPolicy Bypass -File scripts/ui/Run-UiSmoke.ps1`.
4. Review the screenshots under `artifacts/ui/smoke/` or `artifacts/ci/windows-validation/ui-smoke/`.

## Desktop app

1. Launch the app in a normal PowerShell session.
2. Confirm it opens in `Simple view`.
3. Confirm the default screen clearly shows current status, what to do now, and why SessionGuard is warning.
4. Switch to `Technical view` and confirm the detailed tables still render correctly.

## Workspace and process detection

1. Start a protected app such as Windows Terminal, VS Code, or a browser.
2. Trigger a scan or wait for the next interval.
3. Confirm the dashboard detects the app and explains why the session is restart-sensitive.
4. Edit [`config/protected-processes.json`](../config/protected-processes.json), save it, and confirm the next scan reflects the change.

## Service-backed path

1. Run the service locally or install it.
2. Launch the desktop app and confirm it reports `Control plane: Service`.
3. Run `powershell -ExecutionPolicy Bypass -File scripts/service/Get-SessionGuardServiceStatus.ps1`.
4. Confirm the status output reports control-plane reachability and health snapshot details.

## Elevated behavior

1. Launch the app from an elevated shell.
2. Apply the recommended mitigation.
3. Confirm the mitigation state changes to applied.
4. Reset managed settings and confirm the state returns to the prior value or `<not set>`.

## Published-layout and upgrade path

1. Run `powershell -ExecutionPolicy Bypass -File scripts/service/Publish-SessionGuardService.ps1`.
2. Run `artifacts\publish\SessionGuard.Service\SessionGuard.Service.exe validate-runtime`.
3. Run `powershell -ExecutionPolicy Bypass -File scripts/service/Install-SessionGuardService.ps1 -ValidateOnly`.
4. Run `powershell -ExecutionPolicy Bypass -File scripts/service/Validate-SessionGuardPublishedLayout.ps1`.
5. Remove `schemaVersion` from a published config file, run `powershell -ExecutionPolicy Bypass -File scripts/service/Upgrade-SessionGuardServiceConfig.ps1`, and confirm a backup appears under `state/config-backups/`.

## Packaging

1. Run `powershell -ExecutionPolicy Bypass -File scripts/release/Publish-SessionGuardReleaseAssets.ps1 -SelfContained`.
2. Confirm the app, service, and source zip files exist under `artifacts/releases/<version>/`.
3. Confirm the published desktop app folder contains `SessionGuard.App.exe`.

## Logs and state

1. Review the latest file under `logs/`.
2. Confirm the latest scan is serialized to `state/current-scan.json`.
3. If workspace risk is present, confirm `state/workspace-snapshot.json` exists and contains a summary.
4. If the service is running, confirm `state/service-health.json` is present and current.
