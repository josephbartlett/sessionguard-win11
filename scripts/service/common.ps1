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

function Get-SessionGuardInstallManifestPath {
    param(
        [string]$PublishRoot = (Get-SessionGuardPublishRoot)
    )

    return Join-Path $PublishRoot "install-manifest.json"
}

function Get-SessionGuardServiceHealthPath {
    param(
        [string]$ProbeExecutable = ""
    )

    $candidates = @()
    $probeExe = Get-SessionGuardProbeExePath -PreferredPath $ProbeExecutable
    if ($null -ne $probeExe) {
        $candidates += (Join-Path (Split-Path -Parent $probeExe) "state\\service-health.json")
    }

    $repoHealthPath = Join-Path (Get-SessionGuardRepositoryRoot) "state\\service-health.json"
    if ($candidates -notcontains $repoHealthPath) {
        $candidates += $repoHealthPath
    }

    $existing = @(
        $candidates |
        Where-Object { Test-Path $_ } |
        Sort-Object { (Get-Item $_).LastWriteTimeUtc } -Descending
    )

    if ($existing.Count -gt 0) {
        return $existing[0]
    }

    return $candidates[0]
}

function Get-SessionGuardProbeExePath {
    param(
        [string]$PreferredPath = ""
    )

    if (-not [string]::IsNullOrWhiteSpace($PreferredPath)) {
        return $PreferredPath
    }

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

function Invoke-SessionGuardRuntimeValidation {
    param(
        [string]$ServiceExecutable,
        [switch]$AllowFailure
    )

    if ([string]::IsNullOrWhiteSpace($ServiceExecutable) -or -not (Test-Path $ServiceExecutable)) {
        return [pscustomobject]@{
            Available = $false
            ExitCode = $null
            Report = $null
            Error = "Service executable not found."
            RawOutput = ""
        }
    }

    try {
        $output = & $ServiceExecutable validate-runtime 2>&1
        $exitCode = $LASTEXITCODE
        $rawOutput = if ($output) { [string]::Join([Environment]::NewLine, $output) } else { "" }
        $report = $null

        if (-not [string]::IsNullOrWhiteSpace($rawOutput)) {
            try {
                $report = $rawOutput | ConvertFrom-Json
            }
            catch {
            }
        }

        if (-not $AllowFailure -and ($exitCode -ne 0 -or $null -eq $report -or -not $report.CanRun)) {
            $details = if ($null -ne $report) {
                $report.Issues -join "; "
            }
            elseif (-not [string]::IsNullOrWhiteSpace($rawOutput)) {
                $rawOutput
            }
            else {
                "No validation output was returned."
            }

            throw "Service runtime validation failed: $details"
        }

        return [pscustomobject]@{
            Available = $true
            ExitCode = $exitCode
            Report = $report
            Error = if ($exitCode -eq 0) { "" } else { $rawOutput }
            RawOutput = $rawOutput
        }
    }
    catch {
        if (-not $AllowFailure) {
            throw
        }

        return [pscustomobject]@{
            Available = $true
            ExitCode = $LASTEXITCODE
            Report = $null
            Error = $_.Exception.Message
            RawOutput = ""
        }
    }
}

function Invoke-SessionGuardConfigUpgrade {
    param(
        [string]$ServiceExecutable,
        [switch]$AllowFailure
    )

    if ([string]::IsNullOrWhiteSpace($ServiceExecutable) -or -not (Test-Path $ServiceExecutable)) {
        return [pscustomobject]@{
            Available = $false
            ExitCode = $null
            Report = $null
            Error = "Service executable not found."
            RawOutput = ""
        }
    }

    try {
        $output = & $ServiceExecutable upgrade-config 2>&1
        $exitCode = $LASTEXITCODE
        $rawOutput = if ($output) { [string]::Join([Environment]::NewLine, $output) } else { "" }
        $report = $null

        if (-not [string]::IsNullOrWhiteSpace($rawOutput)) {
            try {
                $report = $rawOutput | ConvertFrom-Json
            }
            catch {
            }
        }

        if (-not $AllowFailure -and ($exitCode -ne 0 -or $null -eq $report -or $report.HasErrors)) {
            $details = if ($null -ne $report) {
                $report.Issues -join "; "
            }
            elseif (-not [string]::IsNullOrWhiteSpace($rawOutput)) {
                $rawOutput
            }
            else {
                "No upgrade output was returned."
            }

            throw "Service config upgrade failed: $details"
        }

        return [pscustomobject]@{
            Available = $true
            ExitCode = $exitCode
            Report = $report
            Error = if ($exitCode -eq 0) { "" } else { $rawOutput }
            RawOutput = $rawOutput
        }
    }
    catch {
        if (-not $AllowFailure) {
            throw
        }

        return [pscustomobject]@{
            Available = $true
            ExitCode = $LASTEXITCODE
            Report = $null
            Error = $_.Exception.Message
            RawOutput = ""
        }
    }
}

function Wait-SessionGuardServiceHealthy {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ProbeExecutable,

        [int]$TimeoutSeconds = 30,

        [string]$ExpectedProductVersion = ""
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $healthPath = Get-SessionGuardServiceHealthPath -ProbeExecutable $ProbeExecutable

    do {
        $probeOutput = & $ProbeExecutable probe 2>&1
        $probeReady = $LASTEXITCODE -eq 0
        $health = $null

        if (Test-Path $healthPath) {
            try {
                $health = Get-Content $healthPath -Raw | ConvertFrom-Json
            }
            catch {
            }
        }

        if ($probeReady -and $null -ne $health -and $health.HealthState -eq "Running") {
            if ([string]::IsNullOrWhiteSpace($ExpectedProductVersion) -or
                $health.ProductVersion -eq $ExpectedProductVersion) {
                return [pscustomobject]@{
                    ControlPlaneReachable = $true
                    Health = $health
                    HealthPath = $healthPath
                }
            }
        }

        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)

    throw "Timed out waiting for SessionGuard service health to reach a running state."
}
