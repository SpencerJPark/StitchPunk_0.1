---
title: Verify Ink Look (hatched shadows, ground ink detail, tile transitions)
status: awaiting owner look pass
created: 2026-11-18
area: Shaders / PaintedGround
---

## Goal
Judge the Ink Look experiment ([InkLook_System.md](InkLook_System.md)) against `Tasks/Claude/TextureInspo.png`
and `DontStarveTiles.png`, then answer D4, D5, D8 and D9 in the spec. The whole look is one bridge toggle, so it can
be A/B'd live in one Play session. Hatch shading removed 2026-11-20 (owner call). Older Editor captures (with hatch) in
[[InkLook_Captures]] (`Tasks/Captures/InkLook_2026-11-19/`). Still owed: the zoomed-out stand-in in Play mode.

## Setup
- [ ] Unity console: no shader errors on `Painted/Ground` or any graph (the hatch change touched the shared include).
- `Assets/Scenes/CampLoop.unity` → **Ink Look Bridge**: tick/untick **Ink Look Enabled** to A/B the whole look
  (ground ink detail, flat colour, tile edges). Every ground material follows, zoomed-out stand-ins included.

## Ground
- [ ] Flat local colour per surface, detail in brown ink (cobble outlines, grass strokes, pebbles).
- [ ] Turf edges are Don't Starve style tiles (flicks + sparse strokes), snapped to a 4 m grid, no warping on curves.

## Close out
- [ ] Record D4/D5 in the spec (hatching was removed 2026-11-20, so D3/D8/D9 are moot), keep or delete each piece, then update
      `Memories/Code/Shaders.md`.
