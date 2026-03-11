[CmdletBinding()]
param(
    [string[]]$Scenario = @(),
    [string]$OutputDirectory = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot "artifacts\\ui\\smoke"
}

& dotnet build (Join-Path $repoRoot "SessionGuard.sln")
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed while preparing the UI smoke run."
}

$arguments = @(
    "run",
    "--project",
    (Join-Path $repoRoot "tests\\SessionGuard.UiSmoke\\SessionGuard.UiSmoke.csproj"),
    "--",
    "--output-dir",
    $OutputDirectory
)

foreach ($scenarioName in $Scenario) {
    $arguments += @("--scenario", $scenarioName)
}

& dotnet @arguments
