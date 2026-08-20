# Getting started

This walks through the smallest end-to-end loop the toolkit supports: a rig
with one target, a clip that moves it, an actor prefab wired to entities, and
a command that plays the clip in Play mode. It uses the **transform-track**
(2D cutout) technique, which needs no offline bake — the fastest path to
seeing something move. VAT setup is a separate, later step once you have a
skinned source mesh to bake; see
[`shader-contract.md`](shader-contract.md) and the VAT Bake window mentioned
below for that path.

Before starting, confirm the package is installed and its dependencies are
resolved (see the project `README.md`).

> **In a hurry?** Import the **Quick Start Actor** sample from the Package
> Manager, then run **Window ▸ DOTS Animation Toolkit ▸ Samples ▸ Build Quick
> Start Actor**. It generates the whole graph below — rig, clip set, an animated
> clip, and a bake-ready prefab — in one click, so you can look at a working
> setup before building one. Read this page afterwards to understand what it
> made and why. The sample generates rather than shipping `.asset` files, so it
> mints fresh stable ids and cannot collide with assets you already have.

## 1. Create a rig

A `RigAsset` is the skeleton of *slots* your clips will animate — not bones,
but named targets and ordered layers.

1. **Assets ▸ Create ▸ DOTS Animation Toolkit ▸ Rig Asset.** Name it, e.g.
   `DemoRig`.
2. In the Inspector, add one entry to **Targets** — give it a display name
   (e.g. `Body`) and leave `Kind` at `Quad`. Each target gets a stable id
   automatically; you never edit it by hand.
3. Add one entry to **Layers** — give it a display name (e.g. `Base`) and
   check `Default Active` so the layer starts playing on spawn. A rig needs at
   least one layer and allows at most eight.

## 2. Create a clip

1. **Assets ▸ Create ▸ DOTS Animation Toolkit ▸ Clip Asset.** Name it, e.g.
   `DemoBob`.
2. In the Inspector, set **Rig** to `DemoRig` and **Duration** to something
   short, e.g. `1`. Leave **Default Loop** at `Loop`.

## 3. Author the clip in the Clip Editor

1. **Window ▸ DOTS Animation Toolkit ▸ Clip Editor.** The window is a dock:
   clips and the rig hierarchy down the left, the viewport in the middle, the
   inspector on the right, the timeline along the bottom. Drag any boundary —
   where you leave them is where they are next time. The viewport draws a
   reference grid from the moment it opens, so an empty one means "nothing
   selected yet", not "something is broken".
2. Assign a clip set to the editor's clip-set field, or press **New Set** in the
   toolbar to create one and load it — it asks where to save it, and leaves the
   rig empty for you to assign. (Step 4 covers creating one from the Project
   window instead.)
3. Pick a clip to work on. Select `DemoBob` in the Clips pane, or press **New**
   to create one: it writes a clip beside the set on disk, gives it the set's
   rig, adds it to the set and selects it, ready to key. (**New** is disabled
   until a set is assigned — a clip inherits its set's rig, and one created
   without a set would fail validation rule V06 immediately.) Rename it in the
   inspector's **Name** field; the name becomes a C# identifier if you later
   generate clip id constants, so a set full of `NewClip 3` is worth avoiding.
   **Delete** asks first, and offers two outcomes: *Delete Asset* sends the file
   to the trash and cannot be undone, *Remove From Set* only un-registers it and
   can.
4. Add a transform track for the `Body` target and place a couple of keys on
   the timeline with different `position`/`rotationZ` values — the timeline
   supports draggable keys, a zoomable ruler, and a live preview pane so you
   can see the motion as you key it.
5. Scrub or press play in the editor's transport to confirm the clip moves
   before wiring up an entity — the preview samples through the same runtime
   code that plays the clip at runtime, so what you see here is what you get
   in Play mode.

## 4. Create a clip set

1. **Assets ▸ Create ▸ DOTS Animation Toolkit ▸ Clip Set Asset.** Name it,
   e.g. `DemoClipSet`.
2. Set **Rig** to `DemoRig` and add `DemoBob` to **Clips**. Leave
   **Vat Textures** empty — this walkthrough doesn't use VAT.

(If your clip needs to reference a rig with more targets/layers, or you're
adding VAT-sourced clips later, this is also where you'd assign a
`VatTextureSetAsset` produced by **Window ▸ DOTS Animation Toolkit ▸ VAT
Bake**.)

## 5. Set up an actor prefab

1. Create an empty GameObject in the scene (or a prefab), e.g. `DemoActor`.
2. Add the **DOTS Animation Toolkit ▸ Actor** component (`ActorAuthoring`).
   Set **Clip Set** to `DemoClipSet`.
3. Under **Starting Layers**, add one entry: `Layer Index = 0`, `Clip =
   DemoBob`, `Speed = 1`.
4. Add a child GameObject positioned/scaled as your quad (e.g. with a
   `MeshRenderer`/`MeshFilter` showing a quad, or your own render setup — the
   toolkit doesn't ship a quad prefab). Add the **DOTS Animation Toolkit ▸
   Rig Target** component (`RigTargetAuthoring`) to it. Leave **Rig** empty
   (it inherits the actor's rig) and set **Target Stable Id** to the `Body`
   target's id, visible in `DemoRig`'s inspector.
5. If this is a scene GameObject rather than a prefab, make sure it's inside
   a subscene so it bakes to an entity — this package participates in
   Entities' normal baking pipeline, not a custom one.

## 6. Enter Play mode

Enter Play mode. The actor should bake to an entity carrying the runtime
archetype, and — because `Default Active` was checked on the rig's `Base`
layer and `DemoBob` was seeded as its starting clip — it should already be
looping `DemoBob` with no code required.

## 7. Send an `AnimationCommand`

To drive playback from your own code instead of (or in addition to) the
seeded starting layer, use `AnimationCommandUtil` — never write
`AnimationCommand` buffer elements by hand, since every request also has to
enable the `AnimationCommandPending` gate, and the two are easy to forget
independently:

```csharp
using DotsAnimationToolkit;
using Unity.Burst;
using Unity.Entities;

[BurstCompile]
public partial struct PlayDemoClipSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (commands, commandPendingEnabled) in
                 SystemAPI.Query<DynamicBuffer<AnimationCommand>,
                                  EnabledRefRW<AnimationCommandPending>>())
        {
            AnimationCommandUtil.Play(
                ref commands,
                commandPendingEnabled,
                layerIndex: 0,
                clip: myClipId,
                speed: 1f,
                loop: LoopMode.Loop);
        }
    }
}
```

A few things worth knowing before you write this for real:

- `myClipId` is a `ClipId`, not a `ClipAsset` reference — resolve it once
  (e.g. via a baked constant, or the editor's "Generate Clip Id Constants"
  action on a `ClipSetAsset`) rather than looking it up by name every frame.
- `AnimationCommandUtil.Play`'s `blendDuration` parameter defaults to `NaN`,
  meaning "use the clip's authored default blend"; pass `0f` explicitly for a
  hard cut.
- To read back what's currently playing (for UI, animation-driven gameplay
  logic, etc.), use `PlaybackQuery` rather than indexing the `PlaybackLayer`
  buffer yourself — its fields have state-dependent meanings that
  `PlaybackQuery`'s methods already account for.
- If you need to stop the whole toolkit for a world (e.g. on scene unload),
  call `ToolkitWorldControl.SetEnabled(world, false)` rather than disabling
  actors one at a time.

## Where to go next

- [`index.md`](index.md) — the concept model and a map of every window and
  inspector, including the VAT and flipbook paths this walkthrough skipped.
- [`shader-contract.md`](shader-contract.md) — required reading before
  writing or modifying any shader that consumes this package's per-instance
  properties.
- The package `README.md` — which parts of the toolkit are battle-tested
  versus newly landed and unverified, before you build on them.
