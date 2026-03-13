[CmdletBinding()]
param(
    [string]$Version = "1.1.4",
    [string]$OutputPath = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $repoRoot "artifacts\\sessionguard-win11-$Version.zip"
}

$stagingRoot = Join-Path $repoRoot "artifacts\\staging\\sessionguard-win11-$Version"
if (Test-Path $stagingRoot) {
    Remove-Item $stagingRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $stagingRoot -Force | Out-Null
New-Item -ItemType Directory -Path (Split-Path -Parent $OutputPath) -Force | Out-Null

$trackedFiles = git -C $repoRoot ls-files
if ($LASTEXITCODE -ne 0) {
    throw "git ls-files failed while preparing the release package."
}

foreach ($relativePath in $trackedFiles) {
    $sourcePath = Join-Path $repoRoot $relativePath
    $destinationPath = Join-Path $stagingRoot $relativePath
    $destinationDirectory = Split-Path -Parent $destinationPath

    if (-not (Test-Path $destinationDirectory)) {
        New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
    }

    Copy-Item $sourcePath -Destination $destinationPath -Force
}

if (Test-Path $OutputPath) {
    Remove-Item $OutputPath -Force
}

Compress-Archive -Path (Join-Path $stagingRoot "*") -DestinationPath $OutputPath
Write-Host "Created release package: $OutputPath"
