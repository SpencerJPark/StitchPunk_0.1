# The Materials tab

**Window ▸ DOTS Animation Toolkit ▸ DOTS Animator** — the Materials tab, after
Flipbooks.

Checks every material a rig actually uses against the per-instance property
contract in [shader-contract.md](shader-contract.md), so a broken material
shows up as a list of missing properties instead of a silent frame-0 part.

The tab follows the window's shared Rig, picked in the Rigs tab, and never
changes that selection itself. It also reads the window's shared clip set for
the flipbook check below.

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
- Any flipbook warnings (see below).

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

## Flipbook check

If a sprite track on any clip in the window's shared clip set has a Flipbook
assigned and binds a part that uses this material, but the material has no
`_MainTexArray` property, the tab warns, naming the flipbook, the part, and the
material. This catches a flipbook material that was set up before its part
was pointed at a named flipbook.

## Creating a material

Pick a Target in the tab's header and press **Create and assign**. The new
material is built from the package's own shader graph for that target's
kind:

- Quad → `ToolkitSpriteUnlit`
- Flipbook Plane → `ToolkitSpriteUnlitArray`
- VAT Mesh → `ToolkitVatCrowdUnlit`

GPU instancing is enabled on the new material. It saves beside the rig's
Source Prefab as `M_<Rig>_<Target>.mat`, numbering the filename if one
already exists there — Create never overwrites an existing material.

The new material is also written onto the renderer at the target's Source
Node Path inside the rig's Source Prefab. This is a prefab **asset** edit,
saved immediately, and it is **not undoable** — the same as every other
rig structure edit the window makes.

Which slot gets replaced depends on the renderer. A renderer with one
material slot has that slot replaced. A renderer with several slots has
the slot holding the material currently selected in the catalog replaced,
if that material is on this renderer — otherwise slot 0 is replaced. The
result line names the slot, reading like `Created M_NewRig_BaseHead.mat
and assigned it to BaseHead (replaced BaseHead.mat).`

Nothing is lost when the assignment cannot happen: a missing node, a node
with no Renderer, or a Source Prefab that is not a saved asset still
creates the material, and the result line says why, reading like `Created
M_NewRig_BaseHead.mat; not assigned: node 'BaseHead' has no Renderer.`

**To undo it**, assign the old material back — the replaced material is
still on disk, untouched.

## At bake time

Baking a VAT Mesh part whose material has the VAT texture slot filled runs
the same check as this tab and emits one warning listing any missing
contract property or disabled instancing. Fixing the material here before
baking avoids that warning.
