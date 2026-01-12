---
title: "Restoring Order: EOS Fixes & Repo Cleanup"
date: 2026-01-12
categories: [update, engineering]
---

We've just pushed a major update to the **RedactedCraft** repository infrastructure and game configuration.

### 🔧 EOS Service Update
The game has been updated to strictly use our new Render-hosted EOS configuration service (`eos-service.onrender.com`). This ensures smoother multiplayer connectivity and easier management of game credentials.

### 🛡️ Robust Connectivity
We've implemented a **credential fallback system**. If the remote configuration service hangs or times out (as free tiers often do!), the game will now automatically seamlessly fall back to hardcoded internal credentials. This means: **EOS will essentially always work.**

### 🧹 Repository Cleanup
We've scrubbed the repository of sensitive SDK tools to follow best security practices while keeping the runtime DLLs necessary for the game to launch immediately.

### 📦 Easier Access
You can now find the **latest playable executable** (`RedactedCraft.exe`) and the **full source code archive** (`RedactedcraftCsharp.zip`) directly in the root of the repository. No more digging through build folders!

Check out the latest files on the [GitHub Repository](https://github.com/Redactedcraft/Redactedcraft).
