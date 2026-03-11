[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputDir = "",
    [switch]$SelfContained
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
$selfContainedValue = "false"
if ($SelfContained.IsPresent) {
    $selfContainedValue = "true"
}

New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

$publishArgs = @(
    "publish",
    $serviceProject,
    "-c", $Configuration,
    "-r", $Runtime,
    "--self-contained", $selfContainedValue,
    "-o", $OutputDir
)

Write-Host "Publishing SessionGuard.Service to $OutputDir"
& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed for SessionGuard.Service."
}

if (Test-Path $configSource) {
    Copy-Item $configSource -Destination (Join-Path $OutputDir "config") -Recurse -Force
}

$serviceExe = Get-SessionGuardServiceExePath -PublishRoot $OutputDir
if (-not (Test-Path $serviceExe)) {
    throw "Expected published service executable at '$serviceExe'."
}

Write-Host "Published service executable: $serviceExe"
