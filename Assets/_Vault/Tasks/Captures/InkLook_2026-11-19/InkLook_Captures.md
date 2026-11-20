---
title: Ink Look captures (procedural hatch on ground and objects)
created: 2026-11-19
updated: 2026-11-20
area: Shaders / PaintedGround
---

Spec: [[InkLook_System]] · checklist: [[verify-ink-look]] · reference: `Tasks/Claude/TextureInspo.png`

Captured in the Editor from the CampLoop Main Camera (1600×900), Ink Look Bridge on, Object Hatch Anchor = Object
unless noted. The magenta squares are two `Quad` objects in CampLoop with no material; they are not part of this work.
The zoomed-out stand-in ground only exists in Play mode, so it is not in these shots.

## 1. Game camera, ink look on
Ground shadows hatched; objects now hatched on the side away from the sun (caravan canvas, trees, crates, characters).
![[01_game_view_ink_on.png]]

## 2. Same view, ink look off (before)
![[02_game_view_ink_off.png]]

## 3. Ground close-up
![[03_ground_closeup.png]]

## 4. Cart shadow close-up
![[04_cart_shadow_closeup.png]]

## 5. Camera turned 45°
![[05_camera_turned_45.png]]

## 6. Zoomed out to 150 m
![[06_zoomed_out_150m.png]]

## 7. Zoomed out to 400 m
![[07_zoomed_out_400m.png]]

## 8. Caravan close-up
The canvas side facing away from the sun is hatched; half the normal shade darkening is kept underneath (bridge dial 0.5).
![[08_caravan_closeup.png]]

## 9. Building close-up
The front faces away from the sun, so the whole face is hatched, like the shaded building fronts in the reference.
![[09_building_closeup.png]]

## 10. Character close-up, Object anchor (default)
Lines ride with the unit in every direction. Faint block seams show where its separate body parts meet.
![[10_character_closeup_object_anchor.png]]

## 11. Character close-up, World anchor
Same seams; in motion this one slides when walking along X but not along Z (the owner's report), so Object is the default.
![[11_character_closeup_world_anchor.png]]
