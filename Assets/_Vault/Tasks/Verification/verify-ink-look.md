---
title: Verify Ink Look (hatched shadows, ground ink detail, tile transitions)
status: awaiting owner look pass
created: 2026-11-18
area: Shaders / PaintedGround
---

## Goal
Judge the Ink Look experiment ([InkLook_System.md](InkLook_System.md)) against `Tasks/Claude/TextureInspo.png`
and `DontStarveTiles.png`, then answer D4, D5, D8 and D9 in the spec. The whole look is one bridge toggle, so it can
be A/B'd live in one Play session. Compiled clean 2026-11-19; Editor captures in
[[InkLook_Captures]] (`Tasks/Captures/InkLook_2026-11-19/`). Still owed: the zoomed-out stand-in in Play mode.

## Setup
- [ ] Unity console: no shader errors on `Painted/Ground` or any graph (the hatch change touched the shared include).
- `Assets/Scenes/CampLoop.unity` → **Ink Look Bridge**: tick/untick **Ink Look Enabled** to A/B the whole look
  (ground ink detail, tile edges, hatched ground shadows). Every ground material follows, zoomed-out stand-ins included.

## Ground
- [ ] Flat local colour per surface, detail in brown ink (cobble outlines, grass strokes, pebbles).
- [ ] Turf edges are Don't Starve style tiles (flicks + sparse strokes), snapped to a 4 m grid, no warping on curves.

## Hatch (ground shadows only)
- [ ] Shadows cast on the ground are fine diagonal lines (about 5 px apart, 1 px wide), with no darkening under them.
- [ ] Characters and props are never hatched; their shaded sides look as before.
- [ ] Turn and pan the camera: the lines stay put on the ground, no sliding.
- [ ] A unit walks: its shadow moves over fixed lines (a stencil), nothing rides on the unit.
- [ ] Zoom from closest to the strategic view: line spacing on screen stays about the same, lines stay crisp, the hatch
      never vanishes or turns grey, and the zoomed-out stand-in ground has the same ink look as the real tiles.
- [ ] Tune **Hatch Spacing Pixels** / **Hatch Line Width Pixels**; note values.

## Close out
- [ ] Record D4/D5/D8/D9 in the spec (D3 is settled: world-locked, ground only), keep or delete each piece, then update
      `Memories/Code/Shaders.md`.
