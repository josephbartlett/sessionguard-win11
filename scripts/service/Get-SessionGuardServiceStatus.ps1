[CmdletBinding()]
param(
    [switch]$AsJson,
    [string]$ProbeExecutable = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "common.ps1")

$service = Get-Service -Name $script:SessionGuardServiceName -ErrorAction SilentlyContinue
$installedService = Get-SessionGuardInstalledServiceConfiguration
$probeExe = $null
if (-not [string]::IsNullOrWhiteSpace($ProbeExecutable)) {
    $probeExe = Get-SessionGuardProbeExePath -PreferredPath $ProbeExecutable
}
elseif ($null -ne $installedService -and
    -not [string]::IsNullOrWhiteSpace($installedService.ImagePath) -and
    (Test-Path $installedService.ImagePath)) {
    $probeExe = $installedService.ImagePath
}
else {
    $probeExe = $null
}

$probePublishRoot = if ($null -ne $probeExe) {
    Split-Path -Parent $probeExe
} else {
    ""
}

$healthPath = if (-not [string]::IsNullOrWhiteSpace($probePublishRoot)) {
    Join-Path $probePublishRoot "state\\service-health.json"
} else {
    ""
}

$manifestPath = if (-not [string]::IsNullOrWhiteSpace($probePublishRoot)) {
    Get-SessionGuardInstallManifestPath -PublishRoot $probePublishRoot
} else {
    ""
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

if (-not [string]::IsNullOrWhiteSpace($healthPath) -and (Test-Path $healthPath)) {
    try {
        $healthSnapshot = Get-Content $healthPath -Raw | ConvertFrom-Json
    }
    catch {
    }
}

if (-not [string]::IsNullOrWhiteSpace($manifestPath) -and (Test-Path $manifestPath)) {
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
    InstalledImagePath = if ($null -ne $installedService) { $installedService.ImagePath } else { "" }
    InstalledPublishRoot = if ($null -ne $installedService) { $installedService.PublishRoot } else { "" }
    InstalledPathMatchesProbeExecutable = if ($null -ne $installedService -and $null -ne $probeExe) {
        Test-SessionGuardPathMatch -Left $installedService.ImagePath -Right $probeExe
    } else {
        $false
    }
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
    $status | ConvertTo-Json -Depth 8 -Compress
}
else {
    $status
}
