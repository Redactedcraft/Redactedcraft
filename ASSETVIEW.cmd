@echo off
setlocal
cd /d "%~dp0"

echo Building (Release)...
dotnet build "RedactedCraftMonoGame\RedactedCraftMonoGame.csproj" -c Release
if errorlevel 1 echo Build returned a non-zero exit code. Continuing to asset viewer anyway.

echo Launching asset viewer with local development assets...
set REDACTEDCRAFT_LOCAL_ASSETS=1
dotnet run --project "RedactedCraftMonoGame\RedactedCraftMonoGame.csproj" -c Release -- --assetview

endlocal
