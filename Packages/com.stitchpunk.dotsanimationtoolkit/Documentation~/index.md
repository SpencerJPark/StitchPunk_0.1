# DOTS Animation Toolkit — Documentation

A DOTS-native animation toolkit for Unity Entities projects: 2.5D cutout
transform-track animation, flipbook sprite animation, and vertex/bone
animation textures (VAT) for crowd-scale instancing. Clips are authored as
ScriptableObjects, baked into blob-asset registries, and sampled by
Burst-compiled systems. Root C# namespace: `StitchPunk.AnimationToolkit`
(`.Authoring` and `.Editor` sub-namespaces).

New here? Start with [`getting-started.md`](getting-started.md) for an
end-to-end first-run walkthrough. This page is the map.

## Concept model

```
RigAsset ──defines──> Targets (stable-id'd named slots) + Layers (ordered priority slots)
   ▲                        ▲
   │ scoped to              │ tracks bind to targets by TargetId
ClipAsset ──contains──> TransformTracks / SpriteTracks / EventMarkers / (optional) VatSource
   ▲
   │ registered in
ClipSetAsset ──references──> RigAsset + N ClipAssets + (optional) VatTextureSetAsset
   ▲                                                        ▲
   │ bound by                                               │ produced by the VAT bake window from
ActorAuthoring (prefab root) ── children ──> RigTargetAuthoring parts (quads / VAT meshes / flipbook quads)
```

- **`RigAsset`** — the skeleton of *slots*, not bones: named, stable-id'd
  **targets** (the parts an actor can animate) and ordered **layers**
  (compositing priority — index *is* meaning, so reordering layers is a
  content edit, not a rename). Also carries the left/right mirror-pair table
  the Mirror Clip utility uses.
- **`ClipAsset`** — one authored animation against a specific rig: duration,
  loop mode, blend-in/out defaults, transform tracks, sprite tracks, event
  markers, and (optionally) a source `AnimationClip` to bake into VAT.
- **`ClipSetAsset`** — the registry: one rig, the clips authored against it,
  and (if any clip has a VAT source) the baked `VatTextureSetAsset`. This is
  what an actor references and what the bake turns into one
  `BlobAssetReference<ClipRegistryBlob>`.
- **`ActorAuthoring`** (on a prefab root) — bakes to the runtime actor entity:
  the shared registry blob, one `PlaybackLayer` per rig layer, the command and
  event buffers, and the actor-space rest bounds.
- **`RigTargetAuthoring`** (on each animatable child) — binds that child to
  one of the rig's targets by stable id and bakes its rest pose and technique
  components.

Identity throughout is a **stable, random, folded-GUID id** — never a name, a
list position, or an enum ordinal — so renaming, reordering, or moving an
asset never breaks a reference. See the rig/clip/set source files under
the package's `Authoring` folder for the exact fields.

## Choosing a technique

An actor is built from **parts**, and each part picks its own technique — a
VAT torso and a flipbook face can coexist on one actor. The two runtime
techniques:

| | Transform tracks | VAT (vertex animation texture) |
|---|---|---|
| What's authored | Keyed 3D position/rotation/scale per target, in the Clip Editor | A source `AnimationClip` on a skinned mesh, baked offline into a texture |
| What plays it | `TransformSampleSystem` / `TransformApplySystem` | `VatMaterialSystem` + a VAT-aware shader (`ToolkitVat.hlsl`) |
| Best for | Anything keyed part-by-part — 2.5D paper-doll characters, and 3D props and vehicles alike, since position, rotation and scale are all three-axis; cheap, precise, blends properly | Organic or skinned motion at crowd scale — hundreds to thousands of instances in one draw |
| Cost shape | Per-part transform writes, scales with part count | One texture read per bone/vertex influence in the vertex shader; instance count is nearly free once baked |
| Blend quality | Proper keyframe interpolation and crossfade | Linear frame-to-frame lerp — correct within one clip, can look "rubbery" across very different poses in a long crossfade |

Flipbook sprite frames (`Texture2DArray` slice or atlas rect) are a third,
lighter option for parts that are just swapping a 2D image — a face, an icon,
a simple creature — and compose with either technique on other parts of the
same actor. Billboarding (`ActorBillboard`, `ToolkitBillboard.hlsl`) is an
orthogonal per-target render modifier, not a technique of its own — it can sit
on top of any of the above.

## Windows and inspectors

| Window / inspector | Menu | What it's for |
|---|---|---|
| Clip Editor | Window ▸ DOTS Animation Toolkit ▸ Clip Editor | Timeline authoring for a `ClipSetAsset`'s clips. A dock of three zones — clips and rig hierarchy on the left, viewport in the middle, inspector on the right — over a timeline. Every boundary drags, and each position is remembered. The viewport renders from the moment the window opens, with or without a selection, and objects and bones can be clicked in it directly. Clip sets are created from the toolbar; clips are created, renamed and deleted from the Clips pane. Transform values are live for the current selection and update as you scrub; W/E/R gizmos and the numeric fields write through one path; keys are box-selectable on per-channel rows with editable easing, including Bézier tangent handles. |
| VAT Bake | Window ▸ DOTS Animation Toolkit ▸ VAT Bake | Wizard over the VAT texture baker: pick a source prefab and a clip set, choose bone or vertex flavour, bake to a `VatTextureSetAsset`. |
| `RigAsset` inspector | Select a `RigAsset` | Target and layer lists, mirror-pair table. |
| `ClipSetAsset` inspector | Select a `ClipSetAsset` | Clip roster with a per-clip validation status column. |
| `VatTextureSetAsset` inspector | Select a generated `VatTextureSetAsset` | Read-only bake stats (format, memory, per-clip frame ranges). |
| `ActorAuthoring` inspector | Select a GameObject with `ActorAuthoring` | Starting-layer editor. |

The clip inspector and clip-set inspector share the same `ClipValidation` rule
set the bake enforces, so a problem you see in the editor is the same one that
would fail the bake.

## Runtime API surface

- **`AnimationCommandUtil`** (Runtime/Api) — the write side: `Play`, `Queue`,
  `Stop`, `SetSpeed`, `SetTime`. Always pairs an `AnimationCommand` buffer
  append with enabling `AnimationCommandPending` — that pairing is why you
  call this instead of writing buffer elements by hand.
- **`PlaybackQuery`** (Runtime/Api) — the read side: query a layer's current
  clip, normalized time, and finished state.
- **`ToolkitWorldControl.SetEnabled(world, enabled)`** — the supported way to
  turn the whole toolkit on or off in a world (stops every system, timers
  included). To hide actors while keeping timers running, disable the
  `AnimVisible` enableable on them instead — that's a different question (the
  visibility boundary, not the world switch).
- **`ClipId` / `TargetId`** (Runtime/Identity) — the 64-bit and 32-bit stable
  id types commands and tracks are keyed by.

## Editor tooling not covered above

- **Mirror Clip** (Editor/ClipUtilities) — reflects an authored clip against
  the rig's mirror-pair table, for building a matching opposite-facing clip
  without re-authoring keys by hand.
- **`FacingResolver`** (Runtime/Sampling) — pure functions mapping a movement
  direction to "which clip, mirrored or not" across 2/4/8-direction facing
  sets.
- **Sockets** (Runtime/Identity, Blobs, Components, Systems;
  Authoring/Assets, Build, Baking) — named attachment points that resolve to a
  world transform each frame, following either a rig part directly or a baked
  bone of a VAT source.

The Mirror Clip utility, `FacingResolver`, sockets, and several custom
inspectors are the newest code in this package and have not yet been through
this project's compile-and-test gate — see the **README**'s "Not
battle-tested yet" section and the `CHANGELOG` before depending on them in a
shipping project.

## Further reading

- [`getting-started.md`](getting-started.md) — create a rig, author a clip,
  wire up an actor, play it.
- [`shader-contract.md`](shader-contract.md) — the full CPU↔GPU per-instance
  property contract, one section per HLSL include, and a troubleshooting
  table for the most common integration mistakes.
- The package `README.md` — feature status, requirements, installation.
- `CHANGELOG.md` — what shipped, phase by phase.

## Running the tests

Open **Window ▸ General ▸ Test Runner**. The **EditMode** tab covers packaging
conformance, sampling/event math, validation, bake determinism, and
shader-source conformance. The **PlayMode** tab covers entity baking and
system/world integration — it is Editor-only by design, because Unity's
baking pipeline (which these fixtures drive directly) has no player-side
equivalent; "Run all tests (Player)" cannot execute it.

Expect the Console to carry toolkit errors and warnings after a PlayMode run:
several acceptance tests deliberately provoke a diagnostic (an unknown target
id, a clip set that fails validation) and assert on its content. Every other
test is held to zero unexpected errors.
