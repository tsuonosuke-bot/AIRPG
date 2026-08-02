@echo off
setlocal
set "DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER=1"
set "DOTNET_CLI_TELEMETRY_OPTOUT=1"
pushd "%~dp0.."
dotnet build GuildSimulator.Balance\GuildSimulator.Balance.csproj --disable-build-servers -m:1 -nr:false -v:minimal
if errorlevel 1 (
  set "TOOL_EXIT=%ERRORLEVEL%"
  popd
  exit /b %TOOL_EXIT%
)
dotnet run --project GuildSimulator.Balance --no-build -- %*
set "TOOL_EXIT=%ERRORLEVEL%"
popd
exit /b %TOOL_EXIT%
