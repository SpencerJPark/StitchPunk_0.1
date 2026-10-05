# The Materials tab

**Window ▸ DOTS Animation Toolkit ▸ DOTS Animator** — the Materials tab, after
Cutouts and before Rigs.

Makes a material's **look** (texture, tint, cutoff) before any rig exists, then checks it
against the per-instance property contract in [shader-contract.md](shader-contract.md), so a
broken material shows up as a list of missing properties instead of a silent frame-0 part.

A material has two parts: its look, made here, and its **motion**, which is either Flipbook or
VAT. Flipbook follows the art (a cutout drawn over a flipbook gets it); VAT is switched on by the
VAT bake, never ticked here.

---

## The sidebar: Materials | Cutouts | Meshes

- **Materials** lists every material under `Assets/` on one of the package's shader graphs, plus
  every material the bound rig's prefab uses (on any shader).
- **Cutouts** lists every cutout. Picking one shows the material stored on it
  (`CutoutAsset.material`).
- **Meshes** lists every mesh under `Assets/`. A bare mesh has no field to store a material, so
  its material is found by name: `M_<MeshName>.mat` in the mesh's folder, the name Create writes.

When a cutout or mesh has no material yet, the right column shows **Create material**. The new
material is saved beside the cutout's mesh (or the mesh) as `M_<Name>.mat`, from the shipped graph
for its motion: a flipbook source gets `ToolkitSpriteUnlitArray` with the array assigned, an image
or a bare mesh gets `ToolkitSpriteUnlit`. GPU instancing is on.

## Check against (optional)

The bar's **Rig** and **Clip Set** follow the window's shared selection. Neither is needed to make
a material. With a rig bound:

- the rig prefab's materials join the Materials list, each with the target kinds it serves;
- a picked material the rig uses also shows its Usage and Contract cards (below);
- a picked cutout or mesh offers **Use in rig: N parts**, which puts the material on every
  renderer in the rig's Source Prefab whose mesh is this one. That is a prefab **asset** edit,
  saved immediately, and not undoable; assign the old material back to revert it.

## What it shows

**Motion** — a **Flipbook** toggle and a **VAT** status line. On a toolkit material the toggle
swaps the shader between the shipped graphs on the same asset, so the GUID and every renderer
using it survive; turning Flipbook on or off changes the texture type, so the texture is cleared
and the status line says so. VAT reads "Not baked: VAT Bake turns this on" until a bake writes it.

**Shader** — the shader, **Toolkit** or **Custom**, and GPU instancing. On a custom shader the
toggle is read-only, and the card lists which features its properties provide and which contract
properties it lacks (see "Your own shader" in [shader-contract.md](shader-contract.md)).

**Usage and Contract** (only for a material the bound rig uses):

- "Used by": the rig part names it is assigned to.
- Per target kind, one row per contract property, marked:
  - `✓` present.
  - `✗` missing — an error.
  - `–` present but not needed by this kind.
  - `–` covered by the other frame property (see the Flipbook Plane row
    below).
- Any flipbook warnings (see below).

GPU instancing off is always an error — Entities Graphics requires it.
**Select** pings the material asset; texture, tint and cutoff are edited on the
material in the Inspector.

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

## At bake time

After the VAT Bake tab writes a bone-flavour set, it updates each baked
part's material and lists one line per part in its report:

- a toolkit material switches to `ToolkitVatCrowdUnlit` in place, keeping its
  texture, tint and cutoff, and gets `_VatBoneTex` and `_VatTexelParams`;
- a custom material that declares `_VatBoneTex` gets the textures, and is never
  swapped;
- a material shared by two baked parts is **refused** and both parts are named:
  VAT textures are per part, so give each part its own material;
- a Flipbook material is refused (no shipped graph does both);
- a vertex-flavour bake leaves every material alone.
