[CmdletBinding()]
param(
    [string[]]$Scenario = @(),
    [string]$OutputDirectory = "",
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [switch]$SkipBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot "artifacts\\ui\\smoke"
}

if (-not $SkipBuild) {
    & dotnet build (Join-Path $repoRoot "SessionGuard.sln") -c $Configuration
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed while preparing the UI smoke run."
    }
}

$appPath = Join-Path $repoRoot "src\\SessionGuard.App\\bin\\$Configuration\\net9.0-windows\\SessionGuard.App.exe"
$arguments = @(
    "run",
    "--project",
    (Join-Path $repoRoot "tests\\SessionGuard.UiSmoke\\SessionGuard.UiSmoke.csproj"),
    "-c",
    $Configuration,
    "--",
    "--app",
    $appPath,
    "--output-dir",
    $OutputDirectory
)

foreach ($scenarioName in $Scenario) {
    $arguments += @("--scenario", $scenarioName)
}

& dotnet @arguments
