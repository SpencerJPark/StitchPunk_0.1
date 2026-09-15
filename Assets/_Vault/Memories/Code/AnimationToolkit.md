---
tags: [memory, code, package, animation-toolkit]
related: "[[RULES]]"
---

# Packages/com.dotsanimationtoolkit — DOTS Animation Toolkit

A sellable UPM package, developed under a **separate doc system** from this
repo's own `_Vault/`: `Docs/AnimationToolkit/`. Before touching anything under
`Packages/com.dotsanimationtoolkit/`, read
[`Docs/AnimationToolkit/HANDOFF.md`](../../../Docs/AnimationToolkit/HANDOFF.md)
first — it names the active spec, the standing owner directives, and what a
session may not decide alone. Everything else in `Docs/AnimationToolkit/` is
closed-phase history; do not read it up front.

## Clips, sets and rigs are independent — only an actor pairs them (Phase F)

**Neither `ClipAsset` nor `ClipSetAsset` has a rig field at all.** An **actor** names a `RigAsset`
and a **list** of `ClipSetAsset`s, and that is the entire pairing mechanism in the data model. Which
dense target a track drives is resolved at bake against the actor's rig; a track is otherwise just a
tag and some keys.

**The Clip Editor's Rig and Clip Set pickers are independent, and this is load-bearing.** The rig is
a plain window field (`activeRig`, `[SerializeField] sessionRig` for the reload, `CarriedState.rig`
for the re-dock) — no asset records it. `OnClipSetChanged` deliberately does not touch the rig field
and `OnSkinnedSourceChanged` deliberately writes no asset. **If you find yourself syncing one from
the other, that is the bug this phase removed**: swapping the open set used to swap the rig out from
under it. `ClipEditorWindow.RigOfOpenWindow` is the one way a rig reaches code outside the window
(the Mirror utility's project-browser action needs one and has nothing on the clip to read).

Four consequences that are easy to get wrong:

- **`ClipRegistryBuilder.Build` takes `(RigAsset, IReadOnlyList<ClipSetAsset>)`,** and
  `ClipValidation.ValidateSet` is now `ValidateBind(rig, clipSets, …)`. There is one blob per
  **(rig, set-list) bind**, its clip list is the union across sets sorted by clip id, and
  `ClipRegistryBlob.setKey` holds the bind key — `rig.StableId` XOR-folded with every set's. The set
  list is sorted by set id first, so a repeated set would cancel itself out of the XOR if it were not
  deduplicated; `BuildCanonicalClipSets` does both.
- **An unresolved binding is a warning-and-skip, on both halves.** A tag the rig lacks is T2/`V35`;
  a *target id* the rig lacks is T6/`V38`, the mirror. Neither can be an error, because with no rig
  on the clip there is no "wrong", only "does not line up". `V02` survives **for VAT tracks only** —
  VAT cannot retarget, so it is the one binding with no lenient fallback. (The spec calls the new
  rule "T4"; that name was already `V37`'s.)
- **A null rig in `ValidateBind` means unbound, not broken** — no `V13`, no binding rules, just the
  set-scoped ones. `ValidateClip` judges no binding at all. Both are what let a set inspector show a
  clean set that is paired with nothing.
- **VAT is the exception and pins its set to one rig.** `VatTextureSetAsset.sourceRigKey` is stamped
  by the bake panel — which now has its own **Rig** field, since a set no longer supplies one — and a
  mismatch is `V40`, an error. A bind may carry at most one VAT texture set (`V39`), because the blob
  has exactly one `vatSetKey`.

`ClipSetAsset.eventKeys` is gone; `VocabularyRegistryProvider.AnimEventKeys` is the only source.

## The vocabulary pattern (target tags, event names)

As of amendment A52 (Phase E; A51 specified this, A52 closed the gap between
spec and tree), two project-wide vocabularies exist — `TargetTagRegistry` and
`AnimEventKeyRegistry` — both auto-created under `ProjectSettings/` on first
use via `VocabularyRegistryProvider`, no asset to create by hand. **No
`ObjectField` of either registry type exists anywhere in the package** — every
surface that used to offer one (the Clip Editor toolbar, `RigAssetEditor`'s
Target Tags section, the New Rig wizard) now reads `VocabularyRegistryProvider`
directly. Both follow the same rule: **a name is typed in exactly one place,
the registry, through `VocabularyPicker`'s inline "Create …" row or its
"Edit …" row into the registry inspector.** Every other editor surface —
pickers, rig rows, timeline lanes — only ever selects, never accepts free
text. A row minted through the picker's "Create …" row persists immediately
via `VocabularyRegistryProvider.PersistVocabulary` — `CreateVocabularyEntry`
itself only mints in memory, since `Authoring/` cannot write
`ProjectSettings/` files, so every editor call site that adds a row must
persist it explicitly or the row is lost on the next domain reload. The
canonical list for either vocabulary — add, rename, remove — is
**Project Settings → DOTS Animation Toolkit → Target Tags / Event Names**
(`VocabularySettingsProvider`), hosting the same registry inspector a
picker's "Edit …" row opens in a utility window; a rename there is not
undoable, like Unity's own Tags & Layers page, because the registry lives
outside the asset database. If you add a third vocabulary (or extend either
of these two), route it through `IVocabularyRegistry` and
`VocabularyPicker`/`VocabularyPickerConfig` rather than building a parallel
dropdown — that duplication is exactly what amendment E6 Task 3 undid for
events.

**No raw ids in an editor surface.** A tag id or event key is display-only as
`(unresolved 0x1A2B3C4D)`, and only when the registry cannot name it (a
dangling reference after a delete). Anywhere else, resolve the name first.

**Two tag buttons live in the same Clip Editor pane — don't conflate them (amendment A53).** The
*part* tag (`ClipEditorWindow.BindPartTagButton`/`OpenPartTagPicker`, in `ComponentStack.cs`) sits
at the far edge of the selection heading and writes `RigTargetDefinition.tagId` on the **rig** —
shared by every clip set that uses it. The *track* tag binding (`BuildTagBindButton`, same file)
sits in a component block's header and writes the **clip**'s `TransformTrack.tagId`/
`SpriteTrack.tagId` — whether that one track resolves by target id or by tag. Same
`VocabularyPicker`, different asset written.

**An event key carries a payload schema (A85, 0.32.0).** `AnimEventKeyEntry` has `intParamLabel`,
`intParamValueNames` (index = value), `floatParamLabel`, `floatParamUnit`, edited under a "Payload"
foldout per row in `AnimEventKeyRegistryEditor`. `EventPayloadFieldBuilder`
(`Editor/ClipEditor/Components/`) renders a marker's two fields from it: no schema at all → the
old raw "Int Param"/"Float Param"; value names → `DropdownField` (an out-of-range int shows its raw
number tinted, never clamped); label → labelled field; unlabelled → hidden unless the marker
still stores a non-zero value ("… (unused)", tinted). Traps: `VocabularyQuickEditWindow` hosts the
registry editor through `CreateEditor`, so edit the editor, never the window. Generation runs in
`VocabularyConstantsSection.RegenerateIfConfigured` (no button), with the schema passed as two
optional closures; payload edits regenerate only on `OnDisable`, an add/remove immediately.
`Conformance_G`'s static-class regex reads string literals, so the generator writes
`"public static class " + name`, never a literal name. A nested `FootstepValues` class takes its
name from the row-wide `usedNameCounts` (a row named `FootstepValues` cannot collide in either
order), and its value dictionary is pre-seeded with the class name (a member named like its type
is CS0542).

See [`sharing-clips.md`](../../../Packages/com.dotsanimationtoolkit/Documentation~/sharing-clips.md)
for the target-tag authoring workflow and
[`animation-events.md`](../../../Packages/com.dotsanimationtoolkit/Documentation~/animation-events.md)
for events.

## Event lanes are per-name, not per-track

`ClipAsset.events` is one flat list; the Clip Editor's timeline gives each
distinct event name its own row (lane), addressed through
`EventLaneAddressing` — a pure function mapping `(laneIndex, localIndex)` to
a flat list position, recomputed on demand rather than cached. **The flat list
is not kept globally time-sorted** — each lane's own sort writes back only
into the flat slots that lane's markers already occupied. That's safe only
because nothing downstream needs global order: validation checks events
against V04/V09 (never V03), and `ClipRegistryBuilder.FillEvents` re-sorts by
time before baking regardless of authoring order. If you add code that reads
`ClipAsset.events` directly, do not assume it is time-ordered.

**One inspector, one pin, one rule set for clip and cutscene markers (A86, 0.33.0).**
`EventMarkerInspectorElement` (`Editor/ClipEditor/Components/`) edits either marker through
`IEventMarkerAccessor` (`ClipEventMarkerAccessor` by flat index, `CutsceneEventMarkerAccessor` by
list index); `EventLaneStyle` draws the pin on both lanes; `AnimEventValidation`
(`Authoring/Validation/`) owns V09/V19/V20 plus V41 (key not in the registry) and V42 (int outside
the value names). Traps:

- **The accessor records undo itself, so a clip host must not call `CommitClipEdit`** — it collapses
  from the window's last `gestureUndoGroup`, which is stale here and would fold unrelated history
  into this edit. `ClipInspectorPane` calls `RefreshSerializedClip` + `MarkPreviewDirty` instead.
  The cutscene panel writes through the accessor and then `serializedObject.Update()`, or the next
  `ApplyModifiedProperties` writes the old value back.
- **There is no cutscene validator.** Cutscene findings show only under the inspector
  (`SetFindings`). V41/V42 run only when a registry is passed: the badge and both inspectors pass
  the project registry, the bake passes none, so a deleted name never fails a build.
  `ValidationCode` bytes 41–47 are P1–P7, so V41 = 48 and V42 = 49.
- **Cutscene pins are still one `VisualElement` per marker**, not painted by the lane:
  `CutsceneMomentLaneElement.drawsEventPins` keeps the marker USS class (queries use it) and
  outranks the diamond with inline `rotate`/border/background overrides, painting the pin in
  `generateVisualContent`. A holding event is a `ToolkitPalette.Holding` outline; the `--holding`
  class is never added to a pin (its 3px border would box it).
- **A host `ICutsceneEventInspectorProvider` still wins the payload** through the element's
  `PayloadOverride`; the schema builder renders every other key.
- `G1CheckpointCutscene.asset` stores event key 1 (reserved), so its inspector shows V09 — a game
  asset fact, not an A86 bug.
- **Cutscene events are one row per name too (A86 owner follow-up, 2026-09-13).**
  `CutsceneEditorPanel.BuildEventRows` draws an "Events" group row (its **+** button, right-click
  and double-click all open the event picker first) and then one `BuildEventNameRow` per
  `EventLaneAddressing.ComputeLaneKeys(cutscene.events)` key — the cutscene overloads skip null
  rows. Each lane is registered as `SelectedLaneKind.Event` with `originalIndices`, so every index
  it raises (select, drag, delete, marker menu) is the real `cutscene.events` index. Traps:
  **set `drawsEventPins` in the object initializer** — `SetTimes` builds the markers, and A86-T7
  set it afterwards, so the old single row kept drawing diamonds; and **`InsertArrayElementAtIndex`
  at the end copies the last element**, so an insert must write every field (the old
  `InsertEventDefault` never reset `holdUntilReleased`, and its key 0 is reserved).
- **Cutscene rows wear the Clip Editor's lane look.** `AddTimelineRow` tags each non-ruler lane row
  `cutscene-editor__lane-row` and every other one `--alternate` (rgb 46/54, `TrackLaneElement`'s
  `LaneBackground`/`LaneAlternate`); the ruler row resets the count. Lane elements are transparent
  and rows carry no divider line. Both editors' rows were already 22px — the difference was shade,
  header text and marker centring (markers now sit at `top: 50%` with a negative half-height
  margin), not height.


## Never rebuild a pane from a value-changed callback

A UI Toolkit field's drag handle captures the pointer **on the element itself**, so removing that
element from the panel releases the capture and ends the drag. Every `Clear()` in a rebuild does
exactly that. A `RebuildInspector()` / `RebuildTimeline()` / `RebuildHierarchy()` fired from a
`RegisterValueChangedCallback` therefore kills the drag that produced the value, after roughly one
pixel — the symptom is "the field drags for a split second and stops, every time".

`ClipEditorWindow.IsPointerGestureInProgress()` is the guard, and `RequestInspectorRebuild` /
`RequestTimelineRebuild` / `RequestHierarchyRebuild` are the entry points: they defer to
`FlushDeferredPaneRebuilds`, which the editor tick runs once the capture is gone. **Use the
`Request…` form from anything a field callback can reach.** Two indirect routes are easy to miss:

- `CommitSocketEdit` rebuilt the hierarchy, and a hierarchy rebuild raises `selectionChanged` — not
  suppressed as an echo, because a rebuilt tree hands out fresh items — which rebuilds the inspector.
  It now takes a `rebuildRows` flag; an offset or layer edit passes `CommitSocketPlacementEdit`
  instead, which rebuilds nothing and only re-places the markers.
- `RefreshSocketPlacement` exists because `RebuildSockets` destroys and recreates every marker object
  and re-fetches their material. That is not what an offset change invalidates.

**A field callback must read its siblings off the fields, never out of the closure.** With the
rebuild gone, values captured when the block was built go stale the moment any sibling changes, so
editing one channel writes an old value back over another. `ApplyTransformEditFromFields`,
`ApplyBoneEditFromFields` and `AddBillboardFields`' shared `writeFloatChannel` all do this.

`IsBeingEdited` guards the per-tick live refresh from stamping over a field in use, and it must test
**pointer capture as well as focus** — a label drag never focuses the input behind it.

## The transport bar's numbers drag by their captions

The transport captions ("Length", "FPS", "Frame", "Time", "Speed") are standalone `<ui:Label>`
elements in `ClipEditorWindow.uxml`, not the fields' own labels — the bar is a compact strip and a
`BaseField` label carries an inspector's width. **A field with no label has no drag zone**, which is
why none of those numbers scrubbed. `MakeCaptionDragHandle` attaches a real
`FieldMouseDragger<T>` to the caption and `SetDragZone`s it; `ClipEditorLayoutTests` guards the
caption names, because a rename presents as a number that quietly stops scrubbing.

Two traps live in that helper:

- **`isDelayed` defeats a drag entirely.** A delayed field's `ApplyInputDeviceDelta` writes only the
  displayed *text* and commits on release — the exact "the number moves but nothing happens until I
  let go" symptom. The helper lifts it on `PointerDownEvent` and restores it on
  `PointerCaptureOutEvent` (not `PointerUpEvent`, so a capture lost any other way still restores it).
  Length and FPS still need it for typing: without it `"0.5"` arrives as `"0"` first and the
  minimum-duration clamp collapses the clip to a millisecond mid-word.
- **`IsBeingEdited` does not see a caption drag**, and that is deliberate — the capture is on the
  caption, outside the field — so `SyncTransportPlayhead`'s write-back still runs and is what clamps
  the Frame and Time readouts at the clip's ends.

FPS is an `IntegerField`, not a `FloatField`: whole frames per second is what the value has always
meant, and it is the only way to get one-per-notch drag stepping. `ClipAsset.frameRate` is still a
float, so a fractional rate authored elsewhere reads back rounded.

## The preview is throttled, not merely debounced

`MarkPreviewDirty` re-stamps its timer on every call, so a trailing-edge debounce alone never fires
during a continuous drag: the viewport stood still until the gesture ended. `UpdatePreview` refreshes
on `PreviewSettleSeconds` (quiet) **or** `PreviewMaxWaitSeconds` (max wait) — the second is what makes
a drag live. Keep both if you touch it.

Separately, a **held** transform edit (auto-key off) is in no registry, since the registry is built
from committed keys. `ClipPreviewController.ApplyHeldTargetPose` layers it onto the sampled pose,
composing the way `ClipSampler.ApplyClipToPose` composes an Override track — position and rotation
added to the rest pose, scale multiplying it (§5.11). Without it, an unkeyed drag moves the numbers
and nothing else.

## The viewport camera is an orbit rig with no position of its own (2026-08-29)

**Update (A74, 2026-09-08):** the gesture state machine below left `ClipEditorWindow.CameraNavigation.cs`
for a standalone `PreviewCameraNavigation` (`Editor/ClipEditor/Preview/`) driving any `IPreviewCameraRig`;
`ClipPreviewController` implements that interface directly, a new `PreviewOrbitCameraRig` carries the
same math for a viewport with no controller of its own, and all three viewports (Clip Editor, Actor
Editor, VAT Bake) now share one implementation instead of three. The window keeps its old method names
as thin forwarding calls — see that file's own header comment for why (picking still has to happen between the exclusive gestures and the plain left-drag orbit).

`ClipPreviewController` stores **`orbitFocus` + yaw/pitch + `orbitDistance`**, and derives the camera
position from them (`CameraOrbitPosition`). Everything the Scene view can do is expressed against that
one rig, in `ClipEditorWindow.CameraNavigation.cs`:

| Gesture | Method | What it moves |
|---|---|---|
| Left-drag (**existing**, no modifier needed) | `Orbit` | yaw/pitch, focus fixed |
| Middle-drag | `Pan` | focus, sideways/up |
| Right-drag | `LookAround` | yaw/pitch **about the camera**, then focus is recomputed ahead of it |
| Alt + right-drag, wheel | `Dolly` / `Zoom` | distance |
| Right-drag + `WASD`/`QE` | `Fly` | focus, along the camera's axes |
| `F`, Reset Camera, double-click | `FrameSelection` / `ResetView` | focus + distance |

Three traps, all of them silent:

- **A look is not an orbit with a different sign** — it is the same rotation about a different pivot.
  With no stored camera position, `LookAround` has to capture the position *first*, rotate, then put
  `orbitFocus` back `orbitDistance` ahead of it. Skipping that swings the camera around the rig.
- **`Pan` must be handed the viewport's pixel height.** The controller is given a size a render at a
  time and never keeps one, so any fixed rate drifts under the cursor as soon as the pane is resized.
- **Fly is stepped on the editor tick, never on `KeyDownEvent`.** Key repeat is an OS setting; moving
  per key event makes the fly speed a property of the user's control panel.

**Alt + left is deliberately *not* a gesture of `ResolveCameraGesture`.** Alt/Shift + *click* is the
pick-cycle modifier (`isPickCycleRequested`), and a camera gesture is exclusive — claiming Alt + left
would orbit correctly and kill cycling. The plain left-drag path already tells a drag from a click by
travel distance, so it gets both. Same reason `PointerCaptureOutEvent` ends the gesture: a capture
lost without a release leaves the viewport in fly mode, silently eating every keystroke.

## Verification gate

Unity MCP only works while the Editor is open. After a `.cs` change:
`refresh_unity(compile: "request")` → poll `editor_state` until idle →
`read_console` for `error CS####` → `run_tests` EditMode
(`DotsAnimationToolkit.Tests.EditMode`) → PlayMode
(`DotsAnimationToolkit.Tests.PlayMode`, `init_timeout: 120000`). Check the
discovered **total**, not just pass/fail — `total: 0` with `resultState:
"Passed"` is a suite that silently stopped compiling.

## Never write `GUIUtility.hotControl` from the preview tick

`GUIUtility.hotControl` is not an int field with an accessor — **assigning it takes or releases the
mouse capture**, and UI Toolkit's pointer capture is synced through it (UIElements uses an internal
`SetHotControlWithoutSendingEvents` precisely to avoid the setter). `ClipPreviewController.Render`
runs on an `EditorApplication.update` tick 30 times a second, so a save-and-restore around
`BeginPreview`/`EndPreview` released the captured pointer within ~33ms of *any* gesture starting:

- a `Button`'s `Clickable` holds the pointer from PointerDown to PointerUp and fires `clicked` only
  if it still has it → **every button stopped opening its picker**;
- a slider/dragger lost the pointer the moment it grabbed it → **every drag died on the spot**
  (timeline zoom being the obvious one).

Amendment A54 added it as speculative "free insurance" and it cost three sessions. The symptom
—everything in the window half-works — looks nothing like its cause, so **if buttons and drags break
together and nothing about them changed, look for something writing global IMGUI state on a timer.**

## A timeline row is named by its tag, and the header is the binding surface (A56)

`RebuildTimeline` skips any transform/sprite/bone track whose `keys` list is empty, and names the
rest **tag first, rig part second** (`DescribeTrackBinding` → `TrackBindingLabel`), no kind
prefixes. As of amendment A56 both halves of a transform/sprite header are **pickers, not labels**:

- The tag half opens `VocabularyPicker` with `ForTrackTagRebind` (no "(none)" row — a keyed track
  cannot be cleared to untagged) and lands in `RetagTrack`, the one retag core the inspector's
  `ApplyTrackTagBinding` also routes through. Picking a tag another keyed same-kind track binds
  **merges this row into it and deletes it** (`ClipComponentModel.MergeTransformTracks` /
  `MergeSpriteTracks`; incoming key wins a same-time collision; sprite merge refused unless
  mode/sliceSpace/baseIndex match). A merge invalidates every stored track index —
  `OnTrackListChanged` clears key selection and expansion rather than remapping.
- The part half opens `RigTargetPicker` and lands in `MoveTagToRigPart`: a **rig** edit (undo on
  the rig, never the clip) that clears the old wearer (T1 uniqueness) and can displace the new
  part's previous tag — deliberate and stated on the picker's hover card.
- Selecting all keys on a binding row moved to the row's empty background (`pointerEvent.target ==
  headerRow` guard); bone/event rows keep label-click-select.

**No keyed track goes tagless (A56 D4, owner directive).** Every track-creating path — Add
Component, first key (`CommitPendingTransformEdit`), paste — runs the window's
`EnsureClipTrackTagsAssigned`, which tags an untagged part via
`ClipComponentModel.EnsureTargetTagged`: reuse the registry tag named like the part when no other
part on the rig wears it, else mint `Name 2`, … and persist the vocabulary. The registry is a
*parameter* there — EditMode tests call it with an in-memory registry, and routing it through
`VocabularyRegistryProvider` inside the model would mint test tags into real ProjectSettings.
Legacy `tagId == 0` rows render `(assign tag)` (never "(untagged)"), and both halves open the tag
picker until one is assigned.

Three things not to rediscover:

- **`trackIndex` is still the real list index, never a filtered counter.** `AddLane`, `MakeTrackKey`
  (expansion state) and `SelectAllKeysOnTrack` all address `selectedClip.transformTracks[i]`
  directly, while `rowIndex` counts only the rows actually drawn. Renumbering the first to match the
  second would repoint every key address in the window at the wrong track.
- **An empty track has no row, so the timeline is not where its first key comes from** — the part's
  **Key** button in the inspector (`Panes/ClipInspectorPane.cs`, the `keyRow` block in `AddTransformFields`; the window's `KeyDisplayedTransform` does the write) is, and it never
  needed a lane. The status line says how many tracks are hidden this way, because "I added a
  Transform and no row appeared" otherwise has no answer on screen.
- **Focus mode resolves through the tag**, using the shared `ClipComponentModel.FindTargetByTag`
  (made public for this) rather than the track's own `targetId`, which a tag-bound track may leave
  stale. Two answers to "which part does this track drive" in one loop is exactly the disagreement
  `TrackBindsTarget`'s own remarks warn about.

`(untagged)` means the `tagId == 0` sentinel — keys that play but will not share. `(no tagged part)`
means the tag resolves to nothing on the open rig — rule T2, skip not fail, so the row stays.

## Four tag surfaces, three cores — and which of them carries the keys

`RigTargetDefinition.tagId` (which tag a part wears) is written **only** by `WriteRigPartTag`.
`TransformTrack/SpriteTrack.tagId` (which tag a row's keys belong to) is written by `RetagTrack`
(one row) and `ClipComponentModel.MoveTracksToTag` (a whole clip). Every surface routes through
those; do not add a fourth writer.

|Surface|Subject|What it does|
|---|---|---|
|Timeline row, tag half|the row|`RetagTrack` — keys move to the picked tag, merging into a row already on it|
|Timeline row, part half|the row|`MoveTagToRigPart` → `WriteRigPartTag` — the tag lands on another part; **no key carry**, the keys are already on the right tag|
|Inspector component block, "Tag:"|the row|`ApplyTrackTagBinding` → `RetagTrack`, same as the timeline's tag half (A56 D6)|
|Inspector selection heading, "Tag:"|the **part**|`RetagRigPart` → `WriteRigPartTag` **plus** `CarryClipSetKeysToTag`|

**The last row is the one that surprises.** The part is the subject there — "this part is the Torso
now" — so the owner's call (2026-08-28, after being shown the alternative) is that its animation
comes along: every row in **every clip of the open clip set** keyed against the part's old tag is
moved onto the new one, merging under A56 D2's rules where the clip already has a row there. The
timeline's part half is the mirror image and deliberately does *not* carry: there the row is the
subject and its keys are already on the tag being placed.

Two consequences to know before touching this:

- **The carry is scoped to the open clip set**, because that is the set of clips the window has.
  Another clip set on the same rig keyed against the old tag is not rewritten and its rows read
  `(no tagged part)` until retagged in its own window. That is why the sweep reports on screen
  (`DescribeTagCarry`) rather than being silent — and why it is one undo group.
- **The inspector's part button used to assign straight to the field**, which cost three bugs at
  once: rule T1 went unenforced (two parts wearing one tag, which every `FindTargetByTag` resolves
  by picking whichever it reaches first), the timeline kept showing the pre-pick binding, and the
  part's keys were left behind on a tag nothing wore.

## The header column and the lane column agree by height, not by index

Nothing links a track header to its lane: they are two sibling stacks in `#timeline-row`, and a row
lines up with its keys **only** because every header is exactly as tall as every lane
(`--clip-editor-lane-height`, one token for both, in `ClipEditorWindow.uss`). One header that grows
with its content slides every row below it away from its own keys, and the symptom — keys that look
like they belong to the row above — reads as a hit-testing bug rather than as a layout one.

So a header that wraps onto a second line does **not** measure itself:

- `.clip-editor__track-header--wrapped` and `.clip-editor__lane--wrapped` are one pair, both
  `--clip-editor-lane-height-wrapped` (exactly two lanes), toggled together by
  `BindTrackHeaderWrap`. Changing one height without the other is the drift above.
- The wrap itself is flexbox: the header row is `flex-wrap: wrap` and the arrow + part live in one
  `…__track-header-part-group` so they move down together — wrapping the part alone would leave the
  arrow trailing the tag, pointing at nothing. Every header child is one lane tall, so a wrapped
  row's two lines land on the two lanes beside them.
- Wrapped-ness is **observed, not computed**: the geometry callback watches the *group's* `layout.y`
  (0 on line one, a lane's height on line two). Watching the header row instead would be watching
  this callback's own output — it is what changes the row's height.
- `partGroup.pickingMode = Ignore`, or a press on the gap between the arrow and the part stops
  reaching the row background, which is what selects the track's keys (A56).

The name column's width is the one size in that stylesheet a user can change: `#track-header-resizer`
drags it and writes `style.width` inline, which by design beats the `--clip-editor-track-header-width`
token — the token is what an unresized window starts at, and nothing is written until the strip is
actually dragged. A drag strip rather than a `TwoPaneSplitView` because the row lives inside the
timeline's scroll view, where its height is whatever the tracks add up to and a split would have
nothing definite to divide. Persisted on pointer-up (`…ClipEditor.TrackHeaderWidth`), and re-fitted
on every `#timeline-row` resize so a narrowed window borrows width back — which is why
`requestedTrackHeaderWidth` is kept unclamped beside the applied one.

## The Clip Editor goes dead after any recompile while it's open

Any script recompile anywhere in the project — the vocabulary registries' constants regeneration
(`VocabularyConstantsSection` writes a `.cs` under `Assets/Generated/`) is the most common cause, so
editing a tag or an event from inside the Clip Editor triggers it reliably — destroys and re-creates
the `ClipEditorWindow` instance. Unity **does** call `CreateGUI` again on the far side, so the tree
comes back looking healthy; what does not come back is any plain field on the window, `clipSet` and
`selectedClip` included. Every control gated on those then does nothing when pressed —
`OpenPartTagPicker` returns immediately on a null clip set, so the part-tag button reads as a dead
button rather than as lost state.

Fixed by `[SerializeField]`-ing the window's identity (`sessionClipSet`, `sessionSelectedClip`, the
playhead, Rig Edit and the hierarchy selection), captured on `AssemblyReloadEvents.beforeAssemblyReload`
+ `OnDisable` and reapplied by `RestoreView` at the end of `CreateGUI`, sharing that path with the
re-dock's `AdoptCarriedState`.

**Two traps this cost two sessions to find:**

- `CreateGUI` is Unity's post-reload hook. Do not also call it (or a `RebuildLayout` extracted from
  it) from `OnEnable` "so the window recovers" — `OnEnable` runs first, so you get two builds per
  open. `rootVisualElement.Clear()` removes children but **not** callbacks registered on
  `rootVisualElement` itself, which `RegisterTransportShortcuts` and `BindKeyTransform` both do. Two
  `KeyDownEvent` handlers on one element both run (`StopPropagation` does not stop a sibling handler,
  only `StopImmediatePropagation` would): Space toggles play twice and so does nothing, arrows step
  two frames, Ctrl+Z undoes twice, G restarts the gesture it just began.
- Bind order in `CreateGUI` is load-bearing. `BindTimelineView` registers its resize handlers on
  `laneStack`/`laneColumn`, which only `BindTimeline` resolves — it used to be called from
  `BindToolbar`, before that, so all three registrations were skipped every single time behind their
  own null guards.

## A session CAN see the editor UI — capture the window, don't guess (2026-08-30)

"Anything on-screen needs the owner to look" is now only half true: an agent session can capture
any EditorWindow pixel-exactly via `execute_code` — reflect `EditorWindow.m_Parent` (a
`DockArea`), walk up to `GUIView`, call its internal `GrabPixels(RenderTexture, Rect)`, read the
RT back and flip it vertically before saving. **Do not use `InternalEditorUtility.ReadScreenPixel`**
— it reads the physical screen, so whatever app overlaps that region is what you capture, and
`Focus()` does not reliably win the fight while the owner works in another program. Capture
BEFORE and AFTER any UI change; three cutscene-editor rounds shipped "gate green" UI the owner
immediately rejected because nobody looked. The owner's judgment still closes a visual pass —
but "the layout is broken" is now findable without them.

The Cutscene Editor's in-tab viewport renders the OPEN scene with a hidden `HideAndDontSave`
camera through URP's `SingleCameraRequest` (`CutsceneViewportElement`). Two traps: sweep leaked
cameras with `Resources.FindObjectsOfTypeAll` (`GameObject.Find` cannot see `HideAndDontSave`
objects, and such objects survive domain reloads); and the open cutscene rides `SessionState`
(`RestoreSessionCutscene`) because the panel dies with every reload — remove that and the tab
comes back empty, reading as a dead tool.

**`GrabPixels` goes stale — check `EditorApplication.isFocused` before trusting a capture
(2026-09-08, A75).** When another application holds OS-level window focus, `GrabPixels` on the
docked view returns a byte-identical frame (confirmed via MD5 across five attempts, real sleeps
between each) no matter how many `RepaintImmediately`/`RepaintAllViews`/`QueuePlayerLoopUpdate`
calls precede it — Unity does not redraw a docked view's actual backbuffer while unfocused,
regardless of internal repaint requests. `EditorWindow.Focus()` does not fix this (it only changes
which tab is internally active, not OS focus). Check `UnityEditor.EditorApplication.isFocused`
first: `false` means any capture attempt is wasted calls. Do not fall back to
`InternalEditorUtility.ReadScreenPixel` to route around this — it reads the physical screen and
will faithfully capture whatever application actually has focus instead (verified: it captured the
owner's code editor, not Unity). Do not force OS focus via `SetForegroundWindow` or similar to
route around this either — that steals input focus from whatever the owner is actively doing.
When unfocused, report that no capture is possible rather than saving a stale image under the
capture's filename; a stale frame with an unrelated result label is more misleading than no image.

## Cutscenes — traps only (shipped 0.15.0)

Full reference lives in the package, not here:
[`cutscenes.md`](../../../Packages/com.dotsanimationtoolkit/Documentation~/cutscenes.md) (concept
model, authoring, playback) and
[`cutscene-api.md`](../../../Packages/com.dotsanimationtoolkit/Documentation~/cutscene-api.md)
(member reference). File list: `find Packages/com.dotsanimationtoolkit -iname "*cutscene*"`.

- **A stage must live in the bound objects' own scene.** `CutsceneStageBaker`'s `Baker.GetEntity`
  only resolves GameObjects baked in the same subscene as the `CutsceneStageAuthoring`; a binding
  naming a GameObject outside it resolves to `Entity.Null` and the host must supply that binding at
  play time instead.
- **A hold is not a pause.** `CutsceneControl.paused` freezes the clock *and* every bound actor's
  clip layer; a hold freezes only the clock — looping clips keep cycling under it by owner decision,
  and outstanding marks keep resolving arrival while held.
- **An outstanding mark suspends its slot's root lane**, exactly like an attached slot: whatever is
  walking the entity there owns the transform, and the merged rehearsal key would otherwise drag it
  along the authored path while the real walk is still happening.
- **An attached slot's root lane is ignored entirely, and permanently once it has ever detached** —
  `hasEverDetached` retires it for the rest of the cutscene, because the flat lane's only real content
  is the pre-ride pickup key; resampling it post-detach snaps the rider back to where it was picked up.
- **A skip replays every attach marker it jumped over**, in order, so a skipped run and a watched one
  leave the identical world — including any detach signal a host was waiting on.
- **Authored beats auto, until a stop key (A73).** A clip block claims its profile layer from the
  moment it starts, for the rest of the cutscene; auto locomotion only ever fills a layer nothing has
  claimed, or one a stop key just handed back. A block's own duration running out does **not** hand
  the layer back — a `Once` "sit down" does not snap to standing on its own just because its clip
  ended; only the explicit **■** stop key does that.
- **The cutscene writes `ActorFacing`, not just `CutsceneFacing` (A73).** Besides the host-mirror
  input `CutsceneFacing`, a running cutscene folds its resolved angle onto the bound actor's own
  `ActorProfileAsset.turnDirections` and writes `ActorFacing.facing` directly — `ActorFacingRepickSystem`
  does the actual turn from there, the same re-pick any other `PlayAnimation` goes through. This
  amends A70-D6's "host-written and never derived" rule for exactly this one case; a host whose own
  facing writer also maps `CutsceneFacing` writes the identical value and nothing fights.

## Shared editor chrome — ToolkitPalette / ToolkitIcons / TransportCoreElement (A72, 0.18.0)

Every tab that plays implements `ITransportTarget` and inserts one `TransportCoreElement` into a
`toolkit-transport__group` named `transport-core-slot`; the window's single root `KeyDownEvent`
handler resolves the active tab's target per keystroke. Three traps:

- **The palette is mirrored, and the mirror is a test.** `ToolkitPalette.Tokens` (C#) and the
  `--toolkit-color-*` block in `ClipEditorWindow.uss` must agree channel for channel;
  `ToolkitPaletteTests.UssTokens_MatchToolkitPalette` fails on any drift. Add a colour in both
  places or the fixture, not the compiler, tells you.
- **One root KeyDown registration, ever.** `CreateGUI` re-runs after a domain reload and the
  root's callbacks survive `Clear()`; a second `RegisterCallback<KeyDownEvent>` on
  `rootVisualElement` doubles every keystroke. Route new keys through
  `OnTransportKeyDown` / `ResolveActiveTransportTarget`, and gate Clip-Editor-only cases on
  `activeTab`.
- **Inline styles are layout only.** A panel built in C# may set `flexGrow`, `width`,
  `flexDirection`, margins, `position`. A colour, border, radius, chrome padding or opacity
  inline outranks the sheet and silently breaks the shared look; it is a class in the `Shared`
  section, or a data-driven colour (an event lane's accent) with a one-line comment saying so.

Three smaller ones: **a `Button` with a child loses its text measure** — Yoga gives a node with
children no measure function, so `button.text` beside an icon `Image` wraps one letter per line
(the first A72 capture showed "Add Event" as a vertical strip); `ToolkitIcons.SetButtonIconAndText`
puts the word in a `Label` child instead, and that is the only way to build icon-plus-word.
`ToolkitIcons.MakeIconButton(null, …)` is the deliberate text path for glyphs Unity has no crisp
icon for (▲ ▼). And an unattached `VisualElement` drops `SendEvent` (no panel, no dispatcher), so a
fixture that wants a click calls the bound action instead.

## The 4-arg SetButtonIcon does not parent the icon (2026-09-08)

`ToolkitIcons.SetButtonIcon(button, icon, name, fallback)` and `SetToggleIcon(toggle, icon, …)`
only assign `icon.image`; **the caller must `Insert(0, icon)` / `Add(icon)` itself.** The 3-arg
`SetButtonIcon(button, name, fallback)` is the one that creates and parents an icon. Miss the
parenting and the fallback never fires either — the texture resolves, so no text is set, and you
get a completely blank button. That is how the VAT preview's Reset Camera button shipped with no
glyph and no word. `ActorEditorPanel` shows the correct order.

A glyph the editor has no icon for is drawn, not shipped as a PNG: `ToolkitIcons.GhostGlyph`
renders a 32×32 signed-distance ghost once and caches it, and there is a `SetToggleIcon` overload
taking a `Texture` for that path. A new static class would have needed a `Conformance_G` allowlist
entry, so drawn glyphs live on `ToolkitIcons`.

## A per-frame readout in a transport row re-spaces the whole row

`.toolkit-transport` is `justify-content: space-evenly` with `flex-wrap: wrap`, so a label whose
text changes every tick shifts every control beside it — the VAT preview's frame counter made the
transport visibly crawl during playback. Any counting readout also takes
`.toolkit-transport__derived--counter`, which reserves a fixed width.

## VatClipRange.bounds has never been written by a bake (2026-09-08)

`VatTextureBaker` fills `clipId`/`targetId`/`frameStart`/`frameCount`/`fps` and leaves `bounds` at
`default(Bounds)` — every VAT set in the project has `m_Center: 0, m_Extent: 0`. Two consequences:
`ClipRegistryBuilder` bakes that zero box into the runtime blob as the VAT clip's culling bounds,
and any editor code that frames it slams the camera to `MinimumOrbitDistance` at the origin (the
"VAT preview opens super zoomed in" report). `VatPreviewElement.ResolveFrameBounds` works around it
by framing `textureSet.runtimeMesh.bounds` when the range box is degenerate, but that is the rest
pose — VAT displacement can still reach outside it, so the preview crops a deformed clip. **Fixing
it properly means measuring per-frame extents inside `SampleClip` and re-baking every existing
set**; not done, and it is the owner's call because it changes baked output.

## The VAT bone-flavour render path — three bugs the new preview found (2026-09-08)

The A74 preview plus its rest-pose Ghost was the first thing to actually *look* at a bake, and it
immediately surfaced three defects that no fixture caught. All three are fixed; the point of this
section is that they were invisible until something rendered the bake beside its source.

- **`VatMeshPreparer` and `ToolkitVatCrowdUnlit.shadergraph` disagreed about UV channels.** The graph
  wires `UV1 → Bone Indices` and `UV2 → Bone Weights` (dump the edges: parse the `.shadergraph` JSON,
  `GraphData.m_Edges`, and resolve `m_Node`/`m_SlotId` against each node's slots). The preparer packed
  *two* influences into UV1 alone as `(index0, index1, weight0, weight1)` and never wrote UV2 — so the
  shader read two weights as bone indices and took its weights from an absent channel. It renders as a
  mesh that inflates with distance from the root, which reads like a bad bake rather than a wiring bug.
  This hit the shipped runtime crowd shader too, not just the preview. Every bone-flavour set baked
  before this needs re-baking.
- **The bake left the source rig posed.** `VatTextureBaker` sampled inside `AnimationMode` with
  `BeginSampling`/`EndSampling` correctly paired and still left every bone at the last sampled pose —
  measured, with the bones set to identity immediately before the bake. **Do not trust
  `AnimationMode.StopAnimationMode()` to revert.** The bake now snapshots each transform's local TRS
  before `StartAnimationMode` and reapplies it last in the `finally`, after both the poser's restore
  and `StopAnimationMode`. Baking is a read of the user's scene; anything less is a destructive edit
  they did not ask for.
- **The preview's source copy strands itself on every domain reload.** It is a `HideAndDontSave`
  instantiate, which outlives a reload while the field pointing at it does not — the same trap
  `CutsceneViewportElement`'s cameras have. Five copies had accumulated within one session. The fix is
  a static `HashSet` of copies a live preview still owns plus a `Resources.FindObjectsOfTypeAll` sweep
  of anything named `VatPreviewSourceCopy` that nobody owns; ownership rather than name alone is what
  lets the standalone window and the Clip Editor's VAT Bake tab each hold one.

Also: `VatTentacleRigBuilder.CreateTentacle` assigned no material at all, so the sample rendered
magenta the moment anything drew it. It now builds a URP Lit (Standard fallback) material, and
`VatSampleTentacleUtility` saves both the procedural mesh *and* that material as assets before writing
the prefab — `PrefabUtility` cannot serialise a reference to an in-memory object, so either one left
unsaved comes back null in the prefab.

**A generated `RigAsset` is useless to the Clip Editor without `sourcePrefab`.** The Skinned Source
field takes a *`RigAsset`*, not a prefab, and `ClipEditorWindow.LoadedPrefab` is just
`rig.sourcePrefab` — a rig with that field null gives `PreviewSkeletonMirror.Rebuild` nothing to
instantiate, so the viewport, the Rig Hierarchy pane and the skinned mesh are all simply absent with
no error anywhere. `VatSampleTentacleUtility` shipped exactly that rig for a while. Build the prefab
first, then mint the rig through `RigAssetUtility.CreateRig(path, prefab, targets)`, which wires it.
Empty `targets` is correct for a skinned chain driven by bone tracks — it only costs an informational
"declares no targets" banner over the viewport, not the preview.

## Clip Editor preview was gated on the baked cutout registry (2026-09-08)

`ClipPreviewController.SamplePose` opened with `if (!registry.IsCreated) { return false; }` — and the
bone-track posing sat at the *end* of that method. `registry` is the baked `ClipRegistryBlob`, built
from the rig's **targets**, so a skinned rig declaring no cutout targets built no registry
(`registry.IsCreated = False`, measured) and scrubbing posed nothing at all. Bone tracks never enter
the blob — `FindClipById`'s own comment says so — yet they were gated on it.

Bone tracks now pose **before** the registry guards, and those guards return `posedBones` rather than
`false`. Socket markers moved after the bone pose too: they read the skeleton, and were being updated
before it was posed, so they trailed the bones by a frame.

**There were two gates, and the second one hid the first.** `ClipEditorWindow`'s render loop called
`previewController.HasRegistry && !previewController.SamplePose(...)`, so with a targetless rig the
sampler was never invoked at all and fixing its interior changed nothing on screen. **Calling
`SamplePose` directly is not a test of the window** — it walks past the caller. Scrub through
`SetPlayheadTime` (what the ruler calls), then read the live previewed Transforms in a *separate*
execute_code call, having called no sampler yourself.

**The general trap: preview is coupled to the cutout bake, not to the authored data.** `vatTracks`
have no lane and no preview anywhere under `Editor/ClipEditor/` (grep returns nothing), and an
imported `AnimationClip` behind `vatSource` is invisible to the timeline, which only draws authored
tracks. Scoped in
[`Tasks/NewPlans/UnifiedClipAuthoring_System.md`](../../Tasks/NewPlans/UnifiedClipAuthoring_System.md)
— P0 built, P1–P5 open, with the owner decisions listed rather than guessed.

## The sample tentacle is authored, not imported (2026-09-08)

`VatSampleWave` carries its motion as **`ClipAsset.boneTracks`** — 12 lanes, 11 keys each — and no
`vatSource.sourceClip` at all. It used to be the other way round, and the cost was that the Clip
Editor timeline was empty: an imported `AnimationClip` behind `vatSource` is invisible to the
timeline, which only draws authored tracks. `CollectVatClips` treats authored bone tracks as a VAT
source in their own right, so the clip still bakes; the sample now demonstrates the toolkit's own
authoring path end to end (see keys → bake → play them back) rather than hiding the animation in a
`.anim`.

Three things to keep right if this is regenerated:

- **`BoneKey.localPosition` is assigned outright, not added to the bind pose**, despite what the
  field's doc comment says. `BoneTrackPoser.ApplyTracks` writes `localPosition`/`localRotation`/
  `localScale` straight onto the Transform, so every key has to restate the bone's rest offset
  (`(0, SegmentLength, 0)` down the chain) or the whole rig collapses onto its root.
- **Key spacing has to divide the frame count.** `(keysPerBone - 1)` must divide the clip's frames,
  or the keys land mid-frame and the Clip Editor opens with a lit "Quantize N Keys" button over a
  sample it just generated. 11 keys over 60 frames is 6 frames apart; 9 keys was 7.5 and wrong.
- **`loopSafe` is no longer gated on having an imported clip.** `CollectVatClips` read
  `hasImportedSource && clip.vatSource.loopSafe`, so a bone-track-only clip never got the duplicated
  final frame the shader interpolates across at the loop point. It now reads the flag whenever a
  `vatSource` exists, `sourceClip` or not — which is why the sample sets `vatSource` with a null
  `sourceClip`.

## The VAT preview shows the source before anything is baked (2026-09-08)

Owner's model, and the shape the panel is built to: assign Clip Set + Rig + Skinned Mesh and the
subject appears **at rest, not moving**; bake and press play and the *baked* mesh moves; Ghost lays
the still-at-rest source over it, so "how far did the bake move" is visible against something that
does not. The Ghost is therefore **frozen, never animated** — an earlier build posed it per frame to
double-image any drift, which the owner replaced with the rest-pose reading on 2026-09-08.

One object serves both roles: `VatPreviewElement` keeps a single copy of the source hierarchy and
picks its look from state — authored materials and visible when no set is baked, translucent accent
when a set exists and Ghost is on, hidden otherwise. It is rebuilt on *every* `Show`, not only when
the renderer reference changes, because the copy is a snapshot of the rig's current pose and
re-posing between bakes would otherwise leave a "rest pose" that is not the rest pose. `VatBakePanel`
routes all four fields through one `RefreshPreview()`; `SetSource` must call it too, since it writes
its fields with `SetValueWithoutNotify`.

## A cover pane that owns a PreviewRenderUtility must be disposed by the host (2026-09-08)

The Clip Editor's tabs are **cover panes**, hidden by a USS class rather than removed — so a hidden
panel is still attached, still ticking, and nothing ever raises `DetachFromPanelEvent` on it. The
only place its native memory can be released is `ClipEditorWindow.OnDisable`, which is why
`newRigPanel` and `vatBakePanel` are disposed there beside `previewController`. `VatBakePanel` was
not, from A74 until this was found: every window close leaked a `PreviewRenderUtility` plus a copy
of the source hierarchy, and neither host of the standalone `VatBakeWindow` disposed it either. Two
rules fell out. A `Dispose` on one of these panels stays **idempotent** — it does not null its
element field, since the panel's other methods keep running if the pane is merely hidden. And a
preview element's `Dispose` unsubscribes its own `EditorApplication.update` tick first, or the next
tick calls `EnsureRenderUtility` and quietly builds a second one nothing owns.

## The New Rig list and its viewport are one choice (2026-09-08, 0.21.0)

`NewRigPanel` is two columns: the form at a fixed 420px, `RigSourcePreviewElement` filling the rest,
the same shape `VatBakePanel` uses. Nodes are addressed between them by the hierarchy path
`PrefabAuthoringBridge.GetHierarchyPath` produces — computed against the *copy's* root in the
preview and against the prefab's in the panel, which agree because the copy is a straight
`Instantiate`. Ticking a node forces its copy visible (`SetActive` + `enabled`) rather than leaving
the prefab's authored visibility, on the reasoning that a ticked target drawing nothing reads as a
broken tick; unticking restores exactly what was captured at build time. The row label needs
`labelElement` truncation (`minWidth 0`, ellipsis, no-wrap) — a deep path otherwise pushes the tag
button out of the row and puts a horizontal scrollbar under the whole list.

## Clip Sets tab (A75)

**The panel reports, the window acts** (same split as `NewRigPanel`/`VatBakePanel`): `ClipSetsPanel`
never writes the window's `clipSet` field itself, even after a create or an edit — it raises
`ClipSetCreated`/`OpenInEditorRequested`/`SetClipsChanged` and the window decides what loading a
set means, same reasoning `CreateClipSet`'s old comment gave for going through `clipSetField.value`
rather than the backing field directly.

**Edit mode applies every tick as its own undo step; Create mode does not touch an asset until
Create is pressed.** With a real set selected, `ClipPickerListElement.ClipCheckedChanged` calls
`ClipAssetUtility.AddExistingClipToSet`/`RemoveClipFromSet` immediately — there is no Apply button
to forget, and Ctrl+Z undoes one clip at a time. With no set yet (Create mode), ticks only mutate
the picker's own `ClipPickerModel`; nothing hits `AssetDatabase` until `Create()` walks
`picker.CheckedClips`. A create with "Load this set into the editor" ticked (the default) also
raises `Closed`, which switches the window back to the Clip Editor tab — driving the panel
end-to-end from `execute_code` means the tab visibly closes itself after every default-toggle
create, which reads as a bug the first time you see it but is D9's intended behavior.

## Both catalog tabs create, rename and delete in place (A77, 0.25.0)

**New creates the asset immediately; there is no create mode on either tab.** `RigsPanel` and
`ClipSetsPanel` both write an empty asset into a remembered `EditorPrefs` folder through
`GenerateUniqueAssetPath`, then select it. The folder row on each means *where the next New lands*,
not where the open asset lives. Creating no longer implies loading: "Use in Clip Editor" /
"Open in Clip Editor" is the only path to a toolbar field, on both tabs.

**Renaming is one routine with two entry points.** A row's context-menu Rename and the editor
column's Name field funnel into the same private method on each panel, so the rescan-and-reselect
behaviour cannot drift apart. The Name field commits on `FocusOutEvent` and Return only — a
`RegisterValueChangedCallback` there would rename the asset once per typed character.

**`InlineRenameEditing.Begin` hides the row's title label rather than removing it.** The label is
what `bindItem` re-reads by name, so removing it breaks rebinding on a recycled row. Its three
handlers (Return, Escape, `FocusOutEvent`) share one `isFinished` flag: Escape cancels and then
*immediately* blurs, so without the flag the focus-out would commit the edit Escape just cancelled.

**A rig delete moves to the OS trash and clears the selection first.** Reversing A76-D8, which had
deliberately shipped no delete. `AssetDatabase.MoveAssetToTrash`, never `DeleteAsset`: a rig is
referenced by actor profiles and by every clip track bound to one of its targets, and the package
still has no sweep that can price a delete. The panel clears its selection *before* rescanning,
or the editor column and its preview stay bound to a trashed asset.

**A clip set has no `RenameClipSet` utility.** `ClipAssetUtility.RenameClip` takes a `ClipAsset`,
so `ClipSetsPanel` duplicates its guard body against `AssetDatabase.RenameAsset` inline. Worth
absorbing into `ClipAssetUtility` next time that file is open.

## Rigs tab (A76, 0.23.0)

**Only `RigAsset.EnsureStableIds()` can mint a target id, and it must run after the target is in
the list.** `RigTargetDefinition.stableId` is `internal` to the Authoring assembly, so an Editor
write path cannot set it. That is why `RigAssetUtility`'s edit methods use
`Undo.RecordObject` + direct list mutation rather than the `SerializedObject`/`SerializedProperty`
route `ClipAssetUtility` uses for clip sets: a `SerializedObject` constructed before
`EnsureStableIds` writes the freshly-minted id back to 0 on its next `ApplyModifiedProperties`.
Order is record → mutate → `EnsureStableIds` → `SetDirty` → `SaveAssetIfDirty`, and do **not** call
`MarkStableIdPersisted` there; that discharges the *asset's* report and belongs to `CreateRig`.

**A nested `TwoPaneSplitView` inside a cover pane needs its own `minWidth`, or the tab comes back
as nothing but its flexible pane.** Hiding a cover pane drops both split views' stored dimension to
the `-1` "uninitialised" sentinel. On the way back the outer split re-lays its fixed pane from
scratch — and when that fixed pane is *another split view*, it has no intrinsic width, so it lands
at zero and every column inside it disappears. The columns' own `minWidth`s cannot save it: they
sit inside the element being zeroed, not on it. `RigsPanel` floors the inner split at the sum of
its two columns (560). Re-assigning `fixedPaneInitialDimension` does **not** repair a collapsed
split — the setter does not re-run the control's `Init`, verified live. `ClipSetsPanel` is not
affected: its single split's fixed pane is a plain column that carries its own floor. Both tabs do
lose the dragged width across a hide, settling at the floor rather than the initial dimension.

**An immediate-write panel has to listen for `Undo.undoRedoPerformed`.** Edit-mode ticks write
straight to the asset, so Ctrl+Z changes the rig without the panel being involved and the row keeps
showing the tick the undo removed. `RigsPanel` subscribes in its constructor and unsubscribes in
`Dispose`. Any future panel that writes on interaction inherits this problem.

**Undo cannot be verified across an `AssetDatabase.Refresh()`.** Calling `SaveAssets`/`Refresh`
between the edit and the `PerformUndo` reloads the object and the undo appears to do nothing —
this reads exactly like a broken undo and is not. Assert on the in-memory instance, with no
refresh in between; verify persistence separately.

**`ReadScreenPixel` cannot reach a window on a monitor left of the primary.** The Clip Editor
often sits at a negative x; scaling that by `pixelsPerPoint` addresses off-screen space and the
capture comes back solid black. Move the window to a positive position, capture, then restore it —
and note `EditorWindow.RepaintImmediately` is non-public, so it needs reflection.

**A target whose node the prefab no longer has is never handed to the preview.** The preview holds
a copy of the prefab and has no such node; `SetNodeIncluded`/`FocusNode` must be guarded on
`!IsMissingNode` at every call site. Those rows exist so a target cannot vanish from the list —
dropping them would make an unticked box the only evidence a part still exists.

## The Cutscene Inspector pane was a fixed-width VisualElement, not a split pane (2026-09-08)

`CutsceneEditorPanel`'s `centerColumn` (viewport | inspector) was a plain `Row` `VisualElement` with
the inspector pinned at `style.width = 300f`, unlike every other divider in the same window
(`castSplit`, `verticalSplit`), which is why it alone had no drag handle. Fixed by making
`centerColumn` itself a `TwoPaneSplitView(1, 300f, Horizontal)` — same pattern as `castSplit`, fixed
pane index 1 (the inspector, added second). Per the cover-pane trap below, the new nested split needs
its own `minWidth` (`380` = viewport's `160` + inspector's `220`) or a hide/show cycle of the
Cutscene tab collapses it to nothing but the viewport.

## Actor Editor: three flex columns became two nested TwoPaneSplitViews, slider became a dropdown (2026-09-08)

Same fixed-width complaint as the Cutscene Inspector above, times three: `ActorEditorPanel.BuildBody`
laid out layers | viewport | inspector as one `Row` `VisualElement` with the two side columns pinned
(`flexShrink = 0`, explicit `width`). Now `layersColumn` (minWidth 220) and an inner `rightSplit`
(`viewportColumn` minWidth 200 | `inspectorColumn` minWidth 260, itself minWidth 460) sit inside an
outer `TwoPaneSplitView(0, SideColumnWidth, Horizontal)` with its own `minWidth = 680` — the same
cover-pane collapse trap applies, since this whole panel is hidden via USS class on tab switch
(`ClipEditorWindow.ShowActorEditorTab`), not destroyed.

The transport row's **Direction** control was a continuous `Slider(0, 360)` degrees value fed through
`FacingResolver.FromMovement` (angle → vector → quantized `Direction`) purely so a drag could produce
any of the 8 facings; the readout label then resolved that quantized `Direction` to its authored side
via `ToAuthoredSide`. Replaced with an `EnumField` bound to `Direction` directly, feeding
**`FacingResolver.Snap(desiredFacing, profile.turnDirections)`** instead of `FromMovement` — `Snap` is
the same quantization `FromMovement` calls internally at its tail, just without the angle round-trip.
`currentFacingAngleDegrees`/`SouthEastSliderAngleDegrees` are gone entirely; `currentMemberFacing`
(already the *quantized* result, same as before) is the only state left, and the readout now reads
`{currentMemberFacing} → {clipFacing}[, mirrored]` with no degree number. A profile with fewer than
eight `turnDirections` can still show a dropdown pick that doesn't match the readout's resolved side
(e.g. picking North on a two-direction profile snaps to SouthEast) — that mismatch is the same
by-design behavior the old slider had (you could drag to any angle; only the *output* was quantized).

## The VAT bake asks the rig, and runs once per part (A78, 0.26.0)

**The bake samples one throwaway instance of `rig.sourcePrefab` and calls `VatTextureBaker.Bake`
once per VAT part.** `VatBakeSourceResolver` is the single place the part list is decided, and its
rule is ordered rather than conditional: a `kind == VatMesh` target wins, a target merely carrying a
skinned mesh is next, and a lone skinned mesh is the untargeted fallback. Gating on `kind` outright
would have made every rig authored before the Rigs tab grew a Kind picker unbakeable — nothing wrote
`kind` before then, so they all carry `Quad`.

Four traps, all of them load-bearing:

- **Sockets are sampled on the first part's call only.** `VatTextureBaker` samples them *inside*
  `Bake`, against the hierarchy **root** rather than the renderer, so passing the list on all N calls
  writes N copies of every socket track and nothing downstream complains — the sword just rides the
  wrong pose.
- **`DestroyImmediate` the bake instance only after the last `Bake` returns.** Each call's own
  `finally` stops `AnimationMode`; tearing the hierarchy out from under a live sampling session
  strands the Editor in AnimationMode with no way back but a domain reload. One `finally` around the
  whole loop, not one per part.
- **Never hand the baker a prefab asset or a scene object.** It writes local TRS onto every bone of
  whatever renderer it is given (and restores them in a `finally` precisely because it does), so
  posing the asset writes the last sampled frame into the `.prefab` on disk. `Object.Instantiate`,
  **not** `PrefabUtility.InstantiatePrefab`: a copy with no prefab link cannot write back even if a
  later change forgets the rule. Measured 2026-09-08: a `HideAndDontSave` instance belongs to no
  scene at all, is fully poseable, and never dirties the open scene — but `StopAnimationMode()` does
  **not** revert the pose, which is why the baker snapshots TRS itself.
- **Each part's frames are numbered from its own 0.** That is why per-part baking needed no change to
  the blob, the registry builder's range filling, or any runtime system: a part only ever indexes
  into its own texture. `clipRanges` stays flat and set-level, each row carrying its `targetId`.

`VatTextureSetAsset.TryGetPart` mirrors `TryGetTrackRange` exactly — exact target, else the
`targetId == 0` entry — and that symmetry is the contract that keeps pre-A78 single-part sets working.

**A `Quad`-kinded VAT part renders as a motionless clump with no error.** `RigTargetBaker` only adds
`VatDriven` and the VAT shader properties in the `VatMesh` arm, so correct textures on a `Quad`
target are silently useless. That is what the Rigs tab's Kind button exists to prevent.

**Staleness is judged by `VatSourceHashResolver` (A89, 0.35.0), and it is the only code that
computes the stored hash.**

- **How the stamp is written.** `VatTextureSetBuilder.WriteSet` stamps `sourceHash =
  Fold(ComputeClipsHash, ComputeRigStructureHash)` after the part loop. It also stores the rig half
  in `sourceRigStructureHash`, which is how a stale set can say "rig changed" or "clips changed".
- **Who reads it.** Two consumers compare against the same function: the freshness badge
  (`Resolve`) and V08 in `ValidationBadgeElement`. Never compute a second hash to decide
  staleness.
- **Four traps:**
  - **Before 0.35.0 the set stored part 0's hash only**, because the write sat inside
    `if (partResultIndex == 0)`. An edit that touched only the Fin part of a two-part set never
    went stale. `VatBakeResult.sourceHash` is still that per-part hash, and is logged only.
  - **`sourceRigKey` is identity, not structure.** V40 compares it with `rig.StableId`, so the
    rig's structure lives in `sourceRigStructureHash`. Do not fold structure into `sourceRigKey`.
  - **A source AnimationClip is identified by GUID + `GetAssetDependencyHash` + name + length.**
    The dependency hash moves on save, not on an unsaved in-memory edit (measured 2026-09-13), so
    the badge refreshes on import (`VatSourceImportWatcher`) and on selection, never per gesture.
    **`EditorApplication.projectChanged` does not fire when an existing asset is saved**
    (measured in the A89 drive with a probe counter: 0 after `SaveAssetIfDirty` of a clip, even a
    tick later). It reports Project-window changes only. `VatSourceImportWatcher` is an
    `AssetPostprocessor` relay that coalesces through `delayCall`.
    The name and length catch swapping one clip for another inside the same FBX, where the GUID
    and dependency hash stay the same.
  - **Only VAT-bound clips are folded** (a `vatSource.sourceClip`, any `vatTracks`, or any bone
    tracks). Adding a sprite-only clip to a set does not make it stale. `vatSource.sampleFps` is
    not folded because the bake uses `ClipAsset.frameRate`.
- **Where the rig comes from on the Clip Sets tab.** That tab has no rig of its own. Its badge
  uses the shared selection's rig when its `StableId` matches `sourceRigKey`, and otherwise finds
  the rig in the project by stable id.
- **Owner follow-ups (A89 T9–T11, 2026-09-13):**
  - **The VAT Bake receipt line refreshes on import through `VatBakePanel.OnSourcesImported`.**
    - It returns early while its key is unchanged: `ComputeSourceHash` XOR the stored
      `sourceHash`, about 0.07 ms.
    - Otherwise it runs `RefreshResolvedSources(false)`. It calls `RefreshPreview` only when
      `FirstResolvedRenderer()` is a different object, because `VatPreviewElement.Show`
      re-instantiates the whole source hierarchy on every call.
    - Proven by `sourceCopyRoot` keeping its `EntityId` across a rig save.
    - Never pass `true` from an import path.
  - **Rebake on the Clip Sets tab** raises `ClipSetsPanel.RebakeRequested(set, bakedRig)`. The
    window sets the shared selection (the rig only when one was found) and switches to
    `ClipEditorTab.VatBake`. Nothing bakes.
  - **V08's text is rewritten in the editor, not in `ClipValidation`.**
    `ValidationBadgeElement.DescribeStaleVatBake` replaces the generic authoring text with set +
    rig + resolver reason, and `ApplyMessages` sorts V08 first and prefixes the summary with
    "VAT stale · ".
    - Any new badge caller that recomputes the hash must call `DescribeStaleVatBake` too.
      `ActorEditorPanel` does.
    - A94's Health tab pins H06 above every finding (its spec, D8).

**Unexplained, not chased (2026-09-08):** a rig produced by `AssetDatabase.CopyAsset` had a target
added and saved — the YAML on disk carried it, `kind` included — yet after a domain reload Unity
loaded that asset with `targets.Count == 0`, and a `ForceUpdate` reimport did not fix it. The copy
shares its `stableId` with its source, but `EnsureStableIds` never clears targets, so that is not the
cause. A freshly created rig at the same path behaves correctly. Worth knowing before trusting a
copied rig in a test or a drive.

## Do not spawn subagents against this package — unless they never touch the Editor (A73)

Three processes driving one live Unity Editor already caused MCP lock
contention that grew `Logs/Editor.log` to 2.2 GB and broke test runs (per
HANDOFF.md). The root cause was multiple processes calling `mcp__UnityMCP__*`, not multiple
processes existing — Amendment A73's T4/T5 (parallel) and T6 built cleanly with three Sonnet
subagents each editing a disjoint set of `.cs` files and explicitly forbidden from calling any
`mcp__UnityMCP__*` tool; only the one orchestrating session ever compiled, ran tests or committed,
exactly `Cutscene_Roadmap.md` §4's `[parallel-safe]` rule. Default is still sequential, one
editor-connected agent at a time — the exception only holds when a subagent's brief is scoped to
files another agent isn't touching and it has no MCP access at all.

## Shared asset selection (A80, 0.27.0)

**One `ActiveAssetSelection` per window; every panel writes it, the window is one subscriber.**
This supersedes A75's "the panel reports, the window acts" for the clip set and the rig only:
`RigsPanel.SelectRig`, `ClipSetsPanel.SelectSet`, the VAT Bake fields, the Actor Editor's two
fields and `ActorEditorPanel.Profile` (which writes `profile.rig`) all call `selection.Set…`
directly, and `ClipEditorWindow.ApplyRigSelection` / `ApplyClipSetSelection` are just the
window's own handlers. Other panel events (`OpenInEditorRequested`, `UseInEditorRequested`,
`SetClipsChanged`, `RigTargetsChanged`, `ProfileChanged`) still follow the old rule.

**The setter guard is `ReferenceEquals`, never `==`.** Unity's `==` calls a destroyed asset equal
to `null`, so a clear after a delete would be swallowed and every tab would keep a dead reference.

**Every subscriber lands the value with `SetValueWithoutNotify`.** A notifying assignment re-enters
the selection from inside its own event; the guard stops the loop but not the double refresh.
Subscribe in `CreateGUI` *before* `BindToolbar` (the `RestoreView` at the end of `CreateGUI` writes
the selection, so the handlers must already be live) and unsubscribe in `OnDisable`.

**A panel-less element never dispatches its `ChangeEvent`.** `VatBakePanelTests` wanted to write
the rig field with notify and read the selection back; the event never fires with no panel behind
the element, so only the selection→field direction is fixture-testable. The field→selection path
is proven by the T12 drive against the live window.

**A new asset from either catalog's New is the active one everywhere.** `Select…` writes the
selection, so the empty rig or set replaces the Clip Editor's until it has content. The owner was
told at the A80 checkpoint; if he wants create-without-select, the one line to move is the
`selection?.Set…` call in `CreateAndSelect…`.

## Texture Packer tab (A81, 0.28.0)

The game's channel packer moved into `Editor/TexturePacker/` as the DOTS Animator's first tab —
traps only, the rest is `Documentation~/texture-packer.md`:

- **`GraphView` calls `StretchToParentSize()` on itself** — absolute, all insets 0. Added beside a
  header it draws over the header (the old window's toolbar was under the canvas, verified live
  at T0). `TexturePackerPanel` adds the graph to its own `texture-packer-graph-host` under the
  header, never beside it.
- **`ConnectPorts` bypasses `graphViewChanged`**, so the single-capacity replacement documented in
  `Editor.md` never runs for a programmatic wire. `AddSourcesWiredIntoChannel` calls
  `DisconnectExistingEdges` first or the row ends up with two edges. Likewise programmatic
  `AddSourceNode`/`ConnectPorts` raise no `GraphChanged` — the sidebar helpers raise it themselves,
  which is also what makes `AutoAssignResolution` run for a sidebar add.
- **Every radio is `SetValueWithoutNotify`**: the sidebar's `Images | Recipes` toggles, the
  R G B A chips on a source node, and the tab strip. A plain `value = true` re-enters the callback.
- **Three nested drop targets** (source node, output channel row, canvas): the node and the row
  `StopPropagation()` after `AcceptDrag()`, or the canvas handler fires too and a replace becomes a
  replace plus a duplicate node. A sidebar drag starts inside `PointerMoveEvent` with
  `pressedButtons == 1` and stops propagation so the `ListView` does not also rectangle-select.
- **`Conformance_D` scans `*.md` and `*.json`** — `Assets/<Folder>` in the docs page, the changelog
  or a fixture string fails the gate; `Assets/…` and `Assets/` alone pass (empty segment).
- **Only `SaveRecipe` writes a recipe** (owner directive). `BakeTo` no longer writes the output path
  back; a trashed recipe marks the canvas unsaved. Compare recipes with `ReferenceEquals`.
- **The shared `toolkit-pane-header` has `flex-wrap: wrap`**: mode toggles plus three action buttons
  do not fit 280 px, so the sidebar header is two rows tall in Recipes mode. Group left-side
  controls in one child or `space-between` spreads them to the edges.


## Catalog columns and cover-pane splits are shared (A82, 0.29.0)

**One `ToolkitCatalogColumn<TAsset>` (`Editor/ClipEditor/Shared/`) behind the Rigs, Clip Sets,
Actor Profiles and Recipes catalogs.** `RigCatalogColumn`, `ActorProfileCatalogColumn` and
`RecipeCatalogColumn` are thin subclasses that build a `CatalogColumnOptions<T>` (element name,
name prefix, title, tooltips, empty messages, `secondLine`/`tooltip`/`scan` delegates,
allowRename/allowDelete) and forward the events under their old names; `ClipSetsPanel` uses the
generic directly. Child element names come from the prefix: `{prefix}-list`, `{prefix}-row-box`,
`{prefix}-row-title`, `{prefix}-row-info`, `{prefix}-search`, `{prefix}-new-button`. An empty
`title` builds no header and leaves `HeaderActions` unparented for the host — that is how the
Texture Packer sidebar hoists New/Save/Refresh into its own header. `ImageCatalogColumn` is
deliberately not on it: its rows are GUID records with lazily loaded textures, multi-select and
drag-out, not `UnityEngine.Object` catalog assets.

**`CoverPaneSplitView` (same folder) is every cover-pane split.** Prefs key
`DotsAnimationToolkit.Split.<tab>.<pane>` — eight keys: `Rigs.Catalog`, `Rigs.Targets`,
`ClipSets.Catalog`, `VatBake.Form`, `ActorEditor.Profiles`, `ActorEditor.Layers`,
`ActorEditor.Preview`, `TexturePacker.Sidebar`. It stores the fixed pane's resolved dimension on a `PointerUpEvent` on
the drag-line anchor (class `unity-two-pane-split-view__dragline-anchor`, queried lazily on the
first sized geometry pass because it is not in the hierarchy at construction), and re-applies it
on the first `GeometryChangedEvent` with a positive size after one with a zero size — the hide.
Its `contentContainer` is the inner `TwoPaneSplitView`, so hosts still `Add` two panes and set
`minWidth` on the wrapper (the nested-split floor from the Rigs-tab note still applies).

**The re-apply trap, re-measured on 6000.5 (2026-09-10).** The earlier note that assigning
`fixedPaneInitialDimension` "does not repair a collapsed split" is stale for this version: the
public setter re-runs the control's setup and moves both the pane and the drag-line anchor, before
and after a collapse, even with a non-sentinel internal dimension. What does NOT work is writing
`fixedPane.style.width` alone — the pane moves, the anchor stays where it was (verified live:
pane 400, anchor still 280). `ReapplyStoredDimension` writes both. Setting the property while
the split is at zero width is wasted, hence the pending flag rather than re-applying in the
zero-size pass itself.

**Two traps the owner's first drive found (2026-09-12).** (1) The split registers its own size
handler in its first-layout setup — *after* the wrapper's `GeometryChangedEvent` callback — so on
the show pass it runs second and parks the drag-line anchor at the floor it has just measured; the
pane then lays out at the stored width and nothing re-syncs the anchor (the "black line over the
content" the owner saw). Re-assigning an unchanged `fixedPaneInitialDimension` is a no-op, so the
wrapper schedules `SyncDragLineAnchorToFixedPane` for the next frame and places the anchor from the
laid-out pane (`left = paneWidth` for index 0, `splitWidth − paneWidth` for index 1 — the split's
own arithmetic). (2) `splitView.Q(className: dragline-anchor)` walks descendants and, on a split
whose fixed pane is another split, returns the *nested* split's anchor first — releases then wrote
the outer key from the inner drag line. The anchor is a direct `hierarchy` child beside the
content container; look only there.

## The Clip Editor window is four panes (A83, 0.30.0)

`ClipEditorWindow.cs` keeps the tab strip, session state, docking, the transport target, the
viewport and gizmo, the held-transform edit, the retag block and the validation badge; the four
dock panes are elements in `Editor/ClipEditor/Panes/` (`ls` it): `ClipListPane`,
`RigHierarchyPane`, `ClipInspectorPane`, `TimelinePane` plus its `.View` partial (the old
`ClipEditorView.cs`). **Grep across `Editor/ClipEditor/`, not the window file** — a method kept
its name when it moved, so `grep -rn "void RebuildTimeline" Editor/ClipEditor/` finds it.
`ClipEditorSession` (same folder) carries the selected clip, the key selection set, the active
key, the playhead and the hierarchy selection. The window's `selectedClip` and `playheadTime`
fields are mirrored into it, never replaced: a fixture that pokes the field must write the session
too (`ClipEditorAddEventTests` shows the two-sided helpers).

- **Panes are built, bound and wired in `OnEnable` (`WirePanes`) and only rooted in
  `CreateGUI`.** A `VisualElement` cannot be a field initializer on an `EditorWindow` (Unity
  throws "VisualElementCreation is not allowed…"), and the hidden off-screen instance never runs
  `CreateGUI` yet still runs `RememberSessionState`, undo and the tick — so does every fixture that
  `CreateInstance`s the window. A pane registers element callbacks only when it is handed a root.
- **Pane→window calls are delegates named after the window member** (`internal
  System.Action<string> RecordClipEdit { get; set; }`), so a moved body reads as it did on the
  window. Window→pane calls are direct (`timelinePane.RebuildTimeline()`); pane→pane goes through
  the session. Four window properties the timeline reads (`SnapFrameCount`,
  `TransportFrameCount`, `LargeStepFrames`, `IsTransformActive`) are same-named private
  properties behind `…Provider` delegates.
- **Schedule on an element that is in the panel.** The pane elements are never parented, so
  `pane.schedule` never ticks; the key-drag auto-scroll runs on `laneStack.schedule`.
- **Moving code by script: assert every range's first and last line before cutting, re-point by
  regex on code lines only and outside string literals (`playhead` and `ruler` are plain words in
  tooltips), then diff every moved body against `HEAD`.** That is how T3–T5 landed with one to
  eight compile errors each. The viewport/gizmo block is the next natural lift; D7's 2,500-line
  target is still 1,770 lines away.

## Asset Reference Index (A84, 0.31.0)

**`AssetReferenceIndex` (`Editor/ClipUtilities/`) answers "what references this?"** for rigs, clips,
clip sets, profiles, VAT texture sets, event keys and tags, each as a `List<AssetReference>`
(owner, kind, detail). Editor-only, in-memory, no serialized cache: `AssetReferenceIndexPostprocessor`
marks it dirty on any `.asset` import/delete/move and the next query rescans. Every catalog delete
dialog (`RigsPanel`, `ClipSetsPanel`, `ActorEditorProfilesColumn` — not the thin catalog columns)
opens with `SummarizeForDialog`'s "Referenced by 2 profiles, 1 cutscene." plus up to ten names.

- **The scan cost is `FindAssets`, not loading.** Measured 2026-09-12 on 24 toolkit assets: six
  separate `t:` calls 207–328 ms, one combined `"t:ClipAsset t:ClipSetAsset t:RigAsset
  t:ActorProfileAsset t:CutsceneAsset t:VatTextureSetAsset"` 64–68 ms (multiple `t:` terms OR
  together and return the same 24), `LoadAllAssetsAtPath` ~0 ms once cached. Keep the one combined
  call; an incremental rebuild would buy nothing.
- **The tag-first binding rule lives in `TrackTargetMatchResolver.TrackBindsTarget`:** a track
  with a non-zero `tagId` binds the target wearing that tag and never falls back to its raw
  `targetId`; a track with `tagId == 0` binds by raw id only. `RigTargetReferenceResolver`
  delegates to it; `RigHierarchyPane.CountTracksForTarget` goes through
  `AssetReferenceIndex.CountTracksBoundToTarget(clip, rig, targetId)`, which is pure over the two
  assets handed in (never rescans — it runs per hierarchy row on repaint) and falls back to raw-id
  matching when the rig does not declare the target.
- **What references what (verified against the assets, not the spec):** clip sets never reference
  rigs; cutscene slots hold `rig`, `clipSets`, `profile` and tag-addressed `partTracks`, never a
  `ClipAsset` (clip blocks carry an `animationKey`); profiles are referenced only by cutscene slots.
  `VatTextureSetAsset.sourceRigKey` is a hash, not a reference, and is not indexed.

## Sound on scrub (A87, 0.34.0)

Every Clip Editor playhead write goes through `ClipEditorWindow.SetPlayheadTime`: ruler scrub, key
click, frame step, rebuild re-set, and the play tick. The tick wraps with `Floor` *before* calling
it. The setter calls `TimelinePane.ReportPlayheadMoved(previous, current, isPlaying,
isLoopEnabled)`, and the pane resolves crossings with `ScrubEventCrossingResolver`, flashes
`TrackLaneElement.FlashPin(localIndex)` and plays `AnimEventKeyRegistry.FindPreviewClip` through
`EditorEventPreviewPlayer`.

- **D3 outcome (probed 2026-09-13):** `UnityEditor.AudioUtil` is internal but its members are
  public static: `PlayPreviewClip(AudioClip, int, bool)`, `IsPreviewClipPlaying()`,
  `StopAllPreviewClips()`. Reach them by reflection off `typeof(EditorWindow).Assembly`. They need
  no `AudioListener`, give **one voice** (a second call replaces the first) and have no volume
  control. A hidden `AudioSource` + `PlayOneShot` also reports `isPlaying`, but only because the
  open scene had a listener. `isPlaying` / `IsPreviewClipPlaying` are the only proof available from
  a session; whether it is audible needs the owner.
- **The resolver infers wrap direction from the delta, not from speed.** While playing with Loop, a
  jump of more than half the clip is a wrap taken the short way round, so negative-speed playback
  wraps correctly. While paused, the same jump is a seek and fires nothing.
- **Guards in the pane, and why:** a clip switch fires nothing (`lastCrossingReportClip`), and a
  key drag fires nothing (`isDraggingKeys`). The drag carries the playhead with the dragged pin, so
  without the guard the pin re-fires on every pointer move.
- **The project registry is JSON, not YAML.** `VocabularyRegistryProvider` writes it with
  `EditorJsonUtility`, and an `AudioClip` field round-trips as `{fileID, guid, type}`. So a new
  object-reference field on a vocabulary entry needs no extra persistence work.
- Conformance_G scans only **static** classes. A sealed instance class such as the player needs no
  allowlist entry.
- **Cutscenes do not flash or sound yet (D6 deferred).** Their markers are seconds on
  `CutsceneEventMarker`, and their pins are per-marker `VisualElement`s in
  `CutsceneMomentLaneElement`, not `TrackLaneElement` paint. The hook belongs in
  `CutsceneEditorPanel.SetPlayhead`, so it needs three files plus a seconds overload of the resolver.

## Profile name errors at save and build (A91, 0.36.0)

Traps only; the design is in the spec's §7 and HANDOFF §4.
- `ActorProfileValidation.Validate(profile, null)` skips only registry membership. A zero
  `animationKey` is P2 even with null, so the bake already fails it. Any wrapper taking an optional
  registry must resolve null to `VocabularyRegistryProvider.AnimationNames` before calling, or it
  silently checks nothing. `ProfileP2Scan` does, and its second fixture guards it.
- `AssetModificationProcessor.OnWillSaveAssets` fires for `AssetDatabase.SaveAssetIfDirty(object)`.
  It does NOT fire for `AssetDatabase.CreateAsset`, or for a save with nothing dirty. A brand-new
  profile warns on its next save, not on creation.
- `ActorProfileBuildValidation.OnPreprocessBuild` never reads its `BuildReport`, so a drive can call
  it with null. This game's player build is long and writes `EditorBuildSettings`; don't run one to
  test a preprocessor.
- A UI Toolkit `Toggle` in a detached `VisualElement` does not dispatch `ChangeEvent` on `value =`.
  A `SettingsProvider.activateHandler` called by hand is one. To prove the callback one level down,
  mount the root in a floating `EditorWindow` after `ShowUtility()`, then `Close()` it.
- A brief that says "copy lines X–Y" must cover every member. The fake `IVocabularyRegistry` copy
  lost `GeneratedConstantsPath` to a range one line short (CS0535 at the gate).

## Missing camera data warning (A90, 0.37.0)

Traps only; the design is in the spec's §7 and HANDOFF §4.
- Without `AnimationToolkitCameraData`, `BillboardResolveSystem` does not run at all. "Falls back to
  spherical" is the zero-`forward` case with the singleton present. The shader billboard path never
  reads the singleton: it reads `_WorldSpaceCameraPos` and the `_ToolkitCameraForward` global, and
  nothing in this project writes that global (the game bridge included).
- A fixture for "warns once" cannot rest on `LogAssert`: Unity never fails on an unexpected warning.
  Assert the state change instead. `world.Unmanaged.ResolveSystemStateRef(handle).Enabled` reads an
  unmanaged system's flag, and a manual `SystemHandle.Update` honours it.
- One compile can cover two revert-to-fail mutations only if neither masks the other. Removing a
  shared `state.Enabled = false` hid the "LOD forced on" mutation, so the disable was made conditional
  on the billboard branch instead.
- `Samples~` compile check: copy the `.cs` and asmdef into an `Assets/` scratch folder, rename the
  asmdef, refresh, then confirm `Library/ScriptAssemblies/<name>.dll` has a fresh mtime. A clean
  console alone does not prove the assembly was built. `AssetDatabase.DeleteAsset` on the folder
  leaves no stray `.meta`.

## Layered event strip (A88, 0.38.0)

Traps only; the design is in the spec's §7 and HANDOFF §4.
- The runtime emit gate is `Active` **or** `FinishedThisFrame`, not `Active` alone: a Once clip that
  just finished is already inactive and still emits that frame's crossings plus `ClipFinished`. The
  crossfade source emits nothing, and "is blending" means the `Blending` flag, not `blendDuration > 0`.
  `LayerEventRowResolver` mirrors both; its fixture guards them.
- `PlaybackLayer.time` is un-wrapped seconds (speed may be negative, loop may be `UseClipDefault`).
  Anything that feeds `ScrubEventCrossingResolver` must go through
  `ClipSampler.ResolveLoopMode(layer.loop, clip.defaultLoop)` then `MapTimeNormalized`, or a looping
  clip's playhead runs off the row and every crossing after the first loop is lost.
- The preview `ClipRegistryBlob` is sorted and deduped, so its `clipIndex` is not a clip-set index.
  Find the authoring `ClipAsset` (where markers live) by scanning `profile.clipSets[*].clips` for
  `clip.Id`, and do it only when the layer's clip changes; the strip rebuilds rows only when the layer
  count changes, never per tick.
- The Actor Editor ticks the composer in two places, `Tick()` and the paused `Step(int)`. Anything
  that follows `composer.Tick` must hook both. A step passes `isPlaying: true` so a stepped loop wrap
  still fires.
- `new PlaybackLayer()` has `clipIndex 0`, which is a valid index. An "empty" layer must say -1
  explicitly, as `ActorPreviewComposer.Layer(int)` does for an invalid index.
- A fixture with two assertions in one test lets the first mutation mask the second, and Unity's
  NUnit has no `Assert.Multiple`. Give each rule its own test so one compile proves every mutation.

## Refactor operations (A92, 0.39.0)

`RefactorEditing` (`Editor/ClipEditor/Editing/`) re-keys events, merges keys and moves tracks to another tag
across the whole project. `RefactorTargetResolver` is the pure per-asset matcher and `RefactorPromptEditing`
is the shared pick → preview → `DisplayDialog` → run flow behind all four entry points.

- **Undo shape.** `Undo.IncrementCurrentGroup` → `GetCurrentGroup` → `SetCurrentGroupName` →
  `RecordObject` once per changed owner → write → `SetDirty` → `CollapseUndoOperations` → `SaveAssetIfDirty`
  per touched owner → `AssetReferenceIndex.MarkDirty()`. Never `AssetDatabase.SaveAssets()`.
- **Undo does not touch disk.** One `Undo.PerformUndo` reverts every asset in memory and leaves them dirty;
  the files keep the new ids until something saves them (drive-proven: dirty=1, disk unchanged until
  `SaveAssetIfDirty`).
- **Merge undo and the project registry.** Undoing a merge restores the removed entry in memory only; the
  `ProjectSettings` JSON is rewritten on the registry's next `Persist`. `MergeEventKeys(from, into, registry)`
  exists so a drive can merge against a `CreateInstance` registry (`Persist` is a no-op for it).
- **Drives are project-wide.** Every operation walks the whole index, so a drive must use an event key and a
  tag id no real asset uses (A92 used keys 4000001/4000002, tags 0x7A920001/2) and assert the preview is empty
  before creating scratch assets.
- **Preview = what changes.** `PreviewReplaceTrackTag` drops `RigTargetTag` rows (rigs are never retagged);
  ragdoll re-key only touches definitions with `ragdollTrigger != None`, the same filter the index uses.
  Billboard tracks have no tag.
- **`EventMarker` is a struct**: copy out, set `eventKey`, write back. Cutscene markers and tracks are classes.
- **Writes behind a SerializedObject.** The cutscene panel's "Change key everywhere…" callback calls
  `serializedObject.Update()` before rebuilding; any new host that edits through a SerializedObject needs the same.
- **Registry inspector buttons** ("Merge into…", "Replace in clips with…") are disabled unless the inspected
  registry is the project instance, because the prompts always act on `VocabularyRegistryProvider`'s.
- **`EventMarkerContextMenu.Populate`** takes `changeKeyEverywhere` after `openKeyPicker`; pass null to disable.
- **execute_code on 6.5**: `GetInstanceID()` fails CodeDom compilation too (obsolete-as-error), not just the
  project build.

## Events tab (A93, 0.40.0)

- **Keys column is its own `EventKeyCatalogColumn`** (D2): reusing A82's `ToolkitCatalogColumn` meant editing it
  while two other tabs were built against it. It copies the row styling only.
- **Routing asset location.** Created on the first `+ route`, never on opening the tab (`FindDefault` never
  creates), at `Assets/Generated/DotsAnimationToolkit/AnimEventRouting.asset`. Conformance_D lets a package file
  name only `Assets/Generated`; the spec's `Assets/Settings/…` failed it at integration. Any routing asset under
  Assets is found, so a host may move it.
- **The package never handles a route.** Routes are data; the consumer stub is host code written into Assets.
- **Blob by `ref`.** `AnimEventRoutingApi.TryGetRoutes(ref AnimEventRoutingBlob, …)`: an `in` blob makes
  defensive copies whose `BlobArray` offsets read garbage. Routes sort by `(eventKey, kind, routeId)`; key alone
  leaves same-key order list-dependent and breaks bake determinism.
- **Inspector fields are `isDelayed`** and the panel rebinds only when the selected entry object changes:
  `Persist` raises `RegistryChanged` on every edit, and rebinding the same entry rebuilds fields under the cursor.
- **A generated stub is a live `ISystem`.** The drive's stub compiled into Assembly-CSharp; a scratch stub left
  behind would run in every Play world. Delete it and recompile.
- **Drive trick:** `EditorJsonUtility` round trips a `CreateInstance` copy of the project registry, so minting
  and payload edits need no persist; its JSON has no space after the colon.
- **Removed in A93F (0.43.0):** the routing asset, `AnimEventRoutingApi` and the stub generator no longer exist, so
  the routing-asset, route-handling and blob-by-`ref` bullets above describe removed code. Events stay on the
  `AnimEventOutput` buffer; read them with `AnimEventBufferApi`.

## Health tab (A94, 0.41.0)

- **Timing (D4):** the six-type scan is 78 ms cold / 23 ms warm over 24 assets; context + run on this project is
  about 130 ms. The debounced automatic rescan stays.
- **Layout:** `Editor/Health/` holds `HealthFinding`, `HealthScanContext`, `HealthScan`,
  `HealthFindingListElement`, `HealthPanel`; `Editor/Health/HealthRules/` holds six `…Validation` classes, one
  `Evaluate…` per code, called in code order by `HealthScan`. Rules are pure over the context; a null registry
  skips its rule.
- **`AssetReferenceIndex.Rebuilt` never fires on an import by itself.** Listen to `Dirtied`.
- **Every `CreateInstance`'d toolkit asset reports an unpersisted stable id** (H09). A fixture that runs
  `HealthScan.Run` must `MarkStableIdPersisted()` on what it creates.
- **H06 fires for unbaked sets even with zero VAT texture sets** (the Phase 0 guess that it would be silent was
  wrong); an unbaked set's row reads "on rig 'no rig'".
- **This project, 2026-09-14:** H06 `VatSampleTentacleClips` unbaked, H02 `NewClipSet` lists 3 missing clips,
  H05 `VatSampleTentacleRig` unused.

## Sprite Sheets tab (A95, 0.42.0)

> **Renamed 0.52.1 (2026-09-15):** Sprite Sheets → **Flipbooks** everywhere (tab, `FlipbookAsset`/`FlipbookFrame`, `SpriteTrack.flipbook` with `[FormerlySerializedAs("sheet")]`, `Editor/Flipbooks/`, `Flipbook*` classes and tests); Cutscene Director tab → **Cutscenes** (label only, `ClipEditorTab.CutsceneEditor` unchanged). The A95/A95F notes below keep the old names as history. Shader comments saying "sprite sheet" mean an atlas grid, not the asset.

- **Arrays, never atlases (D0).** A grid PNG cannot render from a slice key, and an atlas rect only remaps UVs on
  a fixed quad, so every frame of a part shares one canvas anyway.
- **Unity builds each array layer's mips from level 0** (T0 probe): `generateMips` needs no per-layer sources.
- **`SpriteTrack.sheet` is a serialized field on `ClipAsset`, authoring-only.** `ClipRegistryBuilder` copies
  sprite fields by name, so the blob and content hash ignore it; a new field-by-field track copy (see
  `MirrorClipUtility`) must carry `sheet`. `SpriteSheetAsset` sits in Authoring because `ClipAsset` references it.
- **Working copy.** The tab edits a `HideAndDontSave` copy; Save is `CopySerialized` + `SaveAssetIfDirty`.
- **Re-bake keeps the GUID.** (Superseded by A95F: the baker now rewrites a grid PNG and reimports it.) A
  `Texture2DArray` cannot be resized; the overwrite was
  `CopySerialized(newArray, existing)` (drive-proven: same GUID, swapped layers).
- **Frame lookups are by `frame.index` (the layer), not list position.** A reorder renumbers immediately.
- **RelativeToBase is per key** (`SpriteIndexMode`); the track's `SpriteSliceSpace` is `Absolute`/`RelativeToRest`,
  a different thing.
- **A detached `PopupField` never dispatches `ChangeEvent`** (no panel). A drive that needs a popup's callback
  hosts it in a temporary utility window.
- **Package tests may not name `Assets/<Folder>`** (Conformance_D). A fixture that must write under Assets
  creates a GUID-named folder and reads its path back from the GUID.

## Parallel batch A93–A95 (Worktree Toolkit, 2026-09-14)

- **Leads gate only their spec's fixtures, so package-wide conformance first ran on trunk.** Conformance_D failed
  at integration on two branches. Every lead's wave gate must also name
  `DotsAnimationToolkit.Tests.EditMode.PackagingConformanceTests` (the standing Conformance_A failure is expected).
- **Merges went in tab order** (a93, a94, a95) with no conflicts; the window, UXML, layout test, CHANGELOG,
  `package.json` and conformance pin stayed with the stage and went in one integration commit.

## Events tab rework (A93F, 0.43.0)

- **Routing is gone** (F-D1); see the A93 section's last bullet. The right column is `EventUsageColumn`.
- **`EventUsageColumn` never listens to `AssetReferenceIndex.Rebuilt`:** its own `ReferencesToEventKey` query can fire
  it, which loops. It listens to `Dirtied`, debounced 500 ms.
- **`ReferencesToEventKey` returns one reference per marker** with a `marker @` prefix; the column groups by owner and
  reformats to `@0.35, 0.60`. Other callers still see the raw details.
- **`AnimEventBufferApi` takes `in DynamicBuffer`** (a handle) and has no `[BurstCompile]` on the class; it is called
  from inside the consumer's Burst job. `TryFindNextEvent` leaves `searchIndex` one past the match, so a `while` loop
  visits every same-key event (two layers can emit one key).
- **`FocusClip` selects through `clipListPane.SelectClipRow`** (the `RestoreView` path), so the row highlights. It keeps
  the open set when that set lists the clip, and pings a clip no set lists.
- **Drive handles:** `Usage.Refresh()`, `Usage.RequestOpenOwner(Object)`, `userData` on `event-usage-open-button`, and
  a Button's own handler is reachable as `Clickable`'s private `clicked` field.
- **This project, 2026-09-14:** `Attack` (`0x12`) is used by `MeleeContinuous @0.35`, `MeleeContinuous_EastFacing @0.35`
  and `NewClip @0.27`; the generated `AnimEvents` class also has `Damage` (`0x11`).

## Health tab rework (A94F, 0.44.0)

- **Rules stay pure:** `AssetReferenceIndex` and `AssetDatabase.GetAssetPath` run only inside an action's
  `buildConfirmation` or `run` lambda, never while a rule evaluates, so fixtures run on `CreateInstance` assets.
- **`HealthFindingDetailElement` owns the only `DisplayDialog`.** A drive calls an action's `run` directly beneath it.
- **`ValidateBind` emits V08 only when a recomputed hash is passed;** H11 never passes one, and H06 owns staleness.
- **`SharedClipBindingUtility.ValidateSharedClipBinding(clip)` (one argument) scans the project;** Health uses the
  `(clip, clipSets)` overload.
- **VAT bake output is separate `.asset` files per part** (Bone, Position, Normal, RuntimeMesh), not sub-assets; trashing
  the set alone leaves them behind, hence `VatTextureOwnershipResolver`.
- **Trash utilities never `SaveAssets`** (it would flush the owner's unrelated unsaved edits); `DeleteClipFromSet` still
  does.
- **The Clip Editor's badge had eight refresh sites, not four.** All eight are `healthPanel?.RequestRescan()` now; the
  panel is built in `BuildHealthPanel` right after `BindTabs`, with `FindingsChanged` subscribed before `Bind`.
- **This project, 2026-09-14:** `ErrorCount` 2 (H06 `VatSampleTentacleClips` unbaked, H02 `NewClipSet`), three H11 V38
  warnings on `NewClip 1` against `NewRig`, H05 `VatSampleTentacleRig`.

## Sprite Sheets over arrays (A95F, 0.45.0)

- **The Sheet `ObjectField` has `objectType = UnityEngine.Object`** (a UI Toolkit ObjectField takes one type), so its
  picker lists every asset and anything that is neither a sheet nor an array reverts. Don't narrow it back.
- **Arrays match their names sheet by asset path,** not reference. `FindAssets("t:Texture2DArray")` returns the
  importer-made PNG arrays (drive-proven: 10 plus a copy).
- **`IsImportedArray` is derived** (texture set, no frame source): a sheet whose sources all go missing flips into
  names-only mode.
- **Bake imports twice on first write** (`ImportAsset` makes a default importer, `SaveAndReimport` applies the array
  settings). Drives bake in one `execute_code` call and read back in the next.
- **The format follows the source's alpha, not the reference.** Identical importer settings gave DXT1 for opaque
  swatches where `EyeArray` (with alpha) is DXT5. Compare settings, not `graphicsFormat`.
- **Per-platform overrides are copied over a fixed platform-name list** in `SpriteSheetBaker`; a new build target needs
  adding there.
- **Drive trick:** a baked grid PNG's layer order can be checked without a GPU readback by decoding the PNG file bytes
  with `Texture2D.LoadImage` and comparing cells (row `r` sits at `y = (rows - 1 - r) × height`).

## Parallel batch A93F–A95F (Worktree Toolkit, 2026-09-14)

- **An untracked stage file breaks worktree gates.** Gates compile on the stage, so the owner's untracked stub that
  named routing types failed a93f's T1 gate. Neutralise such files on the stage before spawning a removal spec.
- **Merges went a93f → a94f → a95f with no conflicts**, one integration commit, and all three drives on scratch.
  WorktreeToolkit.md traps 21–24 hold the lifecycle lessons (resumed leads, heartbeat gaps, locked worktree folders).

## Materials tab (A96, 0.46.0)

- **There is no example `.shader`.** Create picks a shader graph by kind (Quad → `ToolkitSpriteUnlit`, Flipbook Plane →
  `ToolkitSpriteUnlitArray`, VAT Mesh → `ToolkitVatCrowdUnlit`); `shader-contract.md` still cites `ToolkitCompositeExample.shader`.
- **Each sprite graph has one frame property,** so Flipbook Plane's requirement is an alternative group (`_ImageIndex` or
  `_AtlasFrame`). `_BillboardParams` is required for no kind: nothing writes it. A Quad requires no property, so any material with
  instancing on passes as a Quad.
- **`ActorBakingAcceptanceTests` pins exact toolkit-warning counts.** A new baker warning needs the fixture material made
  contract-correct (the probe shader's frame properties, instancing on). It is a PlayMode fixture, which the gate broker refuses.
- Contract messages use `ValidationCode.None`, so Health does not list them. Create saves with `SaveAssetIfDirty`.
- **This project, 2026-09-14:** `NewRig`'s `MaleCitizen.prefab` carries 30 materials, 16 mapped to targets and 14 on unmapped nodes;
  unit parts use the legacy `Shader Graphs/2DShader`, and `Faceware.mat` is on `Hidden/InternalErrorShader`.

## Retarget tab (A97, 0.47.0)

- **The tag rules live in `ClipValidation.ValidateTrackBindingInto`,** not `ValidateBind`: a registry-unknown tag is Dangling even
  when a rig target still wears it, and a null registry never produces Dangling. Anything reporting coverage keeps that order.
- **Retagging onto a tag another track in the clip carries must merge** (`ClipComponentModel.Merge*Tracks`); `RetargetRemapEditing`
  and the timeline picker both do.
- The resolver judges bone tracks against `rig.sourcePrefab` by first name, so table and roster agree with no live preview.
- **Detached, the track `ListView` realises no rows** (no remap buttons) and the preview status stays empty; drive through
  `ResolvedBindings`, `RosterEntries` and `RemapTrack`.
- **This project, 2026-09-14:** Walk on `NewRig` is 16 of 16 Bound (16 transform tracks).

## Capture tab (A98, 0.48.0)

- **Render path:** `ClipPreviewController` owns its `PreviewRenderUtility`; a source renders its own frame
  (`ICaptureSource.RenderFrame`), and the runner blits it out before the next render reuses the texture.
- `SamplePose`'s parameter is normalised despite the window's `playheadTime` name; `PlayAnimation` starts a blend, so a scrubbed
  profile capture must finish the blend first.
- **GIF LZW widens the code size before the insert** (giflib order), or large frames corrupt silently. D3: 1661 ms for 30 frames at
  512², the median-cut sort most of it.
- A cutscene capture writes and restores the open scene's bound transforms: never while the Cutscene tab previews the same scene.
- The range has an exclusive end: 1 s at 12 fps is 12 frames.
- **Drive tricks:** the runner keeps ticking between `execute_code` calls, so keep it in `AppDomain.CurrentDomain.SetData`. A cancel
  sent as a separate call lost the race (60 frames at 1024² were already done); cancel from the progress callback instead.
- **This project, 2026-09-14:** the preview draws `NewRig`'s part quads magenta with thin white outlines and a red bar above the head,
  through both `Render` and `RenderCaptureFrame`; captures faithfully show it.

## Parallel batch A96–A98 (Worktree Toolkit, 2026-09-14)

- **The broker refuses PlayMode fixtures.** a96's baker change needed `ActorBakingAcceptanceTests`; the stage ran it by hand
  (`stage-commit`, compile, the fixture, `restore-trunk`). WorktreeToolkit.md traps 25–26.
- **A lead may commit no `.meta` files;** Unity generates them on the stage after the merge, and the integration commit takes them.
- Merges went a96 → a97 → a98 with no conflicts, one integration commit and three scratch drives; registry sha256s never moved.

## Materials: Create also assigns (A96F, 0.49.0)

- A panel method that ends in `Refresh()` must capture any selection it wants to pass downstream *before* the create; `Refresh()` rebinds
  `SelectedMaterial` from the rebuilt usage list and silently substitutes a different material.
- An EditMode fixture that must prove a prefab **file** changed has to reload through `AssetDatabase.LoadAssetAtPath` after the call;
  asserting against the `LoadPrefabContents` root or the pre-save instance passes even when `SaveAsPrefabAsset` is never reached.
- `PrefabAuthoringBridge.ResolveByPath` is root-relative and scene-free: the same `sourceNodePath` resolves against a `LoadPrefabContents`
  root, a loaded prefab asset root and a scene instance. `LastAssignedDescription` is `null`, not empty, before the first Create.
- **This project, 2026-09-15:** `BaseHead` lives at `Visual/MaleUnitVisual/Pelvis/Torso/Neck/BaseHead` under `MaleCitizen.prefab`, one slot.

## Retarget: Skipped rows add their tag to a rig part (A97F, 0.50.0)

- **`GenericDropdownMenu` has no submenus on 6000.5** - only `DropdownMenu`/`DropdownMenuSeparator` carry `subMenuPath`; `"Parent/Child"`
  renders as one literal item. Nesting means opening a second `GenericDropdownMenu` on the same anchor (`GenericMenu` is IMGUI, banned).
- **`RigAssetUtility.SetTargetTag` does not enforce one wearer per tag**; only `ClipEditorWindow.WriteRigPartTag` and now
  `RetargetRemapEditing.AddTagToRigPart` do. Any new caller checks `ClipComponentModel.FindTargetByTag` itself.
- `SetTargetTag` ends in `AssetDatabase.SaveAssetIfDirty`, which warns on a `CreateInstance` rig; fixtures set `LogAssert.ignoreFailingMessages`.
- A Skipped row can carry `tagId == 0` (an untagged track matching no target by raw id); a "fix by tag" affordance must exclude it.
- **This project, 2026-09-15:** the rig target is `LeftUpperLeg` (stableId 3452626627, tag 1185793452); Walk's track is `UpperLeftLeg`.

## Ragdoll tab (A99, 0.51.0)

- **A window partial's private members are the window's API to its other partials.** Grep every `ClipEditorWindow*.cs` for a field before
  deleting it; `activeRagdollBoxHandle` was read two files away, so the shrunken partial keeps a read-only proxy onto
  `ragdollBoxDragSession.ActiveHandle`. The drag math itself lives in `RagdollBoxDragSession`, shared by both viewports.
- `ClipPreviewController.TryEnableRagdollPreview` captures whatever pose is on screen and `Disable` restores it - that pair *is* the
  Drop / Reset transport; there is no separate reset path, and a Drop + Reset leaves the rig asset byte-identical (verified on a scratch copy).
- `RagdollInspectorColumn.SetRig` and `SetSelectedBodyId` both redraw, because `RagdollPanel` never calls `Refresh` on it.
- `BodyPicked` is declared but never raised: `ClipPreviewController` can pick a handle on the selected body, never a body from a point.
- `RagdollViewportElement.SetRestPoseSource` is not called from the window (the pose row reads "No clip bound"; the drop uses the rest pose).
- **This project, 2026-09-15:** `NewRig` has 11 ragdoll bodies · 8 joints, 0 unresolved; Pelvis's hinge limit is 0/0, not the ±45° default.

## Parallel batch A96F–A99 (Worktree Toolkit, 2026-09-15, unattended)

- Three opus leads with sonnet workers, merged a96f → a97f → a99 (two fast-forwards, one merge), no conflicts, one integration commit
  `9271b512`, three scratch drives, registry sha256s unchanged. EditMode 863, PlayMode 285.
- A lead that runs out of turns skips its revert-to-fail; the stage ran A99's by hand (mutate, compile, fixture, `git checkout`, sha check).
- An exclusive grant on one window partial worked: a99 edited `ClipEditorWindow.RagdollHandles.cs` alone and the stage did the rest of the wiring.

## Chrome is shared (A101, 0.52.0)

- **Build editor controls through `ToolkitChrome`** (column, pane header, heading, hint, detail title, asset bar, primary action,
  status row, the one ListView slot trap, severity dot), **viewports through `ViewportFrameElement`** (it parents the rail icon
  before resolving it — the blank-button trap lives in one place now), **mode sidebars through `CatalogSidebarElement`** and
  **path rows through `PathPickerRowElement`**. A catalog column whose `options.title` is empty builds no header and hands its
  `HeaderActions` to the sidebar; leave a title in and the header doubles.
- **`Conformance_I` is a ratchet.** `EditorStyleConformanceTests` scans `Editor/**/*.cs` (not `Editor/Inspectors/`) for inline
  `style.` colour/opacity/font/border/radius/text-align writes. A line whose colour is data ends with `// colour from data`; the
  marker must sit on the line the regex matches (a multi-line assignment needs joining). `InlineStyleAllowlist` only shrinks, and a
  listed file with no violation left fails the second fixture. Package-relative paths carry no leading slash — compare against
  `"Editor/Inspectors/"`, not `"/Editor/Inspectors/"`.
- **`ToolkitIcons.Resolve` strips a leading `d_`** since 0.52.0: the factories decide the skin prefix, and `d_d_…` was logging an
  error that `VatBakePanelTests` caught as a failure the moment VAT Bake's Bake went through `MakePrimaryAction`.
- **A `ScrollView` in `VerticalAndHorizontal` mode reports its content height on `contentViewport.contentRect`**, not the visible
  height (verified live: 24 px for a 187 px scroll view holding one ruler row). Anything that fills "what is left" measures
  `scrollView.layout.height` minus the horizontal scroller. The Clip Editor's vertical-only timeline scroll never hit this.
- **GhostLaneStripElement.paintRangeShading = false** for a seconds-based timeline; the Clip Editor's 0–1 range shading is wrong
  there. The cutscene's `timelineLaneRowCount` counts header-only spacer rows too, and resets at the ruler.
- **Unattended wave lesson:** a session rate limit (429) kills every running worker at once; the edits are usually on disk
  (three of eight were complete, one partial), so diff each file before respawning, and respawn only the untouched scope.
- Class renames are an orchestrator sed after the wave (`clip-editor__hint` → `toolkit-hint` etc.); workers write the new names.

## Stats tab (A100, 0.53.0)

- **A system's profiler marker is `"<World.Name> <Type.FullName>"`** (category Scripts, nanoseconds), e.g. `Default World DotsAnimationToolkit.AnimationToolkitSystemGroup`. `ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "AnimationToolkitSystemGroup")` returns `Valid == false`; with the full name it samples with no Profiler window open. The name carries the world, so recorders are rebuilt on a world change. `ProfilerRecorderHandle.GetAvailable` lists every name when guessing fails (11,000+ handles; filter before returning them from `execute_code`).
- **Queries on a destroyed world must be dropped, not disposed.** `ToolkitStatsCollector` disposes its `EntityQuery`s only while `trackedWorld.IsCreated`; after Play exits it just forgets them. Driven: `RefreshNow` after exiting Play and `Dispose` twice, no exception.
- `ToolkitWorldApi` has no world accessor; editor tools read `World.DefaultGameObjectInjectionWorld`, and only while `EditorApplication.isPlaying` (an editor world exists in Edit mode too). `DOTSTestScene` has **no toolkit actors**: a Play-mode drive creates probe entities in the Play world instead of saving anything.
- There is no event-window counter: "windows open" is the count of actors with `AnimEventMask` enabled (`EventWindowSystem` enables it while any window is open).
- A hand-made `AnimEventsPending` + `AnimEventOutput` carrier is cleared by the toolkit after one frame even without a `PlaybackLayer`, so events injected by a drive show once in a poll and then drop to 0.
- **Concurrency, 2026-09-15:** a peer session was mid-rename on trunk when this session began; the first PlayMode run died with CS2001 on a stale Bee graph naming the old path. A clean `git status` at session start is not enough when another session is live: `ListAgents` first, and hold writes until the peer commits.
