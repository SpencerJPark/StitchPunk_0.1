# Amendment A86 — One event editing surface for clips and cutscenes

> **Status:** ✅ built 2026-09-13 as `0.33.0` (T0–T9); ⏸ T10 owner checkpoint open. §7 carries seven spec-vs-code drifts, the build log and the drive.
> **Roadmap:** [`AnimationPackage_Roadmap.md`](AnimationPackage_Roadmap.md) Phase 1.
> **Predecessors:** A55 (clip event lanes), A64/A65 (cutscene event lane and cues), A85 (payload
> fields). **Serialized types are not touched** — see D1.
> **Executor:** one orchestrator; `worker` subagents in **one wave of five**, each ≤ 2 files.

---

## 0. Session prompt (paste into a fresh session)

You are running **Amendment A86** on the DOTS Animation Toolkit package (head `0.32.0` or later).
Spec: `Assets/_Vault/Tasks/AnimationPackage/A86_UnifiedEventEditing_Spec.md`. Read it, the roadmap
§3 protocol, then only what §3 here names. T0 and T1 are yours; one wave (T2–T6); one gate; T7–T9
yours. Stop at T10.

---

## 1. Goal

`EventMarker` (clip: `normalizedTime`, `windowSeconds`, struct) and `CutsceneEventMarker`
(cutscene: `time` in seconds, `fireOnSkip`, `holdUntilReleased`, class) share a key vocabulary and a
payload shape but have two inspectors, two lane drawings, and two validation paths. A key renamed
in the registry shows correctly in one and, when the code paths drift, wrongly in the other; A85's
payload fields would otherwise be wired twice.

After this amendment one `EventMarkerInspectorElement` edits both through a small adapter, one
`EventLaneStyle` draws both, and one `AnimEventValidation` rule set runs over both. The two
serialized types stay exactly as they are.

---

## 2. Decisions (recorded — do not re-ask)

- **A86-D1 — Unify the surface, not the storage.** Changing either type's fields would either
  break every saved clip and cutscene (YAML paths) or demand a migration, and the owner's rule is
  "build no migration paths". The time bases also genuinely differ (fraction of clip vs timeline
  seconds). So: an `IEventMarkerAccessor` adapter with `Key`, `IntParam`, `FloatParam`,
  `DisplayTimeSeconds`, `HasWindow`, `WindowSeconds`, `HasSkipFlag`, `FireOnSkip`, `HasHoldFlag`,
  `HoldUntilReleased`, each with a setter that the adapter routes to the right field and records
  undo on the right asset.
- **A86-D2 — Fields the type lacks are hidden, not disabled.** A clip marker shows no
  fire-on-skip row; a cutscene marker shows no window row.
- **A86-D3 — One colour per key everywhere.** Both lanes already call
  `ToolkitPalette.ColorForEventKey`; the pin shape is the amber pin A55 shipped; the cutscene lane
  adopts it (today it draws its own moment glyph for events — T0 confirms).
- **A86-D4 — Validation moves to `Authoring/Validation/AnimEventValidation.cs`:** key below
  `FirstUserKey` (error), key absent from the registry (error — the T3 analogue), maskable window on
  a pulse-only key (`windowSeconds > 0` with key > 79: warning), out-of-range `intParam` against
  A85's value names (warning). `ClipValidation.ValidateClip` and the cutscene validator both call
  it; their existing event rules are deleted from them, not duplicated.
- **A86-D5 — Right-click on either lane offers the same menu:** Rename key (registry), Change key,
  Duplicate marker, Delete marker, Copy / Paste payload.

---

## 3. Read first

- `Authoring/Assets/ClipAsset.cs` lines 316–340 (`EventMarker`); `Authoring/Assets/CutsceneAsset.cs`
  lines 384–420 (`CutsceneEventMarker`).
- Clip event inspector: grep `EventMarker marker, AnimEventKeyRegistry registry` in
  `Editor/ClipEditor/Panes/ClipInspectorPane.cs`.
- Cutscene event inspector: `Editor/ClipEditor/Cutscene/CutsceneEventInspectorProviders.cs` in full.
- Lane drawing: `Editor/ClipEditor/TrackLaneElement.cs` — grep `EventMarker`;
  `Editor/ClipEditor/Cutscene/CutsceneMomentLaneElement.cs` — grep `CutsceneEventMarker`.
- Validation: `Authoring/Validation/ClipValidation.cs` — grep `events` and `eventKey`; the cutscene
  validator — grep `eventKey` under `Authoring/Build/` and `Authoring/Validation/`.
- `Editor/ClipEditor/Components/EventPayloadFieldBuilder.cs` (A85) in full.

---

## 4. Design

### 4.1 `Editor/ClipEditor/Components/IEventMarkerAccessor.cs` + two adapters (T2)

`ClipEventMarkerAccessor(ClipAsset clip, int flatIndex)` and
`CutsceneEventMarkerAccessor(CutsceneAsset cutscene, int index)`. Setters call
`Undo.RecordObject(asset, "Edit Event")` then write. The clip adapter converts
`DisplayTimeSeconds` from `normalizedTime * clip.duration` and back.

### 4.2 `Editor/ClipEditor/Components/EventMarkerInspectorElement.cs` (T3)

`public void Bind(IEventMarkerAccessor accessor, AnimEventKeyRegistry registry)`. Rows: key
(`VocabularyPicker`), time (read-only label in seconds and, for clips, frames at the registry's
`referenceFrameRate`), payload (A85 builder), window (clip only), fire-on-skip and hold (cutscene
only). Every field `SetValueWithoutNotify` on bind.

### 4.3 `Editor/ClipEditor/Shared/EventLaneStyle.cs` (T4)

`public static class EventLaneStyle` — plain noun, allowlist — with `DrawPin(MeshGenerationContext,
Rect, Color, bool selected)` and `DrawWindow(...)`, the amber pin from `TrackLaneElement` lifted
verbatim so both lanes call it.

### 4.4 `Authoring/Validation/AnimEventValidation.cs` (T5)

`public static void ValidateMarkers(IReadOnlyList<(uint key, int intParam, float floatParam,
float windowSeconds)> markers, Func<uint, bool> registryContainsKey, Func<uint,
IReadOnlyList<string>> valueNamesForKey, string ownerName, List<ValidationMessage> output)`.
Codes: reuse the existing `ValidationCode` values these rules already carry (T0 lists them); add
none unless a rule is new (the pulse-only-window warning is new — take the next free `Vxx`).

---

## 5. Tasks

- [x] **T0 — Baseline (orchestrator).** Gate; totals. Grep both validators for `eventKey` and
  list the codes they emit today (D4 reuses them). Confirm D3's claim about the cutscene lane's
  glyph. Capture both inspectors before.
- [x] **T1 — Interface file (orchestrator).** Write `IEventMarkerAccessor.cs` (the interface only)
  so the wave compiles against it. Gate. Commit `A86-T1`.
- [x] **T2 — Two adapters [parallel-safe]** — Files: new `ClipEventMarkerAccessor.cs`, new
  `CutsceneEventMarkerAccessor.cs` (both `Editor/ClipEditor/Components/`).
- [x] **T3 — Inspector element [parallel-safe]** — Files: new `EventMarkerInspectorElement.cs`.
  Read `EventPayloadFieldBuilder.cs`, `VocabularyPicker.cs` (grep public surface).
- [x] **T4 — `EventLaneStyle` + cutscene lane adopts it [parallel-safe]** — Files: new
  `EventLaneStyle.cs`, `CutsceneMomentLaneElement.cs` (event-drawing range only).
- [x] **T5 — `AnimEventValidation` + fixture [parallel-safe]** — Files: new
  `AnimEventValidation.cs`, new `Tests/EditMode/AnimEventValidationTests.cs`. Fixture:
  `WindowOnPulseOnlyKey_IsAWarning` (key 80, window 0.2 → one Warning) and
  `KeyBelowFirstUserKey_IsAnError` (key 3 → Error). Revert-to-fail: remove each rule.
- [x] **T6 — Docs + changelog [parallel-safe]** — Files: `Documentation~/animation-events.md`
  ("Cutscene events use the same inspector" paragraph), `CHANGELOG.md` `## [0.33.0]`.
- **Gate the wave.** `AnimEventValidationTests`. Commit `A86-T2..T6`.
- [x] **T7 — Orchestrator edits (sequential, two files that the wave could not own).**
  `ClipInspectorPane.cs`: the two event builders become one `EventMarkerInspectorElement.Bind`
  call with a `ClipEventMarkerAccessor`. `CutsceneEventInspectorProviders.cs`: likewise with the
  cutscene adapter. `TrackLaneElement.cs`: pin drawing calls `EventLaneStyle`. `ClipValidation` and
  the cutscene validator call `AnimEventValidation` and drop their own rules. Gate;
  `ClipEditorAddEventTests`, `ClipValidationTests` (grep the fixture's real name), the cutscene
  validation fixture. `Conformance_G` allowlist for `EventLaneStyle`. `package.json`.
- [x] **T8 — Drive.** Full suites. Open a clip and a cutscene each with an event; edit key and
  payload on both; reload both from disk; confirm. Capture both inspectors after.
- [x] **T9 — Close.** HANDOFF §4, vault note ("Event lanes are per-name" section gains: one
  inspector, one validation), roadmap checkbox.
- [ ] **T10 — ⏸ owner checkpoint.** Message: "Select an event on a clip, then on a cutscene. Same
  inspector, same pin. The cutscene one has Fire on skip / Hold rows; the clip one has Window. Say
  if the cutscene pin should have kept its old glyph."

---

## 6. Deliberately out of scope

- Merging the two serialized types (D1). Revisit only if a schema migration is ever scheduled.
- Hold markers, cues, mark lanes — untouched.

## 7. Build log

### T0 — baseline and drift (2026-09-13, head `a6983203`)

Compile gate clean. Totals inherited from A85's close at the same head (EditMode 827 with the one
pre-existing `Conformance_A` failure, PlayMode 283). Before-captures skipped: the Editor was
unfocused (`EditorApplication.isFocused` false), so the docked window does not paint.

Spec vs reality, decided and recorded here rather than re-specced silently:

1. **The cutscene event inspector is `CutsceneEditorPanel.BuildEventInspector`**, not
   `CutsceneEventInspectorProviders.cs`. That file is the public host seam
   (`ICutsceneEventInspectorProvider`, bound to a `SerializedProperty`). It stays: the new element
   takes a payload override the cutscene panel routes to `CutsceneEventInspectorProviders.TryBuild`,
   so a host provider still owns the payload for the keys it claims. T7 edits the panel.
2. **There is no cutscene validator.** `Authoring/Validation/` has only `ClipValidation` and
   `ActorProfileValidation`; the cutscene panel's `BuildSlotValidationNotes` knows nothing about
   events. So nothing is deleted on the cutscene side. Instead both inspectors show the selected
   marker's `AnimEventValidation` findings under the element (`SetFindings`), and `ClipValidation`
   delegates its event rules for the badge and the bake.
3. **Codes (D4).** Existing: V09 (key below `FirstUserKey`, Error), V19 (negative window, Error),
   V20 (window on a pulse-only key, Warning — so this rule is *not* new). All three move. New:
   **V41** = 48 (key absent from the registry, Error, judged only when a registry is passed — the
   badge passes the project registry, the bake passes none, mirroring T3's `tagRegistry`), **V42** =
   49 (`intParam` outside the entry's value names, Warning). Enum byte values 41–47 are P1–P7, so the
   next V takes 48. Added in T1 so T5 stays at two files.
4. **T5's fixtures swap to the two new rules** (`KeyAbsentFromRegistry_IsAnError`,
   `IntParamOutsideValueNames_IsAWarning`). The spec's pair (key 80 window, key 3) already exist as
   `ClipValidationTests.V20_…` / `V09_…`, which exercise `AnimEventValidation` once T7 delegates.
5. **D3: the cutscene lane does not paint.** `CutsceneMomentLaneElement` builds one 10px
   `VisualElement` per marker, shaped by USS (`rotate: 45deg` diamond; `--holding` adds a 3px amber
   border), fill per key already from `ColorForEventKey`. The clip pin is `Painter2D` in
   `TrackLaneElement.DrawEventMarker`. So `EventLaneStyle` takes a `Painter2D` (not a
   `MeshGenerationContext` + `Rect`), and the moment lane gains a pin mode whose markers paint the
   pin in `generateVisualContent`; a holding event keeps its meaning as a `ToolkitPalette.Holding`
   outline.
6. **D5 had no task.** Added **T6b** to the wave: `EventMarkerContextMenu.cs` (Rename key, Change
   key, Duplicate, Delete, Copy / Paste payload) over the accessor plus host callbacks. The clip lane
   has no per-marker menu today (only the lane header's); the cutscene marker menu is "Delete" only.
   T7 wires both.
7. **Time row is read-only (§4.2).** The cutscene inspector's editable "Time (s)" field goes away;
   dragging on the lane still moves the marker. Raised at T10.

### T1 — interface (commit `dd5c04b3`)

`IEventMarkerAccessor` plus an `EventMarkerField` enum (hosts need to know *which* field changed:
a key change can move a clip marker's lane and changes a cutscene pin's colour), and
`ValidationCode.V41`/`V42`. Gate clean.

### T2–T6 + T6b — one wave of six workers (commit `906763e2`)

52–75k tokens each, no guard denials. Post-wave checks: every new file has
`using DotsAnimationToolkit.Authoring;` where needed and null-checks registry rows. One slip fixed
by hand: `EventLaneStyle` carried two method `<summary>`s (Conformance_F). Gate clean;
`AnimEventValidationTests` 2/2. **Revert-to-fail:** both rules switched off behind a probe → both
tests failed ("Expected: 1 But was: 0"); file restored byte-identical (`cmp`).

### T7 — orchestrator wiring (commit `dd185eb5`)

- `ClipInspectorPane`: the key button, window field, payload builder calls and key-choice method
  became one `EventMarkerInspectorElement` over a `ClipEventMarkerAccessor`, plus the marker's
  findings. `OnClipEventMarkerFieldEdited` calls `RefreshSerializedClip` + `MarkPreviewDirty` —
  **not** `CommitClipEdit`, whose `Undo.CollapseUndoOperations(gestureUndoGroup)` would collapse
  from a stale group, because the accessor records its own undo. `DescribeEventKey`,
  `EditEventMarker`, `AddEventWindowField`, `OpenEventKeyPicker`, `ApplyEventKeyChoice` deleted.
- `CutsceneEditorPanel.BuildEventInspector`: same element over `CutsceneEventMarkerAccessor`,
  `PayloadOverride` → `CutsceneEventInspectorProviders.TryBuild`, findings, hold note kept; edits
  end in `serializedObject.Update()`. `BuildEventRows` sets `drawsEventPins` and the marker menu
  (duplicate inserts a copy 0.1 s later).
- `TrackLaneElement`: pin and window bar through `EventLaneStyle`; a `ContextualMenuManipulator`
  raises `eventKeyContextMenu` for the pin under the pointer (middle button pans, so no clash).
  `TimelinePane` fills it: Change key (with the registry default-window rule), Duplicate (one
  reference frame later), Delete (selects then `DeleteSelectedKeys`).
- `ClipValidation.ValidateClip`/`ValidateBind` gain optional `eventKeyRegistry`;
  `ValidationBadgeElement` passes the project registry; the bake passes none.
  `AnimEventValidation.RegistryContainsKey`/`ValueNamesForKey` adapt a registry (null → rule off).
- Conformance_G allowlist: `EventLaneStyle`, `EventMarkerContextMenu`. `package.json` and the
  conformance pin → `0.33.0`.
- Fixtures: `AnimEventValidationTests`, `ClipValidationTests`, `ClipEditorAddEventTests`,
  `PackagingConformanceTests` (72, standing `Conformance_A` only); `CoLocatedEventMarkerTests`,
  `TimelineGeometryHitTests` 10/10.

### T8 — suites and drive

**Suites:** EditMode **829** (827 + 2; standing `Conformance_A` failure only), PlayMode **283**.

**Drive** on scratch copies (`NewClip 1.asset` → clip, `A65CheckpointCutscene.asset` → cutscene),
saved by object with `SaveAssetIfDirty`, then the YAML read back from disk:

- An element with no live panel never dispatches `ChangeEvent`; an unshown `EditorWindow`'s root
  has a null panel too. The elements were therefore hosted for the length of one synchronous call
  on an already-painted window's root (`MainToolbarWindow`) and removed in a `finally` — nothing
  painted in between, no layout touched.
- Clip marker 1 through the real fields: Window 6 frames → `windowSeconds: 0.1`, Int 3, Float 0.5;
  `FieldEdited` raised Window, IntParam, FloatParam. Key 16 → 18 through the accessor. Context menu
  lists Rename key…, Change key…, Duplicate marker, Delete marker | Copy payload, Paste payload
  (Paste disabled until a copy); Copy on marker 1 + Paste on marker 0 → marker 0 `intParam: 3`,
  `floatParam: 0.5`. On disk: `eventKey: 18 / intParam: 3 / floatParam: 0.5 / windowSeconds: 0.1`.
- Cutscene marker 0: rows Event, Time (read-only "3s"), Int/Float Param, Fire On Skip, Hold Until
  Released — no Window row. Key 19 → 17, Int 7, Fire On Skip off. Against an in-memory registry
  whose Damage entry names two values, the payload became a `Strength` dropdown showing raw `7`,
  index −1, label tinted Warning; picking Heavy wrote 1. On disk: `eventKey: 17 / intParam: 1 /
  fireOnSkip: 0 / holdUntilReleased: 1`.
- **Bug found and fixed:** `SetFindings` prefixed every label with a literal `"V09 · "` (the T3
  brief's example text was taken verbatim), so a V42 finding read "V09 · V42 · …".
- `G1CheckpointCutscene.asset` stores event key **1**, so its inspector now shows V09 (Error) — a
  game-side asset fact, not an A86 defect.
- Cleanup: scratch folder deleted after `Undo.ClearUndo`; the event registry file hashes identical
  to before the drive (`b315ce62…`); `NewClip.asset` untouched.
- **No capture.** The docked DOTS Animator window was not driven: showing a scratch clip in it would
  replace the owner's open clip session. The two elements were proven one level down instead.

### T9 — close

HANDOFF §4 paragraph, vault note ("Event lanes are per-name" gains the A86 traps), this log. The
roadmap box stays unticked until T10 is answered.
