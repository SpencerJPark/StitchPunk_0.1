# 0008: The in-play overlays are IMGUI

Status: accepted (2026-09-29)

## Decision
The pause-and-draw overlay and the on-screen record button draw with IMGUI (`OnGUI`) on runtime
MonoBehaviours. The Editor-side UI, meaning settings and later the review window, is UI Toolkit.

## Why
Both overlays must appear over the game view, take mouse and key input while `Time.timeScale` is
zero, and add no asset dependency. Runtime UI Toolkit needs a `PanelSettings` asset and a
`UIDocument` in the scene, so the package would have to inject assets into the user's project just
to draw a circle on screen. IMGUI needs nothing.

The Stitch_Punk rule banning IMGUI applies to the animation toolkit's *Editor* code, where UI
Toolkit is the right answer and Handles-based drawing was the problem. It does not apply here.

## Consequences
The overlay owns the y-flip: IMGUI's origin is top left, and every screen region the resolver
raycasts through is bottom left. That conversion is the one place this choice fails silently, so
it is called out in the build contracts.
