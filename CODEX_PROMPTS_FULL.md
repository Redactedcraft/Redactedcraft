# GPT-5.2 Codex Prompts (Tailored)

Use these prompts with the two zips:
- RedactedcraftCsharp.zip (your game)
- RedactedCraft_LorePack_FULL_v1.zip (this pack)

---

## Prompt 0 — Merge this lore pack into the repo (no code changes)
Extract RedactedCraft_LorePack_FULL_v1.zip at the repository root.
Confirm these folders now exist in the repo:
- RedactedCraftMonoGame/Defaults/Assets/data/lore/
- RedactedCraftMonoGame/Defaults/Assets/textures/blocks/

Do NOT edit any C# files in this prompt.

---

## Prompt 1 — Rename block display names + rename texture PNGs to match (no enum/id change)
Context:
- Texture lookup derives the png filename from BlockDef.Name, unless TextureName is set.
- Default textures are at: RedactedCraftMonoGame/Defaults/Assets/textures/blocks/

Goal:
- BlockId.CraftingTable name: "Crafting Table" -> "Artificer Bench"
- BlockId.Corestone name: "Corestone" -> "Nullrock"
Do NOT change BlockId enum names or values.

Also rename the texture files on disk to match the derived filenames:
- crafting_table.png -> artificer_bench.png
- bedrock.png -> nullrock.png
(These are files in RedactedCraftMonoGame/Defaults/Assets/textures/blocks/)

Edits:
A) In RedactedCraftMonoGame/Core/BlockRegistry.cs replace only the display name strings for those two blocks.
B) Rename/move the two pngs in Defaults/Assets/textures/blocks/ exactly as above.
Do NOT set TextureName for these blocks; leave it null so the name-based lookup works.

---

## Prompt 2 — Implement a minimal Lore Loader + UI (no worldgen yet)
Implement a small, isolated lore module that reads JSON from:
RedactedCraftMonoGame/Defaults/Assets/data/lore/
(which will be copied to Documents/RedactedCraft/Assets on first run)

Requirements:
1) LoreRegistry loads:
- inscriptions.json
- recipe_fragments.json
- loot_tables.json
- npc_dialogue_hearthward.json
- base_block_lore_overrides.json
- (optional) worldgen_overrides.json

2) Add two interactables (minimal):
- InscribedTablet (block or world object): stores inscription id string
- RecipeFragment (pickup item): stores fragment id string

3) Add a simple UI overlay:
- On interaction/pickup, show the text lines and store discovered ids in save data.

Do NOT change world generation in this prompt.

---

## Prompt 3 — Optional: enforce Nullrock at bottom layer (Y==0)
Implement the bottom layer rule described in:
Defaults/Assets/data/lore/worldgen_overrides.json

Minimal safe implementation (hardcoded for now):
- In both VoxelWorldGenerator.GenerateChunk(...) and VoxelWorldGenerator.Step(...):
  after computing block id, set:
    if (wy == 0) id = BlockIds.Corestone; // UI name is Nullrock

Do NOT change world size. Do NOT adjust terrain noise parameters.

---

## Prompt 4 — Later: hook structures/loot into worldgen
When ready (after bigger world work), integrate structures.json + loot tables into your structure spawner.
Keep deterministic seeding; do not hard-require structure placement.
