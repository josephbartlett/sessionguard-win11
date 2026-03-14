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
4. Close the window and confirm SessionGuard stays in the tray instead of exiting.
5. Launch the app again and confirm the existing tray app is reused instead of creating a second copy.
6. Switch to `Technical view` and confirm the detailed tables still render correctly.

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
5. In a non-elevated app session, confirm the UI reports `Needs elevated window` under `Protection changes` and leaves mitigation or approval actions unavailable.
6. In a state that needs approval or mitigation changes, confirm the dashboard and tray workflow expose `Open elevated controls` instead of vague manual-admin wording.

## Combined install path

1. Run `powershell -ExecutionPolicy Bypass -File scripts/install/Install-SessionGuard.ps1 -SelfContained` from an elevated shell, or run `powershell -ExecutionPolicy Bypass -File .\Install-SessionGuard.ps1` from an extracted bundle.
2. Confirm the service is installed and configured for delayed auto-start.
3. Confirm the current user has a SessionGuard startup registration under the Windows Run key.
4. Confirm the installed app launches to the tray without opening duplicate windows.
5. If Windows shows SmartScreen or another protection prompt on first launch, confirm the install still completes and the warning text tells you the app can be launched manually later.
6. Run the installer a second time and confirm it stops the running installed tray app before replacing files instead of failing on locked binaries.
7. Sign out and sign back in, then confirm the app starts minimized and appears in the tray.
8. Confirm the tray app connects to the service instead of falling back locally.
9. Launch `SessionGuard.App.exe` manually and confirm it brings the running tray app forward instead of starting a second copy.
10. Confirm `C:\Program Files\SessionGuard\install-manifest.json` includes `AuthorizedUserSid`.
11. Confirm `C:\Program Files\SessionGuard\logs` and `C:\Program Files\SessionGuard\state` ACLs are limited to the installing user, administrators, and `SYSTEM`.

## Elevated behavior

1. Use `Open elevated controls` from the running tray app, or launch `SessionGuard.App.exe --disable-tray --technical-view --disable-single-instance` from an elevated shell.
2. Apply the recommended mitigation.
3. Confirm the mitigation state changes to applied.
4. Grant and clear a restart approval window if the current policy state requires one.
5. Reset managed settings and confirm the state returns to the prior value or `<not set>`.

## Published-layout and upgrade path

1. Run `powershell -ExecutionPolicy Bypass -File scripts/service/Publish-SessionGuardService.ps1`.
2. Run `artifacts\publish\SessionGuard.Service\SessionGuard.Service.exe validate-runtime`.
3. Run `powershell -ExecutionPolicy Bypass -File scripts/service/Install-SessionGuardService.ps1 -ValidateOnly`.
4. Run `powershell -ExecutionPolicy Bypass -File scripts/service/Validate-SessionGuardPublishedLayout.ps1`.
5. Remove `schemaVersion` from a published config file, run `powershell -ExecutionPolicy Bypass -File scripts/service/Upgrade-SessionGuardServiceConfig.ps1`, and confirm a backup appears under `state/config-backups/`.

## Packaging

1. Run `powershell -ExecutionPolicy Bypass -File scripts/release/Publish-SessionGuardReleaseAssets.ps1 -SelfContained`.
2. Confirm the setup, app, service, and source zip files exist under `artifacts/releases/<version>/`.
3. Confirm the setup zip contains `SessionGuard.App.exe`, `SessionGuard.Service.exe`, `Install-SessionGuard.ps1`, `Uninstall-SessionGuard.ps1`, and the supporting install scripts.
4. Confirm the published desktop app folder contains `SessionGuard.App.exe`.
5. Confirm the public app, service, and setup zip assets contain empty `logs/` and `state/` directories only, not machine-local log files or persisted runtime JSON.

## Logs and state

1. Review the latest file under `logs/`.
2. Confirm the latest scan is serialized to `state/current-scan.json`.
3. If workspace risk is present, confirm `state/workspace-snapshot.json` exists and contains a summary.
4. If the service is running, confirm `state/service-health.json` is present and current.
