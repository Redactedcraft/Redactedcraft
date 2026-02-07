# LatticeVeil Project

A voxel-based survival game with advanced world generation, multiplayer support, and rich lore.

## 🎮 **About LatticeVeil**

LatticeVeil is a sophisticated voxel game featuring:
- **Advanced World Generation** - Biomes, terrain smoothing, and procedural generation
- **Survival Mode** - Health, hunger, combat, and environmental hazards
- **Multiplayer Support** - LAN and EOS-based multiplayer
- **Rich Lore** - Deep world-building and story elements

### **Project Management (Local Only)**
- **🧪 Experimental Section**: `Experimental/` - Local development tools (NOT pushed to GitHub)
  - **GitHub Integration**: `Experimental/GitHub/` - Repository management GUI
  - **Development Tools**: Various utilities for development
  - **Testing Tools**: Experimental testing framework

> **Note**: The `Experimental/` directory contains project management tools only and is excluded from Git. See `PROJECT_STRUCTURE.md` for complete structure.

## 🎮 **Current Features**

### **✅ Implemented (V7.0.0 "Quantum Genesis")**
- Advanced voxel world generation with biomes
- Chunk loading optimization with priority queuing
- Desert biome with terrain smoothing
- Professional world builder with SharpNoise integration
- Complete plugin architecture (5 integrated plugins)
- Comprehensive lore system (25KB document)
- GUI Designer for UI layout

### **🚧 In Development**
- Survival mechanics (health, hunger, combat)
- Crafting system with UI interface
- Tool durability and effectiveness
- Equipment and armor system
- Mob spawning and AI
- Progression system (Attunement)

## 📚 **Documentation**

- **📋 [Development Log](DEVELOPMENT_LOG.md)** - Complete development history
- **📋 [Project Structure](PROJECT_STRUCTURE.md)** - Complete file organization
- **📋 [Documentation Index](docs/README.md)** - Documentation overview
- **🔧 [Master Development Plan](docs/implementation/MASTER_DEVELOPMENT_PLAN.md)** - Complete roadmap
- **📖 [Game Lore](docs/lore/LORE.md)** - World-building and story elements

## 🔧 **Build System**

### **Quick Build**
```powershell
# Launch build GUI
.\BuildGUI.ps1

# Or use command line
.\Tools\build_and_release.ps1
```

### **Build Components**
- **Build GUI**: `BuildGUI.ps1` - Visual build interface
- **Build Tool**: `build/Builder/` - C# build application
- **Scripts**: `Tools/` - PowerShell automation scripts
- **Assets**: `assets/` - Images and layout configurations

## 🎯 **Development Status**

**Version**: V7.0.0 "Quantum Genesis"  
**Framework**: .NET 8.0 Windows  
**Rendering**: MonoGame DesktopGL (OpenGL)  
**Status**: Release Ready - Foundation Complete, Survival Features In Progress

### **Next Priority**
1. **Phase 1**: Health, Hunger, Combat systems (2-3 weeks)
2. **Phase 2**: Crafting UI and Tool system (2-3 weeks)  
3. **Phase 3**: Food resources and world enhancements (3-4 weeks)
4. **Phase 4**: Advanced features and polish (4-6 weeks)

See [MASTER_DEVELOPMENT_PLAN.md](docs/implementation/MASTER_DEVELOPMENT_PLAN.md) for complete roadmap.

## 🌍 **Game World**

### **Lore Integration**
- **Complete Canon**: 25KB lore document with 4 sections
- **5 Living Factions**: Continuists, Veilkeepers, Hearthward, Ascendants, Echo Faith
- **Rule of Three**: Frame/Conduit/Limiter fundamental laws
- **Echo System**: Pressure-based completion mechanics

### **World Generation**
- **9 Biome Types**: Natural, organic generation
- **Advanced Terrain**: Ridged noise and domain warping
- **Structure System**: 7 structure types with loot
- **Resource Distribution**: Balanced ore and material placement

## 📊 **Technical Specifications**

- **Target Framework**: net8.0-windows
- **Platform**: x64
- **Rendering**: OpenGL (Vulkan removed)
- **Build Configuration**: Release with self-contained publishing
- **Output**: Single executable (LatticeVeilMonoGame.exe)
- **Plugin Architecture**: Extensible modular system

## 🔌 **Plugin Ecosystem**

- **SharpNoise Plugin**: Professional noise generation (0.12.1.1)
- **Advanced Terrain Plugin**: Ridged noise and domain warping
- **Biome Diversity Plugin**: 8 biome types with natural transitions
- **Performance Plugin**: Hardware-adaptive optimization
- **Safety Plugin**: Multiple safety layers for world stability

## � **Release Assets**

- **Executable**: Self-contained single executable
- **Dependencies**: All plugins included (no external dependencies)
- **Platform**: Windows x64 only
- **Size**: Optimized for distribution

---

**Last Updated**: 2026-02-03  
**Version**: V7.0.0 "Quantum Genesis"  
**Next Milestone**: Survival Mode Implementation

## 🤝 **Contributing**

1. Fork the repository
2. Create a feature branch
3. Implement your changes
4. Test thoroughly
5. Submit a pull request

## 📄 **License**

This project is licensed under the terms specified in the LICENSE file.

## 🔗 **Links**

- **Documentation**: [docs/README.md](docs/README.md)
- **Implementation Plans**: [docs/implementation/SURVIVAL_PLANS.md](docs/implementation/SURVIVAL_PLANS.md)
- **Game Lore**: [docs/lore/LORE_COMPANION.md](docs/lore/LORE_COMPANION.md)
- **Project Organization**: [docs/project/PROJECT_ORGANIZATION.md](docs/project/PROJECT_ORGANIZATION.md)

---

**Version**: V7.0.0 "Quantum Genesis"  
**Status**: Active Development  
**Last Updated**: 2026-01-31
