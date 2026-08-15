# Changelog

All notable changes to the DOTS Animation Toolkit are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added — flipbook base indices and Bézier easing (schema version 5)

The data model behind the keying/dopesheet rework. The editing surface that
renders it lands in later phases; this is what it renders.

- **Flipbook tracks gain a per-track `baseIndex` and a per-key
  `SpriteIndexMode`** (`Absolute` / `RelativeToBase`). A relative key stores its
  **offset and nothing else**, so moving `baseIndex` retargets every relative key
  at once and no offset is recomputed or lost — the same mouth set slid onto a
  different character's block by editing one number. Storing the resolved index
  instead would make `baseIndex` a one-shot edit that quietly consumed itself.
  - **Two bases now compose, and both survive.** The per-key mode resolves
    against the track's authored `baseIndex` first, producing the track's value;
    `SpriteSliceSpace` (amendment A37) then decides whether that value replaces
    the pose's slice or is added to the rest slice the character's variant chose.
    Collapsing them would have cost one of the two retargeting behaviours — an
    authored base that moves a whole track, and a runtime base that follows the
    character's skin.
  - Several tracks may drive one target from different bases, which is how a
    single texture array holds independent feature sets: a mouth track based at 0
    and an eye track based at 32 animate the same part without either knowing the
    other exists.
  - `SpriteIndexResolver` is the only thing that resolves an index, shared by the
    Burst sampler, validation and (from the next phase) the editor's "+5 → 12"
    display. Three implementations of that arithmetic would eventually disagree,
    and the number an author reads would stop matching the frame that plays.
  - Rule **V14** is now scoped to absolute keys. A relative key's number is a
    displacement, so −3 is three frames back, not a malformed index; warning on it
    would have trained authors to ignore the rule.
  - New rule **V18**: a relative key that resolves below zero. The −1 "no change"
    sentinel belongs to absolute keys only, so there is nothing else it could mean.
- **`Interpolation.Bezier`**, shaped by two editable handles stored per key and
  evaluated in the Burst sampler — what you author is what plays, rather than an
  editor curve the bake approximates. The curve warps the segment's blend weight,
  as the four fixed curves already do, so one key still drives position, rotation
  and scale together.
  - Solved with Newton plus a bisection fallback, so the solve is bounded rather
    than merely usually fast.
  - An all-zero handle pair reads as **linear**, not as a degenerate curve. That
    is the value a key deserializes to when these fields did not exist, and the
    same defensive reading `BoneKey.localRotation` needs for an all-zero
    quaternion.
  - New rule **V17**: handles are confined to the unit square. x outside it makes
    the curve non-functional; y outside it is overshoot, which is well defined but
    breaks the bake's bounds union — section 4.6 unions the keys on the argument
    that every interpolation mode is monotonic between them, so a curve that
    travelled past its own keys would bake a box too small and cull parts that are
    still visible. **Overshoot is therefore not available yet**; lifting the limit
    means teaching `ComputeOffsetBounds` about curve extrema.
- **Schema version 4 → 5**, with the golden content hash re-recorded in the same
  commit. A version-4 blob read as version 5 would resolve every relative key
  against a base of zero and ease every Bézier key as linear.
- `sliceSpace` is in the content hash **for the first time**. It never was, which
  meant two clips differing only in whether their keys were absolute or
  rest-relative hashed identically — so flipping it left every consumer's baked
  registry looking current. Fixed here because a schema bump is the one moment it
  costs nothing.

### Changed — the clip editor is a persistent dock (§7.1)

- **Three zones over a timeline**, declared in `ClipEditorWindow.uxml` as nested
  `TwoPaneSplitView`s: a left column (clips over a hierarchy of the loaded rig's
  transforms), the viewport, and an inspector, above the timeline at roughly
  75/25. Every boundary is draggable and every position is stored in
  `EditorPrefs`, so the layout follows the person rather than resetting each
  time the window opens. The first open derives the timeline's height from a
  proportion of the window — a pixel default only lands on a quarter at one
  window size — and stores the result, after which it is an ordinary remembered
  position.
- **The viewport no longer depends on selection.** It initialises and renders
  when the window opens and keeps rendering through every selection change,
  showing a reference grid when there is no clip set, no rig, or nothing
  selected. Previously `Render` bailed out when no rig mirror existed and the
  window blanked the image whenever no clip was selected, which made an
  unselected editor indistinguishable from a preview that had failed to start.
  Selection now decides only two things: what the inspector shows, and where the
  selection marker sits. Double-clicking the viewport reframes the camera, which
  a camera that persists across selections needs and did not have.
- **The rig hierarchy replaces the bone-name dropdown.** A `TreeView` of the
  assigned prefab's transforms is the picker for bone tracks; bones the selected
  clip animates are bold, and selecting one offers **Add Bone Track** for that
  exact bone. A flat sorted name list could not distinguish two bones with
  similar names, which is the case where a typo silently bakes a bone at rest.
  The typed field remains for sets with no rig assigned.
- **All sizing and spacing moved to `ClipEditorWindow.uss`.** The window, ruler,
  lanes, playhead and validation badge no longer set inline layout styles —
  an inline style beats every rule in a stylesheet, so a leftover `style.height`
  is a value the sheet cannot override. Colours stay in C# where they encode
  state. The ruler's height and the track-header spacer's height are one USS
  custom property, because those two must agree for the header column and the
  lane column to line up.
- The timeline scrolls, since a quarter-height pane cannot show a deep clip's
  tracks; headers and lanes share one scroller so they cannot drift apart.
- Duplicate-bone-track warnings now go to the timeline's status line instead of
  the viewport's, which the preview tick overwrote thirty times a second.

### Added — clip and clip-set lifecycle in the editor

- **New** in the Clips pane creates a `ClipAsset` beside the set on disk, gives
  it the set's rig, appends it to the set as one undo step, and selects it. The
  rig is inherited rather than left empty because validation rule V06 only lets a
  clip join a set whose rig is the same asset — a clip created with a null rig is
  born failing validation.
  - The button is **disabled until a set is assigned**. A clip is only meaningful
    inside a set, so "no set" is not a case to invent a home for.
  - The creation itself moved into `ClipAssetUtility`, shared with
    `ClipSetAssetEditor`'s existing "new clip in set" button, which now calls it.
    A clip made from the window and one made from the inspector have to be
    indistinguishable — same folder, same inherited rig, same id minting, same
    undo entry — and two implementations would agree only until one was edited.
  - The asset write is not undoable and deliberately is not made so: Ctrl+Z does
    not delete a file. The append to the set's clip list is, under the name
    "Create Clip In Set".
- **Delete** in the Clips pane asks before it does anything, and offers both
  outcomes rather than making one word on a button carry the difference:
  - **Delete Asset** sends the clip file to the OS trash and un-registers it.
    Trash rather than `AssetDatabase.DeleteAsset`, because undo does not bring a
    file back and the trash is the only recovery a mis-click has.
  - **Remove From Set** un-registers it and leaves the asset on disk. A
    two-button dialog would have made this reachable only from the clip set's
    inspector, so anyone who meant "take it out of the set" would have had to
    confirm a deletion to get there.
  - **Delete Asset is deliberately not undoable, and the dialog says so.** An
    undoable removal would restore a set entry pointing at a file now in the
    trash — a missing reference that no validation rule reports. Remove From Set
    destroys nothing and stays undoable.
  - The set edit is applied before the file is trashed, so the set never holds a
    reference to an asset that no longer exists.
  - Deleting selects the neighbouring clip, so deleting several in a row does not
    need a manual re-select between each one.
- **New Set** in the toolbar creates a `ClipSetAsset` and loads it into the
  window. The location is asked for rather than derived: a clip is created beside
  its set because the set is a natural anchor, but a set is the root of the graph
  and has none, and a package guessing a folder is how projects end up with
  assets scattered wherever a tool felt like putting them. The rig is left empty
  for the same reason — inheriting whatever the window had open would bind a new
  set to a rig nobody chose, and validation prompts for it immediately.
- `ClipCreationUtility` became `ClipAssetUtility`, since it now covers the whole
  clip lifecycle rather than creation alone. It also absorbs the
  `DeleteArrayElementAtIndex` quirk `ClipSetAssetEditor` documented first — on an
  array of object references the first call only *nulls* the element and a second
  is needed to remove the slot — so the next caller does not rediscover it by
  shipping a set that reports one more clip than it shows.
- **A Name field in the clip inspector**, so a clip can be renamed where it was
  created. A clip's name is not cosmetic — the set's id-constant generator turns
  it into a C# identifier — and without this the flow was "create here, go to the
  Project window to name it". The field is delayed, because committing per
  keystroke would rename the asset on disk once per character typed. An illegal
  or already-taken name is refused and the field snaps back to the asset's real
  name rather than showing one it does not have.

### Added — click-to-select in the viewport

- **Clicking the preview selects the object under the cursor**, and selection is
  bidirectional: the viewport drives the hierarchy tree, the tree drives the
  viewport outline, and grabbing a bone key on the timeline drives both. A
  viewport click works by setting the tree's selection rather than by acting on
  its own, so the three surfaces cannot drift into meaning different things.
  - Picking resolves the **child** under the cursor, never the prefab root.
  - **Alt- or shift-click cycles** through overlapping hits, nearest first. The
    cycle only advances when the same click lands on the same candidates again;
    anything else resets to the nearest, so a modified click somewhere new does
    not open on whatever ordinal the last one left behind.
  - Hits are cast on pointer **down**, from the press position, and applied on
    release only if the pointer did not travel — a drag in the viewport orbits,
    and selecting on press would make every orbit reselect whatever the camera
    started over.
- **Bone handles.** Every joint of a `SkinnedMeshRenderer` gets an octahedral
  handle linked to its parent, drawn as one dynamic line mesh and rewritten each
  frame so it tracks the pose. The drawn radius and the clickable radius are one
  number, read from one place, because a click target that is not where the
  marker is reads as broken picking rather than as two constants disagreeing.
  Joints are ordered **ahead of** geometry rather than merged with it by
  distance: a bone sits inside the mesh it deforms, so sorting purely by depth
  would make it unclickable, which is the one thing handles exist to prevent.
- **Physics queries do not work in a preview scene** — `PhysicsScene.Raycast`
  against one returns nothing, verified rather than assumed, because the scene is
  never simulated. `Collider.Raycast` does work, since it tests one collider's
  shape instead of querying a broadphase, so colliders are walked individually.
  Renderer hits are bounds-level; `PreviewPickHit.isExact` records which kind a
  hit was, and per transform an exact hit always replaces an approximate one
  regardless of distance, since the two describe the same object and only one of
  them knows where its surface is.
- The selection highlight is now an **oriented box around the selected object**,
  built from the renderer's local bounds so it follows the object's rotation
  instead of swinging about as a world-axis-aligned box would. Transforms with no
  geometry — bones, empties — keep a fixed screen-relative box matching the joint
  handle that was clicked.
- The hierarchy tree is built from the preview's **live instance** rather than
  from the prefab asset, and takes its item ids from the preview's own transform
  index. A picked object is therefore literally a node of the tree's own source,
  and selection is identified by index rather than by name — a rig with two bones
  called `Hand` would otherwise have a tree row that selects the wrong one. Bone
  *tracks* still bind by name; that is a separate contract with the bake.
- The inspector names the selected object and says what it is (skinned bone,
  renderer, or plain transform), since the hierarchy lists every transform and
  only a skinned bone moves the mesh when a bone track drives it.

### Added

- `ClipEditorLayoutTests`: the UXML element names are a contract `Q<T>(name)`
  cannot check — a rename returns null and silently empties a pane — so the
  suite asserts every slot the window resolves, and that each split's fixed pane
  is the one the window stores and restores.

## [0.8.0]

### Added — attachment, authoring surface and packaging

- **Sockets.** Named attachment points that resolve to a world transform every
  frame. `RigTarget` sockets follow a part and need no baked data — the sampler
  already computes that transform. `Bone` sockets follow a bone of the VAT
  source rig, whose motion exists only inside a texture at runtime and so is
  sampled into `SocketRegistryBlob` at bake time.
  - Sockets live in their **own blob**, not `ClipRegistryBlob`. Folding them in
    would bump the clip schema and invalidate its golden content hash for every
    project, including those that never attach anything.
  - Baked samples rather than a second VAT texture: an attachment is an entity
    with a `LocalTransform`, so the consumer is the CPU, and reading a texture
    from the CPU means a readback that is slow, async, and unavailable when the
    texture is not marked readable in a build.
  - Attachments are transform **roots, not children** — the resolve system
    writes a world transform into `LocalTransform`, and parenting as well would
    apply the actor matrix twice.
- **Clip editor preview** (§7.3). A GameObject mirror posed by `ClipSampler` —
  the runtime's own functions — out of a registry built by the baker's own
  builder, so it cannot drift from what ships. Doubles as a validation surface:
  a set that fails to build shows the reason instead of rendering nothing.
  Rig-target sockets draw as markers so offsets are authorable rather than
  guesswork.
- **Clip editor transport, ruler, playhead, keyboard map, copy/paste and
  context inspector.** Undo is scoped per gesture, so one drag is one Ctrl+Z.
- **`FacingResolver`** — amendment A38's direction tables. Every direction set
  is closed under mirroring, so a facing is served by an east-side clip plus a
  mirror flag; a four-direction character costs two locomotion clips per state
  rather than four.
- **Mirror Clip utility** — duplicate a clip and flip it, for facings that must
  deviate from a pure reflection. Never combine with runtime `mirrorX` on the
  same facing: that is a double reflection, which is no reflection at all.
- **Inspectors** for `RigAsset` (with socket authoring and bone-name dropdowns),
  `ClipSetAsset` (clip roster, validation column, clip-id constant generation),
  `VatTextureSetAsset` and `ActorAuthoring`.
- **`VatMeshPreparer`** and its wiring into the bake window. Closes a real hole:
  `VatTextureSetAsset.runtimeMesh` was a field nothing in the package ever
  wrote, so a bake produced textures and no usable mesh. A mesh without bone
  influences in `UV1` does not error — it renders as a motionless clump.
- **`Docs/AnimationToolkit/shader-contract.md`** — the integration contract for
  the four standalone HLSL includes, so they are usable by someone other than
  their author.
- **Quick Start sample**, as a generator rather than shipped assets: committed
  `.asset` files carry baked-in stable ids and could collide with a project
  already using the package.
- **Disk round-trip test tier** (§11.1), closing amendment A36's debt — the
  serializer is part of the authoring contract, and a suite that builds every
  input in memory has no coverage of it.

### Fixed

- `FacingResolver.FromMovement` took a `float2` by value on a `[BurstCompile]`
  static. A Burst-compiled static is an external entry point and cannot take a
  struct by value, which failed Burst compilation for the entire Runtime
  assembly rather than just that method.

### Known limitations

- A clip may carry only **one** VAT source, so a torso and a cape cannot come
  from different source animations in the same clip. Note that hybrid
  flipbook + VAT on one actor *does* already work — VAT and sprite parts
  compose per part.
- Sockets, the clip preview and the VAT runtime-mesh step have not been
  exercised by PlayMode integration tests.

## [Unreleased]

Phase C build steps C4 through C7 (core), reconstructed from
`Docs/AnimationToolkit/` and the package's own shipped tree rather than from
dated releases — see the note at the end of this section for what is and is
not verified.

### Added — C4: the systems slice

- `AnimationToolkitSystemGroup` and its three child groups
  (`AnimationToolkitBindingSystemGroup`, `AnimationToolkitLogicSystemGroup`,
  `AnimationToolkitPresentationSystemGroup`) plus `ToolkitWorldControl`, the
  supported way for a host to enable/disable the whole toolkit in a world.
- `RigBindingSystem` — re-resolves a spawned actor's part bindings.
- `CommandApplySystem` + `PlaybackTimeSystem` — the `AnimationCommand` → layer
  state machine: play, queue (one deep), stop, set-speed, set-time, crossfade,
  loop/ping-pong/reverse time mapping, finish signaling.
- `EventEmissionSystem` — wrap-correct event marker emission into
  `AnimEventOutput`, gated by the `AnimEventsPending` enableable.
- `TransformSampleSystem` + `TransformApplySystem` — the transform-track
  (2D cutout) technique end to end, including `PostTransformMatrix`-based
  scale/flip.
- `SpriteMaterialSystem` — the flipbook technique: `_ImageIndex` (array slice)
  and `_AtlasFrame` (atlas rect) per-instance properties.
- `RenderBoundsUpdateSystem` — updates `RenderBounds` on clip change only, via
  the `BoundsDirty` enableable, not every frame.
- `AnimationLodPolicy` + `AnimLodDistanceSystem` — an opt-in
  (`AnimLod`-gated), distance-based sampling-rate/freeze policy with three
  levels.
- `ActorBillboardSystem` and the `ActorBillboard` component.
- A re-runnable smoke scene, generated into the host project by
  **Tools ▸ DOTS Animation Toolkit ▸ Build Smoke Scene**, used as the
  human-verification step for on-screen clip playback.

### Added — C5/C6: shaders and VAT

- `Shaders/Includes/`: `ToolkitBillboard.hlsl`, `ToolkitFlipbook.hlsl`,
  `ToolkitVat.hlsl`, `ToolkitInstancing.hlsl` — standalone HLSL with no
  `#include`s of their own, each usable independently in a host's own shader.
  Full contract in `Docs/AnimationToolkit/shader-contract.md`, mirrored into
  the package as `Documentation~/shader-contract.md` this cycle.
- Reference shaders: `ToolkitSpriteUnlit.shader`, `ToolkitVatCrowdUnlit.shader`,
  `ToolkitCompositeExample.shader` (billboard + flipbook composed in one
  hand-written shader).
- `Editor/VatBaking/VatTextureBaker.cs` — bakes a skinned mesh's clips into a
  bone-matrix or vertex-position VAT texture; point-filtered, clamped,
  loop-safe (duplicates the first frame after the last for seamless looping).
- `VatMaterialSystem` — layers → `_VatFrameA`/`_VatFrameB`/`_VatBlend`
  per-instance properties, including the two-frame crossfade path.
- `ShaderConformanceTests` — structural, source-level checks: the billboard
  include never reads `UNITY_MATRIX_V`/`UNITY_MATRIX_I_V` (the shadow-facing
  hazard), every declared render pass calls the shared displacement function,
  the includes stay standalone (no cross-include `#include`s), no legacy
  built-in-pipeline code (`CGPROGRAM`/`CGINCLUDE`/`UnityCG.cginc`) anywhere in
  the package.

### Added — C7: editor tooling

- `ClipEditorWindow` (**Window ▸ DOTS Animation Toolkit ▸ Clip Editor**) — a
  UI Toolkit timeline: track lanes, time ruler, playhead, transport, clip
  browser, context inspector, and a live preview pane sampled through the
  runtime's own `ClipSampler` (not a separate editor implementation) so
  preview and play mode cannot drift apart. Undo is per-gesture (one drag is
  one Ctrl+Z), via `TimeRulerElement`, `PlayheadElement`, `TrackLaneElement`,
  `ClipKeyClipboard`.
- `VatBakeWindow` (**Window ▸ DOTS Animation Toolkit ▸ VAT Bake**) — a wizard
  over `VatTextureBaker.Bake`: source prefab, clip list, flavor/sample-rate
  settings, validation preflight, bake log.

### Added, not yet compile/test-verified in this environment

The following were written in a development session without a connected
Unity Editor (see the project's own toolkit handoff note, "C8 — built blind").
They are present in the shipped source tree but have not
been through this project's own compile-and-test gate as of this entry — do
not treat them as confirmed working until a session reports a clean compile
and a green test run against them:

- The socket system (`Runtime/Identity`, `Blobs`, `Components`, `Systems`;
  `Authoring/Assets`, `Build`, `Baking`; `Editor/VatBaking`) — named
  attachment points that resolve to a world transform every frame, either by
  following a rig part directly (no bake needed) or by sampling a baked bone
  of the VAT source rig.
- `FacingResolver` and its tests — 2/4/8-direction facing tables (which clip
  to play, mirrored or not, given a facing) and their mirror derivation.
- The Mirror Clip editor utility (`Editor/ClipUtilities/MirrorClipUtility.cs`)
  and its context-menu entry.
- Custom inspectors for `RigAsset`, `ClipSetAsset`, `VatTextureSetAsset`, and
  `ActorAuthoring`.
- `ValidationBadgeElement`, surfacing `ClipValidation` results in the Clip
  Editor toolbar.
- A disk-round-trip EditMode test tier, closing a gap that had let a
  shipping-blocking serialization defect through 221 purely in-memory
  fixtures (that defect — clip-level VAT-source detection reading every saved
  clip as VAT-sourced — is itself already fixed; this tier exists so its class
  of bug cannot recur unnoticed).

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
- PlayMode baking tests. Run them from the Test Runner's PlayMode tab in the
  Editor; Unity's baking pipeline has no player-side equivalent, so a player test
  build cannot execute them (architecture amendment A25, superseding A17).
- `PlayModeAssemblySmokeTest` asserts that the suite is genuinely running in
  PlayMode — `Application.isPlaying`, plus a yielded frame advancing
  `Time.frameCount`. Both are false in EditMode, so a future platform
  restriction fails loudly instead of moving the whole suite's mode in silence.

### Fixed

- **The PlayMode test suite ran in EditMode and nothing noticed.** Amendment A17
  had set `includePlatforms: ["Editor"]` on the PlayMode test assembly, intending
  to declare honestly that a player build cannot run baking tests. But an
  editor-only assembly is classified by Unity's Test Framework as an *EditMode*
  assembly, so the restriction did not narrow the suite's platforms — it moved
  all 27 tests out of PlayMode entirely. A project-wide PlayMode run discovered
  zero tests and reported `Passed`, which is indistinguishable from success.
  Amendment A25 supersedes A17: `includePlatforms` is `[]`, the suite runs in
  PlayMode again, and the smoke test now asserts the mode rather than the
  assembly's name.

### Changed

- `RigPartBakeLink` and `ActorBakeFailed` are `internal`, not `public`. Both are
  bake-time-only types with no consumer contract, reachable from the tests
  through the package's contracted `InternalsVisibleTo`.
- `RigPartBakeLink` carries the authoring object's hierarchy path as a
  `FixedString128Bytes` instead of a hash of it, and every one of
  `RigBindingBakingSystem`'s diagnostics now names the part: `Rig part
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
  avalanche. No observable behaviour changes: the path walk hashes the leaf
  first, so a distinguishing character is mixed by every remaining node before
  the final multiply, and both derivations were measured to spread sibling names
  identically at 200 container positions. A defensible micro-improvement, not a
  bug fix — an earlier draft of this entry claimed it corrected sibling names
  "landing on adjacent phases", which was never demonstrated and is not true.

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
