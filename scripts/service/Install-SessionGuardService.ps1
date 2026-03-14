[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$PublishRoot = "",
    [switch]$SkipPublish,
    [switch]$DoNotStart,
    [switch]$ValidateOnly,
    [switch]$AsJson,
    [int]$StartupTimeoutSeconds = 30
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "common.ps1")

if ([string]::IsNullOrWhiteSpace($PublishRoot)) {
    $PublishRoot = Get-SessionGuardPublishRoot
}

$isElevated = Test-SessionGuardIsElevated
$scAvailable = $null -ne (Get-Command sc.exe -ErrorAction SilentlyContinue)

if (-not $SkipPublish) {
    & (Join-Path $PSScriptRoot "Publish-SessionGuardService.ps1") -Configuration $Configuration -Runtime $Runtime -OutputDir $PublishRoot | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to publish SessionGuard.Service before installation."
    }
}

$serviceExe = Get-SessionGuardServiceExePath -PublishRoot $PublishRoot
$manifestPath = Get-SessionGuardInstallManifestPath -PublishRoot $PublishRoot
$appSettingsPath = Join-Path $PublishRoot "config\\appsettings.json"
$protectedProcessesPath = Join-Path $PublishRoot "config\\protected-processes.json"
$configDefaultsPath = Join-Path $PublishRoot "config.defaults"
$serviceExists = Test-SessionGuardServiceExists
$readinessIssues = New-Object System.Collections.Generic.List[string]
$readinessWarnings = New-Object System.Collections.Generic.List[string]
$runtimeValidation = $null
$installManifest = $null
$upgradeReport = $null
$authorizedUserSid = Get-SessionGuardCurrentUserSid

if (-not (Test-Path $serviceExe)) {
    $readinessIssues.Add("Service executable not found at '$serviceExe'. Run Publish-SessionGuardService.ps1 first or omit -SkipPublish.")
}

if (-not (Test-Path $appSettingsPath)) {
    $readinessIssues.Add("Missing config\\appsettings.json in '$PublishRoot'.")
}

if (-not (Test-Path $protectedProcessesPath)) {
    $readinessIssues.Add("Missing config\\protected-processes.json in '$PublishRoot'.")
}

if (-not (Test-Path $configDefaultsPath)) {
    $readinessWarnings.Add("Published layout is missing config.defaults. Future upgrade-safe config seeding will be unavailable until the service is republished.")
}

if (Test-Path $manifestPath) {
    try {
        $installManifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
    }
    catch {
        $readinessWarnings.Add("Install manifest exists but could not be parsed.")
    }
}
else {
    $readinessWarnings.Add("Install manifest is missing. Republish the service to record versioned install metadata.")
}

if (Test-Path $serviceExe) {
    $runtimeValidation = Invoke-SessionGuardRuntimeValidation -ServiceExecutable $serviceExe -AllowFailure
    if ($null -eq $runtimeValidation.Report) {
        $readinessIssues.Add("Service runtime validation did not return a usable report. Republish the service to ensure the runtime layout is complete.")
    }
    elseif (-not $runtimeValidation.Report.CanRun) {
        $details = if ($runtimeValidation.Report.Issues.Count -gt 0) {
            $runtimeValidation.Report.Issues -join "; "
        }
        else {
            "Unknown runtime validation failure."
        }

        $readinessIssues.Add("Service runtime validation failed: $details")
    }
    elseif ($runtimeValidation.Report.Warnings.Count -gt 0) {
        foreach ($warning in $runtimeValidation.Report.Warnings) {
            $readinessWarnings.Add($warning)
        }
    }

    if ($null -ne $runtimeValidation.Report -and
        ($runtimeValidation.Report.PSObject.Properties.Name -contains "ConfigUpgrade")) {
        $upgradeReport = $runtimeValidation.Report.ConfigUpgrade
    }
    elseif ($null -ne $runtimeValidation.Report) {
        $readinessWarnings.Add("Service runtime validation came from a legacy build and does not expose config-upgrade metadata.")
    }
}

if (-not $scAvailable) {
    $readinessIssues.Add("sc.exe is not available in the current environment.")
}

if (-not $isElevated) {
    $readinessIssues.Add("Service installation requires an elevated PowerShell session.")
}

$readiness = [pscustomobject]@{
    PublishRoot = $PublishRoot
    ServiceExecutable = $serviceExe
    InstallManifestPath = $manifestPath
    AppSettingsPath = $appSettingsPath
    ProtectedProcessesPath = $protectedProcessesPath
    ConfigDefaultsPath = $configDefaultsPath
    Elevated = $isElevated
    ServiceAlreadyInstalled = $serviceExists
    ServiceExecutableExists = Test-Path $serviceExe
    AppSettingsExists = Test-Path $appSettingsPath
    ProtectedProcessesExists = Test-Path $protectedProcessesPath
    ConfigDefaultsExists = Test-Path $configDefaultsPath
    InstallManifest = $installManifest
    ConfigUpgrade = $upgradeReport
    RuntimeValidation = if ($null -ne $runtimeValidation) { $runtimeValidation.Report } else { $null }
    ScExeAvailable = $scAvailable
    CanInstallNow = $readinessIssues.Count -eq 0
    Issues = $readinessIssues.ToArray()
    Warnings = $readinessWarnings.ToArray()
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

if (-not $readiness.Elevated) {
    throw "Service installation requires an elevated PowerShell session."
}

if (-not $readiness.ServiceExecutableExists) {
    throw "Service executable not found at '$serviceExe'. Run Publish-SessionGuardService.ps1 first or omit -SkipPublish."
}

if (-not $readiness.AppSettingsExists -or -not $readiness.ProtectedProcessesExists) {
    throw "Published service layout is missing required config files."
}

if ($null -eq $readiness.RuntimeValidation -or -not $readiness.RuntimeValidation.CanRun) {
    throw "Service runtime validation failed. Republish the service and review the validation report before installing."
}

$upgrade = Invoke-SessionGuardConfigUpgrade -ServiceExecutable $serviceExe
$upgradeReport = $upgrade.Report
if ($null -eq $upgradeReport -or $upgradeReport.HasErrors) {
    throw "Service config upgrade failed. Review the reported migration issues before installing."
}

Set-SessionGuardInstallManifestAuthorizedUserSid -PublishRoot $PublishRoot -AuthorizedUserSid $authorizedUserSid
Set-SessionGuardProtectedRuntimeAcl -Root $PublishRoot -AuthorizedUserSid $authorizedUserSid

if ($serviceExists) {
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
        "binPath=",
        $quotedExe,
        "start=",
        "delayed-auto",
        "DisplayName=",
        $script:SessionGuardServiceDisplayName
    ) | Out-Null
    Invoke-SessionGuardSc @("description", $script:SessionGuardServiceName, $script:SessionGuardServiceDescription) | Out-Null
    Invoke-SessionGuardSc @(
        "failure",
        $script:SessionGuardServiceName,
        "reset=",
        "86400",
        "actions=",
        "restart/5000/restart/15000//"
    ) | Out-Null
    Invoke-SessionGuardSc @("failureflag", $script:SessionGuardServiceName, "1") | Out-Null

    if (-not $DoNotStart) {
        Start-Service -Name $script:SessionGuardServiceName
        Wait-SessionGuardServiceState -DesiredStatus "Running"
        $expectedVersion = if ($null -ne $readiness.InstallManifest) { [string]$readiness.InstallManifest.ProductVersion } else { "" }
        Wait-SessionGuardServiceHealthy -ProbeExecutable $serviceExe -TimeoutSeconds $StartupTimeoutSeconds -ExpectedProductVersion $expectedVersion | Out-Null
    }
}

Get-Service -Name $script:SessionGuardServiceName | Select-Object Name, Status, StartType
