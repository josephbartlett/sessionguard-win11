Set-StrictMode -Version Latest

$serviceCommonPath = Join-Path $PSScriptRoot "..\\service\\common.ps1"
if (-not (Get-Command Get-SessionGuardRepositoryRoot -ErrorAction SilentlyContinue) -and
    (Test-Path $serviceCommonPath)) {
    . $serviceCommonPath
}

function Resolve-SessionGuardSignToolPath {
    $configuredPath = [string]$env:SESSIONGUARD_SIGNTOOL_PATH
    if (-not [string]::IsNullOrWhiteSpace($configuredPath)) {
        if (-not (Test-Path $configuredPath)) {
            throw "SESSIONGUARD_SIGNTOOL_PATH was set, but '$configuredPath' does not exist."
        }

        return (Get-Item $configuredPath).FullName
    }

    $command = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    $kitRoots = @(
        (Join-Path ${env:ProgramFiles(x86)} "Windows Kits\\10\\bin"),
        (Join-Path ${env:ProgramFiles} "Windows Kits\\10\\bin")
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and (Test-Path $_) }

    foreach ($kitRoot in $kitRoots) {
        $candidate = Get-ChildItem -Path $kitRoot -Filter signtool.exe -File -Recurse -ErrorAction SilentlyContinue |
            Sort-Object FullName -Descending |
            Select-Object -First 1
        if ($null -ne $candidate) {
            return $candidate.FullName
        }
    }

    throw "Could not locate signtool.exe. Install the Windows SDK or set SESSIONGUARD_SIGNTOOL_PATH."
}

function Get-SessionGuardSigningSession {
    param(
        [switch]$RequireSigning
    )

    $certificateBase64 = [string]$env:SESSIONGUARD_SIGN_CERT_BASE64
    $certificatePath = [string]$env:SESSIONGUARD_SIGN_CERT_FILE
    $certificatePassword = [string]$env:SESSIONGUARD_SIGN_CERT_PASSWORD
    $timestampUrl = [string]$env:SESSIONGUARD_SIGN_TIMESTAMP_URL
    if ([string]::Equals($timestampUrl, "disabled", [System.StringComparison]::OrdinalIgnoreCase) -or
        [string]::Equals($timestampUrl, "none", [System.StringComparison]::OrdinalIgnoreCase) -or
        [string]::Equals($timestampUrl, "off", [System.StringComparison]::OrdinalIgnoreCase)) {
        $timestampUrl = ""
    }
    elseif ([string]::IsNullOrWhiteSpace($timestampUrl)) {
        $timestampUrl = "http://timestamp.digicert.com"
    }

    $enabled = (-not [string]::IsNullOrWhiteSpace($certificateBase64)) -or
        (-not [string]::IsNullOrWhiteSpace($certificatePath))

    if (-not $enabled) {
        if ($RequireSigning.IsPresent) {
            throw "Release signing was required, but neither SESSIONGUARD_SIGN_CERT_BASE64 nor SESSIONGUARD_SIGN_CERT_FILE was configured."
        }

        return [pscustomobject]@{
            Enabled = $false
            Required = $RequireSigning.IsPresent
            TimestampUrl = $timestampUrl
            SignToolPath = ""
            CertificatePath = ""
            CertificateSubject = ""
            CertificateThumbprint = ""
            Certificate = $null
            TemporaryPaths = @()
        }
    }

    $temporaryPaths = New-Object System.Collections.Generic.List[string]
    $certificate = $null
    try {
        if (-not [string]::IsNullOrWhiteSpace($certificateBase64)) {
            $tempDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("SessionGuard.Signing\\" + [Guid]::NewGuid().ToString("N"))
            New-Item -ItemType Directory -Path $tempDirectory -Force | Out-Null
            $certificatePath = Join-Path $tempDirectory "sessionguard-signing.pfx"
            [System.IO.File]::WriteAllBytes($certificatePath, [System.Convert]::FromBase64String($certificateBase64))
            $temporaryPaths.Add($tempDirectory) | Out-Null
        }

        if (-not (Test-Path $certificatePath)) {
            throw "Configured signing certificate path '$certificatePath' does not exist."
        }

        $flags = [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::Exportable -bor
            [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet

        $certificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new(
            $certificatePath,
            $certificatePassword,
            $flags)

        if (-not $certificate.HasPrivateKey) {
            throw "Configured signing certificate '$certificatePath' does not include a private key."
        }

        return [pscustomobject]@{
            Enabled = $true
            Required = $RequireSigning.IsPresent
            TimestampUrl = $timestampUrl
            SignToolPath = Resolve-SessionGuardSignToolPath
            CertificatePath = $certificatePath
            CertificateSubject = [string]$certificate.Subject
            CertificateThumbprint = [string]$certificate.Thumbprint
            Certificate = $certificate
            CertificatePassword = $certificatePassword
            TemporaryPaths = $temporaryPaths.ToArray()
        }
    }
    catch {
        if ($null -ne $certificate) {
            $certificate.Dispose()
        }

        foreach ($temporaryPath in @($temporaryPaths.ToArray())) {
            if (-not [string]::IsNullOrWhiteSpace($temporaryPath) -and (Test-Path $temporaryPath)) {
                Remove-Item $temporaryPath -Recurse -Force
            }
        }

        throw
    }
}

function Remove-SessionGuardSigningSession {
    param(
        [Parameter(Mandatory = $true)]
        [pscustomobject]$Session
    )

    if ($null -ne $Session.Certificate) {
        $Session.Certificate.Dispose()
    }

    foreach ($temporaryPath in @($Session.TemporaryPaths)) {
        if (-not [string]::IsNullOrWhiteSpace($temporaryPath) -and (Test-Path $temporaryPath)) {
            Remove-Item $temporaryPath -Recurse -Force
        }
    }
}

function Test-SessionGuardScriptFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $extension = [System.IO.Path]::GetExtension($Path)
    return $extension -in @(".ps1", ".psm1", ".psd1")
}

function Invoke-SessionGuardSignatureVerification {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $signatureInfo = Get-SessionGuardFileSignatureInfo -Path $Path
    if ($signatureInfo.Status -ne "Valid") {
        throw "Signature verification for '$(Split-Path -Leaf $Path)' failed with status '$($signatureInfo.Status)'. $($signatureInfo.StatusMessage)"
    }

    return $signatureInfo
}

function Invoke-SessionGuardCodeSigning {
    param(
        [Parameter(Mandatory = $true)]
        [pscustomobject]$Session,

        [Parameter(Mandatory = $true)]
        [string[]]$Paths,

        [string]$Description = "SessionGuard"
    )

    $uniquePaths = @(
        $Paths |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        ForEach-Object { [System.IO.Path]::GetFullPath($_) } |
        Select-Object -Unique
    )

    foreach ($path in $uniquePaths) {
        if (-not (Test-Path $path)) {
            throw "Cannot sign missing file '$path'."
        }
    }

    if (-not $Session.Enabled) {
        return @(
            $uniquePaths |
            ForEach-Object { Get-SessionGuardFileSignatureInfo -Path $_ }
        )
    }

    foreach ($path in $uniquePaths) {
        if (Test-SessionGuardScriptFile -Path $path) {
            $signatureParameters = @{
                FilePath = $path
                Certificate = $Session.Certificate
                HashAlgorithm = "SHA256"
            }

            if (-not [string]::IsNullOrWhiteSpace($Session.TimestampUrl)) {
                $signatureParameters.TimestampServer = $Session.TimestampUrl
            }

            $result = Set-AuthenticodeSignature @signatureParameters
            if ($result.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
                throw "Set-AuthenticodeSignature reported status '$($result.Status)' for '$path'. $($result.StatusMessage)"
            }
        }
        else {
            $arguments = @(
                "sign",
                "/fd", "SHA256",
                "/d", $Description,
                "/f", $Session.CertificatePath,
                "/p", $Session.CertificatePassword
            )

            if (-not [string]::IsNullOrWhiteSpace($Session.TimestampUrl)) {
                $arguments += @("/td", "SHA256", "/tr", $Session.TimestampUrl)
            }

            $arguments += $path

            $output = & $Session.SignToolPath @arguments 2>&1
            if ($LASTEXITCODE -ne 0) {
                throw "signtool.exe failed for '$path'.`n$output"
            }
        }

        Invoke-SessionGuardSignatureVerification -Path $path | Out-Null
    }

    return @(
        $uniquePaths |
        ForEach-Object { Get-SessionGuardFileSignatureInfo -Path $_ }
    )
}
