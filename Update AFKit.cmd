@echo off
rem Double-click to rebuild and update the installed AFKit with no questions asked (opens it when done).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" -Auto
