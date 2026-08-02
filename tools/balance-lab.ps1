param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$BalanceArgs
)

$ErrorActionPreference = "Stop"
$env:DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER = "1"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")

Push-Location $repoRoot
try {
    & dotnet build GuildSimulator.Balance\GuildSimulator.Balance.csproj --disable-build-servers -m:1 -nr:false -v:minimal
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & dotnet run --project GuildSimulator.Balance --no-build -- @BalanceArgs
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
