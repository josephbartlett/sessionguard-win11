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

function New-SessionGuardReleaseAssetRecord {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Type
    )

    $item = Get-Item $Path
    return [ordered]@{
        Name = [System.IO.Path]::GetFileName($Path)
        Type = $Type
        Path = $Path
        SizeBytes = $item.Length
        Sha256 = Get-SessionGuardFileSha256 -Path $Path
    }
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
$checksumsPath = Join-Path $OutputRoot "sessionguard-win11-sha256-$versionValue.txt"
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

$assetRecords = @(
    (New-SessionGuardReleaseAssetRecord -Path $bundleZip -Type "setup"),
    (New-SessionGuardReleaseAssetRecord -Path $appZip -Type "desktop-app"),
    (New-SessionGuardReleaseAssetRecord -Path $serviceZip -Type "service"),
    (New-SessionGuardReleaseAssetRecord -Path $sourceZip -Type "source")
)

$checksumLines = @(
    $assetRecords |
    ForEach-Object { "{0}  {1}" -f $_.Sha256, $_.Name }
)
Set-Content -Path $checksumsPath -Value $checksumLines -Encoding ASCII

$checksumRecord = New-SessionGuardReleaseAssetRecord -Path $checksumsPath -Type "checksums"

$appManifest = Get-Content (Join-Path $appPublishRoot "app-manifest.json") -Raw | ConvertFrom-Json
$serviceManifest = Get-Content (Join-Path $servicePublishRoot "install-manifest.json") -Raw | ConvertFrom-Json
$bundleManifest = Get-Content (Join-Path $bundlePublishRoot "bundle-manifest.json") -Raw | ConvertFrom-Json

$manifest = [ordered]@{
    ProductVersion = $versionValue
    Runtime = $Runtime
    SelfContained = $SelfContained.IsPresent
    CreatedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    OutputRoot = $OutputRoot
    TrustNotes = @(
        "Verify the setup zip hash against the published checksum file before install.",
        "SessionGuard direct-download binaries may still be unsigned; the publish manifests record current Authenticode signature status."
    )
    PublishedComponents = [ordered]@{
        DesktopApp = $appManifest.PrimaryExecutable
        Service = $serviceManifest.PrimaryExecutable
        Bundle = $bundleManifest.PrimaryExecutables
    }
    Assets = @($assetRecords + $checksumRecord)
}

$manifest | ConvertTo-Json -Depth 5 | Set-Content -Path $manifestPath -Encoding UTF8

Write-Host "Created release assets in $OutputRoot"
Write-Host " - $appZip"
Write-Host " - $serviceZip"
Write-Host " - $bundleZip"
Write-Host " - $sourceZip"
Write-Host " - $checksumsPath"
Write-Host " - $manifestPath"
