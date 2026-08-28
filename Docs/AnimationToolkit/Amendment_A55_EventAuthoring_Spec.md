# Amendment A55 — Event authoring reaches tag parity: technical spec

**Opened:** 2026-08-28.
**Package:** `Packages/com.dotsanimationtoolkit`.
**Surface:** Clip Editor event lanes + `AnimEventKeyRegistry` editing.
**Precedent:** Amendments A51/A52 and Phase E §4.2 (target tags). This is the same shape applied
to the second vocabulary.
**Status:** specified, not built. Inserted ahead of Phase F in the queue (owner directive,
2026-08-28) — Phase F waits.
**Numbering note:** this was handed to the session as "Amendment A53"; renumbered to A55 because
A53 (part-tag button relocation) and A54 (vocabulary constants auto-regenerate) already occupied
those numbers in the tree, uncommitted, when this spec arrived. See commit `3cb8b6fb`.

---

## 1. Goal

Authoring an event should feel identical to authoring a target tag:

1. Add Event opens a picker, not a silent insert. Pick an existing event, filter, or Create event
   'Foo' in place.
2. Event names are editable the way tag names are — inline rename row, key shown, Remove behind a
   confirmation that names the cost.
3. The timeline keeps one lane per event kind, labeled with that event's name; changing a marker's
   kind moves it to that kind's lane, creating one if it's the first of its kind.

## 2. Current state (verified against the tree at 8a12e8f9, plus A53/A54 landed on top at `3cb8b6fb`)

Already done — do not rebuild it:

- `EventLaneAddressing.cs` — one lane per distinct `eventKey`, first-appearance order; flat-storage
  ↔ lane-local address mapping.
- `ClipEditorWindow.cs:4536-4557` — timeline builds one row per lane, headed by
  `DescribeEventName(...)`.
- `ClipEditorWindow.cs:6706-6790` — the selected marker's inspector has an `Event: <name>` button
  that opens `VocabularyPicker` and re-homes the marker's lane on change (`ApplyEventKeyChoice`).
- `VocabularyPicker` + `VocabularyPickerConfig.ForEventKeys(...)` — the shared searchable picker,
  with Create row, near-duplicate guard, and (as of A53) a pinned Edit… button beside the search
  field that opens `VocabularyQuickEditWindow`.
- `VocabularyRegistryProvider` — project-scoped registry in
  `ProjectSettings/DotsAnimationToolkitAnimEventKeyRegistry.asset`, auto-created. As of A54, its
  `RegistryChanged` event keeps a still-open picker's rows current, and constants (if configured)
  regenerate themselves with no button.

Line numbers above are from the pre-A53/A54 tree and will have drifted; re-locate by symbol name,
not by line, before editing.

## 3. The three real gaps

| # | Gap | Where |
|---|-----|-------|
| G1 | Add Event inserts a marker using `ResolveNewEventKey()` — the registry's first entry, or a bare `FirstMaskKey` when the registry is empty, producing an `(unresolved 0x…)` lane. It never asks which event. | `ClipEditorWindow.cs:5798-5848`, `ClipEditorTransport.cs:290-294` |
| G2 | `AnimEventKeyRegistryEditor` renders entries as one raw `PropertyField` array drawer. No inline rename row, no key column, no remove confirmation — while `TargetTagRegistryEditor` hand-builds all three. | `Editor/Inspectors/AnimEventKeyRegistryEditor.cs:44-48` vs `TargetTagRegistryEditor.cs:117-240` |
| G3 | Event lanes have no header affordances: no context menu, no way to add a marker to a specific lane except double-clicking it, and lane order is first-appearance-in-storage rather than registry order (so lanes reshuffle in ways the author didn't ask for). | `ClipEditorWindow.cs:4544-4556`, `AddTrackRow` at `:4583` |

G2 is independently confirmed current as of this renumbering: `AnimEventKeyRegistryEditor.cs` still
shows `root.Add(new PropertyField(entriesProperty));` with no hand-built rows (see the file as
committed at `3cb8b6fb`) — A53/A54 touched this file only for the constants-auto-regen wiring, not
the row UI.

## 4. Tasks

### Task 1 — Add Event opens the picker (G1)

File: `ClipEditorWindow.cs`

- Split `AddEventAtPlayhead()` into two:
  - `OpenAddEventPicker()` — the button's new handler. Guards on `selectedClip != null`, resolves
    the registry via `ResolveEventKeyRegistry()`, and calls
    `VocabularyPicker.Open(rootVisualElement, addEventButton, registry, registry, VocabularyPickerConfig.ForEventKeys(registry), chosenEventKey => AddEventAtPlayhead(chosenEventKey), RebuildTimeline)`.
  - `AddEventAtPlayhead(uint eventKey)` — the existing body, but the key comes from the argument
    instead of `ResolveNewEventKey()`.
- `InsertKey`'s `TimelineTrackKind.Event` case (`:5773-5793`) keeps its current lane-targeted
  behaviour for double-click-in-a-lane. Add an explicit key parameter path rather than the
  `trackIndex < 0` fallback: pass the chosen key down so the -1 branch no longer needs
  `ResolveNewEventKey()`.
- Delete `ResolveNewEventKey()` once nothing calls it. It is the "guess an event" behaviour this
  amendment removes; leaving it is how it comes back.
- The picker's `onRegistryChanged` must rebuild the timeline, not just the inspector — a Create
  event mints a name that lane headers display.
- Add Event with an empty registry now works correctly for the first time: type a name → Create
  event 'Foo' → key minted from `FindFirstFreeKey()` → marker placed. No more unresolved-hex lane
  on a fresh project.

File: `ClipEditorTransport.cs:290-294` — rebind `addEventButton.clicked` to `OpenAddEventPicker`.
Keep `addEventButton` as a field; the picker needs it as its anchor. Update the tooltip at `:347`
to "Choose an event and place a marker at the playhead."

### Task 2 — Event registry rows match tag registry rows (G2)

File: `Editor/Inspectors/AnimEventKeyRegistryEditor.cs`

Rewrite `CreateInspectorGUI`'s list section modelled line-for-line on
`TargetTagRegistryEditor.RefreshRows()`/`BuildRow()`:

- A help label: "An event names a moment in a clip (\"Footstep\", \"ApplyDamage\"). Rename
  freely — a marker stores the key, never the name, so nothing breaks."
- Hand-built rows replacing `new PropertyField(entriesProperty)`:
  - `PropertyField` over `name`, `flexGrow = 1` (this is the rename affordance the user is
    missing).
  - A selectable `Label` showing `key <n>` plus a maskable/pulse-only note, tooltip explaining the
    key never changes on rename.
  - `IntegerField` over `defaultWindowFrames` (fixed width) — event-only, no tag equivalent, but
    it belongs on the row rather than behind a foldout.
  - Remove button → `RemoveEntry(entryIndex)`.
  - `description` stays behind a small foldout so the row does not become three lines tall.
- `rowsContainer.Bind(serializedObject)` after building, same as the tag editor (rows created
  post-bind carry no bindings).
- Keep the existing `TrackSerializedObjectValue` → `VocabularyRegistryProvider.Persist` callback
  (present as of A54), and add `RefreshRows()` to it. This is load-bearing: the project-scoped
  registry lives outside the `AssetDatabase`, so a rename has nothing else that would ever write it
  to disk.
- `AddEntry()` stays as-is (`CreateVocabularyEntry("NewEvent")` + `PersistVocabulary` and the
  `AssetDatabase.Contains` override branch).

New file: `Editor/ClipUtilities/AnimEventBindingUtility.cs` — the `TargetTagBindingUtility`
analogue.

- `int CountMarkerBindings(uint eventKey, IReadOnlyList<ClipAsset> clips)` — pure, no
  `AssetDatabase`; counts markers in `clip.events` whose `eventKey` matches. `eventKey == 0` returns
  0 (reserved/invalid is not a binding).
- `int CountMarkerBindings(AnimEventKeyEntry entry)` — the convenience overload that finds clips via
  `AssetDatabase`, mirroring `TargetTagBindingUtility`'s pair.

`RemoveEntry(int entryIndex)` in the event editor, mirroring
`TargetTagRegistryEditor.RemoveEntry`:

> Delete event 'Footstep'?
> 12 marker(s) across 4 clip(s) use it and will show as an unresolved key the moment it is gone.

…or "Nothing currently uses it." Cancel-by-default via `EditorUtility.DisplayDialog`.

Note the divergence from tags: deleting an event does not break a bake. The key is arithmetic;
markers keep firing the same number. The dialog must say "shows as an unresolved key", not "fails
validation" — copying the tag wording here would be a lie.

Remove the now-stale remark in `TargetTagRegistryEditor`'s class docs claiming
`AnimEventKeyRegistryEditor` "gets away with a single `PropertyField` because deleting an event key
has nothing to warn about."

### Task 3 — Lane headers become an authoring surface (G3)

File: `ClipEditorWindow.cs`

- `AddTrackRow` currently takes a header string and wires selection only. Add an optional
  context-menu builder parameter (or a dedicated `AddEventTrackRow`) so the event rows at
  `:4544-4556` get a `ContextualMenuManipulator` on the header label with:
  - **Add marker at playhead** → `AddEventAtPlayhead(laneKey)`, the Task 1 method with this lane's
    key.
  - **Select all markers** → the existing `SelectAllKeysOnTrack`, so the menu doesn't hide the
    click behaviour.
  - **Change event…** → opens the picker anchored to the header; on pick, re-points every marker in
    that lane to the chosen key under one undo gesture, then rebuilds. (This is how a whole lane
    gets re-pointed in place, distinct from renaming the registry row.)
  - **Delete lane** → confirm, then remove every marker in the lane.
- Lane ordering: replace the raw `ComputeLaneKeys` order with a registry-stable sort. Add
  `EventLaneAddressing.ComputeLaneKeys(List<EventMarker>, IReadOnlyList<uint> preferredOrder)` —
  keys present in the registry sort in registry order, unresolved keys sort after them by numeric
  value. Keep the existing one-argument overload delegating with a null order, because
  `ClipKeyClipboard` is static, has no registry access, and must keep agreeing with the window on
  lane identity. Both sides must call the same overload — if the timeline sorts by registry and the
  clipboard doesn't, paste lands in the wrong lane. Simplest safe route: give `ClipKeyClipboard` the
  ordering array at call time from the window.

  ← **DECISION: if threading the order into the clipboard is invasive, drop the ordering change
  entirely and keep first-appearance order.** Lane position is addressing nobody reads meaning into
  (E6's own argument); the label is what carries meaning. Ordering is the least valuable item in
  this amendment — cut it before cutting anything else.
- `DescribeEventName` already renders `(unresolved 0x…)` for a key no registry names. Leave that;
  it's the one place spec §4.2.3 permits a raw id.

### Task 4 — Docs

- `Docs/AnimationToolkit/Phase_B_Architecture.md` — append Amendment A55, dated, stating the
  directive: event authoring uses the same picker, the same quick-edit window, and the same row
  editor as target tags; there is no second way to name an event.
- `Documentation~/animation-events.md` — replace the "Add Event places a marker using the first
  registry entry" description with the picker flow; add the rename/remove section.
- `Documentation~/clip-editor.md` — one line on event lane headers and their context menu.
- `Docs/AnimationToolkit/HANDOFF.md` §4 — record A55's closure, then restore Phase F as the queue.
- `CHANGELOG.md` in the package.

### Task 5 — Visual pass (Clip Editor open, any clip selected)

1. Fresh path: empty registry → Add Event → type "Footstep" → Create → marker lands, lane reads
   Footstep, not hex.
2. Second Footstep at another time → same lane.
3. Add Event → pick ApplyDamage → new lane.
4. Select a marker → Event: button → change kind → marker jumps lanes, stays selected.
5. Picker's Edit… → rename Footstep to Step → close → lane header and marker inspector both read
   Step, marker unmoved.
6. Remove Step from the registry → dialog names the marker count → confirm → lane reads
   `(unresolved 0x…)`, markers still there.
7. Right-click a lane header → each of the four menu items does what it says.
8. Force a domain reload (touch any script) → renames survived. This is the Persist path and the
   one thing that regresses silently.

## 5. Out of scope

- No change to `EventMarker`, the blob, `ClipRegistryBuilder`, or any runtime code. Names are
  authoring-only; a rename must never invalidate a baked clip.
- No change to `ClipSetAsset.eventKeys` back-compat — an explicitly assigned registry still wins
  over the project singleton.
- No new validation rules. A registry is authoring furniture and never reaches a blob; V04/V09 stay
  as they are.

## 6. Ordering

Task 2 (rename rows) is independently useful and touches one file — land it first. Task 1 is the
headline behaviour and depends on nothing. Task 3 is the largest and most cuttable. Tasks 4–5 close
it.
