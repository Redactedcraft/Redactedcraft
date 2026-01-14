# Implementation Notes: Nullblock + Safe Byte IDs (LorePack v1.1c)

## A) Nullblock as the final layer (worldgen)
- Force `Nullblock` at **y == 0** everywhere.
- Apply after terrain/ore placement so it cannot be replaced.
- If chunks can be regenerated/retrogen, apply during those passes too.

## B) Nullblock unbreakable in Survival
- Cancel mining/break attempts server-side when in Survival.
- No drops, no damage progression.
- Explosion immunity recommended.
- Optional: allow break in Creative/Dev.

## C) Byte ID safety when adding new blocks
When adding any new blocks from this lore pack:
- Never reuse an occupied byte id.
- If collision occurs, remap to the next free id, update atlas mapping, update JSON references, and keep save migration if any names change.
