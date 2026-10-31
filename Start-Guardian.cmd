@echo off
title Claude Code Guardian
cd /d "%~dp0"
if exist "%~dp0publish\ClaudeVpnGuard.exe" (
  start "" "%~dp0publish\ClaudeVpnGuard.exe"
  exit /b 0
)
if exist "%~dp0ClaudeVpnGuard\bin\Release\net8.0-windows\win-x64\ClaudeVpnGuard.exe" (
  start "" "%~dp0ClaudeVpnGuard\bin\Release\net8.0-windows\win-x64\ClaudeVpnGuard.exe"
  exit /b 0
)
echo ClaudeVpnGuard.exe not found. Build/publish first.
pause
exit /b 1
