# Shader Rework — Full Setup & Legacy Removal Guide

> **STATUS 2026-07-04: Phases 0–3 COMPLETE.** Import verified, all four graphs
> (incl. the new `PainterlyShader`) run on the Cel Shaded Lighting node,
> `LightingCelShaded.hlsl` deleted (by Spencer, in-Editor), the dead outline
> chain deleted (PostProcessing graph, 6 subgraphs, `GetCrossSampleUVs.hlsl`,
> `CelShadingFeature.cs`). The painterly chain was then **built directly into
> `PainterlyShader.shadergraph` programmatically** (Painterly Color + Object
> Random + 15 blackboard properties wired into the lighting multiply; MainTex
> is the mask input) and `Assets/Materials/Objects/Painterly.mat` created with
> the mask assigned. Remaining: verify in-Editor, tune sliders, the optional
> Phase 4 `Legacy/` sweep, and a commit.

One ordered pass through everything built on 2026-07-04: verifying the new
reflection-node library, setting up the painterly material, rewiring the
production graphs, and deleting every legacy item. Do the phases **in order** —
each phase unlocks the deletions in the next. Related: [[PainterlyShader_Guide]]
(detailed painterly wiring), `_Vault/Memories/Code/Shaders.md` (reference).

**The end state:** `Legacy/LightingCelShaded.hlsl`, `Legacy/GetCrossSampleUVs.hlsl`,
the unused `PostProcessing.shadergraph`, and 6 dead subgraphs gone; the three
production graphs on searchable **StitchPunk** nodes; one painterly material live.
Only `WorldSpaceSurfaceData` remains as a subgraph (production-used, and correctly
a subgraph — it bundles geometry-context nodes).

---

## Phase 0 — First import & sanity check (do nothing else until this passes)

- [ ] Focus Unity. It imports all new `.hlsl` files, `PainterlyMaskGenerator.cs`, and the moved folders.
- [ ] **Console must be clean.** Known possible failure: `Nodes/Screen/RobertsCrossNormals.hlsl` is the only file using Texture2D/Sampler ports — if the reflection importer rejects those types, delete that one file and keep using the `RobertsCrossNormals` subgraph (everything else is unaffected).
- [ ] Open any graph → Create Node menu → search **"StitchPunk"**. Expect **17 nodes**:
  - `StitchPunk/Painterly` (6): Select Channel, Value Contrast, Color Ramp 4, Hue Sat Value, Object Random, Painterly Color
  - `StitchPunk/Lighting` (2): Cel Shaded Lighting, Cel Shaded Lighting (Debug)
  - `StitchPunk/Screen` (8): Reconstruct View Position, Screen Space Normal, Encode View Space Normal, Cross Sample UVs, Cross Sample Screen UVs, Roberts Cross Depth, Roberts Cross Normals, NdotV Transform
  - `StitchPunk/Utility` (1): If Any Non Zero
- [ ] Confirm the game scene **renders exactly as before** — the legacy wrappers keep the old graphs on identical math, so any visual change here is a bug: stop and report it.
- [ ] Commit (this also picks up the auto-generated folder `.meta` files for `Graphs/`, `RenderFeatures/`, `Nodes/Lighting|Screen|Utility/`, `Nodes/Painterly/` file metas).

## Phase 1 — Rewire cel-shading in the three production graphs

Do this BEFORE building the painterly graph, so the painterly duplicate starts clean.

Per graph — `Graphs/3DShader`, `Graphs/2DShader`, `Graphs/2DTextureArrayShader`:

- [ ] Open the graph; find the **Custom Function** node calling `LightingCelShaded_float`.
- [ ] Screenshot/note its 12 input connections (Smoothness, RimThreshold, Position, Normal, View, the 7 Edge* values — mostly blackboard properties) and where its `Color` output goes.
- [ ] Add **Cel Shaded Lighting** (`StitchPunk/Lighting`) — the port list is identical, same order, same math.
- [ ] Reconnect all 12 inputs + the output, delete the Custom Function node, save.
- [ ] Verify in the scene: **zero visual change** (units for the 2D graphs, environment props for 3DShader).

- [ ] All three done → **DELETE `Legacy/LightingCelShaded.hlsl` (+ `.meta`)**. To be certain nothing else references it first:
  `grep -rl "dd1a512ed4b241a982ef15c8b87bc779" Assets/ --include="*.shadergraph" --include="*.shadersubgraph"` → must return nothing.

## Phase 2 — Painterly material (full detail in [[PainterlyShader_Guide]])

- [ ] **Stitch Punk ▸ Generate Painterly Mask Texture** → `Assets/Textures/Painterly/T_PainterlyMask.png`. Inspect it (RGB = three stroke layers).
- [ ] Duplicate the now-rewired `Graphs/3DShader` → `Graphs/PainterlyShader.shadergraph`.
- [ ] Add blackboard properties (table in the painterly guide): MaskTexture, Channel, ColorA–D, PositionB/C, RampSmoothness, Contrast, HueShift, Saturation, Value, HueJitter, ValueJitter, PositionScale, NormalStrength, UVTiling.
- [ ] Wire: `Object(Position) → Object Random`; `Random3.xy × ~0.35 → Tiling And Offset(offset)` → `Sample Texture 2D(MaskTexture)` → **Painterly Color** (all sliders + `Random3 → Object Random` input) → its `Color` replaces whatever fed Base Color/albedo; its `Mask Value` → built-in **Normal From Height** (NormalStrength) → Normal.
- [ ] Create `Assets/Materials/Objects/Painterly.mat`, assign the mask, drop it on a test mesh in `DOTSTestScene`.
- [ ] Tune: RampSmoothness 1 = painterly blend, 0 = toon bands; recolor via the 4 ramp colors or just HueShift; raise Hue/ValueJitter so prop copies differ.
- [ ] New surface type = duplicate the material, change sliders/channel — same texture, same shader.
- [ ] Later: paint the real mask in Affinity (requirements in the painterly guide §6), overwrite the placeholder PNG, everything updates.

## Phase 3 — Outline chain: nothing to rewire, just delete

**Verified 2026-07-04:** `Graphs/PostProcessing.shadergraph` is referenced by
NOTHING (no material, no renderer asset, no scene/prefab, no `Shader.Find`).
The live outline pipeline is the hand-written `RenderFeatures/*.shader` trio
driven by `RobertsCrossRenderFeature` + `SilhouetteOutlineFeature`. Every
outline subgraph is referenced only by the parked `Legacy/OutlineShader.shadergraph`.
So there is **no rewiring** — the whole shader-graph outline chain is dead:

- [ ] Delete (or move to `Legacy/` if you want them as reference):
  - `Graphs/PostProcessing.shadergraph`
  - `SubGraphs/CrossSamplesUVs.shadersubgraph`
  - `SubGraphs/RobertsCrossDepth.shadersubgraph`
  - `SubGraphs/RobertsCrossNormals.shadersubgraph`
  - `SubGraphs/NdotV.shadersubgraph`
  - `SubGraphs/NdotVTransform.shadersubgraph`
  - `SubGraphs/IfAnyNonZeroReturnOneElseZero.shadersubgraph`
  - `Legacy/GetCrossSampleUVs.hlsl` (+ meta) — its only consumer was the dead subgraph chain
- [ ] **KEEP** `SubGraphs/WorldSpaceSurfaceData.shadersubgraph` — used by all three production graphs (feeds the cel-lighting inputs).
- [ ] The new `Nodes/Screen/*` nodes stay regardless — they're the ready-made library for any future shader-graph outline rebuild.
- [ ] Sanity check after deleting: outlines unchanged in the Game view (they never depended on any of this).

## Phase 4 — Final Legacy/ sweep (optional, anytime after 1–3)

`Legacy/` then contains only parked experiments referenced by nothing in production:
old outline/2D graph iterations, `Cel Shading.shadergraph`, `Main2DShader`,
`TextureArray.shadersubgraph`, two prototype `.shader` files.

- [ ] Skim for anything you want as reference; delete the rest of `Legacy/` wholesale (it's all in git history anyway).
- [ ] Update `_Vault/Memories/Code/Shaders.md`: remove the "Legacy custom-function files" and "Subgraph migration state" sections once they're empty truths.

## Cadence & safety

- Commit after each phase; each is independently revertible.
- After every rewire: Console clean + eyeball the scene. The new nodes are math-identical ports of the old code — **any** visual delta means a miswire, not a tuning problem.
- The cel-shading port also fixed nothing visual on purpose; the two real bug fixes (screen-space normal reconstruction, debug light-count landmine) live in nodes nothing references yet, so they can't regress anything.
