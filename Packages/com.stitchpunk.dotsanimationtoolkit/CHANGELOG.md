# Changelog

All notable changes to the DOTS Animation Toolkit are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.2.0] - Unreleased

Phase C build step C1: the M3 data slice — identity types, the baked blob
schema, the runtime component inventory, and the pure sampling/composition
math. No systems, authoring assets, bakers, shaders, or editor tooling ship in
this step; those land in build steps C2 through C8.

### Added

- Identity types `ClipId` (64-bit stable clip identity) and `TargetId` (32-bit
  rig-target identity) from architecture section 3.4, both reserving 0 as
  "none/invalid" and ordered for binary search.
- The architecture section 4.2 blob schema: `ClipRegistryBlob` with its
  `ClipBlob`, `TransformTrackBlob`, `TransformKeyBlob`, `SpriteTrackBlob`,
  `SpriteKeyBlob`, `EventMarkerBlob` and `VatTextureInfoBlob` payloads. Blobs
  store metadata and keys only — never textures or other Unity objects.
- The architecture section 5.2 runtime components: the actor-root set
  (`ClipRegistry`, `PlaybackLayer`, `AnimationCommand`, `AnimEventOutput`,
  `RigPartRef`, `SampleSettings`, `AnimLod`, `VatTextureBinding` and the
  enableable tags), the per-part set (`RigPartBinding`, `TargetRestPose`,
  `TargetPose`, `VatDriven`), and the world singletons.
- The six `[MaterialProperty]` components carrying animation state into
  DOTS-instanced draws, bound to the architecture section 6.2 shader property
  names (`_ImageIndex`, `_AtlasFrame`, `_VatFrameA`, `_VatFrameB`, `_VatBlend`,
  `_BillboardParams`).
- `ClipSampler`: easing, loop-mode resolution, Once/Loop/PingPong time mapping
  including negative (reverse) time, transform and sprite track sampling, pose
  blending, bottom-up layer composition with Override masking and additive
  stacking, and per-entity phased sample-rate quantization.
- `EventWrapMath.CollectCrossings`: wrap-correct event collection across
  forward, reverse, single-wrap, multi-wrap and ping-pong-reflection cases.
- `ClipRegistryUtil`: binary-search clip-id and target-id resolution through the
  registry's sorted-id / dense-index indirection.
- 96 EditMode tests covering the sampling and event math and asserting the blob
  and component layouts against the architecture by reflection.

### Changed

- Added `Unity.Mathematics.Extensions` to all four non-Editor assembly
  definitions. `Unity.Mathematics.AABB` is defined there, and architecture
  section 5.9's bounds system must write `RenderBounds.Value`, so without the
  reference that system could not compile.

## [0.1.0] - Unreleased

Phase C build step C0: the package skeleton. No runtime, authoring, editor, or
shader features ship in this step; those land in build steps C1 through C8.

### Added

- `package.json` with the architecture section 1.1 identity: package id
  `com.stitchpunk.dotsanimationtoolkit`, display name "DOTS Animation Toolkit",
  Unity `6000.5` minimum, and pinned dependencies (Entities 6.5.0,
  Entities Graphics 6.5.0, Burst 1.8.29, Collections 6.5.0, Mathematics 1.4.0,
  URP 17.5.0). The samples list is empty until build step C8.
- The five assembly definitions from architecture section 1.3:
  `StitchPunk.AnimationToolkit.Runtime` (unsafe code enabled for blob-building
  helpers), `StitchPunk.AnimationToolkit.Authoring`,
  `StitchPunk.AnimationToolkit.Editor` (Editor platform only),
  `StitchPunk.AnimationToolkit.Tests.EditMode` (Editor platform only), and
  `StitchPunk.AnimationToolkit.Tests.PlayMode`.
- `InternalsVisibleTo` grants from the Authoring assembly to the Editor assembly
  and both test assemblies (architecture section 8 M1: internal `stableId`
  fields are read by editor tooling and tests).
- Packaging conformance tests (a) through (e) from architecture section 8 M6 in
  the EditMode test assembly, plus supplementary package-manifest identity,
  dependency-pinning, and unsafe-code-flag checks.
- A PlayMode smoke fixture proving the PlayMode test assembly compiles and
  loads under its contracted name.
- `Documentation~/index.md` describing the toolkit, its current pre-release
  state, installation, and how to run the conformance tests.
- `LICENSE.md` proprietary notice and this changelog.
