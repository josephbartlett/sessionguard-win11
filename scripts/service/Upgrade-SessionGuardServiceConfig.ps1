[CmdletBinding()]
param(
    [string]$PublishRoot = "",
    [switch]$AsJson
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "common.ps1")

if ([string]::IsNullOrWhiteSpace($PublishRoot)) {
    $PublishRoot = Get-SessionGuardPublishRoot
}

$serviceExe = Get-SessionGuardServiceExePath -PublishRoot $PublishRoot
$upgrade = Invoke-SessionGuardConfigUpgrade -ServiceExecutable $serviceExe

if ($AsJson) {
    $upgrade.Report | ConvertTo-Json -Depth 8 -Compress
}
else {
    $upgrade.Report
}
