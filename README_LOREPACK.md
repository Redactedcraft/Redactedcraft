# The Continuist Papers - Lore Pack v1.1d

This pack is designed to be extracted at the repo root.
It installs lore data and block textures under:

- RedactedCraftMonoGame/Defaults/Assets/data/lore/
- RedactedCraftMonoGame/Defaults/Assets/textures/blocks/

It does not modify any C# code.

## Lore focus
- The Continuists and their rulework
- The Veil and its thin places
- Nullrock/Nullblock as the world boundary
- Waygates, resonance, and regulators

## What is included
- Inscriptions (faction-tagged pools)
- Recipe fragments (safe vs unstable)
- Loot tables (graves, ruins, shrines, hubs, portal sites, veil seals, hearthholds)
- Structures + inscription pools
- Hearthward Covenant NPC dialogue
- Base block lore overrides (Corestone -> Nullrock name/lore; ore + bench lore)
- Worldgen override spec: force Nullblock at y==0
- Pack metadata (packs.json, schemas, story)

## New texture sheets (cube-net, 3x2)
- runestone.png
- veinstone.png
- veilglass.png
- resonance_core.png
- waybound_frame.png
- transit_regulator.png

## Engine integration notes
- Block IDs 20-64 are reserved for lore pack blocks.
- Nullblock is byte 15 (alias of Nullrock/Corestone).
- Nullblock is enforced at y==0 in worldgen.
- Nullblock is unbreakable in Survival.
- Artificer's Workbench is the existing ArtificerBench block.

## Files
- data/lore: lore JSON/MD
- textures/blocks: cube-net PNGs
- block_additions.json: new block list
- block_id_map.md: ID map

Generated: 2026-01-12T02:09:32.276099Z
Update: v1.1d includes guidance for ID safety and survival rules.
