[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "common.ps1")

Assert-SessionGuardIsElevated -Operation "Service start"

if (-not (Test-SessionGuardServiceExists)) {
    throw "SessionGuard service is not installed."
}

Start-Service -Name $script:SessionGuardServiceName
Wait-SessionGuardServiceState -DesiredStatus "Running"
Get-Service -Name $script:SessionGuardServiceName | Select-Object Name, Status, StartType
