[CmdletBinding()]
param(
    [string]$PublishRoot = "",
    [int]$StartupTimeoutSeconds = 30
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "common.ps1")

Assert-SessionGuardIsElevated -Operation "Service start"

if (-not (Test-SessionGuardServiceExists)) {
    throw "SessionGuard service is not installed."
}

if ([string]::IsNullOrWhiteSpace($PublishRoot)) {
    $PublishRoot = Get-SessionGuardPublishRoot
}

$serviceExe = Get-SessionGuardServiceExePath -PublishRoot $PublishRoot
$manifestPath = Get-SessionGuardInstallManifestPath -PublishRoot $PublishRoot
$expectedVersion = ""
if (Test-Path $manifestPath) {
    try {
        $manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
        $expectedVersion = [string]$manifest.ProductVersion
    }
    catch {
    }
}

Start-Service -Name $script:SessionGuardServiceName
Wait-SessionGuardServiceState -DesiredStatus "Running"
if (Test-Path $serviceExe) {
    Wait-SessionGuardServiceHealthy -ProbeExecutable $serviceExe -TimeoutSeconds $StartupTimeoutSeconds -ExpectedProductVersion $expectedVersion | Out-Null
}
Get-Service -Name $script:SessionGuardServiceName | Select-Object Name, Status, StartType
