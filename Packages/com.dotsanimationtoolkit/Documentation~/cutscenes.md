# Cutscenes

A cutscene stages several actors and props in a real scene: clip blocks and keyframes on one
timeline per slot, a camera lane, an event lane, marks a slot walks to, an attach lane for riding
another slot, and hold points where the clock pauses until the host releases it. Authoring happens
in a `CutsceneAsset` and the Cutscene Editor tab; playback happens through the same
`PlaybackLayer`/`ClipSampler` machinery every other actor uses — there is no second animation
pipeline, just a system that issues the same `AnimationCommand`s a game would issue by hand.

## Concept model

```
CutsceneAsset ──stages──> CutsceneSlot (named, recastable role: "Bertha", "Minion A")
   │                          │
   │ remembers                ├─ Actor: RigAsset + ClipSetAssets + clip blocks + root/facing/part-track/attach/mark lanes
   │ one scene                └─ Prop: no rig, no clip lane — just root/attach/mark lanes
   │
   ├──> CutsceneCameraLane (keys + hard-cut markers)
   ├──> events (same event-key vocabulary clips use, plus a hold-until-released flag)
   └──> holdMarkers (pause the clock; the host releases them by id, or a rendezvous releases itself)

CutsceneBlobBuilder ──bakes──> CutsceneBlob, split into segments at hold points
                                  (the runtime clock is (segmentIndex, timeInSegment),
                                   never one elastic value)

CutsceneTimelineSystem + CutscenePartOverrideSystem ──play──> bound actor/prop entities
```

A slot is abstract: the asset says "an Actor named Bertha, playing this rig," never "this specific
prefab instance." `CutsceneSlot.SlotId` is a stable 32-bit id (same minting space as a rig's
target/socket ids) — never the slot's name or its position in the list — so the same cutscene can
be recast onto a different roster by re-binding its slots in a different scene, or by a host
supplying different entities at play time.

Every lane's time is raw authored seconds along one flat timeline. Hold points are markers on that
timeline, not a break in it — a cutscene's duration is elastic by design, and splitting into
segments is bake-only work `CutsceneBlobBuilder` does once. Segment 0 always exists; the final
segment's `holdId` is always empty, since nothing pauses after the end.

## Authoring, lane by lane

Open **Window ▸ DOTS Animation Toolkit ▸ Clip Editor** and switch to the **Cutscene Editor** tab
(or double-click a `CutsceneAsset` — it opens there directly, via the same `[OnOpenAsset]` seam
`ActorProfileAssetOpener` uses). **+ Actor** / **+ Prop**, in the **Cast** pane's header, add a
slot; a slot's header doubles as its selection target — click it to edit its name, kind,
actor prefab, rig, clip sets and direction set in the inspector. An Actor slot's **Fill from
Profile** button picks an `ActorProfileAsset` and writes its rig and clip sets onto the slot in one
step, so a staged actor stops drifting from the profile that drives it in-game.

### Clip lane (Actor slots only)

Double-click empty space on a slot's clip lane to add a `CutsceneClipBlock`; drag its body to move
it, its edges to resize it (a resize never carries the rest of the selection — only a move does).
**Overlap with the block before it on the same lane is the crossfade window; blocks that merely
touch are a hard cut** — `CutsceneBlockTiming.SeamBlendDuration` derives this from where you drop
the block, never a separate field. A block's clip plays *from its start until the next block on the
lane starts* — its own `duration` field only feeds that crossfade math; a `Once` clip that runs out
before the next block simply holds its last pose.

Two more fields live in the block inspector, both **Speed** and **Start Offset (s)**:

- **Speed** multiplies the cutscene's own running speed for that block's clip — "the same swing,
  half as fast" is one field, not a second clip. `CutsceneBlockTiming.EffectiveBlockSpeed` treats 0
  as "unset" (a bake from before this field existed) and substitutes 1, never a frozen clip — that's
  `CutsceneControl.paused`'s job.
- **Start Offset (s)** starts the clip that many seconds in, for "play the second half of the
  swing." It reaches the actor as a `SetTime` command issued immediately after the block's `Play`,
  because `Play` always starts a clip at 0.

A block's own speed scales the clip, never the timeline — a crossfade window two overlapping blocks
imply is still measured in timeline seconds regardless of either block's speed.

### Root / transform lane

For an Actor this is root motion: the clip plays in place and this lane moves the actor through the
scene. For a Prop it is the entire authored motion, since a Prop has no clip lane to separate it
from. A slot with no root keys authored leaves its bound entity's transform exactly as it already
is — the player never snaps an un-keyed slot to the world origin.

**A slot's root lane is suspended** for as long as it is riding another slot (see Attach lane) or
walking to an outstanding mark (see Marks lane): whatever is moving the transform in those cases
owns it, and a root key authored underneath would fight it every frame.

### Facing lane (Actor slots only)

Empty by default — facing derives from root travel direction. Add a `CutsceneFacingKey` to pin a
facing (a continuous 0–360° angle, measured from +X toward +Z, **not** the `LocalTransform` Y-euler
convention which measures from +Z) for a moment, e.g. "face the camera during this line." Give the
slot a **Direction Set** and a block naming one of that set's five east-side clips gets re-picked as
the actor turns — `Play` with no blend, then `SetTime` carrying the phase over, so a walk cycle
continues on the same foot instead of restarting. A block naming a clip the set has never heard of
is left exactly as authored, whatever the facing resolves to.

**A mirror needs a mirror point.** Only a rig target with **Faces Direction** ticked mirrors, and
ticking it flips that part *and everything beneath it* — animations included — so on a nested rig
you tick the top of each chain, not every part underneath. A part under an already-ticked ancestor
ignores its own flag rather than cancelling the ancestor's; the slot inspector's "Facing at
playhead" line and the bake both call this out when it applies. A rig with nothing ticked at all
resolves the facing and picks the variant clip but turns nothing visibly — the bake warns, and the
slot inspector says so on its own facing line, since both failures are otherwise silent.

### Part tracks (Actor slots only)

**+ Part Track** opens the same tag picker every part-tag surface in this package uses (see
`sharing-clips.md`). A part track's keys layer *over* whatever the clip lane is currently playing,
on just the channels the track's **Channels** mask covers — a channel outside the mask is left at
the part's already-composited value from the clip, not its rest pose. The tag is resolved to a
dense rig-target index once, at bake time, against whatever rig the slot has *then*; recasting a
slot onto a different rig for the runtime path needs a rebake (the Scene-view preview, by contrast,
resolves the tag live against whatever rig is currently assigned).

### Attach lane (every slot)

Double-click to add an Attach at the playhead; the inspector picks the **Host** slot it rides and,
when that host is an Actor whose rig declares sockets, which **Socket** — or `(root)` for the
host's own transform. A **Detach** marker on the same lane lets go again. A diamond marker draws an
Attach, a ring draws a Detach.

While a slot is attached, its root lane is ignored — the host owns the transform via a `Parent` or
`SocketAttachment` component the player adds — but everything else keeps running, so an actor riding
a cart can still play clips and wave. **Attaching while already attached is a hand-over**: the old
binding drops silently, with no signal and no impulse, and the new one takes its place. Two markers
at the same instant apply in authored order. Attachments are left in place when a cutscene ends; a
cutscene that wants its cast free authors a Detach. A skip replays every marker it jumped over, in
order, so a skipped run and a watched one end with the same things riding the same hosts.

Tick **Hide While Attached** for a rider that disappears inside its host — a passenger inside a
cart. This adds `Unity.Rendering.DisableRendering` to the rider and every rendering member of its
`LinkedEntityGroup` while the attachment lasts, and removes it again on detach. It is deliberately
not the toolkit's own `AnimVisible` flag: a host that mirrors its own culling into `AnimVisible`
every frame would fight it.

A **Detach**'s **Impulse** is authored in the host's space and rotated to world at the instant of
detachment — `(0, 2, 5)` throws a thrown prop up and forward *relative to whichever way the actor
was facing*. The prop is left at the world pose it was let go at; **if the prop's slot has any root
key at all, it snaps back onto that lane the instant it detaches** (key sampling clamps to the last
key, so "the root lane resumes" wins immediately) — a prop that should stay where it was thrown
needs an *empty* root lane, so its scene transform is its only home.

### Marks lane (every slot)

A mark is a spot a slot has to reach; marks live on their own lane on Actor and Prop slots alike — a
self-driving cart is a Prop with marks of its own. Double-click the lane at the moment the order
should go out (usually t = 0, so everyone starts walking as the cutscene opens); drag the disc in
the Scene view, or press **Set From Object** to drop it where the slot's bound object currently
stands. Fields: **Tolerance (m)** (how close counts as arrived, XZ only — Y is never tested), and
**Timeout (s, 0 = wait)** (0 waits forever; anything else places a mover there by teleport, with one
warning, so a stuck NPC cannot softlock the scene).

The toolkit does not walk anything there — at the mark's time it enables `CutsceneMoveToMark` on the
bound entity and watches its `LocalTransform`, disabling the component the instant the entity is
within tolerance. **Preview Travel (s)** is editor-only rehearsal: since the editor has no
pathfinding either, every mark bakes an extra Linear key into its slot's root lane at
`time + previewTravelSeconds`, and scrubbing lerps the actor along it — this is *not* what the
runtime does (which waits for real movement and a distance test), but it means one sampler draws
both paths, so the preview cannot quietly disagree with playback about where an actor ends up. Keep
a mark's rehearsed arrival at or before any hold that waits for it; a walk that straddles the hold
releases it mid-walk in the editor rehearsal only (the bake warns about exactly this shape) — the
runtime plays it correctly either way.

A hold with **Auto Release When Marks Reached** ticked is a *rendezvous*: the clock waits there
until nothing is outstanding, then resumes on its own; a host's own `CutsceneHoldRelease` still
overrides it.

### Camera lane

Add keys the same way as any other lane; the inspector's **Align to Scene View** button captures
the Scene view camera's current pose and field of view into the selected key. Add cut markers on
the row below for a hard cut instead of a smooth move between two keys — a cut window is bounded by
the nearest cut markers on either side of a sampled instant, so a shot never blends across the cut
that names it as the one exception to "one camera just moving around the scene."

### Events lane, including holding events

Double-click to add a `CutsceneEventMarker`; the inspector's **Event Key** is a plain numeric field
against the same event-key vocabulary a clip's own events use — every cutscene event lives on one
shared "Events" row (unlike a clip's own Events lane, which grows one row per distinct event name).
**Int Param** / **Float Param** carry a payload the same way a clip event's do, and a payload that
means something to your game rather than to the toolkit (a dialogue sequence id, say) can have its
own inspector: implement `ICutsceneEventInspectorProvider` in your editor assembly and register it
from an `[InitializeOnLoadMethod]` via `CutsceneEventInspectorProviders.Register(...)`. Return `true`
from `TryBuildInspector` for the keys you own and the default int/float fields stay for everyone
else.

**Fire On Skip** defaults on: a skipped cutscene must leave the same world state as a watched one
unless a marker opts out.

**Hold Until Released** makes an event a *cue*: it fires and the clock stops in the same frame, and
resumes only when the host releases a hold named after the event's own registry name — one marker
instead of an event plus a hand-matched hold id.

- The hold id is the event's registry name (`Dialogue`, `Footstep`, …), or `event:XXXXXXXX` when
  nothing in the project's event vocabulary names the key — the bake warns when it has to fall back,
  because the host must then release that exact fallback string.
- The cue fires *before* the pause, not after: the event is baked into the segment that ends at its
  time. A host that never saw the cue could not release the hold it starts.
- Read the id back at runtime with `CutsceneApi.TryGetCurrentHoldId`, which answers only while the
  clock is actually paused on a hold.
- Two holding events at one instant share a hold and both fire. A holding event landing on an
  authored hold marker's own time keeps the authored marker's id, with a bake warning that says
  which id survived.
- In the Cutscene Editor a holding event's marker wears the Holds lane's colour, and the Holds row
  itself draws a dimmed, read-only ghost where it stops the clock — edit the event, not the ghost.

### Hold markers

A `CutsceneHoldMarker` needs an id string; the host releases it by that same string
(`CutsceneHoldRelease.holdId`) at play time. **Auto Release When Marks Reached** defaults on in the
data model — every freshly-authored hold is a rendezvous unless untoggled — but the Cutscene
Editor's own Hold inspector currently exposes only **Time (s)** and **Hold Id**; toggle
**Auto Release When Marks Reached** off from the `CutsceneAsset`'s default Unity inspector (expand
the Hold Markers list there) rather than from the tab.

## Stage baking and scene binding

The toolbar shows whether the cutscene's remembered scene (`CutsceneAsset.sceneGuid`) is the one
open — **Remember Current Scene** the first time, **Open Scene** if you're somewhere else (timing
edits work regardless of which scene is open; only live posing needs the right one). Select a bound
Actor or Prop slot to see its **Scene Object** field; assign the GameObject that plays this slot in
the currently open scene. Bindings are recorded per scene in `CutsceneAsset.sceneBindings`, keyed by
`GlobalObjectId.ToString()` rather than any editor-only object reference — Authoring/ code never
references the editor assembly, so only editor code ever parses one back out.

The cast panel's **Sync to Stage** button writes every bound slot into a `CutsceneStageAuthoring`
component in the open scene — creating one, named `"Cutscene Stage — <asset>"`, the first time you
press it. `CutsceneStageBaker` bakes that into one `CutsceneStage` entity per cutscene: the baked
`CutsceneBlob` plus a `CutsceneStageBinding` buffer, one entry per bound slot. A binding whose
target lives outside the stage's own subscene bakes to `Entity.Null` — a baker's
`GetEntity(GameObject, TransformUsageFlags)` only resolves GameObjects baked in the same subscene —
so the host must supply that binding at play time instead.

Sync is explicit: pressing Place or Bind never writes the stage on its own, so rehearsing a cast
does not dirty the scene. The cast panel's **Stage** status line reads `none` / `synced` / `out of
date`, recomputed on every rebuild by comparing the stage's own bindings against what the cast panel
currently resolves — press Sync to Stage again after changing the cast.

`CutsceneBlobBuilder.Build` is what both `CutsceneStageBaker` and the Cutscene Editor's own preview
call — an unresolved clip id, part-track tag, attach host, or socket is a bake **warning**, never an
error, the same lenient philosophy target tags already use (see `sharing-clips.md`); it is baked
anyway, and the bound actor's own registry (or the host's own rig) gets the final say at play time.

## Editor workflow

The tab is a cast list, a viewport, and an inspector, over a timeline. Every boundary drags and each
position is remembered (a `TwoPaneSplitView` for cast|viewport+inspector, another for
timeline-vs-everything-above).

- **Cast panel** (left). One row per slot: a state dot (`●` bound, `○` unbound, `⚠` bound to
  something this scene no longer has), the slot's name and kind, and four icon buttons — **Place**
  instantiates the slot's Actor Prefab at the viewport's current framing and binds it in one Undo
  step; **Bind** opens an inline object field to drag an already-placed GameObject onto; **Select**
  and **Frame** put the bound object under the cursor and on screen. Selection syncs both ways:
  clicking a slot's row lights its timeline group, and Unity's own `Selection.selectionChanged`
  drives the reverse (walking up the clicked object's ancestry to find which slot's bound object it
  or a parent of it is).
- **Viewport.** A self-contained camera, not Unity's Scene view — **Shot** mode (on by default)
  samples the camera lane at the playhead, exactly like the runtime does, including cuts; turn it
  off (or start navigating) for a free orbit rig. Left-drag orbits, middle-drag pans, right-drag
  looks around, wheel dollies; hold the right button and fly with **WASD** and **Q/E**, Shift to
  boost. **Click an actor** in it to select that slot (Ctrl/Cmd or Shift keeps whatever the timeline
  already has selected, so picking an actor never throws away a beat under construction); picking is
  bounds-based over the bound cast only. **F** frames the selected slot, **Shift+F** frames the
  whole cast. A transform gizmo (its own, drawn only for this viewport, never a scene object) stands
  on the selection; **W/E/R** switch it between move/rotate/scale, matching what **Key** and Auto Key
  record whether you moved the actor here or in the Scene view.
- **Selection.** A click selects one item. Ctrl (Cmd) toggles one item in or out of the selection;
  Shift adds. Clicking something already selected keeps the whole selection, so a drag started on
  one of several items moves all of them together. Dragging on empty lane space draws a band and
  selects everything it crosses — across lanes and across slots — holding Shift to add to what is
  already selected instead of replacing it. The inspector edits the last item clicked
  (`primaryItem`) and reports `"+ N more selected — editing the last …"` when there is more than
  one.
- **Moving.** Dragging any selected item moves every selected item by the same delta, in one Undo
  step (`CutsceneSelectionMath.ShiftTimes`) — drag the group past the start of the timeline and it
  stops with the earliest item on zero, rather than piling the rest on top of it. Resizing a clip
  block by its edge is always single-item. **Delete**/**Backspace** removes everything selected.
- **Clipboard.** Ctrl+C/X/V/D — copy, cut, paste, duplicate (`CutsceneKeyClipboard`). Times are held
  relative to the earliest copied item, so a paste lands the whole beat at the playhead with its
  rhythm intact. A slot-scoped item pastes onto the **selected slot** when one is selected, and back
  onto its own source slot otherwise — that is how a beat travels from one actor to another. The
  camera, event and hold lanes ignore the target slot entirely. A part-track key finds its
  destination track by **tag**, not list position, creating that track on the destination slot if it
  has none yet. Ctrl+D duplicates in place at the copied items' own earliest time and leaves the
  copies selected, ready to drag off. The buffer survives switching cutscenes but not a domain
  reload.
- **Auto Key.** A toggle in the status row over the lanes (beside **Key** and **Skip Holds**),
  lit red while on, off by default, remembered per editor session
  (`SessionState`). With it on and the preview active, moving a bound object or one of its parts
  with Unity's own gizmo (or the in-tab viewport gizmo) writes a key at the playhead the instant you
  release the drag — one key per gesture. It tells a gizmo drag from the preview's own writes by
  comparing against the exact pose the preview last applied, so scrubbing never keys anything, and
  it is inert while the transport plays.
- **Curves.** A selected transform or camera key's **Interpolation** field is followed by an
  `EasingCurveEditorElement` showing that easing; on a preset the curve is drawn for reference and
  does not take a drag, but dragging a handle turns the key into a custom Bézier with those handles.
  It is the same widget and the same `ClipSampler.Ease` a clip's own keys use, so the shape matches
  playback exactly.
- **Transport.** The strip at the top of the timeline pane, in the toolkit's shared icon style:
  ⏮ ◀ ▶ ■ ▶ ⏭ ⟳ (jump to start, step a thirtieth back, play/pause, stop, step forward, jump to
  end, loop), then **Time** and **Speed** as draggable caption+field pairs, **Zoom** with **All** and
  **Playhead**, and a **Continue** button that only appears while gated on a hold. Space, ← →,
  Home and End drive it whenever this tab is showing. This is a rehearsal
  of runtime pacing, not a scrubber — a hold really holds: the transport stops there, names the hold
  id it is waiting on (`⏸ Holding on '<id>'`, noting when the stop came from an event cue rather than
  an authored marker), and waits for Continue exactly as the runtime waits for a host to release
  that id. What keeps running under a hold is the point: the clock freezes, but every actor's own
  clip keeps advancing, so a looping walk keeps cycling and the camera holds its shot. **Stop**
  returns the playhead to wherever Play was pressed.
- **Timeline navigation.** Ctrl+wheel zooms about the cursor, keeping the time you're pointing at
  under the pointer. **Shift+F** (or **All**) fits the whole cutscene to the window, **F** centres on
  the selection, and **Alt+P** (or **Playhead**) brings the playhead to the middle without moving it. The header column freezes vertically with the lanes and never
  scrolls horizontally with them.

## Playing a cutscene

The common path — a cast that was placed and bound in the editor, then synced to a stage — is a
handful of lines against `CutsceneApi`'s public surface:

```csharp
// Once, wherever the game decides a cutscene should start:
if (CutsceneApi.TryFindStage(entityManager, introCutsceneAsset.StableId, out Entity stageEntity))
{
    Entity cutscene = CutsceneApi.CreatePlayRequestFromStage(entityManager, stageEntity);

    // Every staged slot is already bound. Add or overwrite CutsceneActorBinding entries for
    // anything the stage's own subscene could not bake — a spawned unit, or a target that lived
    // outside that subscene at bake time.
    DynamicBuffer<CutsceneActorBinding> bindings =
        entityManager.GetBuffer<CutsceneActorBinding>(cutscene);
    bindings.Add(new CutsceneActorBinding { slotId = heroSlotId, actorEntity = spawnedHero });

    activeCutsceneRequest = cutscene;
}

// Every frame afterward, while activeCutsceneRequest is alive:
if (entityManager.GetComponentData<CutscenePlaybackState>(activeCutsceneRequest).isComplete)
{
    entityManager.DestroyEntity(activeCutsceneRequest);
    activeCutsceneRequest = Entity.Null;
}
```

`TryFindStage` is a linear scan over every `CutsceneStage` entity in the world, matching
`CutsceneStage.cutsceneKey` against the source asset's `StableId` — cache the result rather than
calling it every frame if a host keeps many stages around. For actors that do not exist until
runtime and have no scene object for any stage to bind — spawned units, procedurally placed props —
skip the stage lookup and call `CutsceneApi.CreatePlayRequest(entityManager, blob, layerIndex)`
directly with a blob built or cached ahead of time (a `CutsceneStage.blob` read off an already-baked
stage works fine here too), then fill every `CutsceneActorBinding` entry by hand: explicit casting,
no discovery magic.

Beyond starting it, a host steers a running cutscene with direct field writes rather than more API
surface:

- **Pause / speed** — write `CutsceneControl.paused` / `.speed` directly. `speed` is clamped to 0
  (behaves like paused) if written non-positive.
- **Skip** — `CutsceneApi.RequestSkip(entityManager, cutscene)`. A skip jumps straight to the exact
  same `(segmentIndex, timeInSegment)` a full play-through eventually settles on and fires every
  remaining event whose `fireOnSkip` is set — a skipped cutscene leaves the identical world state a
  fully watched one would, not merely a close one.
- **Reading a paused hold's id** — `CutsceneApi.TryGetCurrentHoldId(entityManager, cutscene, out
  FixedString64Bytes holdId)`, which answers only while the clock is actually paused on a hold.

The player never destroys the request entity itself — that stays the host's job, once its final
state (`CutscenePlaybackState.isComplete`, any events it fired, wherever it left the cast) has been
read.

## Runtime contracts a host consumes

Every one of these is written by `CutsceneTimelineSystem` (in `AnimationToolkitLogicSystemGroup`) or
`CutscenePartOverrideSystem` (between `TransformSampleSystem` and `TransformApplySystem` in
`AnimationToolkitPresentationSystemGroup`); a host only ever reads and, where noted, disables them.

### `CutsceneCameraPose`

A world-scoped singleton — one camera, so only one running cutscene's shot ever drives it. Carries
`position`, `rotation`, `fieldOfView`, `isCut` (true on the exact frame a hard-cut marker fires — the
host's cue to snap instead of easing), and `isDriven`. `isDriven` is cleared at the top of every
frame before any cutscene runs and only set true while a running, incomplete cutscene's *current
segment* has camera keys to sample from — so "no camera lane this segment" and "the cutscene just
ended or was skipped" both read as not-driven rather than holding a stale pose. Apply
position/rotation/fieldOfView to your own camera only while `isDriven` is true; the toolkit never
touches `Camera.main` or spawns a camera of its own.

### `CutsceneMoveToMark`

Enabled on a bound entity for as long as an outstanding mark order stands (`IComponentData,
IEnableableComponent`). Carries the target `position`, a `facingRadians` applied only if the mark
times out, `toleranceMeters`/`timeoutSeconds` copied from the authored mark, and `elapsedSeconds`
written by the player only (frozen while the cutscene is paused, but arrival is still judged even
then — a rendezvous hold exists precisely to be resolved by movement happening while nothing else
advances). A host's own movement system walks the entity toward `position` however it walks
anything else; it must not disable the component or teleport the entity — the toolkit alone judges
arrival (XZ distance within `toleranceMeters`) and disables the component the instant it resolves,
by arrival or by timeout. Querying enabled `CutsceneMoveToMark` *is* the live list of "things still
on their way."

### `CutsceneDetachSignal`

Enabled on the *detached* entity for the single frame a Detach marker fires. Carries `worldImpulse`
(the authored impulse, rotated out of host space into world space at the instant of detachment) and
`previousHost` (whatever the entity was riding, for crediting a throw to it). The host reads it and
disables it — this package assumes no particular physics stack, so it hands over an impulse and
applies none of its own: a throw is your `AddForce`, your own thrown-item request, or nothing at
all.

### `CutsceneFacing`

Written every frame a bound Actor slot's facing has an answer, and disabled when the cutscene ends
or is skipped. Carries one field, `angleDegrees`, measured from +X toward +Z (0 = east, 90 = north)
— the Direction Sets/`FacingResolver.FromMovement` convention, **not** a `LocalTransform` Y-euler,
which measures from +Z. The toolkit never writes `PartFacing` itself — a host's own facing system
already writes that on every part, and two writers would fight — so mapping `CutsceneFacing` onto
whatever drives `PartFacing` in your project is the entire integration: one component read.

### `AnimEventOutput` (on the cutscene request entity)

The exact same buffer shape a clip's own events publish, gated by `AnimEventsPending`, but scoped to
the request entity itself rather than to any one bound actor — a cutscene event is not about one
slot. Each pulse carries `eventKey`, `intParam`, `floatParam` (`layerIndex` and `clip` are always
default on a cutscene-sourced pulse, since there is no one layer or clip it came from). Read it the
same way `animation-events.md` describes reading a clip's own pulses.

### `CutsceneHoldRelease`

The host's write side of releasing a hold: set `holdId` and enable the component. The player
consumes and disables it the instant it matches the current segment's hold id exactly; a mismatched
id is left enabled and simply ignored rather than erroring, so an early or wrong release just keeps
waiting. A rendezvous hold (`autoReleaseWhenMarksReached`) can release itself with no write to this
component at all, once every outstanding mark on every slot is resolved.

## Known limitations

- **The Hold Marker inspector exposes only Time and Hold Id.** `autoReleaseWhenMarksReached`
  defaults on for every freshly-authored hold but has no toggle in the Cutscene Editor tab itself —
  switch it off from the `CutsceneAsset`'s default Unity inspector instead.
- **The Events lane has no per-event vocabulary picker.** Unlike a clip's own Events lane (one row
  per distinct event, with a searchable Add Event picker), a cutscene keeps every event on one flat
  "Events" row and its **Event Key** field is a raw numeric entry — there is no Create/search
  affordance here, so an author needs to already know or look up the key.
- **A recast rig needs a rebake for playback.** The Scene-view preview resolves a part track's tag
  live against whatever rig a slot currently carries; the baked runtime player resolved that tag to
  a dense target index once, against the rig assigned at bake time. Recasting a slot onto a
  different rig for the runtime path is silently stale until the next bake.
- **Mark discs in the Scene view carry no text label.** Which disc is which is read off the
  inspector after clicking one — `Handles.Label` would be the obvious tool, and this package's
  Editor sources avoid `Handles` entirely (line meshes plus a raycast instead, per this repo's
  Conformance_E rule).
- **A mark disc drags on its own ground plane only.** Height is authored in the inspector; no gizmo
  axis pulls it.
- **The preview's facing mirror does not step alt-view frames.** That is `PartFacing.viewOffset`,
  which the toolkit always bakes as 0 and a host owns — there is no package-side rule for which
  frame a given direction shows, so the preview has nothing to derive one from.
- **A sprite frame previews only when the material reads the same `_ImageIndex`/`_AtlasFrame`
  per-instance properties `SpriteMaterialSystem` publishes at runtime.** Every part in this toolkit
  is a mesh renderer by construction (`Quad`, `FlipbookPlane`, `VatMesh`), so there is no
  `SpriteRenderer` path to preview in the first place.
- **A bone socket previews at the host root.** Its real motion lives inside a VAT texture the
  editor never samples; playback places it correctly. The socket dropdown says so when you pick one.
- **Multiple simultaneous cutscenes, each with their own camera, are not supported.**
  `CutsceneCameraPose` is one world singleton, matching the one camera a game actually has.
