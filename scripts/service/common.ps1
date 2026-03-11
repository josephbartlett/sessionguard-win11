Set-StrictMode -Version Latest

$script:SessionGuardServiceName = "SessionGuardService"
$script:SessionGuardServiceDisplayName = "SessionGuard Service"
$script:SessionGuardServiceDescription = "Continuous restart-awareness monitoring and mitigation orchestration for SessionGuard."
$script:SessionGuardDefaultRuntime = "win-x64"

function Get-SessionGuardRepositoryRoot {
    return Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
}

function Get-SessionGuardPublishRoot {
    return Join-Path (Get-SessionGuardRepositoryRoot) "artifacts\\publish\\SessionGuard.Service"
}

function Get-SessionGuardServiceExePath {
    param(
        [string]$PublishRoot = (Get-SessionGuardPublishRoot)
    )

    return Join-Path $PublishRoot "SessionGuard.Service.exe"
}

function Get-SessionGuardProbeExePath {
    $publishExe = Get-SessionGuardServiceExePath
    if (Test-Path $publishExe) {
        return $publishExe
    }

    $repoRoot = Get-SessionGuardRepositoryRoot
    $debugExe = Join-Path $repoRoot "src\\SessionGuard.Service\\bin\\Debug\\net9.0-windows\\SessionGuard.Service.exe"
    if (Test-Path $debugExe) {
        return $debugExe
    }

    $releaseExe = Join-Path $repoRoot "src\\SessionGuard.Service\\bin\\Release\\net9.0-windows\\SessionGuard.Service.exe"
    if (Test-Path $releaseExe) {
        return $releaseExe
    }

    return $null
}

function Test-SessionGuardIsElevated {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Assert-SessionGuardIsElevated {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Operation
    )

    if (-not (Test-SessionGuardIsElevated)) {
        throw "$Operation requires an elevated PowerShell session."
    }
}

function Test-SessionGuardServiceExists {
    return $null -ne (Get-Service -Name $script:SessionGuardServiceName -ErrorAction SilentlyContinue)
}

function Wait-SessionGuardServiceState {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet("Running", "Stopped")]
        [string]$DesiredStatus,

        [int]$TimeoutSeconds = 20
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        $service = Get-Service -Name $script:SessionGuardServiceName -ErrorAction SilentlyContinue
        if ($null -eq $service) {
            if ($DesiredStatus -eq "Stopped") {
                return
            }

            throw "Service '$script:SessionGuardServiceName' is not installed."
        }

        $service.Refresh()
        if ($service.Status.ToString() -eq $DesiredStatus) {
            return
        }

        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)

    throw "Timed out waiting for service '$script:SessionGuardServiceName' to reach state '$DesiredStatus'."
}

function Invoke-SessionGuardSc {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    $output = & sc.exe @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "sc.exe $($Arguments -join ' ') failed.`n$output"
    }

    return $output
}
