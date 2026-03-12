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
Copy-Item (Join-Path $repoRoot "README.md") -Destination (Join-Path $OutputDir "README.md") -Force

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
    BundleRoot = $OutputDir
    AppExecutable = $appExe
    ServiceExecutable = $serviceExe
    StartupCommand = Get-SessionGuardAppStartupCommand -AppExecutable $appExe
    InstallScript = (Join-Path $OutputDir "Install-SessionGuard.ps1")
    UninstallScript = (Join-Path $OutputDir "Uninstall-SessionGuard.ps1")
}

$bundleManifest | ConvertTo-Json -Depth 4 | Set-Content -Path $bundleManifestPath -Encoding UTF8

Write-Host "Published combined SessionGuard bundle: $OutputDir"
Write-Host " - $appExe"
Write-Host " - $serviceExe"
Write-Host " - $bundleManifestPath"
