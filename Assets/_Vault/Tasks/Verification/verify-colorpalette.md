---
title: Verify — ColorPalette System (v2) + Ragdoll Separation
status: active
created: 2026-07-11
area: code
---

## Goal

Confirm the v2 colour/design pipeline works end-to-end: `ColorPaletteSO` assets (colour + zombie `alternative` pairs) bake into the enum-indexed `ColorPaletteLibraryBlob`; `UnitPartSO` `PartDesign` entries (tagged texture span + 3 palette-window slots) bake into `PartLibraryBlob`; spawns roll shape tags ONLY from `CharacterRigAuthoring.randomTags` and one colour index per palette type (uniform skin/hair per character); `ApplyDesign` writes `_BaseColor`/`_SecondaryColor`/`_TertiaryColor` from the matched design; alternate-colour mode flips every colour to its zombie variant; and the ragdoll path (RagdollJointSO/Authoring → zones buffer) still flails + settles with zero design-blob involvement.

## Steps

### Compile gate
- [ ] Focus Unity → clean Console (no `error CS####` / Burst `BC####`). Watch the `DesignRandomizeJob` Burst compile (new `RandomTagOption` buffer param + `SetColorIndex` warning strings).
- [ ] No duplicate-GUID warnings on first import (8 hand-generated `.cs.meta`: ColorEnums, ColorPaletteLibrarySO, ColorPaletteLibraryBlob, ColorPaletteLibraryAuthoring, ColorPaletteLibraryBakingSystem, ColorPaletteResolveTests, RagdollJointSO, RagdollJointAuthoring).
- [ ] Old part assets under `ScriptableObjects/Units/Parts/` are deleted (Spencer, 2026-07-11) — fresh `UnitPartSO` assets are being re-authored under `ScriptableObjects/Parts/`. Confirm the new assets show the `designs` list in the inspector (the `UnitPartSO.cs.meta` GUID was restored to the original `29bf68ec…` during the rename, so either way no missing-script states).

### EditMode tests
- [ ] Test Runner ▸ EditMode → `ColorPaletteResolveTests` (10) + `DesignApplyUtilTests` (12) all green.
- [ ] `SystemPlacementConformanceTests` / `SystemGroupOrderTests` still green.

### Asset creation (Editor)
- [ ] `Assets/ScriptableObjects/Color/` (folder started 2026-07-11): one `ColorPaletteSO` per type in use (**Colors ▸ Palette**) — `Skin`, `Hair`, `Blood`, `Shirts`. Each entry = main colour + optional **alternative** (tick `hasAlternative`; zombie variant for skin, shade-variant for slots like buzz-cut hair that tick `useAlternateColor`; unticked = alt mode keeps the main colour — e.g. Blood/Hair which have no alt rows yet). Reference values live in `Assets/_Scripts/Data/ColorRefs.md`. Alpha matters on colours used by secondary/tertiary slots (layer blend strength).
- [ ] `_ColorPaletteLibrary.asset` (**Colors ▸ Palette Library**) listing every palette.
- [ ] Re-author `UnitPartSO` assets under `ScriptableObjects/Parts/` and rebuild `_PartLibrary.asset` (the old `ScriptableObjects/Units/Parts/` set was deleted) — re-wire `PartLibraryAuthoring` in both scenes to the new library asset and each `BodyPartAuthoring.unitPartDef` to its new part asset.
- [ ] ⚠ **Required before the design pipeline runs at all:** `ColorPaletteLibraryAuthoring` (→ `_ColorPaletteLibrary`) on the subscene GO that carries `PartLibraryAuthoring` — in BOTH `Game.unity` and `DOTSTestScene`. The design systems `RequireForUpdate<ColorPaletteLibrary>`.
- [ ] Re-author each `UnitPartSO`: assign the part's `textureArray` (needed by full-range designs), then the `designs` list — per design: tag (e.g. "Pale"/"Tan"/"Zombie", empty = tag-independent), texture span (leave `useFullTextureRange` ticked for the whole array — zero index entry; untick for a hand-authored `min`/`max`/`step`, e.g. interleaved L/R eyes), and palette slots — leave `useFullRange` ticked (default) for the whole palette with zero index entry; untick to author a `[minColorIndex, maxColorIndex]` window (fixed colour = `[n,n]`); designs like buzz-cut tick `useAlternateColor` to always show the alt shade.
- [ ] `CharacterRigAuthoring.randomTags` on the citizen prefab/rigs: e.g. group "Skin" → [Pale, Tan], group "Hair" → [Black, Blonde]. **Empty list = no tag roll** (parts fall back to their empty-tag designs only) — this replaces the old per-range `randomizable` flags. Do NOT list "Zombie".
- [ ] Ragdoll: create `RagdollJointSO` assets (**Units ▸ Ragdoll Joint**) per joint kind (arm/leg/head — settle speed, segment length, weight, landing zones — values from the old UnitPartSO ragdoll fields). On every rig joint GO (the empties with `BodyPartAuthoring` that used to tick `isRagdollJoint`): add `RagdollJointAuthoring` + assign the joint SO. The `isRagdollJoint` checkbox is GONE — the flag now comes from this component's presence.

### Bake + blob inspection
- [ ] Rebake: `ColorPaletteLibrary` blob shows `ColorBlob {color, alternative}` pairs per palette; `PartDef.designs` shows the authored spans + windows.

### Play mode — spawn roll + apply (visual milestone)
- [ ] Spawn several randomized units: per-character uniform skin (arms = face) and hair (eyebrows = top), varied across characters; only tags listed in `randomTags` appear.
- [ ] Root `CharacterPalette`: `groups` hold rolled tags, `colors` one `ColorChoice` per referenced palette, `useAlternateColors` = 0.
- [ ] Non-packed-shader quads render normally; no Entities Graphics material-property warning spam for `_SecondaryColor`/`_TertiaryColor`.
- [ ] Save → load keeps colours + tags (old saves break — accepted).
- [ ] Ragdoll regression: kill a unit — limbs still flail and settle into the authored zones (zones now come from the joint's `RagdollLandingZone` buffer; a joint without `RagdollJointAuthoring` gets defaults 8/0.5/1 and settles to angle 0).

### Conversion (needs a caller — ZombieConversion plan)
- [ ] Fire `ChangeDesignRequest { paletteChanges("Skin"→"Zombie"), alternateColorMode = Enable }` on a citizen → every palette colour flips to its `alternative` the same frame, rolled identity kept (pale → pale-zombie); parts with Zombie-tagged designs also swap shapes. `alternateColorMode = Disable` reverts.

## Notes

- **Resolve semantics (pinned by tests):** roll is one index per palette type across the FULL palette; each slot clamps into its `[min,max]` window (same window ⇒ exact match, narrow window ⇒ closest); unrolled palette ⇒ `minColorIndex`. Alternative shown when `slot.useAlternateColor` OR `CharacterPalette.useAlternateColors`. The design that supplied the slice supplies the colours — pool spill into an empty-tag design uses THAT design's slots.
- **sRGB → linear at bake** (`ColorPaletteLibraryBakingSystem`) — author colours normally, both variants are converted.
- `ColorPaletteType` no longer has `ZombieSkin` (alternatives replace it); enum is now None/World/Skin/Blood/Hair/Shirts, append-only.
- Colour only shows on parts whose material feeds `PackedChannelRecolor` (packed masks). **Open:** which rigs/arrays migrate to `2DPackedArrayShader` first.
- `BodyPartAuthoring.tintColor` remains the placeholder base tint for parts without designs; palette writes overwrite it on design-slot parts.
