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
$probeSucceeded = $false
$probeOutput = $null
$healthSnapshot = $null

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

$status = [pscustomobject]@{
    ServiceName = $script:SessionGuardServiceName
    Installed = $null -ne $service
    Status = if ($null -ne $service) { $service.Status.ToString() } else { "NotInstalled" }
    StartType = if ($null -ne $service) { $service.StartType.ToString() } else { "Unknown" }
    ProbeExecutable = $probeExe
    HealthFilePath = $healthPath
    ControlPlaneReachable = $probeSucceeded
    ProbeOutput = if ($probeOutput) { [string]::Join([Environment]::NewLine, $probeOutput) } else { "" }
    Health = $healthSnapshot
}

if ($AsJson) {
    $status | ConvertTo-Json -Compress
}
else {
    $status
}
