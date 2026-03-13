[CmdletBinding()]
param(
    [string]$Version = "",
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputRoot = "",
    [switch]$SelfContained
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "..\\service\\common.ps1")

function Compress-SessionGuardDirectory {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SourceDirectory,

        [Parameter(Mandatory = $true)]
        [string]$DestinationZip
    )

    if (-not (Test-Path $SourceDirectory)) {
        throw "Cannot archive missing directory '$SourceDirectory'."
    }

    if (Test-Path $DestinationZip) {
        Remove-Item $DestinationZip -Force
    }

    Compress-Archive -Path (Join-Path $SourceDirectory "*") -DestinationPath $DestinationZip
}

$repoRoot = Get-SessionGuardRepositoryRoot
$versionValue = Resolve-SessionGuardVersion -TagOrVersion $Version
$expectedVersion = Get-SessionGuardProductVersion
if ($versionValue -ne $expectedVersion) {
    throw "Requested release version '$versionValue' does not match Directory.Build.props version '$expectedVersion'."
}

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot "artifacts\\releases\\$versionValue"
}

$appPublishRoot = Join-Path $OutputRoot "publish\\SessionGuard.App"
$servicePublishRoot = Join-Path $OutputRoot "publish\\SessionGuard.Service"
$bundlePublishRoot = Join-Path $OutputRoot "publish\\SessionGuard"
$appZip = Join-Path $OutputRoot "sessionguard-win11-app-$versionValue-$Runtime.zip"
$serviceZip = Join-Path $OutputRoot "sessionguard-win11-service-$versionValue-$Runtime.zip"
$bundleZip = Join-Path $OutputRoot "sessionguard-win11-setup-$versionValue-$Runtime.zip"
$sourceZip = Join-Path $OutputRoot "sessionguard-win11-source-$versionValue.zip"
$manifestPath = Join-Path $OutputRoot "release-assets.json"

New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $OutputRoot "publish") -Force | Out-Null

$appPublishScript = Join-Path $repoRoot "scripts\\app\\Publish-SessionGuardApp.ps1"
$bundlePublishScript = Join-Path $repoRoot "scripts\\install\\Publish-SessionGuardBundle.ps1"
$servicePublishScript = Join-Path $repoRoot "scripts\\service\\Publish-SessionGuardService.ps1"
$sourcePackageScript = Join-Path $repoRoot "scripts\\package-release.ps1"

$publishParameters = @{
    Configuration = $Configuration
    Runtime = $Runtime
}

if ($SelfContained.IsPresent) {
    $publishParameters.SelfContained = $true
}

& $appPublishScript @publishParameters -OutputDir $appPublishRoot
& $servicePublishScript @publishParameters -OutputDir $servicePublishRoot
& $bundlePublishScript @publishParameters -OutputDir $bundlePublishRoot
& $sourcePackageScript -Version $versionValue -OutputPath $sourceZip

Compress-SessionGuardDirectory -SourceDirectory $appPublishRoot -DestinationZip $appZip
Compress-SessionGuardDirectory -SourceDirectory $servicePublishRoot -DestinationZip $serviceZip
Compress-SessionGuardDirectory -SourceDirectory $bundlePublishRoot -DestinationZip $bundleZip

$manifest = [ordered]@{
    ProductVersion = $versionValue
    Runtime = $Runtime
    SelfContained = $SelfContained.IsPresent
    CreatedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    OutputRoot = $OutputRoot
    Assets = @(
        [ordered]@{
            Name = [System.IO.Path]::GetFileName($bundleZip)
            Type = "setup"
            Path = $bundleZip
        },
        [ordered]@{
            Name = [System.IO.Path]::GetFileName($appZip)
            Type = "desktop-app"
            Path = $appZip
        },
        [ordered]@{
            Name = [System.IO.Path]::GetFileName($serviceZip)
            Type = "service"
            Path = $serviceZip
        },
        [ordered]@{
            Name = [System.IO.Path]::GetFileName($sourceZip)
            Type = "source"
            Path = $sourceZip
        }
    )
}

$manifest | ConvertTo-Json -Depth 5 | Set-Content -Path $manifestPath -Encoding UTF8

Write-Host "Created release assets in $OutputRoot"
Write-Host " - $appZip"
Write-Host " - $serviceZip"
Write-Host " - $bundleZip"
Write-Host " - $sourceZip"
Write-Host " - $manifestPath"
