@echo off
setlocal
title GitHub PR Review Submission

set "SCRIPT_DIR=%~dp0"
set "PAYLOAD=%SCRIPT_DIR%review.json"

if not exist "%PAYLOAD%" (
  echo ERROR: review.json was not found beside this launcher.
  echo Put review.json in the same folder as submit-review.cmd.
  echo.
  pause
  exit /b 1
)

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%submit-review.ps1" -ReviewFile "%PAYLOAD%"
set "EXIT_CODE=%ERRORLEVEL%"

echo.
if not "%EXIT_CODE%"=="0" (
  echo Review submission did not complete. Exit code: %EXIT_CODE%
) else (
  echo Review submission completed. Check the URL above.
)
echo.
pause
exit /b %EXIT_CODE%
