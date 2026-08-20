# Composite Actor

Builds an actor that uses **two techniques at once**: cutout limbs driven by
transform tracks, and a flipbook face driven by sprite tracks — from a single
clip, on a single timeline.

Run **Window ▸ DOTS Animation Toolkit ▸ Samples ▸ Build Composite Actor**.

Everything lands in an `AnimationToolkitCompositeActor` folder under your
project's asset root:

| Asset | What it is |
|---|---|
| `CompositeRig.asset` | Three targets — `Body` and `Arm` as `Quad`, `Face` as `FlipbookPlane` — plus a `Hand` socket on the arm |
| `CompositeStride.asset` | One 1-second looping clip carrying a transform track, a sprite track, **and** an event marker |
| `CompositeClipSet.asset` | The registry the actor references |
| `CompositeFlipbook.asset` | A generated 4-slice `Texture2DArray` |
| `CompositeFlipbook.mat` | The toolkit sprite material, in slice mode |
| `CompositeActor.prefab` | The bake-ready prefab |

Drag the prefab into a SubScene and enter Play mode, or open the Clip Editor and
select the clip set to scrub the limbs and the flipbook together.

## Why this sample exists

Quick Start answers "how do I get anything on screen". This one answers the
question the package's design is actually built around, and the one a reader is
most likely to disbelieve: **a part picks its own technique, and techniques
compose on one actor** rather than forcing a choice per character.

The arm and the face are animated by different mechanisms — keyed rotation on
one, slice indices on the other — from the same clip, sharing one playhead. That
is what "composable per part" means in practice.

## What to look at in the code

- **`CreateRig`** — the technique is a property of the *target* (`TargetKind`),
  so every clip and actor built against the rig agrees about what each part is.
- **`CreateStrideClip`** — one `ClipAsset` holding a `TransformTrack` and a
  `SpriteTrack` side by side, plus an `EventMarker`.
- **`CreateFlipbookTexture`** — the `Texture2DArray` is *generated*, so the
  sample ships no binary fixtures at all.
- **`rig.EnsureStableIds()`** — the call that makes a code-built rig valid.
  `CreateInstance` runs the asset's lifecycle hooks while `targets` is still
  empty and `AssetDatabase.CreateAsset` fires none of them, so without it every
  target keeps the reserved id 0 and the rig fails validation rules V02 and V05.
  Any script that builds a rig from code needs this line.

## Notes

- The event marker uses key `16` — keys 0–15 are reserved by the package — and
  carries a `windowSeconds` of 0.1. Gameplay can read it either way: as a
  one-frame pulse with its payload from the `AnimEventOutput` buffer, or as a
  window that stays open for a tenth of a second via `AnimEventMask`. See
  `Documentation~/animation-events.md`.
- The sample is a **generator rather than committed assets**, so it mints fresh
  stable ids through the normal path and cannot collide with a project already
  using this package. It also cannot go stale: it is built through the same
  public API you would call, so if it breaks, your own workflow was already
  broken.
- The flipbook slices are flat colours on purpose. The point is to make the
  slice *stepping* unmistakable; a subtly animating face leaves you unsure
  whether the index is being driven at all.
