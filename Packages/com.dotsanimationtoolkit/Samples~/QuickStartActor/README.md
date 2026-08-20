# Quick Start Actor

A complete working actor built from nothing: a rig, a clip set, one animated clip, and a prefab wired to bake.

## Run it

**Window ▸ DOTS Animation Toolkit ▸ Samples ▸ Build Quick Start Actor**

It writes into `AnimationToolkitQuickStart/` in your project and selects the finished prefab.

Then:

1. Drag `QuickStartActor.prefab` into a **SubScene**. Baking is what turns the authoring assets into entities — a prefab in a plain scene will not animate.
2. Open **Window ▸ DOTS Animation Toolkit ▸ Clip Editor**, assign `QuickStartClipSet`, and scrub. The preview poses through the same `ClipSampler` the runtime uses, so what you see is what plays.
3. Enter Play mode and send an `AnimationCommand` naming the clip id printed to the Console.

## Why this is a generator, not a folder of assets

A sample made of committed `.asset` files carries baked-in stable ids. Import it into a project that already has this package and two assets can hold the same id — exactly the collision the identity scheme exists to prevent. Generating on demand mints fresh ids through the normal path, so the sample cannot corrupt a real project's id space.

It also stays correct. Shipped assets are a snapshot of one schema version and go stale silently the next time the authoring format moves. This builds through the same public API you would use, so if the sample breaks, your own workflow was already broken — which is the more useful signal.

## What it demonstrates

- **A rig is targets plus layers.** Three targets (body, two arms) and one layer. Layer identity is *list position* — index is priority, higher composites later — so a single layer keeps the sample free of ordering questions.
- **Keys are offsets from rest.** The arms swing in opposite directions, so the result reads as a pose rather than two limbs moving identically.
- **`rotationZ` is degrees in authoring**, radians only after the bake converts it. Authoring radians produces a 0.9° swing that looks like nothing happening at all — a mistake worth making once, in a sample, rather than in your own first clip.

## Next

- Add a sprite track and a texture array to drive flipbook frames through `_ImageIndex`.
- Add a second layer and crossfade between clips.
- See `Documentation~/getting-started.md` for the full walkthrough, and `Docs/AnimationToolkit/shader-contract.md` if you want these components in a shader you already own.
