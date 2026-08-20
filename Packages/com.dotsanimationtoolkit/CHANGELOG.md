# Changelog




All notable changes to the DOTS Animation Toolkit are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Changed — every shader is a Shader Graph now (amendment A46)

The package shipped three hand-written `.shader` files and no graphs. It now
ships three graphs and no `.shader` files, plus the reflection-node library
they are built from.

- **`Shaders/Nodes/`** — six Shader Graph nodes over the package's own
  standalone includes: `ToolkitBillboardVertex`, `ToolkitVatBoneSkin`,
  `ToolkitVatVertexFetch`, `ToolkitFlipbookSliceUV`,
  `ToolkitFlipbookSliceIndex`, `ToolkitFlipbookAtlasUV`. They search under
  **DOTS Animation Toolkit**.
- **`ToolkitSpriteUnlit.shadergraph`** — atlas flipbook + billboard, alpha
  clipped. Replaces the hand-written sprite shader.
- **`ToolkitSpriteUnlitArray.shadergraph`** — the `Texture2DArray` slice-mode
  variant. A separate graph rather than a keyword branch, which is how the host
  keeps `2DShader` and `2DArrayShader` apart.
- **`ToolkitVatCrowdUnlit.shadergraph`** — bone-matrix VAT skinning.
- **Removed:** `ToolkitSpriteUnlit.shader`, `ToolkitVatCrowdUnlit.shader`,
  `ToolkitCompositeExample.shader`.

**Section 6.3's displacement rule is now satisfied by construction.** Shader
Graph emits one vertex description and every pass calls it, so a pass cannot
silently skip the displacement the way a hand-written one could. The sprite
graph displaces in all eight passes — including GBuffer, MotionVectors and the
two picking passes, which the hand-written shader never declared at all.

`ShaderConformanceTests` was rewritten rather than repointed. It used to count a
helper's name in the shader *source*, which proved the author had typed the call.
It now reads each pass's real generated code through the public
`ShaderUtil.GetShaderData` and asserts the displacement is present in every one —
what actually ships, not what was written.

**Migration:** materials keep working. Reference names are unchanged
(`_MainTex`, `_BaseColor`, `_AtlasFrame`, `_BillboardParams`, `_Cutoff`,
`_ImageIndex`, `_MainTexArray`, `_VatBoneTex`, `_VatTexelParams`, `_VatFrameA`,
`_VatFrameB`, `_VatBlend`) and the per-instance ones are still per-instance, now
via Shader Graph's hybrid declaration rather than a hand-written
`UNITY_DOTS_INSTANCING_START` block. A material bound to a deleted `.shader`
must be rebound to the corresponding graph.

**`ToolkitCompositeExample` is gone without a replacement, deliberately.** Its
purpose was to demonstrate the hand-written DOTS instancing block and
displacement-in-every-pass. Shader Graph does both automatically, so the example
had nothing left to teach; the contract it documented lives in
`shader-contract.md`.


### Added — billboarding is a hierarchical rig feature (amendment A44, phases D3-D6)

Billboarding is now an authorable, inheritable property of the rig hierarchy.
Any node can be a billboard root; everything beneath it inherits that root
unless it declares one of its own — so a character can billboard as a whole
while the item in its hand billboards independently. See
`Documentation~/billboarding.md`.

- **Roots reach entities.** `ActorBaker` resolves the rig's roots against the
  prefab hierarchy into a `BillboardRootElement` buffer on the actor, ordered
  shallowest first. Parts get a `BillboardMember` naming their nearest ancestor
  root by id. A rig that declares no roots bakes nothing.
- **`BillboardResolveSystem`** turns each root after the pose is applied, reading
  rest orientations through live transforms so a nested root sees its ancestor's
  freshly written rotation and cancels it rather than composing on top of it.
- **`BillboardQuery`** exposes the resolved world-space frame in one hop, and
  `ToBillboardSpace` maps world gravity into it. This is what the ragdoll work
  consumes instead of recomputing facing.
- **Keyable on the timeline** — angle offset, blend weight, and enable/disable,
  as a fifth track kind. The highest active layer carrying a track for a root
  wins.
- **Clip Editor** — right-click a hierarchy row to make or clear a billboard
  root. Roots are marked in the tree and inheriting nodes are marked more
  faintly, with the source root named on hover. The viewport shows billboarding
  live against its orbitable camera, calling the same function the runtime does;
  a toolbar toggle turns it off so the authored pose can still be inspected.
- **Rig asset** — a Billboarding section for tuning roots once they exist.

**Breaking:** `ActorBillboardSystem` and the `ActorBillboard` component are
removed. The whole-actor billboard is now a root at depth 0, and
`ActorAuthoring`'s billboard checkbox still bakes one — unless the rig already
declares a root for the actor root, in which case the rig wins.

**Behaviour change:** the billboard facing sign is corrected in both the CPU and
shader paths. The facing vector is now the direction a node's local +Z must
point — *away* from the viewer — matching the host game and Unity's
`PrimitiveType.Quad`, whose visible normal is on -Z. Content already using
`Full` or `ScreenAligned` will render the other way round from before. This is
the one item in A44 no test can settle, because whether a quad shows its face or
its back depends on a mesh the package cannot see.

**Evaluation order, stated because it decides what animated rotation means:**
pose is composited, pose is applied, billboarding applies on top. At full blend
weight the billboard replaces a node's animated rotation outright.

### Added — billboard orientation maths (amendment A44, phase D2)

`BillboardMath` is the whole of billboard orientation as pure functions, and the
single implementation the runtime job, the Clip Editor preview and the billboard
frame query all share. Nothing calls it yet; D4 wires it to a system.

- **`BillboardSettings`** — one parameter block carrying a root's configuration
  and the two channels a clip can key. A default-constructed value is inert, so
  a block nobody filled in leaves the pose alone.
- **`BillboardMath.TryResolve`** — facing, axis constraint, angle offset, snap
  wheel, arc clamp and blend, in that fixed order. Returns false when there is
  nothing to apply, and the caller then leaves the transform alone.
- **Snapping and clamping are measured from the node's rest orientation**, not
  from the world, so both travel with an animation that turns the node — which is
  what eight-direction sprite art means. Only the rotation *about the reference
  axis* is quantised or limited; a tilted rest pose survives untouched.
- **The clamp outranks the wheel.** At the arc boundary the result may sit off a
  snap step: the clamp is a constraint, the snap is a look.

**The facing sign is corrected here**, per A44: the facing vector is the
direction the node's local +Z must point — *away* from the viewer, matching the
host game and Unity's `PrimitiveType.Quad`, whose visible normal is on −Z. The
package previously used the negation. `ScreenAligned` now reproduces
`quaternion.LookRotation(cameraForward, up)` exactly, and there is a test that
says so. **This changes how existing `Full` / `ScreenAligned` content looks**;
the shader path still carries the old sign until D4.

### Added — billboarding becomes a rig property (amendment A44, phase D1)

Billboarding is being generalised from one flag on an actor into an authorable,
inheritable property of the rig hierarchy, so a character can billboard as a
whole while a held item billboards independently. This entry covers the
authoring data model only; resolution, the runtime system and the keyable clip
tracks land in later phases.

- **`RigAsset.billboardRoots`** — the nodes of a rig that turn to face the
  viewer, and how. Billboard configuration lives on the rig so it travels with
  it and is shared by every actor instanced from it. Empty for a rig that never
  billboards, which bakes nothing and costs nothing.
- **`BillboardRootDefinition`** — mode, constraint axis, angle offset, optional
  snapping (`snapSteps`, `snapOffsetDegrees`) and optional arc clamping
  (`clampArcDegrees`), plus its own `BillboardRootId`. Clip billboard tracks
  will bind to that id rather than to the addressed node, so re-pointing a root
  at a different node keeps the animation authored against it intact.
- **`BillboardNodeAddress`** — addresses a node either by a rig target's stable
  id or by hierarchy path. Two kinds because the rig has two kinds of node and
  only one has an id: `RigAsset.targets` is flat, and the hierarchy a billboard
  inherits down is the authoring prefab's transforms. A grouping node that is
  nobody's animatable part has no id to offer.
- **`BillboardMode.AxisConstrained`** — turn about an arbitrary authored axis.
  `Upright` is exactly this mode with the axis `(0, 1, 0)` and keeps its own
  value. Existing mode numbers are unchanged; they are shared with
  `_BillboardParams.x` so the CPU and shader paths cannot drift.
- **Validation V21, V22, V23** — an address that names nothing, two roots on one
  node, and an axis-constrained root with no axis. Path addresses are resolved by
  the entity bake, which holds the prefab; rig-scope validation cannot judge them
  and does not pretend to.

`RigAsset.EnsureStableIds` now identifies each of its id-bearing lists
independently. It previously returned early on the first null list, which would
have let a rig with no sockets ship billboard roots that no clip could bind to.


### Added — event windows: events that are a *state*, not just a pulse

An event marker can now hold a window open for a duration, so gameplay can ask
"is this actor inside its damage frames right now?" on any frame, not only on the
one frame the marker fired.

The two channels come off the same authored marker and answer different
questions:

| Channel | Component | Answers | Use for |
|---|---|---|---|
| Pulse (existing) | `AnimEventOutput` buffer | "it just happened", with `intParam` / `floatParam` | Footstep and impact **sounds**, spawning a projectile, VFX one-shots |
| Window (new) | `AnimEventMask` component | "it is happening now" | Damage/hit frames, invulnerability, "is committed", parry windows |

- **`EventMarker.windowSeconds`** — 0 (the default, and what every existing clip
  carries) keeps a marker pulse-only. Above 0 it opens the event's bit in
  `AnimEventMask` for that long.
- **`AnimEventMask`** — an enableable component holding one bit per event key.
  The key/bit mapping is arithmetic (`bit n` is key `16 + n`), so keys 16–79 can
  hold windows and keys above 79 stay pulse-only. Test it with
  `AnimEventMaskKeys.IsOpen(mask, key)`; the component is disabled whenever
  nothing is open, so consumers chunk-skip idle actors for free.
- **`EventWindowSystem`** rebuilds the mask from scratch every frame from each
  layer's current time. Nothing is accumulated or counted down, which is what
  makes an interrupt close its windows with no cancel path: a Play command swaps
  the clip, the next rebuild reads the new clip's markers, and the interrupted
  swing's damage window is simply not set again. Scrubbing, reverse playback and
  PingPong reflection stay correct for the same reason.
- **`AnimEventKeyRegistry`** — an optional, authoring-only asset naming a
  project's event keys, assigned on the clip set. With one, the Clip Editor picks
  events by name (`ApplyDamage`) instead of by number; without one it edits raw
  keys exactly as before. It is never baked, so renaming or reordering events
  cannot invalidate a baked clip.

### Added — the Clip Editor authors all of it

- Event keys draw larger than pose keys, and a translucent bar behind each one
  shows how long its window runs, so a hit frame's duration lines up visibly
  against the poses around it.
- Selecting an event marker now gets purpose-built fields — event name/key,
  window **in frames**, and the two payload params — instead of the generic
  struct drawer. The window edits in frames at the registry's reference rate and
  stores seconds, so a window lasts the same wall-clock time on every machine.

### Fixed

- Placing an event marker on the timeline no longer creates it with `eventKey`
  0. That is the reserved "invalid" key, so a marker placed and left alone failed
  validation rule V09 — the clip broke at bake purely for having been authored.
  New markers take the clip set's first registered event, or key 16.

### Fixed — the shipped sample did not compile, and produced invalid assets

Both defects were invisible for the same reason: **`Samples~` is excluded from
compilation**, so nothing in the project ever built the sample and no test could
have caught either. Found by copying it into a temporary assembly under the
project's asset root and compiling it, then running it and validating what it
produced.

- `QuickStartActorBuilder` still wrote `scale = new float2(1f, 1f)`. Schema 6
  made `TransformKey.scale` a `float3`, so the only sample the package ships had
  been a compile error since that change landed.
- Its asmdef was missing a `Unity.Collections` reference, which any assembly
  referencing `Unity.Entities` needs — a second compile error behind the first.
- The generated rig's **target ids were all 0**, so the sample produced a clip
  set failing validation rules V02 and V05. `CreateInstance` runs the asset's
  lifecycle hooks while `targets` is still empty and `AssetDatabase.CreateAsset`
  fires none of them, so nothing ever minted them.

### Changed — `RigAsset.EnsureStableIds()` is public

The id-minting gap above could not be closed from a sample, and cannot be closed
by a user's build script either: the method was `internal`, and the package's
`InternalsVisibleTo` list is contracted to the Editor and test assemblies only.
Any script that builds a rig from code — `CreateInstance`, assign `targets`,
save — needs to mint the row ids itself. It is idempotent and safe to call twice.

Rig targets and sockets are the only identities in the package that live inside a
list a caller populates after construction, so this is the one authoring asset
that needs a public entry point.

### Added — a `Composite Actor` sample

Generates an actor using **two techniques at once**: cutout limbs on transform
tracks and a flipbook face on sprite tracks, from one clip on one timeline, plus
a socket and an event marker carrying a window. Quick Start shows how to get
something on screen; this shows the claim the package's design is actually built
around and that a reader is most likely to disbelieve.

The flipbook's `Texture2DArray` is generated too, so the sample ships no binary
fixtures. Verified by running it: the generated clip set validates clean and
bakes to a schema-7 registry carrying both a transform track and a sprite track.

### Added — the VAT baker has tests

`VatTextureBaker` shipped with **no coverage at all**, despite being the piece
that turns a skinned mesh into the texture the whole VAT path reads. Sixteen
EditMode fixtures now build procedural skinned meshes — the approach §9's C6 row
asks for, and the only one available, since an imported FBX cannot be committed
as a package fixture anyone can diff or regenerate.

They pin the two things that are checkable exactly and had nothing holding them:

- **The layout contract with `ToolkitVat.hlsl`** — three rows per bone frame, one
  per vertex frame, a power-of-two width covering the element count, clips laid
  end to end without overlap, the loop-safe duplicate frame, and a targeted C10
  range occupying its own block rather than aliasing the untargeted one.
- **The failure contract** (§8 M2, "never throws past the API") — a boneless
  mesh, a null renderer, a zero sample rate and an empty clip list each report a
  message instead of throwing, because a baker that throws cannot be driven over
  a content library.

Plus the one that matters most: **a bake of an animated clip writes different
matrices on different frames.** The baker's own source warns that sampling
through the wrong API poses nothing and yields "a texture full of identical,
entirely valid-looking matrices" — and every other fixture here passes against
exactly that bug. Mutation-checked by disabling the sampling call: one failure,
that fixture, out of 345.

### Added — coverage for two paths nothing exercised

- **A socket driven through a real playback frame.** Every socket fixture until
  now ran `SocketResolveSystem` alone against a hand-seeded part, so none could
  say whether the pose it composes from is the one the rest of the toolkit
  produces. Two PlayMode fixtures now run the whole chain — `Play` command,
  clip sampled, `TransformApplySystem` writing the part, socket resolved from
  that same frame's transform. Mutation-checked by resolving ahead of the
  apply: both fail with the unrotated reading, nothing else does.
- **`RigBindingSystem` bound from a populated buffer**, the shape `ActorBaker`
  actually leaves behind. The existing fixtures all started from an empty one —
  a state production never presents — which §5.3 recorded as owed after C4.9.
  Mutation-checked by deleting `partRefs.Clear()`: the new fixture fails on the
  *first* bind, the path every spawn takes.

### Fixed — documentation that had drifted from the code

- The README claimed a clip may carry only one VAT source, so a torso and a
  cape could not come from different source animations. Per-target VAT sources
  (`ClipAsset.vatTracks` → `ClipBlob.vatTargetRanges`) have in fact shipped,
  with five PlayMode fixtures covering them, and never had a changelog entry.
- The README's "not battle-tested" note said no integration test drives a
  socket through a real playback frame. That is now true only of the VAT bake.

### Changed

- **Blob schema version 6 → 7.** `EventMarkerBlob` gained `windowSeconds`.
  Existing subscenes must be re-baked.
- Two new validation rules: **V19** (error) a negative window; **V20** (warning)
  a window authored on a key outside the maskable range, where it can never be
  observed.

## [0.9.0] - 2026-08-15

The editor release. Everything below is authoring surface: the runtime, the blob
layout and the bake are unchanged from 0.8.0 except where a fix says otherwise,
and the one behaviour change affecting content already authored — flipbook step
timing — has its own entry saying what it means for existing clips.

### Added — the editor logic that had no coverage now has some

Six fixtures, each pinning something whose failure is silent and expensive.
The routing one is the important one: **Rig Edit must never produce a keyframe**,
whatever Auto Key is set to. That rule was previously spread across the branches
of an event handler, so it was true but not readable; it now lives in
`GizmoDragRouting` as a table, and the fixture asserts it for both Auto Key
states rather than the one that happens to be set.

The others cover hierarchy path round-tripping (a wrong path reparents the wrong
object in a prefab asset), the reparent guards that refuse cycles before anything
is written, and the claim that a socket marker sits where `SocketResolveSystem`
will put it — asserted with the followed part *rotated*, since an offset added
without rotating into its space is correct while everything is unrotated, which
is exactly the state a rig is authored in.

Both load-bearing fixtures were mutation-checked: inverting the Rig Edit rule and
dropping the rotation from the socket composition each produced exactly one
failure, in the fixture aimed at it.

### Fixed — the shader contract was linked but never shipped

`README.md`, `Documentation~/index.md` and `Documentation~/getting-started.md`
all linked `Documentation~/shader-contract.md`, and the 0.8.0 changelog claimed
it had been mirrored there. It had not: the file existed only outside the
package, so every one of those links was dead for anyone who installed it, and
`rigged-characters.md` reached it through a `../../../` path that only resolves
inside this repository. The document is now actually in `Documentation~`, and the
VAT Bake window's own "see the shader contract" message points at the copy a user
has rather than one they do not.

### Changed — documentation is split by what you are building

`rigged-characters.md` had become the home for material that has nothing to do
with rigs: the Clip Editor's selection model, keying, dopesheet, socket placement
and prefab-authoring round trip all apply just as much to a cutout character, and
a cutout author had no reason to open a page about bone VAT to find them.

- **`clip-editor.md`** is new: the window's own reference, split out whole.
- **`cutout-characters.md`** is new, and fills the obvious gap — there was an
  end-to-end guide for rigged characters and none for the paper-doll workflow
  the toolkit is equally built for, including how a flipbook's two independent
  retargeting bases differ and what each is for.
- `rigged-characters.md` keeps what is genuinely about bones and VAT, and points
  at the editor reference for the rest.
- `index.md` now routes by what you are building rather than listing files.

### Added — sockets are placed, tracked and previewed in the Clip Editor

Sockets existed end to end — runtime resolve, blob, and a VAT bake that captures
bone-socket motion — but they could only be *authored* by typing numbers into the
rig asset's inspector, against a character you could not see and a pose you could
not scrub. The numbers are the same numbers; they are now next to the thing they
move.

- **Socket rows in the hierarchy**, listed after the rig's parts, labelled with
  what each one follows and marked `(unresolved)` when that matches nothing. An
  unresolved binding is otherwise a play-mode discovery: an attachment pinned to
  the actor's feet with no obvious cause.
- **An inspector** for name, what it follows (a dropdown of the rig's parts, or
  of the prefab's bone names — a typed name that resolves to nothing is the
  failure this whole feature exists to make visible), playback layer, and offset.
  **+ Socket** creates one pre-bound to whatever is selected; delete is confirmed.
- **Click a socket in the viewport** to select it, including clicking its
  attachment — a hit walks up to the socket it belongs to, so a sword is not an
  unselectable object sitting on an unreachable marker.
- **Gizmos place sockets.** With one selected, W/E move and rotate it and the
  result is written back as an offset in the followed part's space — the inverse
  of the composition the runtime performs. Writing the dragged numbers raw would
  look correct until the rig rotated.

**Bone sockets are previewed now, which they were not.** They used to draw
nothing, on the stated grounds that the bone they follow "exists only inside a
VAT texture". That stopped being true when the preview began instantiating the
rigged prefab and posing its skeleton (A42/B4) — the bone is right there, posed,
every frame.

**Preview attachments.** Each socket takes an optional prefab that the preview
hangs off it, so "does the sword sit in the hand through the whole swing" is a
question you answer by scrubbing. Editor-only and inside `UNITY_EDITOR`, so the
reference cannot drag a weapon mesh into a player build; nothing reads it at run
time, where what to attach is the game's decision via `SocketAttachmentAuthoring`.

**Baking is unchanged and already covered this**, which is worth stating plainly:
the VAT bake captures bone-socket motion into `VatTextureSetAsset.socketTracks`,
`SocketRegistryBuilder` folds it into the blob, and `SocketResolveSystem` reads
it. Rig-target sockets are deliberately never baked — their motion *is* their
part's transform, resolved live. A socket authored in the window is an ordinary
rig socket and flows through untouched. What is new is that the socket inspector
now *reports* bake state, so an unbaked bone socket is visible before play rather
than after.

### Fixed — a socket added from the window had no stable id

`RigAsset` mints ids in `OnValidate`, which does not run in time for code that
adds a socket and then addresses it. The socket came back with id 0 — and 0 is
the sentinel for "no socket selected", so the thing you had just created was
unselectable and its marker unfindable. Minted explicitly on add.

### Changed — moving between animating and authoring is a mode switch, not a window juggle

Opening prefab mode put the Scene view behind a floating Clip Editor, leaving the
user to drag the window aside and drag it back. The window was always dockable —
it was simply never docked, and a floating window sits above the main window
whatever has focus, so no amount of focusing the Scene view could get it out of
the way.

- **New windows dock beside the Scene view.** The two are alternatives, never
  wanted at the same instant: animating uses the Clip Editor's own viewport,
  authoring structure uses the Scene view. Sharing a tab group makes the switch a
  tab change that Unity's layout system performs by itself.
- **An existing floating window docks itself** on the first trip into prefab
  mode, carrying its clip set, clip, playhead, selection and mode across the one
  re-creation that requires. One-time: it stays docked afterwards.
- **Entering** focuses the hierarchy and then the Scene view, in that order —
  each brings its window to the front of its tab group, and the last also takes
  keyboard focus, which belongs to the Scene view because that is where the user
  is about to click.
- **Exiting** brings the Clip Editor back on its own, with the playhead and
  selection already restored by the round-trip reload.

**Not a layout swap.** `EditorUtility.LoadWindowLayout` destroys and recreates
every editor window including this one, which would take the preview's render
utility and its `Persistent`-allocator registry blob with it, along with the very
playhead and selection the round trip exists to preserve — and it would
rearrange windows this feature was never asked to touch. Sending one window
behind another has a smaller blast radius and fails gracefully: the worst case is
a window that did not come forward, not a rebuilt editor.

### Added — prefab authoring is reachable from the Clip Editor, and the round trip is handled

Structural edits — parenting, adding parts, moving meshes — belong in Unity's
prefab mode, which already does them correctly. The Clip Editor's job is to make
that one click away and to stay honest when the user comes back.

**Getting there.** An **Edit Prefab** button in the hierarchy header, a
right-click menu on every row with **Open Prefab Here** / **Ping in Project** /
**Select in Scene**, and double-click on a row. All of them open prefab mode with
the object selected and framed. Objects are addressed by hierarchy path, because
the preview holds one instance of the prefab and prefab mode opens another —
there is no reference that spans the two.

**Coming back.** The window subscribes to `PrefabStage.prefabSaved` and
`prefabStageClosing`, filtered to the prefab it actually has loaded so an
unrelated save elsewhere in the project does not disturb it. On return the
preview is reinstantiated, the hierarchy tree is rebuilt, and the playhead and
selection are restored — selection by *name*, since the tree ids are indices into
a hierarchy the edit just invalidated.

**Reconciliation, scoped to what actually breaks.** A restructure cannot break a
transform or sprite track: those bind to a rig target's stable id, which is never
derived from a name and which no prefab edit touches. Reporting them would be
noise that teaches the user to dismiss the panel unread. Three bindings *are*
name-based and do break, and those are surfaced:

| Binding | What breaking costs |
|---|---|
| `BoneTrack.boneName` | The track stays authored but bakes nothing |
| A socket with `mode = Bone` | The attachment bakes at the origin |
| A rig target's `displayName` | Tracks still play; the preview has no rest pose for that part |

Each row offers a dropdown of names that exist — not a text field, since a typo
is how you get this problem — plus **Remap**, and **Delete** for the two
track-like kinds. A rig target is deliberately not deletable from the panel: it
carries the id every track in every clip binds to. Deleting a track is confirmed
and states the key count, because "delete this track" and "delete these forty
keys" are different decisions. **Dismiss** hides the panel without changing
anything, and the next save reports the same findings again.

### Added — Rig Edit mode

An explicit toggle for editing the rig's base setup rather than the clip. The two
must never be confusable: the same gizmo drag writes a keyframe in one mode and
the prefab asset in the other, neither is recoverable by doing the other, and a
user who mistook one would find out much later.

So the mode is stated three times — the toolbar toggle is tinted, the viewport
frame is bordered in the same colour, and a banner across the viewport says in
words what a drag will do. Keying is also switched off in behaviour and not only
in signage: **Auto Key** is visibly disabled, and every route into keying is
refused at the single function they all pass through.

In Rig Edit, gizmo drags write the prefab's base pose on release, and hierarchy
rows accept drag-to-reparent. Both go through Unity's prefab APIs. If a prefab
stage for that asset is open the edit lands there — undoable, visible, saved by
the stage. If not, `LoadPrefabContents` / `SaveAsPrefabAsset` writes the asset
directly, which is **not undoable**; that is a real limitation of editing an
asset with no open instance, and the reason the stage route is preferred whenever
one exists.

### Changed — the camera frames the rig instead of staring at the origin

Placing the rig and aiming the camera are separate questions, and conflating
them is why the view opened looking at the ground between a character's feet.
The origin is where the rig is *placed* — it is what the floor grid is drawn
for — but a character stands on the floor, so none of it is near 0,0,0.

The camera now orbits the middle of the rig's bounds and backs off a distance
derived from its bounding sphere and the vertical field of view, so a
two-metre character and a twenty-metre vehicle are both framed without either
being guesswork. Framing happens once, on the first render after the rig or the
loaded prefab changes; after that the camera is yours and no later render will
fight an orbit. Double-clicking the viewport reframes, as it always did.

Both mirrors count towards the bounds: the cutout parts are the rig for a
paper-doll set, and the instantiated prefab is the rig for a skinned one, whose
targets are a handful of quads at rest that would otherwise frame nothing. An
extra in the prefab that is not really part of the character — a health bar
above its head — widens the frame slightly; guessing which children "count" by
name would be wrong in ways nobody could predict.

### Added — a floor, a visible origin, and the rig standing on it

The viewport had one grid, in the XY plane at z = 0, which works as graph paper
behind a cutout rig and does nothing at all for a 3D prop or vehicle.

- **A floor grid in the XZ plane at y = 0**, beside the backdrop rather than
  replacing it. The two answer different questions: the backdrop is what you
  measure a flat rig against without orbiting, the floor is what says which way
  is down and whether a character's feet are on the ground or sunk through it.
  It is drawn a shade darker so the place where the two planes cross stays
  readable.
- **The origin is drawn**, as three short axis stubs in the usual X-red,
  Y-green, Z-blue convention. 0,0,0 is now a place you can see rather than one
  you infer from where lines happen to cross — and it is the point everything
  else is measured from: the camera orbits it, the grids centre on it, and the
  rig is laid out around it.
- **Both preview roots are planted on the origin explicitly** — the part
  collection and the instantiated prefab. A prefab whose own root transform sat
  ten metres out used to put the whole preview somewhere the camera never looks.

### Fixed — a part's rest pose is measured from the prefab root, not its parent

Rest poses were read from each transform's `localPosition`/`localRotation`/
`localScale`, which is wrong the moment a prefab nests — and a cutout character
nests deeply (pelvis → torso → neck → head → eyes). The mirror parents every
part under one flat root, so taking each part's offset from its own parent piled
the character back onto the origin one link at a time: a head at "y = 0.11 above
the neck" landed at y = 0.11 instead of y = 1.85.

Parts are now placed by the transform composed into the prefab root's space,
which is the space the flat mirror root actually stands in, and survives any
nesting depth. On `BaseUnit` that is the difference between every part inside
half a metre of the floor and a figure standing from feet at y ≈ 0.18 to hair at
y ≈ 2.33.

### Fixed — the preview starts from the prefab's transforms, and stops rebuilding itself

Two faults with one visible symptom: parts in the preview were the wrong size
and in the wrong place, and they moved when you edited something unrelated.

- **A part's rest pose is now the loaded prefab's transform, not the origin.**
  The preview sampled every clip against a hard-coded identity rest pose, so
  every part was a unit quad stacked on the origin and what you saw was the
  authored *offsets* rather than the character. It also made the preview
  disagree with the runtime for no reason — `TransformApplySystem` composes
  against the entity's real rest pose, so a clip that looked right in the editor
  would not look right in play. Targets bind to prefab transforms by name (a rig
  target's `displayName` is the only thing it and a prefab transform share); an
  unmatched target falls back to identity, which is the old behaviour and what a
  cutout set with no prefab loaded still gets.

  The composition rules are what make this correct rather than cosmetic:
  position and rotation are additive against the rest pose and scale is
  multiplicative, so a part with no track — or a track authored at zero offset
  and unit scale — now sits exactly where the prefab has it. **No authored data
  changes.** A key of "no offset" finally means no offset.

- **Editing a clip no longer rebuilds the part objects.** Every edit refreshed
  the preview, and the refresh destroyed and recreated all thirty-odd part
  quads. A fresh quad is a unit quad at the origin until the next pose lands on
  it, so an edit with nothing to do with transforms — keying a flipbook index,
  say — made the whole rig visibly jump and resize. Parts are a function of the
  rig, not of the clip being edited, so they now survive an edit to the clip;
  only the registry built from the clip is rebuilt.

A freshly built mirror is also posed to rest immediately, so a rig with no clip
selected shows the character standing as the prefab has it rather than a heap of
unit quads.

### Fixed — a flipbook index now changes *on* its key

`ClipSampler.SampleSpriteTrack` chose between the two keys surrounding the
playhead by which was nearer, so a frame change landed at the **midpoint of the
segment** rather than on the key that caused it. On an evenly spaced flipbook
that is the entire animation playing half a frame-step out of time with the
timeline it was authored on, and it made the last key before a gap appear to
change early.

The key at or before the time now holds until the next key's own time is
reached — a hard step, which is the only thing a frame index can do. The editor's
`ClipSpriteEditing.FindEffectiveKeyIndex` carries the same rule, so what the
inspector shows while scrubbing is still what the runtime plays.

This changes playback of existing clips: a flipbook frame that used to appear at
the midpoint between two keys now appears at the later key. Nothing about the
authored data changes, so no re-bake or migration is needed — but a clip tuned
against the old timing will read as half a segment slower on each change, and is
worth a look.

Cross-clip blending is untouched: `LerpPose` still snaps the frame at the blend
midpoint, because a crossfade has no key to land on.

### Added — timeline focus and multi-select

Selecting a part now filters the timeline to that part's tracks. A busy clip is
readable again, and it works as a focus rather than a mode — the status line
names what is being shown and how many rows are hidden, and deselecting brings
them all back. Event rows stay visible throughout: they belong to the clip rather
than to any one part, and hiding them would make event authoring impossible the
moment anything was selected.

Selecting several parts (ctrl- or shift-click in the hierarchy) shows all of
their tracks together, and gives each one its own labelled block in the
inspector — its own live transform, its own flipbook indices — so with two parts
on screen there is never a question of whose numbers are whose. One block is
marked **(active)**: the one the viewport gizmo and outline are on, which can
only be in one place. The active row is the one most recently added to the
selection, found by diffing against the previous selection rather than by taking
the last of the tree's `selectedIndices` — that enumerable is ordered by row, not
by when each row was clicked.

A held (unkeyed) transform edit now belongs to the part it was made on, so the
other blocks keep showing their own sampled values. Starting an edit on a second
part keys the first rather than dropping it.

### Fixed — selection could recurse until the stack ran out

Rebuilding the timeline refreshes the hierarchy tree to redraw its "animated"
marks, and that refresh re-resolves the tree's selection, which can notify that
the selection changed, which rebuilds the timeline. With the timeline now
rebuilding on selection change, the two called each other until the stack
overflowed. Guarded against re-entry.

### Changed — the inspector shows one value, and it is live

The right-hand pane was a snapshot wearing a live panel's clothes, and in places
it was a list of keys. Both are fixed.

- **It updates as the playhead moves.** The fields were only refilled when the
  selection changed, so scrubbing left them showing a value from whenever you had
  last clicked something. They now track the playhead. In place rather than by
  rebuilding the pane — a rebuild destroys the very field you are typing into —
  and a focused field is skipped rather than overwritten, because half-typed text
  is a value being authored, not a stale one to stamp over.
- **No more key lists.** A flipbook track showed a row per key and a bone track
  rendered its whole key array through a property drawer. Both are gone. The pane
  now shows either the value at the playhead, or the selected key's own data —
  nothing else. Keys live on the timeline, which is where they can be moved and
  selected; repeating them here made a long track unreadable and put the same
  data in two places that could disagree.
- **The flipbook index is a live field.** One `Index` per track showing the value
  at the playhead, with its `Index Mode`, the resolved reading (`+5 → 37`) and the
  track's `Base Index` beside it. Editing it keys at the playhead — always, unlike
  a transform edit, because a frame index has no in-between to hold and a
  held-but-unkeyed value would just be a number that vanished on the next scrub.
- **Bones get a live transform too**, sampled through `BoneTrackPoser` — the same
  function the preview and the VAT bake use — with rotation exchanged as signed
  Euler degrees over the stored quaternion. A joint at −30° reads as −30, not the
  +330 `eulerAngles` would give.
- A selected flipbook key gets purpose-built fields rather than the generic
  property drawer, because its stored number is only meaningful beside its mode
  and its track's base — three fields the drawer renders as three unrelated
  numbers.

### Changed — transform data is 3D (schema version 6)

**Breaking, and the reason for a full re-bake.** A transform key's rotation is now
three Euler angles rather than one z angle, and its scale is a `float3` rather
than a `float2`. Everything animated in this system is treated as 3D data: a
vehicle keyed here needs pitch and yaw as much as roll, and a 2.5D cutout simply
leaves the axes it does not use at their identity and pays nothing for them.

- `TransformKey`, `TransformKeyBlob`, `TargetPose` and `TargetRestPose` all carry
  `float3 rotation` (degrees when authored, radians once baked) and `float3 scale`.
- Euler rather than a quaternion, because these are the numbers an author types
  and drags — three readable fields with a sign and a magnitude. Bone tracks keep
  quaternions, because nobody types those; they arrive from a bake or a solver.
  Angles are ZXY, matching `Transform.eulerAngles`, so a value typed here means
  what it would mean anywhere else in Unity.
- Sampling lerps the angles per component, which is how a keyed rotation curve
  behaves everywhere an author has met one. Slerping a quaternion rebuilt from
  them would take a different path between the same two keys and quietly disagree
  with the curve editor.
- `AnimatedChannels.RotationZ` → `Rotation` and `LayerZ` → `PositionZ`. The bit
  values are unchanged, so existing masks keep their meaning — an enum serializes
  as its number, not its name.
- **Existing clips migrate on load, and the migration is verified rather than
  assumed.** `ClipAsset.OnAfterDeserialize` moves a legacy `rotationZ` onto the z
  component and completes a 2D scale's missing z to 1. Both triggers are "this
  field was never written" rather than "this field is zero": a rotation is only
  adopted when the 3D one is still all zeros, and a scale is only corrected when
  its z is exactly 0, which no author chooses because it collapses the part to
  nothing. The legacy field is retained solely to make that possible and can go
  once no project in flight still has unmigrated assets.
- The gizmos gained the axes to match: three rotation rings, coloured like the
  axis each turns about, and a z arm on the scale gizmo.
- **Baked bounds now include a key's z displacement.** They did not while z meant
  draw-layer order; a box that ignored it would cull a vehicle the moment it drove
  away from the origin plane. `ClipRegistryBuilderTests` asserted the old
  behaviour and now asserts the new one, with the reason written down.
- Mirroring reflects the y and z angles and leaves x alone: a roll about the
  mirror axis survives a reflection, a yaw and a pitch reverse.
- Schema 5 → 6 with the golden hash re-recorded in the same commit. A version-5
  blob read as version 6 would be reading differently-shaped structs, so the gate
  matters more here than for any previous bump.
- Six new tests exercise the axes nothing else touches. Almost every existing
  fixture leaves x and y at zero and would still pass if the new axes were dropped
  on the way to the blob, which is exactly why they needed their own coverage.

### Added — keying, scrubbing, gizmos and a dopesheet

- **The transform block is always visible for the selected part**, whether or not
  a key sits at the playhead, and it updates as you scrub. It samples the
  *authored* keys through `ClipSampler.Ease` — the runtime's own easing — so what
  the fields show while scrubbing is the curve that will play.
  - Three states, carried on the block's left edge as well as in words: **on a
    key** (editing changes that key), **between keys** (the value is sampled, not
    stored), and **modified, not keyed** (it will be lost if you do not key it).
  - Rotation is shown in degrees, as the authored key stores it. The bake
    converts once (§4.5); an editor showing radians would be the only surface in
    the toolkit that did.
- **Auto Key** in the toolbar. On, an edit writes a key at the playhead; off, the
  change is held and drawn as modified until **Key** is pressed. A held edit is
  dropped when the playhead or the selection moves, because it describes one part
  at one instant and neither survives the other changing.
- **Move / rotate / scale gizmos** on the selected part, with **W / E / R**.
  Dragging writes through the *same* method the numeric fields call, so a drag
  and a typed number produce the same key by construction rather than by
  agreement. The key is written on release, so a drag is one key and one undo
  step rather than one per pointer move.
  - The rotate gizmo is a single Z ring and the scale gizmo is XY only, because a
    cutout part's authored rotation is one angle about z and its scale is a
    `float2`. Handles for axes the data does not have would write nowhere.
  - The gizmo's pivot follows the authored value, not the mirrored quad — the
    quad follows the built registry, which is rebuilt on a debounce, so a gizmo
    anchored to it would lag its own drag.
- **The timeline expands into per-channel rows.** A transform track opens into
  Position X/Y/Z, Rotation Z and Scale X/Y; a bone track into position, rotation
  and scale; a flipbook track into its index. **These rows read the same keys**,
  because one `TransformKey` carries every channel — dragging a key on any row
  retimes the one underlying key. Independent per-channel curves would mean
  splitting the key struct, which changes the blob, the sampler and every baked
  clip.
- **Box select**: drag across empty lane space to band-select keys, with shift to
  add. A press only becomes a band once the pointer has travelled, so a plain
  click still just moves the playhead.
- **Interpolation is editable per key**, including `Bezier` with **draggable
  tangent handles**. The curve widget plots through `ClipSampler.EaseBezier`, the
  function the runtime evaluates, so the shape drawn is the shape that plays.
  Switching a key to Bézier writes the diagonal handles rather than leaving the
  zeros the sampler reads as linear.
- **Dragging a key past a neighbour reorders it; it does not clamp.** Keys move
  freely for the gesture and the list is sorted on release. Clamping would have
  been easier but makes the commonest retiming edit — pulling a pose earlier than
  the one before it — impossible without first moving the other key aside.
- Every one of these is an undo step: gizmo drags, keying, interpolation changes
  and tangent edits each collapse to one entry.
- Fixed, found by the new tests: the closest-point solve behind gizmo dragging had
  its sign inverted, which put a drag the right distance on the *wrong side* of
  the pivot and made handle picking miss entirely. It is the kind of error that
  looks equally plausible either way on the page.

### Added — rig parts are selectable, and flipbook tracks are editable

- **The hierarchy pane now lists the rig's parts** alongside the previewed
  prefab's transforms, and the cutout part quads are pickable in the viewport.
  Before this a flipbook track had no object in the UI to belong to: tracks bind
  to rig targets, the pane showed only prefab bones, and the part quads were
  deliberately excluded from picking because they had no row to select into.
  Selecting a part outlines it in the viewport exactly as selecting a bone does.
- **A flipbook section on the selected part**, listing every track that drives
  it — several are expected, since that is how one texture array holds
  independent feature sets — with **Add Flipbook Track** and per-track removal.
- **`baseIndex` is editable per track**, and moving it retargets every relative
  key at once. Verified: sliding a track's base from 0 to 32 moved the resolved
  indices from 0/5 to 32/37 while the stored values stayed exactly `0,5,12`.
- **Each key shows both numbers**: the stored value it holds and the index it
  resolves to, in the `+5 → 12` form, with `no change` spelled out for the
  absolute −1 sentinel. Showing one without the other is how "+5" and "12" become
  the same confusing number in a bug report. A relative key that resolves below
  zero is coloured as the error rule V18 reports.
- **The mode toggle is lossless in the sense that matters** — the frame the key
  shows does not move. Relative(5, base 32) → Absolute stores 37; back again
  recovers the offset 5; Absolute(12) → Relative stores −20 and still resolves to
  12. Both directions go through `SpriteIndexResolver`, so the conversion cannot
  drift from the resolution the sampler performs.
- Every one of these edits is one undo step, recorded explicitly because the rows
  edit `SpriteTrack` objects directly rather than through `SerializedProperty` —
  a key's stored number is only interpretable beside its mode and its track's
  base, which no property drawer can relate.
- Fixed: the hierarchy pane was rebuilt only when the previewed prefab changed,
  which was enough while it listed nothing else. It now rebuilds when the clip set
  changes too, since the rig targets come from the set.

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

## Build history — C4 through C7, shipped in 0.8.0

Kept below the release entries rather than folded into them: this is the
step-by-step record of how 0.8.0 was built, not a separate release. It was
previously headed "Unreleased", which had stopped being true the moment 0.8.0
went out above it.

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
  `com.dotsanimationtoolkit`, display name "DOTS Animation Toolkit",
  Unity `6000.5` minimum, and pinned dependencies (Entities 6.5.0,
  Entities Graphics 6.5.0, Burst 1.8.29, Collections 6.5.0, Mathematics 1.4.0,
  URP 17.5.0). The samples list is empty until build step C8.
- The five assembly definitions from architecture section 1.3:
  `DotsAnimationToolkit.Runtime` (unsafe code enabled for blob-building
  helpers), `DotsAnimationToolkit.Authoring`,
  `DotsAnimationToolkit.Editor` (Editor platform only),
  `DotsAnimationToolkit.Tests.EditMode` (Editor platform only), and
  `DotsAnimationToolkit.Tests.PlayMode`.
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
