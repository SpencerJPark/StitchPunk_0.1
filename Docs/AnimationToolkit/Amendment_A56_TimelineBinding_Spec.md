# Amendment A56 — The timeline row is the binding surface

**Status: written 2026-08-28, never compiled.** The Editor was closed for the session that wrote
this (deliberately — see §6). Everything below is static-review only until the gate runs.

## 1. Owner request (2026-08-28, verbatim intent)

The timeline's row naming reads badly, and binding data is scattered: the track tag hides in a
component-block header, the part tag in the selection heading, and the timeline row shows both but
edits neither. The owner's mental model: **create tags, key against tags, pair tags to a rig by
tagging its parts.** The row should read `tag → rig part` and both halves should be editable in
place:

- Changing a row's **tag** moves the whole row — the keys — to that tag.
- Changing a row's **rig part** re-tags the rig so the keys follow onto that part.
- The `T `/`S ` kind prefixes say nothing; remove them.
- **A keyed row with no tag must be impossible** — the tag is the row's one identifier. "(no
  tagged part)" (rule T2) stays a legal display state; "(untagged)" does not.

## 2. Decisions (recorded, per the delegated-calls directive)

- **D1 — Row anatomy.** A transform/flipbook header row is `[foldout][tag][→][part]`. The tag and
  part are click targets opening pickers; clicking the row's empty background still selects every
  key on the track (the old label-click behaviour moves there). Bone and event rows keep the old
  single-label shape — a bone binds by name, an event lane already has its own menu.
- **D2 — Tag pick = whole-row move, with merge.** Picking a tag another same-kind keyed track
  already binds merges this row's keys into that track and deletes this one: that is literally
  "move these keys to that line". Key collision at one time (within `KeyTimeTolerance`): the
  incoming key wins — the user's gesture was "put *these* keys there". Transform merge unions
  `channels`. A flipbook merge is refused (status-line message, no edit) unless `mode`,
  `sliceSpace` and `baseIndex` all match — a sprite key's number is meaningless under another
  track's settings.
- **D3 — Part pick = move the tag on the rig.** Writes `RigTargetDefinition.tagId`: the old
  wearer is cleared (rule T1 keeps a tag unique per rig), the new part takes it — displacing any
  tag the new part wore, visibly (its old tag's rows go "(no tagged part)"). Undo on the rig,
  never the clip. Every clip set sharing the rig follows; that is the point.
- **D4 — Tags are assigned at track creation, not requested later.** Any path that creates a
  Transform/Flipbook track on an untagged part first tags the part: reuse the registry tag whose
  name equals the part's display name (case-insensitive) when no other part on this rig wears it,
  else mint `Name`, `Name 2`, … through `CreateVocabularyEntry` + `PersistVocabulary`. Implemented
  as `ClipComponentModel.EnsureTargetTagged(rig, targetId, registry, out minted)` — registry is a
  parameter so EditMode tests never touch ProjectSettings — driven by a window-side sweep
  (`EnsureClipTrackTagsAssigned`) after component add, first key, and paste.
- **D5 — Legacy rows.** Assets authored before this amendment can hold `tagId == 0` tracks. No
  silent migration (standing owner rule): such a row renders its tag half as `(assign tag)` and
  both halves open the tag picker until one is assigned. The track-binding picker loses its
  "(none)" row — a track can no longer be *cleared* to untagged (`ForTrackTagRebind` config); the
  part-tag picker keeps "(none)", an unkeyed part may still be untagged.
- **D6 — The inspector's track-tag button stays**, re-routed through the same retag-with-merge
  core as the timeline, so the two surfaces cannot disagree. Its `tagId == 0` text becomes
  "Assign tag…" (was "Target-bound").

## 3. Tasks

1. `ClipComponentModel`: `EnsureTargetTagged`, `MergeTransformTracks`, `MergeSpriteTracks`,
   `SpriteTracksMergeCompatible`.
2. `VocabularyPickerConfig.ForTrackTagRebind` (no "(none)" row).
3. `RigTargetPicker` — new `PickerOverlay` subclass listing the rig's parts (filter field, hover
   card says what each part wears, current wearer dimmed as already-bound).
4. `ClipEditorWindow`: prefix removal, row controls, `RetagTrack`, `MoveTagToRigPart`,
   `EnsureClipTrackTagsAssigned`, "(assign tag)" display, tooltip rewrites; USS hover styles.
5. `ComponentStack`: `AddComponent` records the rig when an add can tag a part;
   `ApplyTrackTagBinding` delegates to `RetagTrack`; button text/config per D5/D6.
6. Tests (two, EditMode, in-memory only): transform merge collision/ordering/channel-union;
   `EnsureTargetTagged` name reuse vs suffix minting. **Neither has been run.**
7. Docs: `clip-editor.md`, `CHANGELOG.md`, HANDOFF, the vault note.

## 4. Not done / not decided here

- No per-key tag move (splitting a selection off to another tag's row). Owner chose whole-row.
- Lane ordering still carries no meaning (A55 Task 3 standing decision).
- Visual pass: **nothing here has been seen on screen.** The eight-step A55 §4 Task 5 pass plus
  this amendment's rows need a live Editor and the owner's eyes.

## 5. Verification owed (blocked on an open Editor)

Compile gate → the two new fixtures → prove D4 persists (create a track on an untagged part
against a real saved rig, reload, assert the rig's part and the track share a non-zero tag; then
undo and assert both revert) → prove D2's merge survives save/reload → visual pass.

## 6. Test-cadence directive (owner, 2026-08-28)

The Editor was closed on purpose: sessions were re-running the full ~700-test suite after every
edit. **Do not run the full EditMode+PlayMode suites per change.** Compile gate plus the fixtures
the change touches per edit; full suites once, at the commit point. And stop growing the suite —
per the standing rule, a test that cannot be watched to fail is deleted, and most UI wiring gets
none.
