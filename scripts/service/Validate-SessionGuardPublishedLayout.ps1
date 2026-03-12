[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$PublishRoot = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "common.ps1")

$repoRoot = Get-SessionGuardRepositoryRoot
if ([string]::IsNullOrWhiteSpace($PublishRoot)) {
    $PublishRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("SessionGuardPublishedLayout\\" + [Guid]::NewGuid().ToString("N"))
}

$publishScript = Join-Path $PSScriptRoot "Publish-SessionGuardService.ps1"
$statusScript = Join-Path $PSScriptRoot "Get-SessionGuardServiceStatus.ps1"

& $publishScript -Configuration $Configuration -Runtime $Runtime -OutputDir $PublishRoot | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "Failed to publish SessionGuard.Service for layout validation."
}

$serviceExe = Get-SessionGuardServiceExePath -PublishRoot $PublishRoot
$configDirectory = Join-Path $PublishRoot "config"
$configDefaultsDirectory = Join-Path $PublishRoot "config.defaults"
$manifestPath = Get-SessionGuardInstallManifestPath -PublishRoot $PublishRoot
$currentScanPath = Join-Path $PublishRoot "state\\current-scan.json"
$healthPath = Join-Path $PublishRoot "state\\service-health.json"

if (-not (Test-Path $serviceExe)) {
    throw "Published service executable not found at '$serviceExe'."
}

if (-not (Test-Path (Join-Path $configDirectory "appsettings.json"))) {
    throw "Published layout is missing config\\appsettings.json."
}

if (-not (Test-Path (Join-Path $configDirectory "protected-processes.json"))) {
    throw "Published layout is missing config\\protected-processes.json."
}

$defaultsAppSettingsPath = Join-Path $configDefaultsDirectory "appsettings.json"
$defaultsProtectedProcessesPath = Join-Path $configDefaultsDirectory "protected-processes.json"
if (-not (Test-Path $defaultsAppSettingsPath) -or -not (Test-Path $defaultsProtectedProcessesPath)) {
    throw "Published layout is missing upgrade-safe config.defaults content."
}

if (-not (Test-Path $manifestPath)) {
    throw "Published layout is missing install-manifest.json."
}

$validation = Invoke-SessionGuardRuntimeValidation -ServiceExecutable $serviceExe
if (-not $validation.Report.CanRun) {
    throw "Published layout runtime validation failed."
}

$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
if ($manifest.ProductVersion -ne $validation.Report.ProductVersion) {
    throw "Install manifest product version does not match the validated service runtime."
}

$service = $null
try {
    $service = Start-Process -FilePath $serviceExe -ArgumentList "console" -WorkingDirectory $PublishRoot -PassThru

    $deadline = (Get-Date).AddSeconds(20)
    do {
        Start-Sleep -Milliseconds 500
        $probeOutput = & $serviceExe probe 2>&1
        $probeReady = $LASTEXITCODE -eq 0
    } while (-not $probeReady -and (Get-Date) -lt $deadline)

    if (-not $probeReady) {
        throw "Published service executable did not respond to 'probe' within 20 seconds."
    }

    $deadline = (Get-Date).AddSeconds(20)
    do {
        Start-Sleep -Milliseconds 500
        $scanReady = Test-Path $currentScanPath
        $healthReady = Test-Path $healthPath
        $status = powershell -ExecutionPolicy Bypass -File $statusScript -ProbeExecutable $serviceExe -AsJson | ConvertFrom-Json
        $statusReady = $status.ControlPlaneReachable -and $null -ne $status.Health
    } while ((-not $scanReady -or -not $healthReady -or -not $statusReady) -and (Get-Date) -lt $deadline)

    if (-not $scanReady) {
        throw "Published layout did not create state\\current-scan.json."
    }

    if (-not $healthReady) {
        throw "Published layout did not create state\\service-health.json."
    }

    if (-not $statusReady) {
        throw "Status script did not report control-plane reachability and health for the published layout."
    }

    if ($status.Health.ProductVersion -ne $manifest.ProductVersion) {
        throw "Published layout health snapshot product version did not match install-manifest.json."
    }

    [pscustomobject]@{
        PublishRoot = $PublishRoot
        ProbeExecutable = $serviceExe
        ConfigDirectory = $configDirectory
        CurrentScanExists = $scanReady
        ServiceHealthExists = $healthReady
        ControlPlaneReachable = $status.ControlPlaneReachable
        HealthState = $status.Health.HealthState
        HostMode = $status.Health.HostMode
        ProductVersion = $status.Health.ProductVersion
        ManifestProductVersion = $manifest.ProductVersion
        RuntimeValidated = $validation.Report.CanRun
    }
}
finally {
    if ($service -and -not $service.HasExited) {
        Stop-Process -Id $service.Id -Force
    }
}
