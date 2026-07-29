# Changelog

All notable changes to the DOTS Animation Toolkit are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.3.0] - Unreleased

Phase C build step C2: the M1 authoring slice — the authoring ScriptableObjects,
stable identity generation, the validation rule set, and `ClipRegistryBuilder`,
the deterministic ScriptableObject-graph-to-blob bake. Entity baking, systems,
shaders, and editor tooling still do not ship; those land in build steps C3
through C8.

### Added

- The architecture section 3.1 to 3.3 authoring assets: `RigAsset` (with
  `RigTargetDefinition`, `LayerDefinition` and `MirrorPair`), `ClipAsset` (with
  `TransformTrack`, `TransformKey`, `SpriteTrack`, `SpriteKey`, `EventMarker`
  and `VatClipSource`), `ClipSetAsset`, and the generated `VatTextureSetAsset`
  (with `VatClipRange`). Rigs, clips and sets are creatable from the
  **Assets ▸ Create ▸ DOTS Animation Toolkit** menu.
- `StableIdUtility`: the architecture section 3.4 identity generator. Ids are
  folded GUIDs — random, never name-derived — so a rename, a list reorder, or an
  asset move can never change identity, and 0 stays reserved as none/invalid.
  Every identity-bearing asset self-assigns on creation and on deserialization.
- `ClipValidation` plus `ValidationMessage`, `ValidationCode`,
  `ValidationSeverity` and `ValidationStage`: the single authoritative
  implementation of the architecture section 3.5 rule table V01 to V14, shared by
  the inspectors, the clip editor and the bake. Rule V08 (stale VAT source hash)
  is an editor-only rule: detecting it requires recomputing the hash from the
  current sources, which needs the Editor-side VAT baker, so a bake cannot
  evaluate it and does not claim to (architecture amendment A12).
- `ClipRegistryBuilder.Build`: the architecture section 4.2/4.5/4.6 bake. It
  applies the canonical ordering (clips by ascending clip id, targets by
  ascending target id defining the dense index, tracks by dense target index with
  authoring order breaking ties, keys and markers by normalized time), the
  canonical value conversions (degrees to radians, resolved loop mode, blend
  defaults clamped to the clip duration, duplicate clip entries deduplicated),
  the conservative per-clip bounds, and the `xxHash3` content hash that becomes
  the `BlobAssetStore` dedup key. A set carrying validation errors throws
  `ClipValidationException` listing the offending rule codes instead of baking.
- 66 EditMode tests: one fixture per validation rule that asserts the rule fires
  and nothing else does, id generation and stability across rename, reorder,
  duplication and a serialization round trip, canonical ordering and value
  conversion, and determinism fixtures comparing both the content hash and a
  field-by-field signature of the built blob across repeated and shuffled builds.

### Changed

- **Blob layout (schema version 2).** `clipIndexById` is removed: the canonical
  ordering sorts clips by ascending id, so a clip's dense index is its position
  in `sortedClipIds` and the indirection was the identity map in every blob the
  package can emit. `ClipRegistryUtil.TryResolveClip` returns the binary-search
  position directly (architecture amendment A11).
- **`ClipBlob.localBounds` renamed to `offsetBounds`** to name the space it is
  actually computed in. Transform keys are local offsets and rest poses live on
  the prefab, which the authoring assembly cannot read, so the bake's union is
  offset space — not actor space. The entity baker combines it with rest poses
  into the new `ActorRestBounds` component (architecture amendment A13).

### Fixed

- **The content hash did not cover the whole blob.** `sortedTargetIds`,
  `targetBoundsExtents`, all four `vatInfo` fields and `ClipBlob.debugName` were
  absent from the hashed stream that keys the `BlobAssetStore`, so an edit
  confined to them returned a stale blob — rebaking VAT textures to a new
  `textureWidth` with unchanged frame ranges being the concrete case. The stream
  now visits every field, with each array preceded by its element count, and new
  fixtures assert the general property that a blob which differs must hash
  differently (architecture amendment A10).
- Documented `ClipRegistryBuilder`'s hash mechanism as the `xxHash3` streaming
  state it has always used. The architecture's `UnsafeAppendBuffer` formulation
  cannot be implemented in the Authoring assembly, which is not permitted unsafe
  code; the two are byte-for-byte equivalent (architecture amendment A5).
- Added `TryComputeContentHash`, so a baker can probe the `BlobAssetStore` before
  deciding to build instead of allocating a blob it may immediately discard.

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
