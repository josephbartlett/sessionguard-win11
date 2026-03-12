Set-StrictMode -Version Latest

$script:SessionGuardAppStartupValueName = "SessionGuard"

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
