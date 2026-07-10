---
title: Verify — Texture Channel Packer (Editor node tool)
status: active
created: 2026-07-09
area: code
---

## Goal

Verify the channel-packing tool end-to-end: greyscale textures drag onto the graph, their
R/G/B/A ports wire into the Pack Output node, the live preview tracks the wiring, and Bake
writes a packed PNG that keeps its GUID (and material references) across repacks. Recipes
round-trip the whole graph. Spec + resolved decisions:
[TextureChannelPacker_Tool.md](TextureChannelPacker_Tool.md).

Everything here is Editor-only — **no Play mode, no subscene rebake**. The tool writes a
`.png` asset and nothing else.

## Steps

### Compile gate  ← do this first
- [ ] Focus Unity → clean Console: no `error CS####` (6 new scripts in `Assets/_Scripts/Editor/TexturePacker/`, 1 deleted script).
- [ ] **Specifically confirm `TexturePackerWindow.cs:65` compiles.** The last observed compile still had `error CS0619: EditorUtility.InstanceIDToObject(int) is obsolete`; the fix (`EditorUtility.EntityIdToObject(instanceId)`) landed **after** that compile and has not itself been through the Editor. If `EntityId` has no implicit `int` conversion in 6000.5, wrap it: `EditorUtility.EntityIdToObject(new EntityId(instanceId))`.
- [ ] Console has no leftover reference to the deleted `PainterlyMaskPacker` (nothing in code referenced it; only `Shaders.md` did, and that is updated).
- [ ] Metas: Unity generated all `TexturePacker/` `.meta` files itself during import — confirm no duplicate-GUID warnings.

### Phase 1 — graph shell
- [ ] **Window ▸ Stitch Punk ▸ Texture Channel Packer** opens; the purple "Pack Output" node is already on the canvas.
- [ ] Drag two greyscale PNGs from `Assets/Textures/` onto the canvas → each becomes a node with a thumbnail, its `W x H` label, and four ports (R/G/B/A).
- [ ] Dragging the **same** texture twice does not create a second node.
- [ ] Wiring is refused between two source nodes, and between two ports of the same direction.
- [ ] Unwired output rows show a **slider**; wiring a channel swaps it for the **inv** toggle. Unwiring swaps it back.
- [ ] Wiring a second edge into an already-wired output channel **replaces** the first (the old edge disappears).
- [ ] The Pack Output node cannot be deleted (select it, press Delete); source nodes can.
- [ ] Resolution auto-fills from the largest source the first time one is dropped in, and is then freely editable.

### Phase 2 — bake
- [ ] Wire image 1's **A** port → output **R**; image 2's **R** port → output **G**. Leave B unwired (default 0) and A unwired (default 1). Tick **inv** on the G row.
- [ ] Press **Bake** → a save panel appears (first bake only). Save as `Assets/Textures/Test_Packed.png`.
- [ ] Select the PNG; in the Inspector preview cycle the R/G/B/A channel buttons: **R** = image 1's alpha, **G** = image 2's red **inverted**, **B** = black, **A** = white.
- [ ] The importer shows sRGB **off** and mipmaps **on** (first-creation defaults only).
- [ ] Because alpha is unwired with default 1, the console log reports `RGB24`, and the importer's Alpha Source is `None`.
- [ ] Change an import setting (e.g. tick sRGB), Bake again → **your setting survives** (defaults are only stamped on first creation).

### Phase 3 — preview
- [ ] The output node's thumbnail updates within ~half a second of any wiring / invert / slider / resolution change.
- [ ] The **View** dropdown switches between RGB composite and each isolated channel (isolate renders that channel as greyscale).
- [ ] Dragging a default slider on a 4K source stays responsive (sources are decode-cached; only the first change pays the decode).

### Phase 4 — repack in place  ← the load-bearing guarantee
- [ ] Assign `Test_Packed.png` to a material slot somewhere.
- [ ] Repaint one source PNG in Affinity, re-export over it, return to Unity, press **Bake** (no save panel this time).
- [ ] The material reference still resolves — the texture's **GUID survived** the overwrite — and the new paint shows up.

### Phase 5 — recipes
- [ ] **Save Recipe** → the panel opens in the packed PNG's folder, pre-named `Test_Packed_Recipe`. Save it.
- [ ] Close the window (and ideally restart the Editor). **Double-click the recipe asset** → the window reopens with every source node, wire, invert, slider, resolution, and node position restored.
- [ ] Press Bake → output is byte-identical to before (no save panel; it remembers the output path).
- [ ] Move or rename one source PNG in the Project window, reload the recipe → the node still resolves (recipes store GUIDs, not paths).
- [ ] Delete a source PNG, reload the recipe → a red **"Missing source"** node appears instead of an error, and baking treats that channel as unwired rather than failing.

### Phase 6 — resampling + formats
- [ ] Wire a 512² source and a 1024² source, set output to 1024² → the 512² source is bilinearly upsampled with no offset, tiling, or half-pixel shift (check a hard edge against the original).
- [ ] Drag in a **non-PNG** source (TGA/PSD, or any imported texture): it still packs, and the Console logs the "read through a RenderTexture blit … re-export as PNG for a byte-exact pack" warning.

### Phase 7 — PainterlyMaskPacker parity (it was deleted; confirm nothing was lost)
- [ ] New graph: wire `Mask_R.png` **R** → output **R**, `Mask_G.png` **R** → output **G**, `Mask_B.png` **R** → output **B**. Alpha unwired.
- [ ] Bake over `Assets/Textures/Painterly/T_PainterlyMask.png` → the painterly material looks unchanged in the Scene view.
- [ ] Save the recipe beside it as `T_PainterlyMask_Recipe.asset` so the workflow is one click from now on.
- [ ] Note the old tool defaulted missing channels to **mid-grey (0.5)**, not black — if you bake with a channel unwired, set its slider to `0.5` to match.

## Notes

- The tool never touches subscenes, ECS, or Play mode; a failure here cannot affect runtime.
- `Editor.md` (new, in `_Vault/Memories/Code/`) documents the GraphView traps found while
  building this — single-capacity ports not self-enforcing, and `RemoveElement(edge)` leaving
  surviving ports reporting `connected == true`. Read it before touching either node window.
- If the preview shows a flat colour with everything wired, the source almost certainly failed
  to decode — check the Console for the packer's error line naming the asset path.
