# DOTS Animation Toolkit

A DOTS/ECS-native animation toolkit for Unity 6.5 Entities projects: 2.5D cutout
transform-track animation, flipbook sprite animation, and vertex/bone animation
textures (VAT) for crowd-scale instancing — all sampled by Burst-compiled
systems, with clips baked into shared blob-asset registries.

## Why this instead of Unity's own animation

`Animator`/Mecanim is GameObject-shaped: one controller graph, one component,
one skinned mesh per character. That model doesn't survive an ECS world with
thousands of instanced actors. This toolkit is built the other way round:

- **ECS-native from the data up.** Clips bake into a `BlobAssetReference`
  registry shared by every actor on the same clip set (deduplicated through the
  `BlobAssetStore`); playback state lives in plain components on the entity,
  not in a managed controller object.
- **Burst-compiled sampling.** Track sampling, layer composition
  (Override/Additive), easing, loop/ping-pong time mapping, and event
  wrap-crossing math are pure static Burst functions with no managed
  allocation on the hot path.
- **Crowd-scale VAT.** Skeletal or per-vertex animation is prebaked into a
  texture and played back per-instance through DOTS-instanced material
  properties, so a crowd is one Batch Renderer Group draw rather than one
  `SkinnedMeshRenderer` per instance.
- **Composable techniques, one sampler.** Transform-track cutout animation,
  flipbook sprites, and VAT can live on the same actor — a VAT torso next to a
  flipbook face — because the technique is a property of the *part*, not the
  actor.

## Who it's for

Unity DOTS/Entities projects (Unity 6.5+) building 2.5D or crowd-scale
character animation — top-down or side-scrolling cutout characters,
sprite-based creatures, or instanced crowds — that want animation state and
sampling living in ECS instead of bridging out to a GameObject `Animator` per
entity.

## What it does today

- **Authoring:** `RigAsset` (targets + layers + mirror pairs), `ClipAsset`
  (transform tracks, sprite tracks, event markers, optional VAT source), and
  `ClipSetAsset`, all created from **Assets ▸ Create ▸ DOTS Animation
  Toolkit**. Stable, rename/reorder/move-safe identity (folded-GUID ids, never
  name-derived) and a full validation rule set shared by the inspectors, the
  Clip Editor, and the bake.
- **Deterministic bake:** `ClipRegistryBuilder` turns a `ClipSetAsset` into a
  content-hashed blob; the same assets produce the same bytes on every machine
  and every session.
- **Entity baking:** `ActorAuthoring` + `RigTargetAuthoring` bake a prefab into
  the runtime actor/part archetype — command buffer, playback layers, event
  output, rest bounds, the lot.
- **Runtime playback:** a command → layer state machine (`Play`/`Queue`/
  `Stop`/`SetSpeed`/`SetTime` via `AnimationCommandUtil`) with crossfade,
  wrap-correct event emission, per-clip bounds updates, and an opt-in
  distance-based LOD policy.
- **Two animation techniques, composable per part:** keyed transform tracks
  for 2D cutout parts, and flipbook sprite frames (`Texture2DArray` slice or
  atlas rect) for sprite parts.
- **VAT (vertex animation textures):** `VatTextureBaker` bakes a skinned mesh's
  clips into a bone-matrix or vertex-position texture (Editor ▸ **Window ▸
  DOTS Animation Toolkit ▸ VAT Bake**); `VatMaterialSystem` drives playback
  per-instance at runtime, including a two-frame crossfade. A clip may carry
  **per-target VAT sources** (`vatTracks`), so a torso and a cape can come from
  different source animations inside one clip, each resolving its own baked
  frame range.
- **Animation events, two channels from one marker:** a one-frame pulse
  carrying an int/float payload (`AnimEventOutput`) for sounds and spawns, and
  a sustained window bit (`AnimEventMask`) testable on any frame for damage or
  invulnerability. See
  [`Documentation~/animation-events.md`](Documentation~/animation-events.md).
- **Shaders:** four standalone HLSL includes
  (`ToolkitBillboard`/`Flipbook`/`Vat`/`Instancing.hlsl`) meant to be dropped
  individually into your own shaders, plus three hand-written reference
  shaders (sprite, VAT crowd, and a composite example). See
  [`Documentation~/shader-contract.md`](Documentation~/shader-contract.md) for
  the full CPU↔GPU property contract.
- **Clip Editor:** a UI Toolkit timeline window (**Window ▸ DOTS Animation
  Toolkit ▸ Clip Editor**) — track lanes, transport, a live preview pane
  driven by the runtime's own sampling code (not a divergent editor copy), and
  per-gesture undo.
- A large automated test suite backs the above: 340+ EditMode tests
  (validation, identity stability, bake determinism, sampling/event math,
  VAT baking, shader-source conformance) and 200+ PlayMode tests (entity
  baking, system behaviour, socket resolution, VAT playback).

## Not battle-tested yet

The socket/attachment system, the `FacingResolver` 2/4/8-direction helper, the
Mirror Clip editor utility, the clip-editor preview, and the custom inspectors
(`RigAsset`/`ClipSetAsset`/`VatTextureSetAsset`/`ActorAuthoring`) **compile
clean and pass the EditMode and PlayMode suites.**

What they have not had is *use*. The suites cover their logic, not their
ergonomics. Expect rough edges in the authoring flow rather than incorrect
maths.

Two of these are no longer only unit-covered. Sockets: two PlayMode fixtures
drive one through a real frame — a `Play` command, the clip sampled,
`TransformApplySystem` writing the part, and the socket resolved from that same
frame's transform. The VAT bake: sixteen EditMode fixtures bake procedural
skinned meshes, covering the texture-layout contract, the soft-failure paths,
and that an animated clip actually produces differing frames.

What remains genuinely unexercised is the bake's **output rendering** — no test
puts a baked texture on a mesh and looks at it, which is a job for eyes rather
than for assertions.

## Not shipped yet

- **Two samples ship**, `Quick Start Actor` and `Composite Actor`, both of which
  *generate* their assets rather than shipping `.asset` files — committed assets
  carry baked-in stable ids that could collide with a project already using this
  package. The `VatCrowd` sample the design doc calls for is not packaged yet.
- No package-shipped sample drives a **bone socket** end to end; bone sockets
  need a VAT rebake to populate their sample tracks.

## Requirements

| | |
|---|---|
| Unity | 6000.5 or newer |
| com.unity.entities | 6.5.0 |
| com.unity.entities.graphics | 6.5.0 |
| com.unity.burst | 1.8.29 |
| com.unity.collections | 6.5.0 |
| com.unity.mathematics | 1.4.0 |
| com.unity.render-pipelines.universal | 17.5.0 |

Package id: `com.dotsanimationtoolkit`. Version: see
`package.json` (pre-release). Root C# namespace: `DotsAnimationToolkit`
(with `.Authoring` and `.Editor` sub-namespaces).

## Installing

This package currently develops as an **embedded package** inside its host
repository. To use it in another project:

1. Use Unity 6000.5 or newer.
2. Copy the `com.dotsanimationtoolkit` folder into your project's
   `Packages/` directory, or reference it from `Packages/manifest.json` with a
   `file:` entry.
3. The dependencies above resolve automatically through the Package Manager.

Per `LICENSE.md`, this package is proprietary and not yet licensed for
redistribution.

## 60-second quick start

See [`Documentation~/getting-started.md`](Documentation~/getting-started.md)
for a full walkthrough: create a rig, author a clip in the Clip Editor, wire
up an actor prefab, and send it its first `AnimationCommand`.

## Further reading

- [`Documentation~/index.md`](Documentation~/index.md) — the doc hub: concept
  model, technique choice, window map.
- [`Documentation~/getting-started.md`](Documentation~/getting-started.md) —
  end-to-end first-run walkthrough.
- [`Documentation~/shader-contract.md`](Documentation~/shader-contract.md) —
  the shader integration contract.
- `CHANGELOG.md` — what shipped in each version.
