[CmdletBinding()]
param(
    [Alias("TestFilter")]
    [string]$Filter,

    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",

    [switch]$SkipFull,
    [switch]$Restore,

    [ValidateRange(1, 16)]
    [int]$MaxCpuCount = 1
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ($SkipFull -and [string]::IsNullOrWhiteSpace($Filter)) {
    throw "-SkipFull requires -Filter."
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$testProject = Join-Path $repoRoot "GuildSimulator.Tests\GuildSimulator.Tests.csproj"
$assetsFile = Join-Path $repoRoot "GuildSimulator.Tests\obj\project.assets.json"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "dotnet was not found on PATH. Install or enable the .NET 8 SDK."
}

function Invoke-DotNetCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Label,

        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    Write-Host ""
    Write-Host "==> $Label" -ForegroundColor Cyan
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Label failed (exit code: $LASTEXITCODE)."
    }
}

Push-Location $repoRoot
try {
    # Avoid lingering dotnet processes during short development cycles.
    $env:DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER = "1"
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"

    if ($Restore -or -not (Test-Path -LiteralPath $assetsFile)) {
        Invoke-DotNetCommand -Label "Restore test dependencies" -Arguments @(
            "restore",
            $testProject,
            "-m:$MaxCpuCount",
            "-nr:false",
            "--verbosity",
            "minimal"
        )
    }

    $testArguments = @(
        "test",
        $testProject,
        "--configuration",
        $Configuration,
        "--no-restore",
        "-m:$MaxCpuCount",
        "-nr:false",
        "--verbosity",
        "minimal"
    )

    if (-not [string]::IsNullOrWhiteSpace($Filter)) {
        Invoke-DotNetCommand -Label "Focused tests" -Arguments (
            $testArguments + @("--filter", $Filter)
        )

        if (-not $SkipFull) {
            Invoke-DotNetCommand -Label "Full test suite (no rebuild)" -Arguments (
                $testArguments + "--no-build"
            )
        }
    }
    else {
        Invoke-DotNetCommand -Label "Full test suite" -Arguments $testArguments
    }
}
finally {
    Pop-Location
}
