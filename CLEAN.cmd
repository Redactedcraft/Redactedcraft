@echo off
setlocal
cd /d "%~dp0"

echo === Cleaning Project Artifacts ===

REM --- Remove top-level temp folders ---
if exist "artifacts" (
    echo Deleting artifacts...
    rmdir /s /q "artifacts"
)
if exist "publish" (
    echo Deleting publish...
    rmdir /s /q "publish"
)
if exist "out" (
    echo Deleting out...
    rmdir /s /q "out"
)

REM --- Remove build output ---
if exist "Builds" (
    echo Cleaning Builds folder...
    if exist "Builds\RedactedCraft.exe" del /q "Builds\RedactedCraft.exe"
    if exist "Builds\_temp_publish" rmdir /s /q "Builds\_temp_publish"
    if exist "Builds\build_failed.log" del /q "Builds\build_failed.log"
)

REM --- Remove all bin/obj folders recursively ---
echo Deleting all bin and obj folders...
for /d /r . %%d in (bin, obj) do (
    if exist "%%d" (
        echo   - %%d
        rmdir /s /q "%%d"
    )
)

echo.
echo === Cleanup Complete ===

