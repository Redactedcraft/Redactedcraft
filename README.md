# LatticeVeil

## Build System

The repository uses a single-file GUI builder:

### Build.ps1
- **Dark-themed WPF interface** with no console window
- **Double-click to run** or execute: `powershell -ExecutionPolicy Bypass -File Build.ps1`
- **All build features** in one interface

### Features
1. **DEV Build + Run (Launcher)** - Builds and starts LatticeVeil.exe
2. **DEV Build + Run (No Launcher)** - Builds and starts LatticeVeilGame.exe directly  
3. **RELEASE Publish + Package** - Creates publish output and ZIP package
4. **CLEANUP** - Safe cleanup with optional deep clean
5. **Open Dev Output** - Opens build output folder
6. **Open Release Output** - Opens .builder/releases folder
7. **Open Logs** - Opens .builder/logs folder

### Safety Features
- **No-hang execution** - All external processes have timeouts
- **Process cleanup** - Kills existing game instances before launch
- **Log-first approach** - All output logged, no live pipes
- **Safe cleanup** - Never deletes source code or assets

### Development Mode
- Sets `LATTICEVEIL_DEV_LOCAL=1` and `LATTICEVEIL_LOCAL_ASSETS=1`
- Uses local assets from `LatticeVeilMonoGame/Defaults/Assets`
- No GitHub asset downloads

### Release Mode
- Uses `dotnet publish` for release builds
- Creates ZIP packages in `.builder/releases/`
- Compatible with launcher-based asset updates

## Repository Structure

```
LatticeVeil_project/
├── Build.ps1                    # Single GUI builder (double-click to run)
├── LatticeVeilMonoGame/        # Game project
├── Tools/                       # Utility scripts
├── .builder/                    # Build outputs (gitignored)
│   ├── logs/                   # Build logs
│   ├── staging/                 # Temporary staging
│   └── releases/               # Release packages
└── .gitignore                  # Excludes build artifacts
```

## Getting Started

1. **Double-click `Build.ps1`** to open the GUI
2. **Select "DEV Build + Run (Launcher)"** for development
3. **Select "RELEASE Publish + Package"** for release builds

### Execution Policy
If PowerShell execution policy is restricted:
- Right-click `Build.ps1` → "Run with PowerShell"
- Or run: `powershell -ExecutionPolicy Bypass -File Build.ps1`

## Notes

- **No duplicate builder scripts** - Build.ps1 is the single entrypoint
- **Clean repository** - Only essential files committed
- **No "Minecraft" references** - Uses "launcher-based architecture" terminology
