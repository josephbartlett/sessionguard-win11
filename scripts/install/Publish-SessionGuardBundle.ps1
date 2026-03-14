[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputDir = "",
    [switch]$SelfContained
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "..\\service\\common.ps1")
. (Join-Path $PSScriptRoot "common.ps1")

$repoRoot = Get-SessionGuardRepositoryRoot
if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $repoRoot "artifacts\\publish\\SessionGuard"
}

$appPublishScript = Join-Path $PSScriptRoot "..\\app\\Publish-SessionGuardApp.ps1"
$servicePublishScript = Join-Path $PSScriptRoot "..\\service\\Publish-SessionGuardService.ps1"
$bundleManifestPath = Join-Path $OutputDir "bundle-manifest.json"
$appExe = Join-Path $OutputDir "SessionGuard.App.exe"
$serviceExe = Join-Path $OutputDir "SessionGuard.Service.exe"
$bundleReadmePath = Join-Path $OutputDir "README.md"

$publishParameters = @{
    Configuration = $Configuration
    Runtime = $Runtime
    OutputDir = $OutputDir
}

if ($SelfContained.IsPresent) {
    $publishParameters.SelfContained = $true
}

& $appPublishScript @publishParameters
& $servicePublishScript @publishParameters

if (-not (Test-Path $appExe)) {
    throw "Expected bundle app executable at '$appExe'."
}

if (-not (Test-Path $serviceExe)) {
    throw "Expected bundle service executable at '$serviceExe'."
}

$scriptsRoot = Join-Path $OutputDir "scripts"
if (Test-Path $scriptsRoot) {
    Remove-Item $scriptsRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $scriptsRoot -Force | Out-Null
Copy-Item (Join-Path $repoRoot "scripts\\install") -Destination $scriptsRoot -Recurse -Force
Copy-Item (Join-Path $repoRoot "scripts\\service") -Destination $scriptsRoot -Recurse -Force
Copy-Item (Join-Path $repoRoot "LICENSE") -Destination (Join-Path $OutputDir "LICENSE") -Force

$bundleReadme = @'
# SessionGuard

This package contains the full SessionGuard runtime for one-machine installation on Windows 11.

## What is in this folder

- `SessionGuard.App.exe`: tray icon and dashboard
- `SessionGuard.Service.exe`: background engine
- `Install-SessionGuard.ps1`: installs both pieces in the intended way
- `Uninstall-SessionGuard.ps1`: removes the combined install

## Recommended install

Run this from an elevated PowerShell session:

```powershell
powershell -ExecutionPolicy Bypass -File .\Install-SessionGuard.ps1
```

That installs SessionGuard to `C:\Program Files\SessionGuard`, installs the Windows Service, registers the app to start at sign-in for the current user, stops a running installed tray app before replacing files during reinstall or upgrade, and attempts to launch the app minimized to the tray unless you opt out with `-DoNotLaunchApp`.

Install it from the same Windows account that should see the tray icon at sign-in.

If Windows blocks the immediate launch, the install still succeeded. SessionGuard setup zips are direct-download unsigned binaries today, so Windows may show a SmartScreen or protection prompt on first launch. Open `C:\Program Files\SessionGuard\SessionGuard.App.exe` manually from your normal desktop session, use `-DoNotLaunchApp`, or wait for the next sign-in.

## How SessionGuard runs

- `SessionGuard.Service.exe` is the background engine and auto-starts with Windows when installed.
- `SessionGuard.App.exe` owns the tray icon and opens the dashboard on demand.
- The tray icon belongs to the app, not the service.
- Launching the same installed `SessionGuard.App.exe` again at the same privilege level reuses the already-running tray app instead of starting a second copy.
- Launching `SessionGuard.App.exe` as administrator starts a separate elevated SessionGuard session when you need service-owned write access.
- When the dashboard says protection changes still need elevation, use `Open elevated controls`.

## Uninstall

```powershell
powershell -ExecutionPolicy Bypass -File .\Uninstall-SessionGuard.ps1 -RemoveFiles
```

## Notes

- SessionGuard does not disable Windows Update.
- SessionGuard reduces restart disruption but does not guarantee prevention of every Windows restart path.
- To change service-backed guard mode, mitigation, or approval state, use `Open elevated controls` from the dashboard or launch `SessionGuard.App.exe` as administrator.
- Installer switches: `-DoNotLaunchApp`, `-DoNotStartService`, and `-ValidateOnly -AsJson`.
'@

Set-Content -Path $bundleReadmePath -Value $bundleReadme -Encoding ASCII

$bundleInstallScript = @'
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$InstallRoot = "",
    [switch]$DoNotStartService,
    [switch]$DoNotLaunchApp,
    [switch]$ValidateOnly,
    [switch]$AsJson,
    [int]$StartupTimeoutSeconds = 30
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

& (Join-Path $PSScriptRoot "scripts\install\Install-SessionGuard.ps1") `
    -SkipPublish `
    -PublishRoot $PSScriptRoot `
    -InstallRoot $InstallRoot `
    -DoNotStartService:$DoNotStartService `
    -DoNotLaunchApp:$DoNotLaunchApp `
    -ValidateOnly:$ValidateOnly `
    -AsJson:$AsJson `
    -StartupTimeoutSeconds $StartupTimeoutSeconds
'@

$bundleUninstallScript = @'
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$InstallRoot = "",
    [switch]$RemoveFiles
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

& (Join-Path $PSScriptRoot "scripts\install\Uninstall-SessionGuard.ps1") `
    -InstallRoot $InstallRoot `
    -RemoveFiles:$RemoveFiles
'@

Set-Content -Path (Join-Path $OutputDir "Install-SessionGuard.ps1") -Value $bundleInstallScript -Encoding ASCII
Set-Content -Path (Join-Path $OutputDir "Uninstall-SessionGuard.ps1") -Value $bundleUninstallScript -Encoding ASCII

$bundleManifest = [ordered]@{
    ProductVersion = Get-SessionGuardProductVersion
    PublishedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    PublishConfiguration = $Configuration
    Runtime = $Runtime
    SelfContained = $SelfContained.IsPresent
    StartupArguments = @("--start-minimized")
    IncludedComponents = @(
        "SessionGuard.App.exe",
        "SessionGuard.Service.exe",
        "Install-SessionGuard.ps1",
        "Uninstall-SessionGuard.ps1",
        "scripts/install",
        "scripts/service"
    )
}

$bundleManifest | ConvertTo-Json -Depth 4 | Set-Content -Path $bundleManifestPath -Encoding UTF8

Write-Host "Published combined SessionGuard bundle: $OutputDir"
Write-Host " - $appExe"
Write-Host " - $serviceExe"
Write-Host " - $bundleManifestPath"
