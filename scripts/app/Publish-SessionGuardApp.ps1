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

$repoRoot = Get-SessionGuardRepositoryRoot
if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $repoRoot "artifacts\\publish\\SessionGuard.App"
}

$appProject = Join-Path $repoRoot "src\\SessionGuard.App\\SessionGuard.App.csproj"
$configSource = Join-Path $repoRoot "config"
$defaultsDestination = Join-Path $OutputDir "config.defaults"
$runtimeConfigDestination = Join-Path $OutputDir "config"
$logDirectory = Join-Path $OutputDir "logs"
$stateDirectory = Join-Path $OutputDir "state"
$manifestPath = Join-Path $OutputDir "app-manifest.json"
$selfContainedValue = if ($SelfContained.IsPresent) { "true" } else { "false" }

New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

$backupRoot = $null
$preservedDirectories = @{}

function Restore-PreservedDirectory {
    param(
        [string]$Name,
        [string]$DestinationRoot
    )

    if (-not $preservedDirectories.ContainsKey($Name)) {
        return
    }

    $source = $preservedDirectories[$Name]
    $destination = Join-Path $DestinationRoot $Name
    if (Test-Path $destination) {
        Remove-Item $destination -Recurse -Force
    }

    Copy-Item $source -Destination $DestinationRoot -Recurse -Force
}

if (Test-Path $OutputDir) {
    $backupRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("SessionGuard.AppPublishBackup\\" + [Guid]::NewGuid().ToString("N"))
    foreach ($name in @("config", "logs", "state")) {
        $source = Join-Path $OutputDir $name
        if (Test-Path $source) {
            New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
            Copy-Item $source -Destination $backupRoot -Recurse -Force
            $preservedDirectories[$name] = Join-Path $backupRoot $name
        }
    }
}

$publishArgs = @(
    "publish",
    $appProject,
    "-c", $Configuration,
    "-r", $Runtime,
    "--self-contained", $selfContainedValue,
    "-o", $OutputDir
)

Write-Host "Publishing SessionGuard.App to $OutputDir"
try {
    & dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for SessionGuard.App."
    }

    if (Test-Path $defaultsDestination) {
        Remove-Item $defaultsDestination -Recurse -Force
    }

    if (-not (Test-Path $configSource)) {
        throw "Repository config source was not found at '$configSource'."
    }

    Copy-Item $configSource -Destination $defaultsDestination -Recurse -Force

    if ($preservedDirectories.ContainsKey("config")) {
        Restore-PreservedDirectory -Name "config" -DestinationRoot $OutputDir
    }
    else {
        if (Test-Path $runtimeConfigDestination) {
            Remove-Item $runtimeConfigDestination -Recurse -Force
        }

        Copy-Item $configSource -Destination $runtimeConfigDestination -Recurse -Force
    }

    Restore-PreservedDirectory -Name "logs" -DestinationRoot $OutputDir
    Restore-PreservedDirectory -Name "state" -DestinationRoot $OutputDir

    New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
    New-Item -ItemType Directory -Path $stateDirectory -Force | Out-Null

    $appExe = Join-Path $OutputDir "SessionGuard.App.exe"
    if (-not (Test-Path $appExe)) {
        throw "Expected published desktop executable at '$appExe'."
    }

    $productVersion = Get-SessionGuardProductVersion
    $manifest = [ordered]@{
        ProductVersion = $productVersion
        PublishedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
        PublishConfiguration = $Configuration
        Runtime = $Runtime
        SelfContained = $SelfContained.IsPresent
        AppExecutable = $appExe
        ConfigDirectory = $runtimeConfigDestination
        ConfigDefaultsDirectory = $defaultsDestination
        LogDirectory = $logDirectory
        StateDirectory = $stateDirectory
    }

    $manifest | ConvertTo-Json -Depth 4 | Set-Content -Path $manifestPath -Encoding UTF8

    Write-Host "Published desktop executable: $appExe"
    Write-Host "Desktop manifest: $manifestPath"
}
catch {
    Restore-PreservedDirectory -Name "config" -DestinationRoot $OutputDir
    Restore-PreservedDirectory -Name "logs" -DestinationRoot $OutputDir
    Restore-PreservedDirectory -Name "state" -DestinationRoot $OutputDir
    throw
}
finally {
    if ($backupRoot -and (Test-Path $backupRoot)) {
        Remove-Item $backupRoot -Recurse -Force
    }
}
