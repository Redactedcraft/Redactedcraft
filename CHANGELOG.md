# LatticeVeil Changelog

## v1.3.0 - "Gatekeeper's Fix" - 2026-02-21

### 🚀 Major Online System Overhaul
- **Fixed EOS Secret Request**: Resolved invalid gate ticket errors by implementing proper JWT validation and database storage
- **Enhanced Gate Ticket System**: Created robust ticket minting and validation with proper expiration handling
- **Improved Build Configuration**: Fixed release build detection to use proper build configuration instead of file existence checks
- **Added Supabase Functions**: 
  - `eos-secret`: Secure EOS client secret retrieval with gate ticket validation
  - `game-hashes-get`: Centralized hash management for both dev and release channels
  - `online-ticket`: Enhanced ticket creation with database persistence
- **Fixed Database Schema**: Updated `online_tickets` table with proper UUID generation and NOT NULL constraints
- **Enhanced Launcher Integration**: Improved launcher-to-game communication with proper environment variable handling

### 🔧 Core Improvements
- **Asset System**: Enhanced asset pack installation and validation
- **Build Verification**: Improved official build verification with proper hash checking
- **Error Handling**: Better error reporting and user feedback throughout the application
- **Configuration Management**: Centralized Supabase configuration with proper environment variable handling

### 🎨 Asset Updates
- **Added New Texture**: Added `air.png` texture for improved block rendering
- **New Background Images**: Added 5 new multiplayer background images:
  - `InviteFriends_bg.png` for friend invitation screen
  - `JoinByCode_bg.png` for code joining screen
  - `Kicked_background.png` for kick/disconnect screen
  - `MultiplayerHost_bg.png` for multiplayer hosting screen
  - `ShareJoinCode_bg.png` for code sharing screen
- **Asset Structure Cleanup**: Removed duplicate textures and maintained proper folder structure
- **Enhanced Compatibility**: Ensured proper launcher asset loading paths

### 🛡️ Security & Reliability
- **JWT Token Validation**: Proper token verification with expiration checking
- **Database Integrity**: Enhanced data validation and constraint handling
- **Network Resilience**: Improved retry logic and connection handling
- **Build Authentication**: Stronger verification of official builds

### 🐛 Bug Fixes
- **Fixed Release Build Detection**: Release builds now correctly use release hash lists instead of dev
- **Resolved Database Errors**: Fixed null constraint violations in ticket storage
- **Corrected CORS Issues**: Proper cross-origin handling for web functions
- **Fixed Asset Loading**: Improved asset pack discovery and installation

### 📋 Technical Details
- **Supabase Integration**: Complete overhaul of authentication and data persistence
- **RPC Functions**: Added direct SQL functions to bypass PostgREST cache issues
- **Environment Configuration**: Proper handling of dev/release build modes
- **Hash Validation**: Enhanced executable hash verification and allowlist checking

---

## Previous Versions
### v1.2.0 - "Foundation Update" - 2026-02-18
- Initial EOS integration framework
- Basic online authentication system
- Launcher protocol implementation

### v1.1.0 - "Alpha Release" - 2026-02-16
- Core multiplayer functionality
- Basic world hosting and joining
- Initial asset system

### v1.0.0 - "Pre-Alpha" - 2026-02-10
- Initial game release
- Basic singleplayer functionality
- Foundation UI systems
