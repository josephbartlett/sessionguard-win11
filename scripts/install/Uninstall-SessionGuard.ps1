[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$InstallRoot = "",
    [switch]$RemoveFiles
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "..\\service\\common.ps1")
. (Join-Path $PSScriptRoot "common.ps1")

if ([string]::IsNullOrWhiteSpace($InstallRoot)) {
    $InstallRoot = Get-SessionGuardDefaultInstallRoot
}

Assert-SessionGuardIsElevated -Operation "Combined SessionGuard removal"

Unregister-SessionGuardAppStartup

$installedAppExe = Get-SessionGuardAppExecutablePath -Root $InstallRoot
if ($PSCmdlet.ShouldProcess($installedAppExe, "Stop running SessionGuard app")) {
    $stopResult = Stop-SessionGuardRunningApp -AppExecutable $installedAppExe
    if ($stopResult.Attempted) {
        Write-Host ("Stopped {0} running SessionGuard app process(es)." -f $stopResult.ProcessCount)
    }
}

$serviceUninstallScript = Join-Path $InstallRoot "scripts\\service\\Uninstall-SessionGuardService.ps1"
if (Test-Path $serviceUninstallScript) {
    & $serviceUninstallScript | Out-Host
}
elseif (Test-SessionGuardServiceExists) {
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
}

if ($RemoveFiles.IsPresent -and (Test-Path $InstallRoot)) {
    if ($PSCmdlet.ShouldProcess($InstallRoot, "Remove installed SessionGuard files")) {
        Remove-Item $InstallRoot -Recurse -Force
    }
}

Write-Host "SessionGuard app startup registration removed."
if ($RemoveFiles.IsPresent) {
    Write-Host "SessionGuard files removed from $InstallRoot"
}
