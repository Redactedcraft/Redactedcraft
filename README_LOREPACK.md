# RedactedCraft Artificer Update (FULL) v6.0

This zip is designed to be extracted at the **repo root** of your project.
It will add lore data files and placeholder textures under:

- RedactedCraftMonoGame/Defaults/Assets/data/lore/
- RedactedCraftMonoGame/Defaults/Assets/textures/blocks/

It does NOT modify any C# code.

## Included lore systems
- Inscriptions (faction-tagged pools)
- Recipe fragments (safe vs unstable)
- Loot tables (graves/ruins/shrines/hubs/portal sites/veil seals/hearthholds)
- Structures + inscription pools
- Hearthward Covenant NPC dialogue
- Base block lore overrides (Corestone → Nullrock name/lore; ore + bench lore)
- Worldgen override spec: force Nullrock (Corestone) at Y==0 (bottom layer)

## Notes about Nullrock
- Your current enum name stays `Corestone` (byte 15).
- Lore/UI display name is **Nullrock**.
- This pack includes `worldgen_overrides.json` describing the bottom layer rule.

## v6.0 – The Artificer Update (Consolidated)
- Consolidated all lore and assets to match Game Version V6.
- Includes The Continuist Papers addendum.
- Final assets are now sourced from `RedactedCraftMonoGame/Defaults/Assets/`.

