---
title: Ink Look captures (round 3, procedural ground hatch)
created: 2026-11-19
area: Shaders / PaintedGround
---

Spec: [[InkLook_System]] · checklist: [[verify-ink-look]] · reference: `Tasks/Claude/TextureInspo.png`

Captured in the Editor from the CampLoop Main Camera (1600×900), Ink Look Bridge on unless noted.
The magenta squares are two `Quad` objects in CampLoop with no material; they are not part of this work.
The zoomed-out stand-in ground only exists in Play mode, so it is not in these shots.

## 1. Game camera, ink look on
Hatch only inside shadows on the ground (cart, trees), no darkening under it; units and props unhatched.
![[01_game_view_ink_on.png]]

## 2. Same view, ink look off (before)
![[02_game_view_ink_off.png]]

## 3. Ground close-up
Flat local colour with ink detail; Don't Starve style turf edges on a 4 m grid.
![[03_ground_closeup.png]]

## 4. Cart shadow close-up
Lines stay about 1 px wide and about 5 px apart this close: crisp, not pixelated.
![[04_cart_shadow_closeup.png]]

## 5. Camera turned 45°
The lines are fixed to the ground: they turn with the world instead of sliding with the camera.
![[05_camera_turned_45.png]]

## 6. Zoomed out to 150 m
Hatch still visible under the cart at the same on-screen spacing.
![[06_zoomed_out_150m.png]]

## 7. Zoomed out to 400 m
Ink look on every tile; the faint square grid seen in the first pass (atlas bleed) is fixed.
![[07_zoomed_out_400m.png]]
