[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputDir = "",
    [switch]$SelfContained,
    [switch]$PreserveRuntimeState
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "common.ps1")

$repoRoot = Get-SessionGuardRepositoryRoot
if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Get-SessionGuardPublishRoot
}

$serviceProject = Join-Path $repoRoot "src\\SessionGuard.Service\\SessionGuard.Service.csproj"
$configSource = Join-Path $repoRoot "config"
$defaultsDestination = Join-Path $OutputDir "config.defaults"
$runtimeConfigDestination = Join-Path $OutputDir "config"
$manifestPath = Get-SessionGuardInstallManifestPath -PublishRoot $OutputDir
$selfContainedValue = "false"
if ($SelfContained.IsPresent) {
    $selfContainedValue = "true"
}

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
    $backupRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("SessionGuard.PublishBackup\\" + [Guid]::NewGuid().ToString("N"))
    $preservedNames = @("config")
    if ($PreserveRuntimeState.IsPresent) {
        $preservedNames += @("logs", "state")
    }

    foreach ($name in $preservedNames) {
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
    $serviceProject,
    "-c", $Configuration,
    "-r", $Runtime,
    "--self-contained", $selfContainedValue,
    "-o", $OutputDir
)

Write-Host "Publishing SessionGuard.Service to $OutputDir"
try {
    & dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for SessionGuard.Service."
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

    if ($PreserveRuntimeState.IsPresent) {
        Restore-PreservedDirectory -Name "logs" -DestinationRoot $OutputDir
        Restore-PreservedDirectory -Name "state" -DestinationRoot $OutputDir
    }
    else {
        $logDirectory = Join-Path $OutputDir "logs"
        $stateDirectory = Join-Path $OutputDir "state"
        if (Test-Path $logDirectory) {
            Remove-Item $logDirectory -Recurse -Force
        }

        if (Test-Path $stateDirectory) {
            Remove-Item $stateDirectory -Recurse -Force
        }
    }

    New-Item -ItemType Directory -Path (Join-Path $OutputDir "logs") -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $OutputDir "state") -Force | Out-Null

    $serviceExe = Get-SessionGuardServiceExePath -PublishRoot $OutputDir
    if (-not (Test-Path $serviceExe)) {
        throw "Expected published service executable at '$serviceExe'."
    }

    $upgrade = Invoke-SessionGuardConfigUpgrade -ServiceExecutable $serviceExe
    $validation = Invoke-SessionGuardRuntimeValidation -ServiceExecutable $serviceExe

    $manifest = [ordered]@{
        ServiceName = $script:SessionGuardServiceName
        DisplayName = $script:SessionGuardServiceDisplayName
        ProductVersion = $validation.Report.ProductVersion
        ProtocolVersion = $validation.Report.ProtocolVersion
        PublishedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
        PublishConfiguration = $Configuration
        Runtime = $Runtime
        SelfContained = $SelfContained.IsPresent
        PublishRoot = $OutputDir
        ServiceExecutable = $serviceExe
        ConfigDirectory = $validation.Report.ConfigDirectory
        ConfigDefaultsDirectory = $validation.Report.ConfigDefaultsDirectory
        LogDirectory = $validation.Report.LogDirectory
        StateDirectory = $validation.Report.StateDirectory
        ConfigUpgrade = $upgrade.Report
        Validation = $validation.Report
    }

    $manifest | ConvertTo-Json -Depth 8 | Set-Content -Path $manifestPath -Encoding UTF8

    Write-Host "Published service executable: $serviceExe"
    Write-Host "Install manifest: $manifestPath"
}
catch {
    Restore-PreservedDirectory -Name "config" -DestinationRoot $OutputDir
    if ($PreserveRuntimeState.IsPresent) {
        Restore-PreservedDirectory -Name "logs" -DestinationRoot $OutputDir
        Restore-PreservedDirectory -Name "state" -DestinationRoot $OutputDir
    }
    throw
}
finally {
    if ($backupRoot -and (Test-Path $backupRoot)) {
        Remove-Item $backupRoot -Recurse -Force
    }
}
