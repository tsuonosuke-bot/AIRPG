@echo off
setlocal

set "MODE=%~1"
if "%MODE%"=="" set "MODE=export"
if /I not "%MODE%"=="export" if /I not "%MODE%"=="check" if /I not "%MODE%"=="diff" if /I not "%MODE%"=="import" if /I not "%MODE%"=="migrate" (
  echo Usage: Run-MasterDataTool.cmd export^|check^|diff^|import^|migrate
  exit /b 2
)

set "TOOL_DIR=%~dp0"
set "REPO_ROOT=%TOOL_DIR%..\.."
set "BUNDLED_NODE=%USERPROFILE%\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe"
set "EXPORT_MARKER=%REPO_ROOT%\outputs\master-data-editor\export.ok"

if exist "%BUNDLED_NODE%" (
  set "NODE_EXE=%BUNDLED_NODE%"
) else (
  where node >nul 2>nul
  if errorlevel 1 (
    echo Node.js was not found. Load the Codex workspace dependencies first.
    exit /b 1
  )
  set "NODE_EXE=node"
)

if /I "%MODE%"=="export" if exist "%EXPORT_MARKER%" del /q "%EXPORT_MARKER%"
if /I "%MODE%"=="migrate" if exist "%EXPORT_MARKER%" del /q "%EXPORT_MARKER%"

"%NODE_EXE%" "%TOOL_DIR%master-data-tool.mjs" "%MODE%"
set "TOOL_EXIT=%ERRORLEVEL%"

if /I "%MODE%"=="export" if exist "%EXPORT_MARKER%" exit /b 0
if /I "%MODE%"=="migrate" if exist "%EXPORT_MARKER%" exit /b 0
exit /b %TOOL_EXIT%
