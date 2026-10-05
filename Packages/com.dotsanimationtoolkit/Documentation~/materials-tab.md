# The Materials tab

**Window ▸ DOTS Animation Toolkit ▸ DOTS Animator** — the Materials tab, after
Cutouts and before Rigs.

A check, not a material editor. For a rig and a clip set it reads what each
part's animations drive, and tells you which parts sit on a material whose
shader does not declare those inputs. Without the input the shader never sees
the value: a flipbook part sits on frame 0, a VAT part never moves. Wiring the
inputs is done in the shader; the toolkit ships shaders that already have them,
and you can build your own modelled on them (see "Your own shader" in
[shader-contract.md](shader-contract.md)).

The tab follows the window's shared **Rig** and **Clip Set**. Nothing is checked
without a clip set, because the clips are what say which inputs a part needs.
Showing the tab runs the check again; so does the refresh button over the parts.

---

## What each animation needs

| A clip in the set has... bound to the part | The part's shader must declare |
|---|---|
| a sprite track in slice mode (flipbook frames) | `_ImageIndex` and `_MainTexArray` |
| a sprite track in atlas mode | `_AtlasFrame` |
| a VAT track for the part, or the clip's VAT source (every VAT Mesh part) | `_VatFrameA`, `_VatFrameB`, `_VatBlend`, `_VatBoneTex`, `_VatTexelParams` |
| transform tracks only | nothing |

A part with several materials needs the input on every one of them.

## The three columns

- **Parts** — one row per rig part: its name, its material, and ✗ (missing
  inputs), ✓ (has everything its animations need) or – (nothing to check).
- **Preview** — the rig's prefab in 3D. A part with missing inputs is washed
  red over its art; the selected part is boxed. Billboarding is off in this
  view. Drag to orbit, right-drag + WASD/QE to fly, middle-drag to pan, the
  wheel to zoom, and **Return** (or F) to frame the whole rig again.
- **Part** — the selected part's inputs, each ✓ or ✗ with the clips that ask
  for it, its materials with their shaders and GPU instancing (off is always
  wrong: Entities Graphics needs it), and warnings such as a flipbook material
  with no texture array assigned or a VAT material not baked yet.

## Fixing a ✗

Give the part a material whose shader declares the input. The shipped graphs:

- `ToolkitSpriteUnlitArray` — flipbook frames (`_MainTexArray`, `_ImageIndex`)
- `ToolkitSpriteUnlit` — atlas frames (`_AtlasFrame`)
- `ToolkitVatCrowdUnlit` — VAT (`_VatBoneTex`, `_VatFrameA/B`, `_VatBlend`, `_VatTexelParams`)

Or add the input to your own shader with a Custom Function node from
`Packages/com.dotsanimationtoolkit/Shaders/Nodes/`.

## At bake time

After the VAT Bake tab writes a bone-flavour set, it puts `_VatBoneTex` and
`_VatTexelParams` on each baked part's material, one report line per part. It
never changes a shader: a material whose shader lacks `_VatBoneTex` is named in
the report and shows ✗ here. A material shared by two baked parts is refused
(VAT textures are per part, so give each part its own material), and a
vertex-flavour bake leaves materials alone.
