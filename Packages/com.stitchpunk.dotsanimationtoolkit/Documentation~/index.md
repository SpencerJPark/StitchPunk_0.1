# DOTS Animation Toolkit

## What this package is

The DOTS Animation Toolkit (`com.stitchpunk.dotsanimationtoolkit`) is a
DOTS-native animation toolkit for Unity Entities projects. It targets 2.5D
character animation built from composed techniques:

- **Transform tracks** — keyed position / rotation / scale animation of 2D
  cutout ("paper doll") part quads.
- **Flipbook** — sprite-frame animation via `Texture2DArray` slices or atlas
  rects.
- **VAT (vertex / bone animation textures)** — prebaked skinned animation
  sampled in the vertex shader, for crowd-scale instancing.
- **Billboarding** — a per-target render modifier, combinable with the above.

Clips are authored as ScriptableObjects, baked into blob-asset registries, and
sampled by Burst-compiled systems. The C# root namespace is
`StitchPunk.AnimationToolkit` (with `.Authoring` and `.Editor` sub-namespaces).
The package is developed against a fixed, reviewed architecture; features are
built module by module, and this manual only documents what actually exists in
the installed version.

## Current status: pre-release 0.2.0, build step C1

This version contains the **data and sampling layer**: the types, blob schema,
and pure math the animation systems will be built on. Nothing drives an entity
yet. What exists today:

- The package manifest (identity, Unity 6000.5 minimum, pinned dependencies),
  five assembly definitions, and the `InternalsVisibleTo` grants from the
  Authoring assembly to the Editor and test assemblies.
- **Identity types** — `ClipId` (64-bit stable clip identity) and `TargetId`
  (32-bit rig-target identity), both reserving 0 as "none/invalid".
- **Blob schema** — the baked `ClipRegistryBlob` and its clip, transform-track,
  sprite-track, key, event-marker and VAT-info payloads. Blobs carry metadata
  and keys only; textures are resolved through components, never stored inside
  a blob.
- **Runtime components** — the actor-root, per-part, and singleton component
  set, including the `[MaterialProperty]` components that carry animation state
  into DOTS-instanced draws.
- **Sampling and composition math** — `ClipSampler` (easing, loop/clamp/ping-pong
  time mapping, track sampling, pose blending, layer composition, sample-rate
  quantization), `EventWrapMath` (wrap-correct event crossings), and
  `ClipRegistryUtil` (binary-search id resolution). These are pure static
  functions with no ECS world dependency, so editor preview and runtime share
  one implementation.
- Packaging conformance tests plus 96 EditMode tests covering the sampling math
  and asserting the blob and component layouts against the architecture.

What does **not** exist yet: all systems, authoring asset types, bakers,
shaders, editor windows, and samples. Do not install this version expecting to
animate anything — nothing here is wired into a running world.

## Installing

The package is currently developed as an embedded package inside its host
repository, under `Packages/com.stitchpunk.dotsanimationtoolkit`. To try it in
another project:

1. Use Unity 6000.5 or newer.
2. Copy the `com.stitchpunk.dotsanimationtoolkit` folder into your project's
   `Packages/` directory (Unity picks up embedded packages automatically), or
   reference it from `Packages/manifest.json` with a `file:` entry.
3. The dependencies declared in `package.json` (Entities 6.5.0, Entities
   Graphics 6.5.0, Burst 1.8.29, Collections 6.5.0, Mathematics 1.4.0,
   URP 17.5.0) resolve automatically through the Package Manager.

Note: per `LICENSE.md`, this package is not yet licensed for redistribution.

## Running the tests

Open **Window ▸ General ▸ Test Runner**. The **EditMode** tab lists the
packaging conformance tests and the C1 data/sampling suites under
`StitchPunk.AnimationToolkit.Tests.EditMode`; the **PlayMode** tab lists the
PlayMode assembly smoke test. The packaging tests read the real files of the
installed package from disk, and the contract tests assert the shipped blob and
component layouts by reflection — together they are the mechanical check that
the package matches its architecture contract.
