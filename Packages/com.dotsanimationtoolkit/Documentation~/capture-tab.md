# The Capture tab

**Window ▸ DOTS Animation Toolkit ▸ Clip Editor** — the Capture tab.

Renders a clip, a profile animation, or a cutscene over a frame range to a
PNG sequence (optionally transparent) or an animated GIF, framed with the
same orbit camera every preview in the window uses. Use it for store-page
stills, devlog GIFs, regression captures, and bug reports.

---

## Sources

- **Clip** — a shared Clip Set and Rig, then a Clip from that set.
- **Profile Animation** — a Profile, an Animation picked by name, and a
  Facing.
- **Cutscene** — the cutscene's own scene must be the open scene. The tab
  poses the bound scene objects while it previews, and puts them back the
  moment you switch source or close the window.

## Framing

Orbit, pan, and zoom the viewport exactly as in the rest of the Clip Editor.
The reset-camera button reframes on the current source. The viewport shows
exactly what will be captured — no grid, the capture background, the
capture aspect ratio — so what you see is what lands in the output.

The camera pose is remembered per source, so switching away and back (or
re-running a capture later) reframes the same shot without re-aiming.

## Settings

- **Size** — presets at 256×256, 512×512, 1024×1024, and 1920×1080.
- **FPS** — 1 to 60.
- **Range** — a fraction of the source's length. The end is exclusive: a
  one-second loop sampled at 12 FPS gives 12 frames, and a looping GIF does
  not repeat its first pose.
- **Background** — Transparent or a solid Colour.
- **Format** — PNG Sequence or GIF.
- **Name** — defaults to the source's own name.
- **Output Folder** — defaults to
  `Assets/Generated/DotsAnimationToolkit/Captures/<name>/`.

## Output

- PNG Sequence writes `<folder>/<name>_0001.png`, `_0002.png`, and so on.
- GIF writes a single `<folder>/<name>.gif`.

The output folder is created if it does not exist. If files with those
names already exist, you are asked once before anything is overwritten.

PNGs import uncompressed with no mipmaps — they are stills for reference
and marketing, not game textures.

## While capturing

Capture runs one frame per editor update, with a progress bar and a Cancel
button, so the Editor stays responsive throughout. Cancelling keeps
whatever PNGs have already been written; a GIF is only written once every
frame in the range has been captured.

## Traps

- A GIF uses one 256-colour palette for its whole length, so smooth
  gradients band. Use PNGs when you need clean stills.
- GIF frame delays are whole centiseconds, and browsers treat anything
  under two centiseconds as slow — 50 FPS is the fastest honest GIF you can
  ask for.
- Transparent background on a Cutscene only clears the sky. The scene's own
  geometry still renders, so a cutscene capture is rarely truly transparent.
- Captures contain no audio, no overlays, and no gizmos.
- There is no video (MP4) output — PNG Sequence or GIF only.
