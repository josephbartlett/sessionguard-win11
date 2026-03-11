[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [string]$OutputRoot = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot "artifacts\\ci\\windows-validation"
}

$uiSmokeOutput = Join-Path $OutputRoot "ui-smoke"
$testResultsOutput = Join-Path $OutputRoot "test-results"
$jsonSummaryPath = Join-Path $OutputRoot "validation-summary.json"
$markdownSummaryPath = Join-Path $OutputRoot "validation-summary.md"

if (Test-Path $OutputRoot) {
    Remove-Item $OutputRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $uiSmokeOutput -Force | Out-Null
New-Item -ItemType Directory -Path $testResultsOutput -Force | Out-Null

$summary = [ordered]@{
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    configuration = $Configuration
    dotnetSdk = (& dotnet --version).Trim()
    overallStatus = "in-progress"
    failureMessage = $null
    artifacts = [ordered]@{
        uiSmokeDirectory = $uiSmokeOutput
        uiSmokeSummary = Join-Path $uiSmokeOutput "summary.json"
        testResultsDirectory = $testResultsOutput
        testResultsTrx = Join-Path $testResultsOutput "sessionguard-tests.trx"
    }
    steps = [ordered]@{
        build = [ordered]@{
            status = "not-run"
            command = "dotnet build SessionGuard.sln -c $Configuration"
            startedAtUtc = $null
            completedAtUtc = $null
            durationSeconds = $null
        }
        test = [ordered]@{
            status = "not-run"
            command = "dotnet test SessionGuard.sln -c $Configuration --no-build --logger trx --results-directory $testResultsOutput"
            startedAtUtc = $null
            completedAtUtc = $null
            durationSeconds = $null
        }
        uiSmoke = [ordered]@{
            status = "not-run"
            command = "powershell -ExecutionPolicy Bypass -File scripts/ui/Run-UiSmoke.ps1 -Configuration $Configuration -OutputDirectory $uiSmokeOutput -SkipBuild"
            startedAtUtc = $null
            completedAtUtc = $null
            durationSeconds = $null
        }
    }
}

function Write-ValidationSummary {
    param(
        [System.Collections.IDictionary]$State,
        [string]$JsonPath,
        [string]$MarkdownPath
    )

    $json = $State | ConvertTo-Json -Depth 10
    [System.IO.File]::WriteAllText($JsonPath, $json)

    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add("# SessionGuard Windows Validation")
    $lines.Add("")
    $lines.Add("- Generated: $($State.generatedAtUtc)")
    $lines.Add("- Configuration: $($State.configuration)")
    $lines.Add("- .NET SDK: $($State.dotnetSdk)")
    $lines.Add("- Overall status: $($State.overallStatus)")

    if (-not [string]::IsNullOrWhiteSpace($State.failureMessage)) {
        $lines.Add("- Failure: $($State.failureMessage)")
    }

    $lines.Add("")
    $lines.Add("## Steps")
    $lines.Add("")

    foreach ($stepEntry in $State.steps.GetEnumerator()) {
        $step = $stepEntry.Value
        $lines.Add("- $($stepEntry.Key): $($step.status)")
        $lines.Add(('  - command: `{0}`' -f $step.command))
        if ($step.durationSeconds -ne $null) {
            $lines.Add("  - duration: $($step.durationSeconds)s")
        }
    }

    $lines.Add("")
    $lines.Add("## Artifacts")
    $lines.Add("")
    $lines.Add(('- UI smoke directory: `{0}`' -f $State.artifacts.uiSmokeDirectory))
    $lines.Add(('- UI smoke summary: `{0}`' -f $State.artifacts.uiSmokeSummary))
    $lines.Add(('- Test results directory: `{0}`' -f $State.artifacts.testResultsDirectory))
    $lines.Add(('- Test results TRX: `{0}`' -f $State.artifacts.testResultsTrx))

    [System.IO.File]::WriteAllLines($MarkdownPath, $lines)
}

function Invoke-TrackedStep {
    param(
        [System.Collections.IDictionary]$State,
        [string]$StepName,
        [scriptblock]$Action
    )

    $step = $State.steps[$StepName]
    $startedAt = (Get-Date).ToUniversalTime()
    $step.status = "running"
    $step.startedAtUtc = $startedAt.ToString("o")

    try {
        & $Action
        $step.status = "passed"
    }
    catch {
        $step.status = "failed"
        throw
    }
    finally {
        $completedAt = (Get-Date).ToUniversalTime()
        $step.completedAtUtc = $completedAt.ToString("o")
        $step.durationSeconds = [Math]::Round(($completedAt - $startedAt).TotalSeconds, 2)
    }
}

try {
    Invoke-TrackedStep -State $summary -StepName "build" -Action {
        & dotnet build (Join-Path $repoRoot "SessionGuard.sln") -c $Configuration
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet build failed."
        }
    }

    Invoke-TrackedStep -State $summary -StepName "test" -Action {
        & dotnet test (Join-Path $repoRoot "SessionGuard.sln") `
            -c $Configuration `
            --no-build `
            --logger "trx;LogFileName=sessionguard-tests.trx" `
            --results-directory $testResultsOutput
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet test failed."
        }
    }

    Invoke-TrackedStep -State $summary -StepName "uiSmoke" -Action {
        & (Join-Path $repoRoot "scripts\\ui\\Run-UiSmoke.ps1") `
            -Configuration $Configuration `
            -OutputDirectory $uiSmokeOutput `
            -SkipBuild
    }

    $summary.overallStatus = "passed"
}
catch {
    $summary.overallStatus = "failed"
    $summary.failureMessage = $_.Exception.Message
    throw
}
finally {
    Write-ValidationSummary -State $summary -JsonPath $jsonSummaryPath -MarkdownPath $markdownSummaryPath
}
