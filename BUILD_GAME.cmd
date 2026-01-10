@echo off
setlocal enabledelayedexpansion
cd /d "%~dp0"

REM ============================================================
REM BUILD_GAME.cmd
REM Release build output:
REM   - Builds single-file Release EXE via Tools\BuildRunner
REM   - This is an EOS-only build.
REM ============================================================

set "ROOT=%CD%"
set "OUT=%ROOT%\Builds"

REM --- BuildRunner project/exe (existing tooling in this repo) ---
set "RUNNER_PROJ=%ROOT%\Tools\BuildRunner\BuildRunner.csproj"
set "RUNNER_EXE=%ROOT%\Tools\BuildRunner\bin\Release\net8.0-windows\BuildRunner.exe"

REM --- Ensure output folder exists (do NOT wipe it) ---
if not exist "%OUT%" mkdir "%OUT%" >nul 2>&1

REM --- Clean ONLY the previous EXE (do not clear the whole folder) ---
if exist "%OUT%\RedactedCraft.exe" del /q "%OUT%\RedactedCraft.exe" >nul 2>&1

echo === Building BuildRunner (Release) ===
dotnet build "%RUNNER_PROJ%" -c Release
if errorlevel 1 goto build_failed

if not exist "%RUNNER_EXE%" (
  echo ERROR: BuildRunner.exe not found at: "%RUNNER_EXE%"
  goto build_failed
)

echo === Running BuildRunner (creates Builds\RedactedCraft.exe) ===
"%RUNNER_EXE%" "%ROOT%"
if errorlevel 1 goto build_failed

REM ------------------------------------------------------------
REM Locate final EXE output (prefer Builds\RedactedCraft.exe)
REM ------------------------------------------------------------
set "FINAL_EXE=%OUT%\RedactedCraft.exe"
if not exist "%FINAL_EXE%" (
  for /r "%OUT%" %%F in (*.exe) do (
    set "FINAL_EXE=%%F"
    goto found_exe
  )
)
:found_exe
if not exist "%FINAL_EXE%" (
  echo ERROR: Could not locate final EXE under "%OUT%"
  goto build_failed
)

for %%I in ("%FINAL_EXE%") do set "FINAL_EXE_DIR=%%~dpI"
echo Final EXE: "%FINAL_EXE%"

echo.
echo === Build completed successfully ===
exit /b 0

:build_failed
echo Build failed.
if exist "%OUT%" (
  echo See output folder: "%OUT%"
  echo Build failed. > "%OUT%\build_failed.log"
)
exit /b 1
