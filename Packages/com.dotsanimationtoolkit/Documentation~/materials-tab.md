# The Materials tab

**Window ▸ DOTS Animation Toolkit ▸ DOTS Animator** — the Materials tab, after
Sprite Sheets.

Checks every material a rig actually uses against the per-instance property
contract in [shader-contract.md](shader-contract.md), so a broken material
shows up as a list of missing properties instead of a silent frame-0 part.

The tab follows the window's shared Rig, picked in the Rigs tab, and never
changes that selection itself. It also reads the window's shared clip set for
the sheet check below.

---

## Opening the tab

Select a rig anywhere in the window (Rigs tab, or any tab that shares the rig
selection) and switch to Materials. The tab scans every `Renderer` under the
rig's Source Prefab, collects the materials those renderers use, and maps
each one to the rig targets it serves by the target's source node path.

This is a per-rig scan, not a project-wide one: a material no rig prefab
references never appears here, even if it exists in the project. A material
found on a renderer that matches no rig target still appears, marked
"no rig target", since a broken path mapping is exactly the kind of problem
this tab exists to surface.

## What it lists

**Left column** — one row per material, showing:

- The target kinds it serves, e.g. `Quad ×3 · VAT Mesh`.
- A problem count, so a broken material stands out in a long list.

**Right column** — the selected material's detail:

- The shader it uses.
- "Used by": the rig part names it is assigned to.
- Per target kind, one row per contract property, marked:
  - `✓` present.
  - `✗` missing — an error.
  - `–` present but not needed by this kind.
  - `–` covered by the other frame property (see the Flipbook Plane row
    below).
- GPU instancing: `✓` or `✗`. Off is always an error — Entities Graphics
  requires it.
- Any sheet warnings (see below).

**Select in Inspector** pings the material asset. The tab itself is
read-only — properties are edited on the material in the Inspector, not here.

## What each target kind needs

| Target kind | Required properties |
|---|---|
| Quad | None — transform-only, no per-instance property. |
| Flipbook Plane | `_ImageIndex` **or** `_AtlasFrame` — either one satisfies the kind. |
| VAT Mesh | `_VatFrameA`, `_VatFrameB`, `_VatBlend` — all three. |

`_BillboardParams` is never required on any kind: the host game writes it at
runtime, not the material. Every kind also needs GPU instancing enabled.

## Sheet check

If a sprite track on any clip in the window's shared clip set has a Sheet
assigned and binds a part that uses this material, but the material has no
`_MainTexArray` property, the tab warns, naming the sheet, the part, and the
material. This catches a flipbook material that was set up before its part
was pointed at a named sheet.

## Creating a material

Pick a Target in the tab's header and press **Create**. The new material is
built from the package's own shader graph for that target's kind:

- Quad → `ToolkitSpriteUnlit`
- Flipbook Plane → `ToolkitSpriteUnlitArray`
- VAT Mesh → `ToolkitVatCrowdUnlit`

GPU instancing is enabled on the new material. It saves beside the rig's
Source Prefab as `M_<Rig>_<Target>.mat`, numbering the filename if one
already exists there — Create never overwrites an existing material.

Create does not assign the new material to any renderer. Drag it onto the
part's renderer in the prefab yourself to put it to use.

## At bake time

Baking a VAT Mesh part whose material has the VAT texture slot filled runs
the same check as this tab and emits one warning listing any missing
contract property or disabled instancing. Fixing the material here before
baking avoids that warning.
