# LatticeVeil Build & Release Tools

This directory contains essential build and release tools for the LatticeVeil project.

## 🛠️ **Available Tools**

### **🏗️ Main Build & Release**
- **`Build.ps1`** - Main build script (44KB, comprehensive)
- **`build_and_release.ps1`** - Unified build and release automation
- **`create_release.ps1`** - Create release packages
- **`create_github_release.ps1`** - GitHub release automation
- **`verify_release.ps1`** - Verify release integrity

### **🧹 Cleanup Utilities**
- **`cleanup.ps1`** - **NEW** Unified cleanup with multiple levels
- **`deep_clean.ps1`** - Complete project cleanup before git push

### **🔄 Git Operations**
- **`push_all_repos.ps1`** - Multi-repository push (also in Experimental/Development)

## 📋 **Usage Guide**

### **Building the Project**
```powershell
# Main build
.\Tools\Build.ps1

# Unified build and release
.\Tools\build_and_release.ps1 -Action build

# Create release
.\Tools\build_and_release.ps1 -Action release -Version "1.0.0"

# GitHub release
.\Tools\build_and_release.ps1 -Action github-release -Version "1.0.0"

# Verify release
.\Tools\build_and_release.ps1 -Action verify -Version "1.0.0"
```

### **Cleanup Operations**
```powershell
# Light cleanup (temp files only)
.\Tools\cleanup.ps1 -Level light

# Medium cleanup (build artifacts + temp files)
.\Tools\cleanup.ps1 -Level medium

# Deep cleanup (before git push)
.\Tools\cleanup.ps1 -Level deep

# Verbose cleanup
.\Tools\cleanup.ps1 -Level medium -Verbose

# Force cleanup (no confirmation)
.\Tools\cleanup.ps1 -Level deep -Force
```

### **Git Operations**
```powershell
# Push all repositories
.\Tools\push_all_repos.ps1
```

## ⚠️ **Important Notes**

- **Backup First**: Always backup before running cleanup scripts
- **Test Environment**: Test release scripts in safe environment
- **Git Safety**: Deep cleanup removes .md files (except .gitignore)
- **Dependencies**: Some scripts require PowerShell 5.1+

## 🔄 **Recent Changes**

### **✅ New Consolidated Tools**
- **`cleanup.ps1`** - Replaces 3 separate cleanup scripts
- **`build_and_release.ps1`** - Unified build/release automation

### **📦 Archived Tools**
Moved to `Experimental/Archive/`:
- `simple_cleanup.ps1` → Replaced by `cleanup.ps1 -Level medium`
- `temp_cleanup.ps1` → Replaced by `cleanup.ps1 -Level light`
- `final_cleanup.ps1` → Replaced by `cleanup.ps1 -Level deep`

## 📝 **Cleanup Script Levels**

1. **`-Level light`** - Remove temp files (*.tmp, *.cache, etc.)
2. **`-Level medium`** - Remove build artifacts + temp files
3. **`-Level deep`** - Complete cleanup (aggressive, before git push)
4. **`-Level all`** - All cleanup levels combined

Choose based on your needs - start with lightest and escalate if needed.
