---
title: Ink Look captures (procedural hatch on ground and objects)
created: 2026-11-19
updated: 2026-11-20
area: Shaders / PaintedGround
---

Spec: [[InkLook_System]] · checklist: [[verify-ink-look]] · reference: `Tasks/Claude/TextureInspo.png`

Captured in the Editor from the CampLoop Main Camera (1600×900), Ink Look Bridge on, Object Hatch Anchor = World. The magenta squares are two `Quad` objects in CampLoop with no material; they are not part of this work.
The zoomed-out stand-in ground only exists in Play mode, so it is not in these shots.

## 1. Game camera, ink look on
Ground shadows hatched; caravan canvas, trees and crates hatched on the side away from the sun. Characters standing in
sun stay clean: flat cards are hatched only where a cast shadow falls on them.
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

## 12. Character in the building's shadow
The character was moved into the building's cast shadow for this shot (editor-only, not saved): hatched, like the ground
around it.
![[12_character_in_building_shadow.png]]

## 13. Same character nudged 0.3 m along Z
The dash pattern on the body is different from shot 12: the world-fixed lines slide across the character.
![[13_character_moved_along_z.png]]

## 14. Same character nudged 0.3 m along X
Different again: the lines slide the same way for X motion as for Z.
![[14_character_moved_along_x.png]]
