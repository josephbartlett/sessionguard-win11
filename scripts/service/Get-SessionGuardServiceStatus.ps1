[CmdletBinding()]
param(
    [switch]$AsJson
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "common.ps1")

$service = Get-Service -Name $script:SessionGuardServiceName -ErrorAction SilentlyContinue
$probeExe = Get-SessionGuardProbeExePath
$probeSucceeded = $false
$probeOutput = $null

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

$status = [pscustomobject]@{
    ServiceName = $script:SessionGuardServiceName
    Installed = $null -ne $service
    Status = if ($null -ne $service) { $service.Status.ToString() } else { "NotInstalled" }
    StartType = if ($null -ne $service) { $service.StartType.ToString() } else { "Unknown" }
    ProbeExecutable = $probeExe
    ControlPlaneReachable = $probeSucceeded
    ProbeOutput = if ($probeOutput) { [string]::Join([Environment]::NewLine, $probeOutput) } else { "" }
}

if ($AsJson) {
    $status | ConvertTo-Json -Compress
}
else {
    $status
}
