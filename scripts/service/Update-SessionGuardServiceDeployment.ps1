[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$PublishRoot = "",
    [switch]$SelfContained,
    [switch]$SkipPublish,
    [switch]$ValidateOnly,
    [switch]$AsJson,
    [int]$StartupTimeoutSeconds = 45
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "common.ps1")

$publishScript = Join-Path $PSScriptRoot "Publish-SessionGuardService.ps1"
$installScript = Join-Path $PSScriptRoot "Install-SessionGuardService.ps1"
$statusScript = Join-Path $PSScriptRoot "Get-SessionGuardServiceStatus.ps1"

$installedService = Get-SessionGuardInstalledServiceConfiguration
if ([string]::IsNullOrWhiteSpace($PublishRoot)) {
    if ($null -ne $installedService -and -not [string]::IsNullOrWhiteSpace($installedService.PublishRoot)) {
        $PublishRoot = $installedService.PublishRoot
    }
    else {
        $PublishRoot = Get-SessionGuardPublishRoot
    }
}

$serviceExe = Get-SessionGuardServiceExePath -PublishRoot $PublishRoot
$manifestPath = Get-SessionGuardInstallManifestPath -PublishRoot $PublishRoot
$dotnetAvailable = $null -ne (Get-Command dotnet -ErrorAction SilentlyContinue)
$targetMatchesInstalledPath = $null -ne $installedService -and
    (Test-SessionGuardPathMatch -Left $PublishRoot -Right $installedService.PublishRoot)
$willStopService = $null -ne $installedService -and $installedService.Status -ne "Stopped"
$preManifest = Get-SessionGuardInstallManifest -PublishRoot $PublishRoot

$installReadinessJson = & $installScript -SkipPublish -PublishRoot $PublishRoot -ValidateOnly -AsJson
if ($LASTEXITCODE -ne 0) {
    throw "Install readiness check failed before deployment orchestration."
}

$installReadiness = $installReadinessJson | ConvertFrom-Json
$validateIssues = New-Object System.Collections.Generic.List[string]
foreach ($issue in $installReadiness.Issues) {
    $validateIssues.Add([string]$issue)
}

if (-not $SkipPublish -and -not $dotnetAvailable) {
    $validateIssues.Add("dotnet is not available in the current environment, so the service cannot be republished.")
}

if (-not $SkipPublish -and $null -eq $preManifest -and -not (Test-Path $serviceExe)) {
    $validateIssues.Add("Target publish root is not yet published. The update flow can still proceed because it will publish before install.")
}

$canExecuteNow = if ($SkipPublish) {
    $installReadiness.CanInstallNow
}
else {
    (Test-SessionGuardIsElevated) -and $installReadiness.ScExeAvailable -and $dotnetAvailable
}

$summary = [ordered]@{
    TargetPublishRoot = $PublishRoot
    ServiceExecutable = $serviceExe
    InstallManifestPath = $manifestPath
    DotnetAvailable = $dotnetAvailable
    InstalledService = if ($null -ne $installedService) { $installedService } else { [pscustomobject]@{ Exists = $false } }
    TargetMatchesInstalledPath = $targetMatchesInstalledPath
    WillStopService = $willStopService
    WillPublish = -not $SkipPublish
    InstallReadiness = $installReadiness
    PreDeploymentManifest = $preManifest
    CanExecuteNow = $canExecuteNow
    Issues = $validateIssues.ToArray()
}

if ($ValidateOnly) {
    if ($AsJson) {
        [pscustomobject]$summary | ConvertTo-Json -Depth 10 -Compress
    }
    else {
        [pscustomobject]$summary
    }

    return
}

Assert-SessionGuardIsElevated -Operation "Service deployment update"

if ($willStopService) {
    if ($PSCmdlet.ShouldProcess($script:SessionGuardServiceName, "Stop SessionGuard service before deployment update")) {
        Stop-Service -Name $script:SessionGuardServiceName
        Wait-SessionGuardServiceState -DesiredStatus "Stopped"
    }
}

if (-not $SkipPublish) {
    $publishArguments = @(
        "-Configuration", $Configuration,
        "-Runtime", $Runtime,
        "-OutputDir", $PublishRoot
    )

    if ($SelfContained.IsPresent) {
        $publishArguments += "-SelfContained"
    }

    & $publishScript @publishArguments | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to publish SessionGuard.Service during deployment update."
    }
}

& $installScript -SkipPublish -PublishRoot $PublishRoot -StartupTimeoutSeconds $StartupTimeoutSeconds | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "Failed to install or restart SessionGuard service during deployment update."
}

$manifest = Get-SessionGuardInstallManifest -PublishRoot $PublishRoot
if ($null -eq $manifest) {
    throw "Install manifest was not available after deployment update."
}

$statusJson = & $statusScript -ProbeExecutable $serviceExe -AsJson
if ($LASTEXITCODE -ne 0) {
    throw "Failed to query SessionGuard service status after deployment update."
}

$status = $statusJson | ConvertFrom-Json
if (-not $status.ControlPlaneReachable) {
    throw "SessionGuard service control plane was not reachable after deployment update."
}

if ($null -eq $status.Health -or $status.Health.HealthState -ne "Running") {
    throw "SessionGuard service health did not report a running state after deployment update."
}

$installedImagePath = if ($null -ne $status.InstalledImagePath) { [string]$status.InstalledImagePath } else { "" }
if (-not (Test-SessionGuardPathMatch -Left $installedImagePath -Right $serviceExe)) {
    throw "Installed SessionGuard service image path does not match the updated publish root."
}

$runningVersion = [string]$status.Health.ProductVersion
$manifestVersion = [string]$manifest.ProductVersion
if ($runningVersion -ne $manifestVersion) {
    throw "Running SessionGuard service version '$runningVersion' did not match install-manifest.json version '$manifestVersion'."
}

[pscustomobject]@{
    TargetPublishRoot = $PublishRoot
    ServiceExecutable = $serviceExe
    StoppedExistingService = $willStopService
    Published = -not $SkipPublish
    ReinstalledService = $true
    InstalledImagePath = $installedImagePath
    Status = $status.Status
    HealthState = $status.Health.HealthState
    ControlPlaneReachable = $status.ControlPlaneReachable
    RunningVersion = $runningVersion
    ManifestVersion = $manifestVersion
    VersionVerified = $true
}
