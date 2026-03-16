[CmdletBinding()]
param(
    [string]$BundleRoot = "",
    [switch]$AsJson
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "..\\service\\common.ps1")
. (Join-Path $PSScriptRoot "common.ps1")

if ([string]::IsNullOrWhiteSpace($BundleRoot)) {
    $candidateRoot = Get-SessionGuardBundleSourceRoot
    if (-not [string]::IsNullOrWhiteSpace($candidateRoot)) {
        $BundleRoot = $candidateRoot
    }
    else {
        $publishedBundleRoot = Join-Path (Get-SessionGuardRepositoryRoot) "artifacts\\publish\\SessionGuard"
        if ((Test-Path (Join-Path $publishedBundleRoot "bundle-integrity.json")) -and
            (Test-Path (Join-Path $publishedBundleRoot "SessionGuard.App.exe")) -and
            (Test-Path (Join-Path $publishedBundleRoot "SessionGuard.Service.exe"))) {
            $BundleRoot = $publishedBundleRoot
        }
        else {
            $BundleRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
        }
    }
}

$report = Invoke-SessionGuardBundleVerification -BundleRoot $BundleRoot

if ($AsJson) {
    $report | ConvertTo-Json -Depth 6 -Compress
}
else {
    $report
}

if (-not $report.Available -or -not $report.Verified) {
    exit 1
}
