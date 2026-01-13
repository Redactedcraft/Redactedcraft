# GPT-5.2 Codex Prompts — RedactedCraft Lore Pack Integration (v1)

You (Codex) will be given:
- the RedactedCraftMonoGame C# codebase (already uses `BlockId : byte`)
- this content pack folder (or zip)

**Goal:** integrate lore content without breaking existing worlds, and without requiring the player to engage with lore.

---

## Prompt A — Add New Blocks (minimal engine changes)

1) In `Core/BlockId.cs`, append new enum values starting at **20** (keep existing values unchanged).
   - Use the mapping in `Assets/data/lore/block_additions.json`.

2) In `Core/BlockIds.cs`, add matching `public const byte` values for each new block.

3) In `Core/BlockRegistry.cs`, register new `BlockDef`s for the blocks list:
   - `id` → `BlockId.<enumName>`
   - `name` → `displayName`
   - `solid`, `transparent`, `hardness`, `atlasIndex`
   - Set `textureName` to `textureName` from JSON (snake_case, no extension).
   - Leave `hasCustomModel = false` for all of these (no special rendering yet).

4) Textures:
   - Copy `Assets/textures/blocks/*.png` from this pack into `Documents/RedactedCraft/Assets/textures/blocks/`

5) Rebuild atlas:
   - On next run, `CubeNetAtlas` will regenerate the atlas from these cube-net textures.

---

## Prompt B — Data-Driven Lore Registry (no gameplay lock-in)

Load these files from:
`Path.Combine(Paths.AssetsDir, "data", "lore", "<file>")`

- `inscriptions.json`
- `recipe_fragments.json`
- `loot_tables.json`  (item strings are BlockId enum names now)
- `structures.json`
- `npc_dialogue_hearthward.json`
- `recipes_structured.json`
- `lore_story.md` (optional for a “Codex” in-game menu later)
- `base_block_lore_overrides.json` (optional: add lore + display-name override for existing blocks; Corestone→Nullrock)

Implement `LoreRegistry` (or `ContentRegistry`) with APIs:
- `GetRandomInscriptions(poolId, seed)`
- `GetFragmentsByTopic(topic, tier)`
- `RollLoot(tableId, tier, seed)` → outputs list of `{ BlockId id, int count }`
- `GetDialogue(faction, role, context, seed)`
- `GetStructuredRecipe(recipeId)` (for future crafting UI)


### Nullrock (Corestone) note
- In the shipped codebase, the unbreakable bottom block is `BlockId.Corestone` (byte 15).
- This pack treats its **display name** as **Nullrock** via `base_block_lore_overrides.json`.
- Do **not** rename enum values yet unless the user explicitly asks; just override UI strings.
---

### World bottom layer requirement (Nullrock)
- The bottom-most world layer (global Y == 0) should always be **Nullrock**.
- In code, keep using the existing enum: `BlockId.Corestone` / `BlockIds.Corestone` (byte 15). The lore/UI name is **Nullrock**.
- Implement this as a minimal, safe rule:
  - In `Core/VoxelWorldGenerator.GenerateChunk(...)`, after computing `id`, set `if (wy == 0) id = BlockIds.Corestone;`
  - In `Core/VoxelWorldGenerator.Step(...)` generation loop, after computing `id`, set `if (wy == 0) id = BlockIds.Corestone;`
- This does not depend on world size. If the user later increases world height, keep applying it only at Y==0.
- (Optional) Read `worldgen_overrides.json` to make this configurable later.



## Prompt C — Worldgen Hooks (safe even before “big world” is finished)

**Do not** change the base terrain generator in a disruptive way.

Add *optional* structure spawning that can be toggled:
- A setting flag (e.g., `EnableLoreStructures` default OFF for now)
- Or a world meta flag saved per world

When enabled:
- Place very small “props” first:
  - Graves: 1–3 blocks + an inscription marker
  - Tablets: 1–2 blocks in ruined corners

Keep everything deterministic per chunk seed.

---

## Prompt D — Inscription Interaction (minimal UI)

When the player looks at an **InscribedTablet** or **Gravestone** within range and presses Interact:
- show a small overlay panel with 2–6 lines of text
- allow closing with Escape / Interact again

Store “which inscriptions were already seen” in the player profile optionally.

---

## Prompt E — Loot (optional now, future-ready)

If you implement loot containers:
- Graves can drop materials and 0–1 recipe fragment item.
- Use `loot_tables.json`.

Fragments can be represented for now as:
- `FragmentScrap` blocks/items with metadata later
- or a simple “journal unlock” event without an inventory item

Either is acceptable; do not block progression on it.

---

## Prompt F — Waygates & World Gates (leave stubs)

Create block IDs now (already provided), but implement **only stubs**:
- Placeable blocks exist
- Interacting shows “Dormant — requires Keystone” etc

Later, you can implement:
- stable always-on portals
- fast travel networks

---

### Non-negotiables
- No “hard failures” for unstable recipes: always produce an item, plus side effects.
- No lore is required to play: every system should be optional.

