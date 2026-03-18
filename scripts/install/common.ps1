Set-StrictMode -Version Latest

$script:SessionGuardAppStartupValueName = "SessionGuard"

$serviceCommonPath = Join-Path $PSScriptRoot "..\\service\\common.ps1"
if (-not (Get-Command Test-SessionGuardPathMatch -ErrorAction SilentlyContinue) -and
    (Test-Path $serviceCommonPath)) {
    . $serviceCommonPath
}

function Get-SessionGuardDefaultInstallRoot {
    return Join-Path ${env:ProgramFiles} "SessionGuard"
}

function Get-SessionGuardBundleSourceRoot {
    $candidateRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
    if ((Test-Path (Join-Path $candidateRoot "SessionGuard.App.exe")) -and
        (Test-Path (Join-Path $candidateRoot "SessionGuard.Service.exe"))) {
        return $candidateRoot
    }

    return ""
}

function Get-SessionGuardAppExecutablePath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Root
    )

    return Join-Path $Root "SessionGuard.App.exe"
}

function Get-SessionGuardServiceExecutablePath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Root
    )

    return Join-Path $Root "SessionGuard.Service.exe"
}

function Get-SessionGuardRelativePath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Root,

        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $rootPath = [System.IO.Path]::GetFullPath($Root)
    $fullPath = [System.IO.Path]::GetFullPath($Path)

    if (-not $rootPath.EndsWith([System.IO.Path]::DirectorySeparatorChar)) {
        $rootPath += [System.IO.Path]::DirectorySeparatorChar
    }

    $rootUri = [System.Uri]::new($rootPath)
    $pathUri = [System.Uri]::new($fullPath)
    $relativePath = $rootUri.MakeRelativeUri($pathUri).ToString()
    return [System.Uri]::UnescapeDataString($relativePath)
}

function Get-SessionGuardFileSha256 {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if (-not (Test-Path $Path)) {
        throw "Cannot hash missing file '$Path'."
    }

    $stream = [System.IO.File]::OpenRead($Path)
    try {
        $sha256 = [System.Security.Cryptography.SHA256]::Create()
        try {
            return ([System.BitConverter]::ToString($sha256.ComputeHash($stream))).Replace("-", "").ToUpperInvariant()
        }
        finally {
            $sha256.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

function Get-SessionGuardFileSignatureInfo {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if (-not (Test-Path $Path)) {
        return [pscustomobject]@{
            Path = Split-Path -Leaf $Path
            Exists = $false
            Status = "Missing"
            StatusMessage = "File was not found."
            IsSigned = $false
            SignerSubject = ""
            SignerThumbprint = ""
        }
    }

    $signatureCommand = Get-Command Get-AuthenticodeSignature -ErrorAction SilentlyContinue
    if ($null -eq $signatureCommand) {
        try {
            Import-Module Microsoft.PowerShell.Security -ErrorAction Stop | Out-Null
            $signatureCommand = Get-Command Get-AuthenticodeSignature -ErrorAction SilentlyContinue
        }
        catch {
            $signatureCommand = $null
        }
    }

    if ($null -eq $signatureCommand) {
        return [pscustomobject]@{
            Path = Split-Path -Leaf $Path
            Exists = $true
            Status = "Unknown"
            StatusMessage = "Authenticode signature status could not be resolved in this PowerShell host."
            IsSigned = $false
            SignerSubject = ""
            SignerThumbprint = ""
        }
    }

    try {
        $signature = Get-AuthenticodeSignature -FilePath $Path
        $signer = $signature.SignerCertificate
        $pathLabel = Split-Path -Leaf $Path
        $status = $signature.Status.ToString()
        $statusMessage = switch ($status) {
            "Valid" { "The digital signature on $pathLabel is valid." }
            "NotSigned" { "The file $pathLabel is not digitally signed." }
            default {
                $rawMessage = [string]$signature.StatusMessage
                if ([string]::IsNullOrWhiteSpace($rawMessage)) {
                    "The digital signature status for $pathLabel is $status."
                }
                else {
                    "The digital signature status for $pathLabel is $status. $rawMessage"
                }
            }
        }
        return [pscustomobject]@{
            Path = $pathLabel
            Exists = $true
            Status = $status
            StatusMessage = $statusMessage
            IsSigned = $signature.Status -ne [System.Management.Automation.SignatureStatus]::NotSigned
            SignerSubject = if ($null -ne $signer) { [string]$signer.Subject } else { "" }
            SignerThumbprint = if ($null -ne $signer) { [string]$signer.Thumbprint } else { "" }
        }
    }
    catch {
        return [pscustomobject]@{
            Path = Split-Path -Leaf $Path
            Exists = $true
            Status = "Unknown"
            StatusMessage = "Authenticode signature status for $(Split-Path -Leaf $Path) could not be resolved in this PowerShell host."
            IsSigned = $false
            SignerSubject = ""
            SignerThumbprint = ""
        }
    }
}

function Get-SessionGuardBundleIntegrityManifestPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Root
    )

    return Join-Path $Root "bundle-integrity.json"
}

function Get-SessionGuardBundleFileInventory {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Root,

        [string[]]$ExcludeRelativePaths = @()
    )

    $excludeSet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($excludePath in $ExcludeRelativePaths) {
        if (-not [string]::IsNullOrWhiteSpace($excludePath)) {
            $excludeSet.Add(($excludePath -replace "\\", "/")) | Out-Null
        }
    }

    return @(
        Get-ChildItem -Path $Root -File -Recurse |
        Sort-Object FullName |
        ForEach-Object {
            $relativePath = Get-SessionGuardRelativePath -Root $Root -Path $_.FullName
            if ($excludeSet.Contains($relativePath)) {
                return
            }

            [pscustomobject]@{
                Path = $relativePath
                SizeBytes = $_.Length
                Sha256 = Get-SessionGuardFileSha256 -Path $_.FullName
            }
        }
    )
}

function Invoke-SessionGuardBundleVerification {
    param(
        [Parameter(Mandatory = $true)]
        [string]$BundleRoot
    )

    $issues = New-Object System.Collections.Generic.List[string]
    $warnings = New-Object System.Collections.Generic.List[string]
    $manifestPath = Get-SessionGuardBundleIntegrityManifestPath -Root $BundleRoot
    $manifest = $null

    if (-not (Test-Path $manifestPath)) {
        return [pscustomobject]@{
            Available = $false
            Verified = $false
            BundleRoot = $BundleRoot
            ManifestPath = $manifestPath
            ProductVersion = ""
            FileCount = 0
            Issues = @("Bundle integrity manifest was not found.")
            Warnings = @()
            Signatures = @()
        }
    }

    try {
        $manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
    }
    catch {
        return [pscustomobject]@{
            Available = $true
            Verified = $false
            BundleRoot = $BundleRoot
            ManifestPath = $manifestPath
            ProductVersion = ""
            FileCount = 0
            Issues = @("Bundle integrity manifest could not be parsed.")
            Warnings = @()
            Signatures = @()
        }
    }

    $manifestFiles = @($manifest.Files)
    $trackedPaths = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($entry in $manifestFiles) {
        $relativePath = [string]$entry.Path
        $trackedPaths.Add($relativePath) | Out-Null
        $bundlePath = Join-Path $BundleRoot ($relativePath -replace "/", "\")
        if (-not (Test-Path $bundlePath)) {
            $issues.Add("Tracked bundle file '$relativePath' is missing.")
            continue
        }

        $item = Get-Item $bundlePath
        if ($item.Length -ne [long]$entry.SizeBytes) {
            $issues.Add("Tracked bundle file '$relativePath' has an unexpected size.")
            continue
        }

        $actualHash = Get-SessionGuardFileSha256 -Path $bundlePath
        if (-not [string]::Equals($actualHash, [string]$entry.Sha256, [System.StringComparison]::OrdinalIgnoreCase)) {
            $issues.Add("Tracked bundle file '$relativePath' failed SHA256 verification.")
        }
    }

    $actualFiles = Get-SessionGuardBundleFileInventory -Root $BundleRoot -ExcludeRelativePaths @("bundle-integrity.json")
    foreach ($actualFile in $actualFiles) {
        if (-not $trackedPaths.Contains([string]$actualFile.Path)) {
            $issues.Add("Unexpected bundle file '$($actualFile.Path)' is present.")
        }
    }

    $signatureReports = @(
        @(
            "SessionGuard.App.exe",
            "SessionGuard.Service.exe",
            "Install-SessionGuard.ps1",
            "Uninstall-SessionGuard.ps1",
            "Verify-SessionGuard.ps1"
        ) |
        ForEach-Object {
            $targetPath = Join-Path $BundleRoot $_
            if (Test-Path $targetPath) {
                Get-SessionGuardFileSignatureInfo -Path $targetPath
            }
        }
    )

    foreach ($signatureReport in $signatureReports) {
        if ($signatureReport.Status -ne "Valid") {
            $warnings.Add(("{0} is not Authenticode-valid. Verify the downloaded zip hash against the published release checksum file. {1}" -f (Split-Path -Leaf $signatureReport.Path), $signatureReport.StatusMessage))
        }
    }

    return [pscustomobject]@{
        Available = $true
        Verified = $issues.Count -eq 0
        BundleRoot = $BundleRoot
        ManifestPath = $manifestPath
        ProductVersion = if ($null -ne $manifest) { [string]$manifest.ProductVersion } else { "" }
        FileCount = $manifestFiles.Count
        Issues = $issues.ToArray()
        Warnings = $warnings.ToArray()
        Signatures = $signatureReports
    }
}

function Get-SessionGuardStartupRegistryPath {
    return "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
}

function Get-SessionGuardAppStartupCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string]$AppExecutable
    )

    return ('"{0}" --start-minimized' -f $AppExecutable)
}

function Get-SessionGuardAppStartupRegistration {
    $registryPath = Get-SessionGuardStartupRegistryPath
    if (-not (Test-Path $registryPath)) {
        return $null
    }

    $item = Get-ItemProperty -Path $registryPath -ErrorAction SilentlyContinue
    if ($null -eq $item) {
        return $null
    }

    $property = $item.PSObject.Properties[$script:SessionGuardAppStartupValueName]
    if ($null -eq $property) {
        return $null
    }

    return [string]$property.Value
}

function Register-SessionGuardAppStartup {
    param(
        [Parameter(Mandatory = $true)]
        [string]$AppExecutable
    )

    $registryPath = Get-SessionGuardStartupRegistryPath
    New-Item -Path $registryPath -Force | Out-Null
    $command = Get-SessionGuardAppStartupCommand -AppExecutable $AppExecutable
    Set-ItemProperty -Path $registryPath -Name $script:SessionGuardAppStartupValueName -Value $command
    return $command
}

function Join-SessionGuardCommandArguments {
    param(
        [string[]]$Arguments
    )

    if ($null -eq $Arguments -or $Arguments.Count -eq 0) {
        return ""
    }

    return ($Arguments | ForEach-Object {
        if ([string]::IsNullOrWhiteSpace($_)) {
            '""'
        }
        elseif ($_ -match '[\s"]') {
            '"' + ($_ -replace '"', '\"') + '"'
        }
        else {
            $_
        }
    }) -join ' '
}

function Start-SessionGuardInstalledApp {
    param(
        [Parameter(Mandatory = $true)]
        [string]$AppExecutable,

        [string[]]$Arguments = @("--start-minimized")
    )

    if (-not (Test-Path $AppExecutable)) {
        return [pscustomobject]@{
            Attempted = $false
            Succeeded = $false
            Method = "none"
            Warning = "SessionGuard installed successfully, but the app executable was not found at '$AppExecutable'. Launch it manually after install."
        }
    }

    $workingDirectory = Split-Path -Parent $AppExecutable
    $argumentString = Join-SessionGuardCommandArguments -Arguments $Arguments

    try {
        $shell = New-Object -ComObject Shell.Application -ErrorAction Stop
        try {
            # Launch through the interactive shell first so the tray app starts in the signed-in user's desktop session.
            $shell.ShellExecute($AppExecutable, $argumentString, $workingDirectory, "open", 2)
            return [pscustomobject]@{
                Attempted = $true
                Succeeded = $true
                Method = "shell"
                Warning = ""
            }
        }
        finally {
            [System.Runtime.InteropServices.Marshal]::ReleaseComObject($shell) | Out-Null
        }
    }
    catch {
        try {
            Start-Process -FilePath $AppExecutable -ArgumentList $Arguments -WindowStyle Minimized -WorkingDirectory $workingDirectory -ErrorAction Stop | Out-Null
            return [pscustomobject]@{
                Attempted = $true
                Succeeded = $true
                Method = "process"
                Warning = ""
            }
        }
        catch {
            return [pscustomobject]@{
                Attempted = $true
                Succeeded = $false
                Method = "failed"
                Warning = "SessionGuard installed successfully, but the tray app could not be launched automatically. Windows may have shown a SmartScreen or protection prompt. Launch '$AppExecutable' manually from your normal desktop session, use -DoNotLaunchApp on future installs, or wait for the next sign-in. Windows reported: $($_.Exception.Message)"
            }
        }
    }
}

function Get-SessionGuardRunningAppProcesses {
    param(
        [Parameter(Mandatory = $true)]
        [string]$AppExecutable
    )

    return @(
        Get-Process -Name "SessionGuard.App" -ErrorAction SilentlyContinue |
        Where-Object {
            try {
                -not [string]::IsNullOrWhiteSpace($_.Path) -and
                (Test-SessionGuardPathMatch -Left $_.Path -Right $AppExecutable)
            }
            catch {
                $false
            }
        }
    )
}

function Stop-SessionGuardRunningApp {
    param(
        [Parameter(Mandatory = $true)]
        [string]$AppExecutable,

        [int]$GracePeriodSeconds = 2,

        [int]$ShutdownTimeoutSeconds = 5
    )

    $runningAppProcesses = @(Get-SessionGuardRunningAppProcesses -AppExecutable $AppExecutable)
    if ($runningAppProcesses.Count -eq 0) {
        return [pscustomobject]@{
            Attempted = $false
            Stopped = $true
            ProcessCount = 0
        }
    }

    foreach ($process in $runningAppProcesses) {
        try {
            $null = $process.CloseMainWindow()
        }
        catch {
        }
    }

    if ($GracePeriodSeconds -gt 0) {
        Start-Sleep -Seconds $GracePeriodSeconds
    }

    $shutdownDeadline = (Get-Date).AddSeconds([Math]::Max(1, $ShutdownTimeoutSeconds))
    do {
        foreach ($process in $runningAppProcesses) {
            try {
                $process.Refresh()
                if (-not $process.HasExited) {
                    Stop-Process -Id $process.Id -Force -ErrorAction Stop
                }
            }
            catch {
            }
        }

        $remainingProcesses = @(Get-SessionGuardRunningAppProcesses -AppExecutable $AppExecutable)
        if ($remainingProcesses.Count -eq 0) {
            return [pscustomobject]@{
                Attempted = $true
                Stopped = $true
                ProcessCount = $runningAppProcesses.Count
            }
        }

        Start-Sleep -Milliseconds 250
        $runningAppProcesses = $remainingProcesses
    } while ((Get-Date) -lt $shutdownDeadline)

    if ($runningAppProcesses.Count -gt 0) {
        $remainingIds = $runningAppProcesses | ForEach-Object { $_.Id } | Sort-Object
        throw "SessionGuard app executable '$AppExecutable' is still running after shutdown attempts. Remaining process IDs: $($remainingIds -join ', ')."
    }
}

function Unregister-SessionGuardAppStartup {
    $registryPath = Get-SessionGuardStartupRegistryPath
    if (-not (Test-Path $registryPath)) {
        return
    }

    Remove-ItemProperty -Path $registryPath -Name $script:SessionGuardAppStartupValueName -ErrorAction SilentlyContinue
}

function Copy-SessionGuardRuntimeLayout {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SourceRoot,

        [Parameter(Mandatory = $true)]
        [string]$DestinationRoot
    )

    $preserveDirectories = @("config", "logs", "state")
    $backupRoot = $null
    $preservedDirectories = @{}

    function Restore-PreservedDirectory {
        param(
            [string]$Name
        )

        if (-not $preservedDirectories.ContainsKey($Name)) {
            return
        }

        $source = $preservedDirectories[$Name]
        $destination = Join-Path $DestinationRoot $Name
        if (Test-Path $destination) {
            Remove-Item $destination -Recurse -Force
        }

        Copy-Item $source -Destination $DestinationRoot -Recurse -Force
    }

    try {
        if (Test-Path $DestinationRoot) {
            $backupRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("SessionGuard.InstallBackup\\" + [Guid]::NewGuid().ToString("N"))
            foreach ($name in $preserveDirectories) {
                $source = Join-Path $DestinationRoot $name
                if (Test-Path $source) {
                    New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
                    Copy-Item $source -Destination $backupRoot -Recurse -Force
                    $preservedDirectories[$name] = Join-Path $backupRoot $name
                }
            }

            Remove-Item $DestinationRoot -Recurse -Force
        }

        New-Item -ItemType Directory -Path $DestinationRoot -Force | Out-Null
        Copy-Item (Join-Path $SourceRoot "*") -Destination $DestinationRoot -Recurse -Force

        foreach ($name in $preserveDirectories) {
            Restore-PreservedDirectory -Name $name
        }
    }
    finally {
        if ($backupRoot -and (Test-Path $backupRoot)) {
            Remove-Item $backupRoot -Recurse -Force
        }
    }
}
