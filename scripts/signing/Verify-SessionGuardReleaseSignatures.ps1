[CmdletBinding()]
param(
    [string]$OutputRoot = "",
    [switch]$RequireSigned,
    [switch]$AsJson
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "..\\service\\common.ps1")

$repoRoot = Get-SessionGuardRepositoryRoot
$productVersion = Get-SessionGuardProductVersion
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot "artifacts\\releases\\$productVersion"
}
elseif (-not [System.IO.Path]::IsPathRooted($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot $OutputRoot
}

$OutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)

$paths = [ordered]@{
    AppExecutable = Join-Path $OutputRoot "publish\\SessionGuard.App\\SessionGuard.App.exe"
    ServiceExecutable = Join-Path $OutputRoot "publish\\SessionGuard.Service\\SessionGuard.Service.exe"
    BundleAppExecutable = Join-Path $OutputRoot "publish\\SessionGuard\\SessionGuard.App.exe"
    BundleServiceExecutable = Join-Path $OutputRoot "publish\\SessionGuard\\SessionGuard.Service.exe"
    BundleInstallScript = Join-Path $OutputRoot "publish\\SessionGuard\\Install-SessionGuard.ps1"
    BundleUninstallScript = Join-Path $OutputRoot "publish\\SessionGuard\\Uninstall-SessionGuard.ps1"
    BundleVerifyScript = Join-Path $OutputRoot "publish\\SessionGuard\\Verify-SessionGuard.ps1"
}

$reports = @(
    $paths.GetEnumerator() |
    ForEach-Object {
        $signature = Get-SessionGuardFileSignatureInfo -Path $_.Value
        [pscustomobject]@{
            Name = $_.Key
            Path = $_.Value
            Signature = $signature
        }
    }
)

$issues = New-Object System.Collections.Generic.List[string]
$warnings = New-Object System.Collections.Generic.List[string]

foreach ($report in $reports) {
    if (-not $report.Signature.Exists) {
        $issues.Add("$($report.Name) was not found at '$($report.Path)'.")
        continue
    }

    if ($RequireSigned.IsPresent) {
        if ($report.Signature.Status -ne "Valid") {
            $issues.Add("$($report.Name) is not Authenticode-valid. $($report.Signature.StatusMessage)")
        }
    }
    elseif ($report.Signature.Status -ne "Valid") {
        $warnings.Add("$($report.Name) is not Authenticode-valid. $($report.Signature.StatusMessage)")
    }
}

$result = [pscustomobject]@{
    OutputRoot = $OutputRoot
    RequireSigned = $RequireSigned.IsPresent
    Verified = $issues.Count -eq 0
    Issues = $issues.ToArray()
    Warnings = $warnings.ToArray()
    Signatures = $reports
}

if ($AsJson.IsPresent) {
    $result | ConvertTo-Json -Depth 6
}
else {
    Write-Host "Release signature verification for $OutputRoot"
    foreach ($report in $reports) {
        Write-Host (" - {0}: {1}" -f $report.Name, $report.Signature.Status)
    }

    foreach ($warning in $warnings) {
        Write-Warning $warning
    }
}

if ($issues.Count -gt 0) {
    foreach ($issue in $issues) {
        Write-Error $issue
    }

    exit 1
}
