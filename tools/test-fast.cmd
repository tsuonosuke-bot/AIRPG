@echo off
setlocal

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0test-fast.ps1" %*
exit /b %ERRORLEVEL%
