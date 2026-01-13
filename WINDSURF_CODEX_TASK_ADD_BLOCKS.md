# Windsurf Codex Task — Integrate LorePack v1.1d (lore + textures) (Nullblock + Artificer’s Workbench)

Paste this entire file into GPT-5.2 Codex inside Windsurf, then run it against your repo.

You are working inside the **RedactedCraft MonoGame** repo. Apply the LorePack updates and ensure **Nullblock** and **ArtificersWorkbench** are fully integrated as playable blocks/stations.

Generated: 2026-01-13T00:13:35.256791Z

## Non-negotiables
- Do **not** rename existing enums unless you also add backward-compat aliasing for saves.
- Keep block IDs stable.
- Keep textures pixel-perfect (no filtering); treat 16×16 tiles as sacred.
- Prefer simple, explicit code changes over clever abstractions.

---

## 1) Verify existing implementation (search-first)
Search the codebase for:
- `Nullblock` and `Corestone`
- `ArtificersWorkbench`, `Artificer`, `artificer_bench`, `Workbench`

### If `Corestone` exists but `Nullblock` does not
- Add `Nullblock` as the canonical enum/value.
- Add compatibility so older save data that mentions `Corestone` maps to `Nullblock`.
- Update any worldgen “bottom layer” references to use `Nullblock`.

### If `Nullblock` exists already
- Ensure its display name is **Nullrock** (UI/lore), and that worldgen forces it at `y==0`.

---

## 2) Import the data pack files
Copy/merge these files from the lore pack into:
`RedactedCraftMonoGame/Defaults/Assets/data/lore/`

- worldgen_overrides.json (now uses Nullblock)
- base_block_lore_overrides.json (adds ArtificersWorkbench lore + Nullblock canonical lore)
- block_additions.json (other lorepack blocks only — **do not add** Nullblock/ArtificersWorkbench here)
- recipes_structured.json (adds station + moves high-tier gatecraft to the workbench)
- packs.json / loot_tables.json / inscriptions.json / recipe_fragments.json / lore_story.md
- block_id_map.md (adds row for id 65)

If the project has a runtime asset build step, ensure these JSON/MD files are included in the content pipeline.

---

## 3) Textures (pixel art, 48×32 sheets)
This repo already contains `artificer_bench` and `nullrock` textures/blocks.
**Do not add or overwrite those two assets.**

If any optional new textures are provided under:
`RedactedCraftMonoGame/Defaults/Assets/textures/blocks/lorepack_generated/`

Required behavior:
- Each PNG is **48×32** and contains **six 16×16 faces** (3 columns × 2 rows).
- Load with **nearest-neighbor** sampling (no smoothing).
- Ensure they are discoverable by your texture/atlas loader under their `textureName`:
  - `runestone`, `veinstone`, `veilglass`, `resonance_core`, `waybound_frame`, `transit_regulator`

If your engine expects textures in a different folder, move them and update any manifest accordingly.

---

## 4) Ensure Artificer’s Workbench is recognized as a crafting station
The block already exists in the base game.
- Verify it is placeable and uses the existing `artificer_bench` texture.
- Ensure the crafting system recognizes it as `station.artificers_workbench` (or map that station id to the existing block).

---

## 5) Validate IDs and Atlas Indices
The data pack assumes:
- `ArtificersWorkbench` is `id = 65` and `atlasIndex = 65`.
- The bottom layer is `Nullblock` (byte id commonly 15 if it replaced Corestone).

If your code already assigned different IDs:
- Update the JSON to match the real IDs **(do not change code IDs unless you also migrate saves)**.
- Update `block_id_map.md` accordingly.

---

## 6) Quick test checklist
- New world: bottom layer at y==0 is **Nullblock** and displays as **Nullrock**.
- Crafting:
  - You can craft **Artificer’s Workbench**.
  - Gatecraft recipes fail unless you are using the Workbench.
- Textures render crisp; no blur; correct faces.
- Old saves containing `Corestone` still load (mapped to `Nullblock`).

Return a brief summary of what you changed and where.
---

## 7) ID/Atlas correctness (must not break existing worlds)

### How to allocate NEW block IDs safely (unique byte IDs)
Your project uses a **byte-based block id system**. When adding any new blocks from `block_additions.json`, you MUST:
1. Locate the authoritative registry of block ids in code (commonly one of):
   - `enum BlockId : byte`
   - a `BlockRegistry` / `BlockDatabase` mapping `byte -> BlockDef`
   - a texture atlas index table that assumes `atlasIndex == byteId`
2. Collect the set of **already-used byte ids** from that registry.
3. For each block in `block_additions.json`:
   - If its `byteId` is free: keep it.
   - If its `byteId` collides: assign the **next free** byte id.
4. If you change any ids:
   - Update the JSON files that reference the affected ids (`recipes_structured.json`, loot, etc.)
   - Update `block_id_map.md` so the pack stays truthful.
5. Add a startup assertion/log that detects duplicate ids and fails fast (prevents silent corruption).

### Atlas index rule
If your engine assumes `atlasIndex == byteId`:
- After any id remap, also set `atlasIndex` to match the final `byteId`.
If your engine uses a separate atlas mapping:
- Update the atlas mapping so the texture for each new block points to the correct sheet/tiles.

### Save compatibility / migrations
If you ever rename or replace a block enum:
- Implement a **load-time alias mapping** (e.g., `"Corestone" -> "Nullblock"`).
- Never delete an enum name without a migration path.

---

## 8) Nullblock worldgen + unbreakable survival rule (hard requirement)

### Worldgen: force Nullblock at y==0
Implement this in the actual world generator (not just lore JSON):
- In the chunk generation step, after terrain/ores are placed, overwrite every block at `y == 0` to `Nullblock`.
- Ensure this runs for all world types and biomes.
- If there is bedrock/cap logic, Nullblock should take precedence.

### Unbreakable in Survival
Nullblock must be **unbreakable** in Survival mode:
- In your block-breaking logic (server-authoritative if applicable):
  - If `blockId == Nullblock` AND `gameMode == Survival`, cancel the break and do not drop items.
- Also make it immune to:
  - explosions / damage systems
  - mining-progress systems (never reaches break threshold)
- Allow breaking only in Creative/Dev mode if your game supports it.

Add a small unit/integration test or a debug command check:
- In Survival: mining Nullblock never removes it.
- In Creative/Dev: mining can remove it (optional, but recommended for builders).

---

## 9) Artificer’s Workbench station linkage (existing block, no asset overwrite)
The lore pack assumes the block already exists **in code/assets**.
You must only ensure that crafting can require it as a station:

- If stations are keyed by **block enum**:
  - Map `station.artificers_workbench` to the existing `ArtificersWorkbench` block.
- If stations are keyed by **string id**:
  - Ensure the string id for the existing station is exactly `station.artificers_workbench`, or add an alias.

Do NOT overwrite `artificer_bench` or `nullrock` textures from this pack.
---

## 10) Use the provided textures (and keep the repo tidy)

This pack includes new block texture sheets for custom blocks and they MUST be used.
Textures are located at:
`RedactedCraftMonoGame/Defaults/Assets/textures/blocks/`

Required behavior:
- Ensure these exact files exist in that folder (do not put them in a special subfolder):
  - `runestone.png`
  - `veinstone.png`
  - `veilglass.png`
  - `resonance_core.png`
  - `waybound_frame.png`
  - `transit_regulator.png`

- Configure your texture/atlas loader so these names resolve for the corresponding blocks.
- If there is a legacy folder such as `textures/blocks/lorepack_generated/`, remove it or leave it empty.
- Do NOT overwrite existing in-game textures for:
  - `artificer_bench`
  - `nullrock`

Sampling:
- These are 48×32 sheets (six 16×16 faces). Enforce nearest-neighbor sampling (no blur).
