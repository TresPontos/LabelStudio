@echo off
rem Build when source changes, then launch the latest validated LabelStudio app.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\run-latest.ps1"
set "RESULT=%ERRORLEVEL%"
if not "%RESULT%"=="0" pause
exit /b %RESULT%
