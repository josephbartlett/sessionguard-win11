[CmdletBinding(SupportsShouldProcess = $true)]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "common.ps1")

Assert-SessionGuardIsElevated -Operation "Service removal"

if (-not (Test-SessionGuardServiceExists)) {
    Write-Host "SessionGuard service is not installed."
    return
}

if ($PSCmdlet.ShouldProcess($script:SessionGuardServiceName, "Uninstall SessionGuard service")) {
    try {
        Stop-Service -Name $script:SessionGuardServiceName -ErrorAction SilentlyContinue
        Wait-SessionGuardServiceState -DesiredStatus "Stopped" -TimeoutSeconds 15
    }
    catch {
    }

    Invoke-SessionGuardSc @("delete", $script:SessionGuardServiceName) | Out-Null
    Start-Sleep -Seconds 2
}

Write-Host "SessionGuard service removed."
