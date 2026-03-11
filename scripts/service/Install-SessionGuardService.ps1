[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$PublishRoot = "",
    [switch]$SkipPublish,
    [switch]$DoNotStart,
    [switch]$ValidateOnly,
    [switch]$AsJson
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
$appSettingsPath = Join-Path $PublishRoot "config\\appsettings.json"
$protectedProcessesPath = Join-Path $PublishRoot "config\\protected-processes.json"
$serviceExists = Test-SessionGuardServiceExists
$readinessIssues = New-Object System.Collections.Generic.List[string]

if (-not (Test-Path $serviceExe)) {
    $readinessIssues.Add("Service executable not found at '$serviceExe'. Run Publish-SessionGuardService.ps1 first or omit -SkipPublish.")
}

if (-not (Test-Path $appSettingsPath)) {
    $readinessIssues.Add("Missing config\\appsettings.json in '$PublishRoot'.")
}

if (-not (Test-Path $protectedProcessesPath)) {
    $readinessIssues.Add("Missing config\\protected-processes.json in '$PublishRoot'.")
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
    AppSettingsPath = $appSettingsPath
    ProtectedProcessesPath = $protectedProcessesPath
    Elevated = $isElevated
    ServiceAlreadyInstalled = $serviceExists
    ServiceExecutableExists = Test-Path $serviceExe
    AppSettingsExists = Test-Path $appSettingsPath
    ProtectedProcessesExists = Test-Path $protectedProcessesPath
    ScExeAvailable = $scAvailable
    CanInstallNow = $readinessIssues.Count -eq 0
    Issues = $readinessIssues.ToArray()
}

if ($ValidateOnly) {
    if ($AsJson) {
        $readiness | ConvertTo-Json -Compress
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
