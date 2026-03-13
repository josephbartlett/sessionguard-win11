[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$PublishRoot = "",
    [string]$InstallRoot = "",
    [switch]$SkipPublish,
    [switch]$SelfContained,
    [switch]$DoNotStartService,
    [switch]$DoNotLaunchApp,
    [switch]$ValidateOnly,
    [switch]$AsJson,
    [int]$StartupTimeoutSeconds = 30
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "..\\service\\common.ps1")
. (Join-Path $PSScriptRoot "common.ps1")

$repoRoot = Get-SessionGuardRepositoryRoot
if ([string]::IsNullOrWhiteSpace($PublishRoot)) {
    $bundleRoot = Get-SessionGuardBundleSourceRoot
    if (-not [string]::IsNullOrWhiteSpace($bundleRoot)) {
        $PublishRoot = $bundleRoot
        $SkipPublish = $true
    }
    else {
        $PublishRoot = Join-Path $repoRoot "artifacts\\publish\\SessionGuard"
    }
}

if ([string]::IsNullOrWhiteSpace($InstallRoot)) {
    $InstallRoot = Get-SessionGuardDefaultInstallRoot
}

if (-not $ValidateOnly -and -not $SkipPublish) {
    $publishArguments = @{
        Configuration = $Configuration
        Runtime = $Runtime
        OutputDir = $PublishRoot
    }

    if ($SelfContained.IsPresent) {
        $publishArguments.SelfContained = $true
    }

    & (Join-Path $PSScriptRoot "Publish-SessionGuardBundle.ps1") @publishArguments | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to publish the combined SessionGuard bundle before installation."
    }
}

$serviceExe = Get-SessionGuardServiceExecutablePath -Root $PublishRoot
$appExe = Get-SessionGuardAppExecutablePath -Root $PublishRoot
$appSettingsPath = Join-Path $PublishRoot "config\\appsettings.json"
$protectedProcessesPath = Join-Path $PublishRoot "config\\protected-processes.json"
$policiesPath = Join-Path $PublishRoot "config\\policies.json"
$configDefaultsPath = Join-Path $PublishRoot "config.defaults"
$bundleManifestPath = Join-Path $PublishRoot "bundle-manifest.json"
$installManifestPath = Join-Path $PublishRoot "install-manifest.json"
$startupRegistration = Get-SessionGuardAppStartupRegistration
$serviceExists = Test-SessionGuardServiceExists
$scAvailable = $null -ne (Get-Command sc.exe -ErrorAction SilentlyContinue)
$isElevated = Test-SessionGuardIsElevated

$issues = New-Object System.Collections.Generic.List[string]
$warnings = New-Object System.Collections.Generic.List[string]
$runtimeValidation = $null
$bundleManifest = $null

if (-not (Test-Path $serviceExe)) {
    $issues.Add("Service executable not found at '$serviceExe'.")
}

if (-not (Test-Path $appExe)) {
    $issues.Add("Desktop app executable not found at '$appExe'.")
}

if (-not (Test-Path $appSettingsPath)) {
    $issues.Add("Missing config\\appsettings.json in '$PublishRoot'.")
}

if (-not (Test-Path $protectedProcessesPath)) {
    $issues.Add("Missing config\\protected-processes.json in '$PublishRoot'.")
}

if (-not (Test-Path $policiesPath)) {
    $issues.Add("Missing config\\policies.json in '$PublishRoot'.")
}

if (-not (Test-Path $configDefaultsPath)) {
    $warnings.Add("Published bundle is missing config.defaults. Runtime config seeding will be unavailable until the bundle is republished.")
}

if (Test-Path $bundleManifestPath) {
    try {
        $bundleManifest = Get-Content $bundleManifestPath -Raw | ConvertFrom-Json
    }
    catch {
        $warnings.Add("Bundle manifest exists but could not be parsed.")
    }
}
else {
    $warnings.Add("Bundle manifest is missing. Republish the combined bundle to record versioned bundle metadata.")
}

if (Test-Path $serviceExe) {
    $runtimeValidation = Invoke-SessionGuardRuntimeValidation -ServiceExecutable $serviceExe -AllowFailure
    if ($null -eq $runtimeValidation.Report) {
        $issues.Add("Service runtime validation did not return a usable report.")
    }
    elseif (-not $runtimeValidation.Report.CanRun) {
        $details = if ($runtimeValidation.Report.Issues.Count -gt 0) {
            $runtimeValidation.Report.Issues -join "; "
        }
        else {
            "Unknown runtime validation failure."
        }

        $issues.Add("Service runtime validation failed: $details")
    }
    elseif ($runtimeValidation.Report.Warnings.Count -gt 0) {
        foreach ($warning in $runtimeValidation.Report.Warnings) {
            $warnings.Add($warning)
        }
    }
}

if (-not $scAvailable) {
    $issues.Add("sc.exe is not available in the current environment.")
}

if (-not $isElevated) {
    $issues.Add("Combined SessionGuard installation requires an elevated PowerShell session.")
}

$startupCommand = if (Test-Path $appExe) { Get-SessionGuardAppStartupCommand -AppExecutable (Get-SessionGuardAppExecutablePath -Root $InstallRoot) } else { "" }
$readiness = [pscustomobject]@{
    PublishRoot = $PublishRoot
    InstallRoot = $InstallRoot
    AppExecutable = $appExe
    ServiceExecutable = $serviceExe
    AppExecutableExists = Test-Path $appExe
    ServiceExecutableExists = Test-Path $serviceExe
    AppSettingsExists = Test-Path $appSettingsPath
    ProtectedProcessesExists = Test-Path $protectedProcessesPath
    PoliciesExists = Test-Path $policiesPath
    ConfigDefaultsExists = Test-Path $configDefaultsPath
    BundleManifestPath = $bundleManifestPath
    BundleManifest = $bundleManifest
    InstallManifestPath = $installManifestPath
    RuntimeValidation = if ($null -ne $runtimeValidation) { $runtimeValidation.Report } else { $null }
    Elevated = $isElevated
    ScExeAvailable = $scAvailable
    ServiceAlreadyInstalled = $serviceExists
    StartupRegistrationExists = -not [string]::IsNullOrWhiteSpace($startupRegistration)
    StartupRegistration = if ($null -ne $startupRegistration) { [string]$startupRegistration } else { "" }
    StartupCommand = $startupCommand
    CanInstallNow = $issues.Count -eq 0
    Issues = $issues.ToArray()
    Warnings = $warnings.ToArray()
}

if ($ValidateOnly) {
    if ($AsJson) {
        $readiness | ConvertTo-Json -Depth 8 -Compress
    }
    else {
        $readiness
    }

    return
}

Assert-SessionGuardIsElevated -Operation "Combined SessionGuard installation"

if (-not $readiness.AppExecutableExists -or -not $readiness.ServiceExecutableExists) {
    throw "Combined SessionGuard installation requires both SessionGuard.App.exe and SessionGuard.Service.exe."
}

if ($null -eq $readiness.RuntimeValidation -or -not $readiness.RuntimeValidation.CanRun) {
    throw "Combined SessionGuard installation requires a runnable service layout. Republish the bundle and review the runtime validation report."
}

if ($serviceExists) {
    try {
        Stop-Service -Name $script:SessionGuardServiceName -ErrorAction SilentlyContinue
        Wait-SessionGuardServiceState -DesiredStatus "Stopped" -TimeoutSeconds 15
    }
    catch {
    }
}

if ($PSCmdlet.ShouldProcess($InstallRoot, "Install combined SessionGuard runtime")) {
    Copy-SessionGuardRuntimeLayout -SourceRoot $PublishRoot -DestinationRoot $InstallRoot

    $serviceInstallScript = Join-Path $InstallRoot "scripts\\service\\Install-SessionGuardService.ps1"
    $serviceInstallArguments = @{
        SkipPublish = $true
        PublishRoot = $InstallRoot
        StartupTimeoutSeconds = $StartupTimeoutSeconds
    }

    if ($DoNotStartService.IsPresent) {
        $serviceInstallArguments.DoNotStart = $true
    }

    & $serviceInstallScript @serviceInstallArguments | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "Service installation failed during combined SessionGuard installation."
    }

    $installedAppExe = Get-SessionGuardAppExecutablePath -Root $InstallRoot
    $registeredCommand = Register-SessionGuardAppStartup -AppExecutable $installedAppExe
    Write-Host "Registered SessionGuard app startup: $registeredCommand"

    if (-not $DoNotLaunchApp) {
        $existingInstalledApps = @(
            Get-Process -Name "SessionGuard.App" -ErrorAction SilentlyContinue |
            Where-Object {
                try {
                    -not [string]::IsNullOrWhiteSpace($_.Path) -and
                    (Test-SessionGuardPathMatch -Left $_.Path -Right $installedAppExe)
                }
                catch {
                    $false
                }
            }
        )

        if ($existingInstalledApps.Count -eq 0) {
            Start-Process -FilePath $installedAppExe -ArgumentList "--start-minimized" -WindowStyle Minimized
            Write-Host "Launched SessionGuard app minimized to tray."
        }
        else {
            Write-Host "The installed SessionGuard app is already running; skipped auto-launch."
        }
    }
}

[pscustomobject]@{
    InstallRoot = $InstallRoot
    ServiceName = $script:SessionGuardServiceName
    ServiceAutoStart = "delayed-auto"
    AppStartupRegistration = Get-SessionGuardAppStartupRegistration
    AppExecutable = Get-SessionGuardAppExecutablePath -Root $InstallRoot
} | Select-Object InstallRoot, ServiceName, ServiceAutoStart, AppStartupRegistration, AppExecutable
