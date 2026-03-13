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
                Warning = "SessionGuard installed successfully, but the tray app could not be launched automatically. Launch '$AppExecutable' manually from your normal desktop session or wait for the next sign-in. Windows reported: $($_.Exception.Message)"
            }
        }
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
