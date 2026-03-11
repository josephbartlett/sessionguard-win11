[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$PublishRoot = "",
    [switch]$SkipPublish,
    [switch]$DoNotStart
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "common.ps1")

Assert-SessionGuardIsElevated -Operation "Service installation"

if ([string]::IsNullOrWhiteSpace($PublishRoot)) {
    $PublishRoot = Get-SessionGuardPublishRoot
}

if (-not $SkipPublish) {
    & (Join-Path $PSScriptRoot "Publish-SessionGuardService.ps1") -Configuration $Configuration -Runtime $Runtime -OutputDir $PublishRoot
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to publish SessionGuard.Service before installation."
    }
}

$serviceExe = Get-SessionGuardServiceExePath -PublishRoot $PublishRoot
if (-not (Test-Path $serviceExe)) {
    throw "Service executable not found at '$serviceExe'. Run Publish-SessionGuardService.ps1 first or omit -SkipPublish."
}

if (Test-SessionGuardServiceExists) {
    if ($PSCmdlet.ShouldProcess($script:SessionGuardServiceName, "Reinstall existing SessionGuard service")) {
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

if ($PSCmdlet.ShouldProcess($script:SessionGuardServiceName, "Install SessionGuard service")) {
    $quotedExe = '"' + $serviceExe + '"'
    Invoke-SessionGuardSc @(
        "create",
        $script:SessionGuardServiceName,
        "binPath= $quotedExe",
        "start= delayed-auto",
        "DisplayName= $script:SessionGuardServiceDisplayName"
    ) | Out-Null
    Invoke-SessionGuardSc @("description", $script:SessionGuardServiceName, $script:SessionGuardServiceDescription) | Out-Null
    Invoke-SessionGuardSc @(
        "failure",
        $script:SessionGuardServiceName,
        "reset= 86400",
        "actions= restart/5000/restart/15000//"
    ) | Out-Null
    Invoke-SessionGuardSc @("failureflag", $script:SessionGuardServiceName, "1") | Out-Null

    if (-not $DoNotStart) {
        Start-Service -Name $script:SessionGuardServiceName
        Wait-SessionGuardServiceState -DesiredStatus "Running"
    }
}

Get-Service -Name $script:SessionGuardServiceName | Select-Object Name, Status, StartType
