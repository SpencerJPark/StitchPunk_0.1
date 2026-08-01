# Changelog

All notable changes to the DOTS Animation Toolkit are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.4.0] - Unreleased

Phase C build step C3: entity baking — the M2 slice, excluding the VAT texture
baker. Actors and their parts now bake to entities; no system drives them yet.

### Added

- `ActorAuthoring` + `ActorBaker`: builds the registry blob from the referenced
  clip set and registers it with `AddBlobAssetWithCustomHash`, produces the whole
  architecture section 5.2 root archetype with its contractual enableable states,
  and seeds authored starting layers with clip ids already resolved to dense
  indices. Uses the probe/store-hit/build/register pattern, so a store hit costs
  no persistent allocation and leaves nothing to dispose.
- `RigTargetAuthoring` + `RigTargetBaker`: the part archetype, rest pose captured
  from the authoring transform, and technique material-property components per
  `TargetKind`, including the material-versus-texture-set validation of section
  4.4.
- `RigBindingBakingSystem`: resolves each part's target id to its dense index and
  records the binding on both ends. The resolve job is scheduled single-threaded
  because it writes into other entities' buffers, which would race in parallel.
- `ActorRestBounds` is now produced, in actor space, by walking each part's full
  transform chain.
- PlayMode baking tests. The suite is **Editor-only**: Unity's baking pipeline
  has no player-side equivalent, so the assembly declares the Editor platform
  (architecture amendment A17).

### Changed

- `RigPartBakeLink` carries the authoring object's hierarchy path as a
  `FixedString128Bytes` instead of a hash of it, and all four of
  `RigBindingBakingSystem`'s diagnostics now name the part: `Rig part
  'Rig/Torso/LeftArm' claims target id 100, which another part …` in place of
  `Rig part entity 41:1 (authoring path hash 2463534242) …`. The job stays
  Burst-pure — a `FixedString` is blittable — and the entity index, which is not
  stable between bakes, is gone from the messages (architecture amendment A21).
  `AuthoringPathHash.Of` remains for `SampleSettings.phase01`, which needs a
  number rather than text.
- A part whose actor bailed out of its own bake no longer restates that failure.
  A missing or invalid clip set produced one actionable error from `ActorBaker`
  and then one unactionable copy per part, burying it.
- `ActorBaker`'s sample phase takes bits 8–31 of the path hash rather than the
  low 24. FNV-1a's last step is a multiply, so its low bits carry the least
  avalanche and sibling names differing in one character landed on adjacent
  phases — the opposite of what the phase is for.

- No baked value derives from `Object.GetInstanceID` or `Object.GetEntityId` any
  longer. Both are session-local, so baking either made the same prefab produce
  different bytes every session. Per-object numbers now come from
  `AuthoringPathHash`, keeping bakes reproducible (amendment A18).
- `SampleSettings` carries `[System.Serializable]` so it can be an inspector
  field on `ActorAuthoring` (amendment A20).

### Fixed

- **`TargetRestPose` could go stale under incremental baking.** `RigTargetBaker`
  read `authoring.transform` directly, which does not register a bake dependency,
  so moving a part in the Editor moved its rendered position — transform baking
  tracks its own components — while the rest pose kept the value captured at the
  last full bake. Every animated pose is composed as an offset from that value,
  so the part animated around a stale origin until something unrelated forced a
  rebake. The transform now comes from `GetComponent<Transform>`, matching what
  `ActorBaker` already did.
- **Baking threw on non-ASCII GameObject names.** The diagnostic path builder
  budgeted 110 *characters* against a `FixedString128Bytes` capacity of 125 UTF-8
  *bytes*, then used the throwing `FixedString128Bytes(string)` constructor. A
  hierarchy of roughly 42 CJK characters stayed under the character guard while
  exceeding the byte capacity, so `CheckCopyError` threw out of
  `RigTargetBaker.Bake` and the part lost its rest pose, output pose and
  technique components — a hard bake failure caused purely by naming objects in a
  non-Latin script. The budget is now counted in UTF-8 bytes, truncation steps
  whole characters so a surrogate pair is never split, and the copy goes through
  `CopyFromTruncated`, which cannot throw. Covered by `AuthoringPathTests`.
- **An actor that lost its registry could fail silently.** `RigBindingBakingSystem`
  said nothing whenever a part's actor carried no `ClipRegistry`, which was
  correct only because each of `ActorBaker`'s bail-outs happened to log first —
  a coupling nothing asserted or enforced. `ActorBaker` now writes an
  `ActorBakeFailed` baking tag when it stops, and the binding pass suppresses
  only on that tag; an unexplained missing registry is reported instead of
  passing in silence (amendment A22).
- **Ancestor edits did not retrigger the bake.** Both hierarchy-path walks read
  ancestor names straight off `Transform`, registering no dependency, so renaming
  or reparenting an ancestor left `SampleSettings.phase01` and a part's recorded
  authoring path at their previous values — an incremental bake and a clean bake
  of the same scene produced different bytes. Names now come from
  `IBaker.GetName` and the chain from `IBaker.GetParents`. Sibling reordering
  remains untracked, since Entities exposes no dependency for it; it affects only
  the sampling phase (amendment A18).

### Changed (C3 re-review)

- The unknown-target-id error is now normatively `RigTargetBaker`'s, which can
  name the object, the rig and the id and attach a click-to-select context, and
  which withholds `RigPartBakeLink` so the Bursted pass never sees the part. The
  binding pass keeps the two failures only it can see. Recorded as **amendment
  A22**; the previous split had moved silently, leaving the architecture, three
  doc comments and the code each stating something different. Two guards that
  had become unreachable by construction were deleted.
- `AnimLod` is documented as opt-in and its absence as the conformant baseline
  archetype (**amendment A23**); `ActorRestBounds` and `ClipBlob.offsetBounds`
  are documented as combined at runtime rather than at bake (**amendment A24**),
  resolving a contradiction between architecture sections 4.6 and 5.8.
- `ClipRegistryBuilder.BuildInvocationCount`, a test seam, is now behind
  `#if UNITY_EDITOR` and incremented atomically. The Authoring assembly compiles
  into player builds, so the counter previously shipped and the public `Build`
  mutated it there.
- The baking test harness suppresses `LogAssert` for the duration of a bake — the
  host's own baking systems log into the same window — and now replaces the
  guarantee that removed: every acceptance test is held to zero unexpected
  toolkit errors unless it declares otherwise.
- `Tests/PlayMode/VatMaterialProbe.shader` retargeted from the built-in pipeline
  to URP. It is never rendered, but section 6 makes this package URP-only, and
  the file ships in the tarball unless `Tests/` is excluded — so a consumer
  project imports and variant-compiles a built-in-pipeline shader out of a
  URP-only package. A new packaging conformance test now fails on `CGPROGRAM`,
  `CGINCLUDE` or `UnityCG.cginc` anywhere in the package.

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
- 86 new EditMode tests — 192 in the suite: one fixture per validation rule that asserts the rule fires
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
- 98 new EditMode tests — 106 in the suite — covering the sampling and event math
  and asserting the blob and component layouts against the architecture by
  reflection.

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
