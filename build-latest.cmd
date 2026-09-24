@echo off
rem Build, test, publish, and refresh artifacts\latest with the newest LabelStudio.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\build-latest.ps1" %*
exit /b %ERRORLEVEL%