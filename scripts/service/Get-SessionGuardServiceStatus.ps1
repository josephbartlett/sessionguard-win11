[CmdletBinding()]
param(
    [switch]$AsJson,
    [string]$ProbeExecutable = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "common.ps1")

$service = Get-Service -Name $script:SessionGuardServiceName -ErrorAction SilentlyContinue
$probeExe = Get-SessionGuardProbeExePath -PreferredPath $ProbeExecutable
$healthPath = Get-SessionGuardServiceHealthPath -ProbeExecutable $probeExe
$manifestPath = if ($null -ne $probeExe) {
    Get-SessionGuardInstallManifestPath -PublishRoot (Split-Path -Parent $probeExe)
} else {
    Get-SessionGuardInstallManifestPath
}
$probeSucceeded = $false
$probeOutput = $null
$healthSnapshot = $null
$installManifest = $null
$runtimeValidation = $null

if ($null -ne $probeExe) {
    try {
        $probeOutput = & $probeExe probe 2>&1
        if ($LASTEXITCODE -eq 0) {
            $probeSucceeded = $true
        }
    }
    catch {
        $probeOutput = $_.Exception.Message
    }
}

if (Test-Path $healthPath) {
    try {
        $healthSnapshot = Get-Content $healthPath -Raw | ConvertFrom-Json
    }
    catch {
    }
}

if (Test-Path $manifestPath) {
    try {
        $installManifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
    }
    catch {
    }
}

if ($null -ne $probeExe -and (Test-Path $probeExe)) {
    $runtimeValidation = Invoke-SessionGuardRuntimeValidation -ServiceExecutable $probeExe -AllowFailure
}

$status = [pscustomobject]@{
    ServiceName = $script:SessionGuardServiceName
    Installed = $null -ne $service
    Status = if ($null -ne $service) { $service.Status.ToString() } else { "NotInstalled" }
    StartType = if ($null -ne $service) { $service.StartType.ToString() } else { "Unknown" }
    ProbeExecutable = $probeExe
    HealthFilePath = $healthPath
    InstallManifestPath = $manifestPath
    ControlPlaneReachable = $probeSucceeded
    ProbeOutput = if ($probeOutput) { [string]::Join([Environment]::NewLine, $probeOutput) } else { "" }
    Health = $healthSnapshot
    InstallManifest = $installManifest
    RuntimeValidation = if ($null -ne $runtimeValidation) { $runtimeValidation.Report } else { $null }
}

if ($AsJson) {
    $status | ConvertTo-Json -Compress
}
else {
    $status
}
