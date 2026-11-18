---
title: Verify Ink Look (hatched shadows, ground ink detail, tile transitions)
status: awaiting owner look pass
created: 2026-11-18
area: Shaders / PaintedGround
---

## Goal
Judge the Ink Look experiment ([InkLook_System.md](InkLook_System.md)) against `Tasks/Claude/TextureInspo.png`
and `DontStarveTiles.png`, then answer D3, D4, D5, D8 and D9 in the spec. Everything is off by default, so each
piece can be toggled live in one Play session. Shader compile is already clean; only the look is open.

## Setup (done 2026-11-18 in CampLoop)
- `Assets/Scenes/CampLoop.unity` has an **Ink Look Bridge** object (hatch 1, 10 m tiles, object-anchored, grain 0.3,
  hatch texture assigned). Disable the object to A/B the whole hatch + grain look.
- Both CampLoop ground materials (`Terrain/Tiles/Tile_0_0` and `Tile_-1_0`) have ink detail and tile transitions on;
  untick the two toggles on the material to A/B them.
- First captures: ink detail and transitions read clearly; hatch reads in tree and cart shadows at play zoom, but
  only after the placeholder strokes were made bolder (the first hatch averaged 7 % ink and read as a grey smear).
- Known unrelated: two `Quad` objects in CampLoop have no material and render magenta.

## Hatched shadows (R1, every lit material)
- [ ] Bridge: **Hatch Strength** 1. Diagonal `/` strokes appear only inside shadow (cast shadows and the dark side
      of units), on ground, units and props alike. Lit areas and eyes stay clean.
- [ ] A campfire or lamp next to a shadow: the light lifts the area around the strokes, the strokes stay ink.
- [ ] **Hatch Space Mode** — try all three while panning the camera and while a unit walks:
  - [ ] World Triplanar: strokes glued to the world; a walking unit slides through them.
  - [ ] Camera Plane World Anchored: always `/` on screen, glued to the world.
  - [ ] Camera Plane Object Anchored: a walking unit carries its strokes. Look for a stroke jump where its body
        parts meet (the known per-part risk, D3). The ground stays world-fixed in this mode on purpose.
- [ ] **Hatch Tile Metres**: stroke density; zoom out and note whether strokes go grey (D7 mips) or too dense (D9).
- [ ] **Paper Grain Strength** 0.3: faint grain over everything, no visible tiling at play zoom.
- [ ] **Hatch Boil Frames Per Second** 6–8: strokes shimmer like a redrawn page (D8: keep or drop).
- [ ] **Ink Color**: compare against the inspo's warm brown and against the outline colour (D5).

## Ground ink detail (R2)
- [ ] Select `Terrain/TestWorld/Tile_0_0/M_Ground_Tile_0_0.mat`, tick **Ink detail sheets**. Stones, grass strokes
      and dirt scratches appear inked in the edge ink colour, only where that layer shows.
- [ ] Tune **Detail tile metres** and **Line, tone, scratch strength** until it reads like the inspo; note values.

## Tile transitions (R3)
- [ ] Same material, tick **Tile transitions instead of fringe**. Borders snap to a 2 m grid of pre-drawn pieces;
      curves no longer warp the edge art.
- [ ] A border crossing the edge between Tile_0_0 and a neighbouring tile (still on the fringe): note the switch.
      To check a seam between two tile-transition tiles, enable the keyword on the neighbour's material too.
- [ ] A saddle (two diagonal corners inside): the piece reads as a deliberate shape, not a smear.
- [ ] **Transition cell size**: 1, 2, 4 m — pick one (D4) and say whether it should replace the fringe everywhere.

## Close out
- [ ] Record D3/D4/D5/D8/D9 in the spec, keep or delete each piece, then update `Memories/Code/Shaders.md`.
