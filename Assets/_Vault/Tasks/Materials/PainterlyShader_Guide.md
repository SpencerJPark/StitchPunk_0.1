# Painterly Shader — Wiring Guide

Recreates the single-texture painterly setup from [[Transcript]] in Unity using the
**StitchPunk/Painterly** reflection nodes (`Assets/Shaders/Nodes/Painterly/`).
One RGB brush-stroke mask drives base color (via a 4-stop ramp + hue/sat/value),
per-object variation, and a normal map — everything else is material sliders.

## 0. Prerequisites

- The six nodes should already appear in any Shader Graph's **Create Node** menu under
  `StitchPunk/Painterly` (search "StitchPunk"). If they don't, check the Console for
  reflection-import errors on the `.hlsl` files.
- Generate the placeholder mask: **Stitch Punk ▸ Generate Painterly Mask Texture**
  → `Assets/Textures/Painterly/T_PainterlyMask.png` (tileable, linear, R/G/B = three
  independent stroke layers).

## 1. Create the graph

Duplicate `Assets/Shaders/Graphs/3DShader.shadergraph` → rename to
`remove dead code.shadergraph` (keep it in `Graphs/`). Duplicating keeps the existing
cel-lighting section (`LightingCelShaded` custom function) intact — the painterly
chain only replaces whatever currently feeds **Base Color**.

## 2. Blackboard properties

| Property | Type | Default | Notes |
|---|---|---|---|
| `MaskTexture` | Texture2D | T_PainterlyMask | The single RGB stroke mask |
| `Channel` | Float (Slider 0–3) | 0 | 0=R 1=G 2=B 3=A |
| `Contrast` | Float (Slider 0–4) | 1 | Pre-ramp value contrast |
| `ColorA`…`ColorD` | Color ×4 | dark→light ramp | The 4 ramp stops (A=value 0, D=value 1) |
| `PositionB`, `PositionC` | Float (Slider 0–1) | 0.33 / 0.66 | Middle stop positions |
| `RampSmoothness` | Float (Slider 0–1) | 1 | 0 = hard toon bands |
| `HueShift` | Float (Slider −0.5–0.5) | 0 | Recolor without touching the ramp |
| `Saturation`, `Value` | Float (Slider 0–2) | 1 / 1 | |
| `HueJitter`, `ValueJitter` | Float (Slider 0–1) | 0.05 / 0.15 | Per-object randomness amounts |
| `PositionScale` | Float (Slider 0–10) | 1 | Sensitivity of position-based randomness |
| `NormalStrength` | Float (Slider 0–2) | 0.4 | Feeds Normal From Height |
| `UVTiling` | Vector2 | (1,1) | Mask tiling |

## 3. Node chain

```
Object (Position, world) ──▶ ObjectRandom (PositionScale)
                              │ Random3 ──────────────┐
                              │ Random3.xy ──▶ (× 0.35)│
                              ▼                        │
UV ──▶ Tiling And Offset (UVTiling, offset = jittered) │
        │                                              │
        ▼                                              ▼
Sample Texture 2D (MaskTexture) ──▶ PainterlyColor ◀── all blackboard sliders
                                     │ Color ─────────▶ into the cel-lighting multiply
                                     │                  that previously took Base Color
                                     │ Mask Value ──▶ Normal From Height (NormalStrength)
                                     │                       │
                                     │                       ▼
                                     │                  Normal (Tangent) on master stack
```

Step by step: 

1. **Object Random**: add `Object` node → its world **Position** into
   `ObjectRandom.Object Position`; `PositionScale` property into `Position Scale`.
2. **Per-object UV offset** (optional but recommended — the video's position-based
   variation): `ObjectRandom.Random3` → **Swizzle (xy)** → **Multiply** by ~0.35 →
   into the **Offset** of a `Tiling And Offset` node (Tiling = `UVTiling`), whose
   output feeds the mask sample UV.
3. **Sample** `MaskTexture` with a `Sample Texture 2D` → **RGBA** into
   `PainterlyColor.Mask Sample`.
4. Wire every blackboard property to the matching `PainterlyColor` input, and
   `ObjectRandom.Random3` into `Object Random` (if unwired, jitter is a no-op).
5. `PainterlyColor.Color` → wherever the old Base Color input went (in 3DShader
   that's the albedo feeding the cel-lighting multiply / master stack Base Color).
6. `PainterlyColor.Mask Value` → built-in **Normal From Height** node (Strength =
   `NormalStrength`, Tangent space) → **Normal** on the master stack.
   This is the video's "normal from height" — same texture, no extra maps.
7. Save the graph.

## 4. Material

Create `Assets/Materials/Objects/Painterly.mat` from the graph
(right-click graph ▸ Create ▸ Material, or set the shader on a new material).
Assign `T_PainterlyMask`, pick a channel, then tune:

- **Recolor**: adjust the 4 ramp colors, or leave them and drag `HueShift`.
- **Painterly vs toon**: `RampSmoothness` 1 → soft blends, 0 → hard cel bands.
- **Break up repetition**: raise `HueJitter` / `ValueJitter`; every prop instance
  hashes its own position, so copies of the same material differ automatically.

Each new surface type = duplicate the material and change sliders (or channel).
Same texture, same shader.

## 5. Composable nodes (advanced graphs)

`PainterlyColor` is just a convenience chaining of the small nodes — for custom
chains (double ramps, different jitter wiring, the future cel-shading port) use:
`Select Channel` → `Value Contrast` → `Color Ramp 4` → `Hue Sat Value`, plus
`Object Random`. All under `StitchPunk/Painterly` in the node search.

## 6. Painting the real mask (Affinity)

- One document, three layers exported into R / G / B channels (the placeholder
  generator shows the target look — open it with the channel viewer).
- Use a textured brush; make strokes at **many different gray values** — the value
  *variation* is what the ramp turns into color variation. Stroke shape matters
  far less than value spread.
- Keep it tileable. Compression artifacts are acceptable (per the video); the
  ramp + lighting hide them.
- Import settings: **sRGB off** (it's data), wrap **Repeat**.
