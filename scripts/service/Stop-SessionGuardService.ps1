[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "common.ps1")

Assert-SessionGuardIsElevated -Operation "Service stop"

if (-not (Test-SessionGuardServiceExists)) {
    throw "SessionGuard service is not installed."
}

Stop-Service -Name $script:SessionGuardServiceName
Wait-SessionGuardServiceState -DesiredStatus "Stopped"
Get-Service -Name $script:SessionGuardServiceName | Select-Object Name, Status, StartType
