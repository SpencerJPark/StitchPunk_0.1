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
