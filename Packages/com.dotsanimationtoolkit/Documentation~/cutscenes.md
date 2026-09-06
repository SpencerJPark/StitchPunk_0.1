# Cutscenes

A cutscene stages several actors and props in a real scene: clip blocks and
keyframes on the same timeline, a camera lane, an event lane, and hold points
where the clock pauses until you release it. Authoring happens in a
`CutsceneAsset` and the Cutscene Editor tab; playback happens through the same
`PlaybackLayer`/`ClipSampler` machinery every other actor uses — there is no
second animation pipeline.

## Concept model

```
CutsceneAsset ──stages──> CutsceneSlot (named, recastable role: "Bertha", "Minion A")
   │                          │
   │ remembers                ├─ Actor: RigAsset + ClipSetAssets + clip blocks + root/facing/part keys
   │ one scene                └─ Prop: no rig, no clip lane — just a transform lane
   │
   ├──> CutsceneCameraLane (keys + hard-cut markers)
   ├──> events (same AnimEventKeyRegistry vocabulary clips use)
   └──> hold markers (pause the clock; the host releases them by id)

CutsceneBlobBuilder ──bakes──> CutsceneBlob, split into segments at hold points
                                  (the runtime clock is (segmentIndex, timeInSegment),
                                   never one elastic value)

CutsceneTimelineSystem + CutscenePartOverrideSystem ──play──> bound actor/prop entities
```

A slot is abstract: the asset says "an Actor named Bertha, playing this rig,"
never "this specific prefab instance." The same cutscene can be recast onto a
different roster by re-binding its slots in a different scene.

## Authoring

Open **Window ▸ DOTS Animation Toolkit ▸ Clip Editor** and switch to the
**Cutscene Editor** tab (or double-click a `CutsceneAsset` — it opens there
directly). The tab is a cast list, a timeline and an inspector; Unity's own
Scene view is the viewport, so dock the three — Hierarchy, Scene view, Clip
Editor — side by side once and Unity remembers the arrangement.

- **Cast panel** (left). One row per slot: a state dot (● bound, ○ unbound,
  ⚠ bound to something this scene no longer has), the slot's name and kind, and
  four actions. **Place** instantiates the slot's **Actor Prefab** at the Scene
  view pivot and binds it — one Undo step, and how an empty scene gets dressed
  without leaving the tool. **Bind** takes a GameObject that is already in the
  scene. **Select** and **Frame** put it under the cursor and on screen.
  Selection syncs both ways: clicking the character in the Hierarchy or the
  Scene view lights its cast row and its timeline group.
- **Slots.** "+ Actor Slot" / "+ Prop Slot" add a row. A slot's header doubles
  as its selection target — click it to edit its name, kind, actor prefab, rig,
  clip sets and direction set in the inspector on the right. Right-click a
  header for **Remove Slot**.
- **Clip lane** (Actor slots only). Double-click empty space to add a block;
  drag its body to move it, its edges to resize it. **Dragging two blocks so
  they overlap makes the overlap the crossfade window; blocks that merely
  touch are a hard cut** — this is derived from where you drop them, never a
  separate field to author.
- **Root / Transform lane.** For an Actor, this is root motion — the clip
  plays in place, this lane moves the actor through the scene. For a Prop, it
  is the prop's entire authored motion, since a Prop has no clip lane to
  separate it from.
- **Facing lane** (Actor slots only). Empty by default — facing derives from
  root travel direction. Add an override key to pin a facing (a direction
  angle, 0–360°) for a moment, e.g. "face the camera during this line." Give
  the slot a **Direction Set** and the preview applies it: the resolved angle
  picks that set's east-side variant and mirrors it when the facing is served
  by a flip. A block that names a clip the direction set holds gets re-picked
  as the actor turns; a block naming a one-off clip the set has never heard of
  is left exactly as authored.
- **Part tracks** (Actor slots only). "+ Part Track" opens the same tag
  picker every part-tag surface in this package uses. A part track's keys
  layer *over* whatever the clip lane is currently playing, on just the
  channels you check.
- **Attach lane** (every slot). Double-click to add an Attach at the
  playhead; the inspector picks the **Host** slot it rides and, when that host
  is an actor whose rig declares sockets, which **Socket** — or `(root)` for
  the host's own transform. A diamond marker is an Attach, a ring is a Detach.
  See [Attach lane](#attach-lane) below.
- **Camera lane.** Add keys the same way; the inspector's **Align to Scene
  View** button captures the Scene view camera's current pose into the
  selected key. Add cut markers on the row below for a hard cut instead of a
  smooth move between two keys.
- **Events / Holds.** Same shape as a clip's own events — double-click to add
  a marker, pick an event key in the inspector. A hold marker just needs an id
  string; the host releases it by that same string at play time.

### Scene binding and Scene-view preview

The toolbar shows whether the cutscene's remembered scene is the one open —
**Remember Current Scene** the first time, **Open Scene** if you're somewhere
else (timing edits work regardless; only live posing needs the right scene
open). Select a bound Actor or Prop slot to see its **Scene Object** field;
assign the GameObject that plays this slot in the currently open scene.

Once a slot is bound and the scene matches, scrubbing the playhead poses the
*real* GameObject — never a preview mirror — so Unity's own Move/Rotate/Scale
gizmo works on it the moment you select it. Move it, then press **Key** in the
toolbar to write its current transform as a key at the playhead. Entering
preview snapshots every affected transform and every part renderer's material
property block; leaving it (switching tabs, saving the scene, loading a
different cutscene) restores every one exactly — nothing here is destructive.

**Clip blocks play.** Each actor slot builds, in the editor, the same
`ClipRegistryBlob` its `(rig, clip sets)` bind would bake for a real actor, and
every part goes through the runtime's own `ClipSampler` against the rest pose
the bake captures. So a scrub shows walk cycles at the right loop phase, seam
overlaps cross-fading with the outgoing clip still advancing on its own clock,
sprite tracks stepping frames, facing applied, part-track keys layered on top,
and the camera lane driving the Scene view. The block-timing rules the runtime
player uses live in one place, `CutsceneBlockTiming`, which both paths call — a
preview that disagrees with playback is not representable.

If a slot's clip blocks show nothing, the slot inspector says why (no rig, no
clip sets, or a clip set with validation errors).

### Playing it in the editor

The transport under the toolbar is a rehearsal of runtime pacing, not a
scrubber: **Play / Pause / Stop**, a **Speed** field, **Loop**, and **Skip
Holds**. Stop returns the playhead to where Play was pressed.

A hold marker really holds — the transport stops there, names the hold id it is
waiting on, and waits for **Continue**, exactly as the runtime waits for a host
to release that id. What keeps running under it is the point: the cutscene
clock freezes, the actors' own clips do not, so looping clips keep cycling and
the camera holds its shot. Turn on **Skip Holds** for a quick full run.

## Baking

`CutsceneBlobBuilder.Build(cutsceneAsset, out blob, warnings)` produces a
`CutsceneBlob`. An unresolved clip id or part-track tag is a warning, not an
error — the same lenient philosophy target tags already use — and is baked
anyway; the bound actor's own registry gets the final say at play time.

The timeline is split into **segments** at hold points. A clip block is
assigned to the segment its start time falls in and is *never* clipped across
a hold, even if its authored span crosses one — once the player starts a
clip, it keeps running through the actor's own `PlaybackLayer` regardless of
what the cutscene clock is doing, which is what lets a looping background
clip survive a hold without its loop phase resetting.

## Staging a cutscene (amendment A61)

The cast panel's **Sync to Stage** button writes every bound slot into a
`CutsceneStageAuthoring` component in the open scene — creating one, named
"Cutscene Stage — &lt;asset&gt;", the first time you press it. That component
bakes to one `CutsceneStage` entity per cutscene: the blob plus a
`CutsceneStageBinding` per slot. This is the normal way a cast that was placed
and bound in the editor reaches the game.

```csharp
// Somewhere the host looks a staged cutscene up by its stable id (its asset's StableId):
if (CutscenePlaybackApi.TryFindStage(entityManager, cutsceneKey, out Entity stageEntity))
{
    Entity cutscene = CutscenePlaybackApi.CreatePlayRequestFromStage(entityManager, stageEntity);
    // Every staged slot is already bound. Add or overwrite CutsceneActorBinding entries
    // for anything the stage's subscene could not bake — a spawned unit, or a target that
    // lived outside the subscene at bake time.
}
```

Sync is explicit — pressing Bind or Place never writes the stage on its own,
so rehearsing a cast does not dirty the scene. Re-press Sync to Stage after
changing the cast; the cast panel's Stage status (`none` / `synced` / `out of
date`) tells you when a rebake is owed.

## Playing a cutscene manually (spawned actors)

Actors that do not exist until runtime — spawned units, procedurally placed
props — have no scene object for a stage to bind, so bind them by hand instead:

```csharp
BlobAssetReference<CutsceneBlob> blob = /* built or cached ahead of time */;
Entity cutscene = CutscenePlaybackApi.CreatePlayRequest(entityManager, blob, layerIndex: 0);

// Explicit casting, no discovery magic: you resolve every slot to an entity yourself.
DynamicBuffer<CutsceneActorBinding> bindings = entityManager.GetBuffer<CutsceneActorBinding>(cutscene);
bindings.Add(new CutsceneActorBinding { slotId = berthaSlotId, actorEntity = berthaEntity });
```

- **Pause / speed** — write `CutsceneControl.paused` / `.speed` directly.
- **Skip** — `CutscenePlaybackApi.RequestSkip(entityManager, cutscene)`. A
  skip jumps straight to the cutscene's final instant and fires every
  remaining event whose `fireOnSkip` is set (on by default) — a skipped
  cutscene leaves the exact same world state as a fully watched one, not
  merely a close one.
- **Releasing a hold** — write a `CutsceneHoldRelease.holdId` matching the
  current segment's hold and enable the component. A mismatched id is simply
  ignored (left enabled) rather than erroring, so an early or wrong release
  just waits.
- **Camera** — read the world's `CutsceneCameraPose` singleton (position,
  rotation, field of view, and `isCut` on the exact frame a hard-cut marker
  fires) and apply it however you drive your camera. The toolkit never
  touches `Camera.main` or spawns anything.
- **Events** — the same `AnimEventOutput` buffer / `AnimEventsPending` gate a
  clip's own events use, on the cutscene request entity itself rather than
  any one bound actor.
- **Detachments** — `CutsceneDetachSignal` is enabled on the *detached entity*
  for the frame a Detach marker fires, carrying `worldImpulse` (the authored
  impulse rotated out of host space) and `previousHost`. Read it, act on it,
  and disable it. The toolkit applies no physics of its own — a package that
  assumed a physics stack would not be usable in a project that has another
  one — so a throw is your `AddForce`, your `ThrownItemRequest`, or nothing.

At the end (naturally or via skip) the player stops each Actor slot's clip
layer on the layer index you gave it and marks `CutscenePlaybackState.isComplete`.
The toolkit does not destroy the request entity — that is yours to do once
you're done reading its final state.

### Holds, pause and speed

These are three different things, easy to conflate:

- **A hold** freezes the *clock* only. Looping clips on every bound actor keep
  cycling and the camera holds its shot — that is the point of a hold, so a
  background walk cycle does not pop back to frame 0 when the hold releases.
- **`CutsceneControl.paused`** freezes the *actors* too: every bound Actor
  slot's clip layer is issued `SetSpeed 0`, so nothing advances anywhere,
  hold or no hold. Use this for "the whole scene is frozen," not a hold.
- **`CutsceneControl.speed`** scales every bound actor's clip layer to match
  the clock, reissued the instant it changes and inherited by any block
  issued afterward — slowing a cutscene down slows what plays in it down too,
  not just how fast the clock ticks.

### What recasting does and does not carry over

A slot's rig can be reassigned and the Scene-view preview (above) resolves
tag-addressed part tracks against whatever rig is currently assigned, live.
The **baked runtime player does not**: a part track's tag is resolved to a
dense target index once, at bake time, against the rig the slot had *then*.
Recasting a slot onto a different rig for the runtime path needs a rebake.

## Marks and rendezvous holds

A **mark** is a spot a slot has to reach. Marks live on their own lane, on
Actor and Prop slots alike — a self-driving cart is an actor without a rig, and
your game decides what "move" means for it.

The toolkit does not walk anything there. At the mark's time it enables
`CutsceneMoveToMark` on the bound entity and leaves the walking to you; it then
watches the entity's own `LocalTransform` and disables the component the moment
it is within **Tolerance** of the mark, measured on XZ only. Author marks on
the ground: a mark floating above the walkable plane still resolves, and the Y
an arriving entity stands at is its own.

A hold with **Auto Release When Marks Reached** ticked is a *rendezvous*: the
clock waits there until nothing is outstanding, then resumes on its own. Your
own `CutsceneHoldRelease` still overrides it — leaving without them stays
possible.

**Timeout** is the safety catch, in real seconds and frozen while the cutscene
is paused. Left at 0 the mark waits forever; set to anything else, a mover that
has not arrived in time is placed on the mark, facing the authored angle, with
one warning. A stuck NPC then cannot softlock the scene.

### Authoring one

1. Double-click the slot's **Marks** lane at the moment the order should go
   out — usually t = 0, so everyone starts walking as the cutscene opens.
2. Drag its disc in the Scene view, or press **Set From Object** to drop it
   where the slot's bound object currently stands.
3. Set **Tolerance** (how close counts as there) and **Preview Travel** (how
   long the editor pretends the walk takes — see below).
4. Add a hold after the last arrival and leave **Auto Release When Marks
   Reached** ticked.

### Preview Travel, and why a mark is also a root key

The editor has no pathfinding either, so it *rehearses*: every mark bakes an
extra Linear key into its slot's root lane at `time + previewTravelSeconds`, and
scrubbing lerps the actor along it. That is not what run time does — run time
waits for your movement and a distance test — but it means one sampler draws
both, so the preview cannot quietly disagree with playback about where an actor
ends up.

It also gives the segment after a rendezvous hold a real starting pose: the
boundary key baked at the hold carries the arrival position, so the root lane
resumes from where the actor arrived instead of snapping.

Keep each mark's rehearsed arrival **at or before** the hold that waits for it.
A walk that straddles the hold releases it mid-walk in the editor (the bake
warns about exactly this) — run time plays it correctly either way, but preview
and playback stop agreeing, which is the whole point of the rehearsal.

While a mark is outstanding the slot's **root lane is suspended**, the same way
an attached slot's is: whatever is moving the entity owns the transform. An
actor with root keys *during* the walk therefore ignores them.

### The host contract

```csharp
// Somewhere in your own movement system.
foreach ((RefRO<CutsceneMoveToMark> order, Entity entity) in
    SystemAPI.Query<RefRO<CutsceneMoveToMark>>().WithEntityAccess())
{
    // Walk `entity` toward order.ValueRO.position however your game walks things.
    // Do not disable the component and do not teleport: the toolkit judges
    // arrival itself and disables it, and a player-driven entity is one you
    // simply decline to path — the arrival test is the same for everyone.
}
```

The component is enabled while the order stands and disabled the frame it is
resolved, so a query over enabled `CutsceneMoveToMark` *is* the list of things
still on their way.

## Attach lane

Actors and props can touch. An **Attach** marker binds the slot it sits on to
another slot — either to a **socket** on that host's rig, or to the host's
root — and a **Detach** marker lets go again.

While a slot is attached, **its root lane is ignored**: the host owns the
transform. Everything else keeps running, so an actor riding a cart can still
play clips and wave. Attaching while already attached is a **hand-over** — the
old binding drops silently, with no signal and no impulse. Attachments are left
in place when a cutscene ends; a cutscene that wants its cast free authors a
Detach. A skip replays every marker it jumped over, so a skipped run and a
watched one end with the same things riding the same hosts.

### Carry and throw

1. On the prop's slot, add an **Attach** at the moment the hand closes. Host:
   the actor. Socket: the hand socket on the actor's rig. Offset nudges it to
   sit right in the grip.
2. Add a **Detach** at the moment of release and give it an **Impulse**, in the
   host's space — `(0, 2, 5)` throws it up and forward *relative to the actor*,
   so the same marker works whichever way the actor is facing.
3. At run time, read `CutsceneDetachSignal.worldImpulse` off the prop entity
   and hand it to whatever moves things in your game.

The prop is left at the world pose it was let go at. **If the prop's slot has
any root key at all it snaps back to that lane the instant it detaches** — key
sampling clamps to the last key, so "the root lane resumes" wins immediately.
A prop that should stay where it was thrown gets an *empty* root lane; its
scene transform is its home.

### Board a cart

1. On each rider's slot, add an **Attach** at the boarding moment. Host: the
   cart. Socket: `(root)`. Offset places them on it — a bench seat, or nowhere
   in particular for someone hidden inside.
2. Tick **Hide While Attached** for riders who disappear inside. That adds
   `Unity.Rendering.DisableRendering` to the rider and every rendering member
   of its linked group while the attachment lasts, and removes it again on
   detach. It is deliberately *not* the toolkit's own visibility flag: a host
   that mirrors its own culling into `AnimVisible` every frame would fight one.
3. Key the **cart's** root lane to drive off. Riders come with it.
4. Add a **Detach** for each rider where they get out, with no impulse.

### Hand-over

Give the prop two Attach markers on one lane: the first naming actor A's hand
socket, the second, later, naming actor B's. No Detach in between — an Attach
onto a new host *is* the release of the old one.

## Known gaps

- Mark discs in the Scene view carry no text label; which disc is which is
  read off the inspector after clicking one. `Handles.Label` is the obvious
  tool and this package's Editor sources may not use `Handles`.
- A mark disc drags on its own ground plane only. Height is authored in the
  inspector, never pulled by a gizmo axis.
- No box-select or multi-key drag in the timeline; one item at a time.
- No Auto Key — move with the gizmo, then press Key.
- The header column scrolls horizontally with the lanes rather than staying
  frozen.
- Facing is applied in the **editor preview** (variant pick and mirror) but
  has no runtime-side application: nothing in this package drives facing
  outside host movement code for a runtime system to hook into, so a baked
  cutscene leaves `PartFacing` to the host.
- The preview's facing mirror does not step alt-view frames. That is
  `PartFacing.viewOffset`, which the toolkit bakes as 0 and a host owns — there
  is no package-side rule saying which frame a given direction shows, so the
  preview has nothing to derive one from.
- A sprite frame previews by writing the same `_ImageIndex` / `_AtlasFrame`
  per-instance properties `SpriteMaterialSystem` publishes at run time, so a
  part shows the right frame only if its material reads them. Parts in this
  toolkit are mesh renderers by construction (`Quad`, `FlipbookPlane`,
  `VatMesh`); there is no `SpriteRenderer` path to preview.
- A **bone** socket previews at the host root. Its motion lives inside a VAT
  texture the editor never samples; playback places it correctly. The
  inspector says so when you pick one.
- Multiple simultaneous cutscenes each with their own camera are not
  supported — `CutsceneCameraPose` is one world singleton, matching the one
  camera a game actually has.
