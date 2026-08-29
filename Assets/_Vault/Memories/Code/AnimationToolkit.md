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
  **Key** button in the inspector (`ClipEditorWindow.cs`, the `keyRow` block) is, and it never
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

## Do not spawn subagents against this package

Three processes driving one live Unity Editor already caused MCP lock
contention that grew `Logs/Editor.log` to 2.2 GB and broke test runs (per
HANDOFF.md). Work sequentially, one editor-connected agent at a time.
