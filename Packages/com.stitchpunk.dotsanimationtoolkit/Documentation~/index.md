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

## Current status: pre-release 0.1.0, build step C0

This version contains the **package skeleton only**. What exists today:

- The package manifest (identity, Unity 6000.5 minimum, pinned dependencies).
- Five assembly definitions: `StitchPunk.AnimationToolkit.Runtime`,
  `StitchPunk.AnimationToolkit.Authoring`, `StitchPunk.AnimationToolkit.Editor`
  (Editor-only), and the EditMode / PlayMode test assemblies.
- `InternalsVisibleTo` grants from the Authoring assembly to the Editor and
  test assemblies.
- Packaging conformance tests that assert the package's structure against its
  architecture contract (assembly references, platform restrictions, no
  UnityEditor usage outside the Editor and test folders, no host-project
  references, no IMGUI in editor sources).
- This documentation skeleton.

What does **not** exist yet: all runtime components and systems, authoring
asset types, bakers, shaders, editor windows, and samples. Do not install this
version expecting to animate anything — it is the foundation the feature
modules are built on.

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
packaging conformance tests under `StitchPunk.AnimationToolkit.Tests.EditMode`;
the **PlayMode** tab lists the PlayMode assembly smoke test. All tests read the
real files of the installed package from disk — they are the mechanical check
that the package structure matches its architecture contract.
