# Amendment A86 — One event editing surface for clips and cutscenes

> **Status:** 📝 specced 2026-09-10, not built. Takes `0.33.0`.
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

- [ ] **T0 — Baseline (orchestrator).** Gate; totals. Grep both validators for `eventKey` and
  list the codes they emit today (D4 reuses them). Confirm D3's claim about the cutscene lane's
  glyph. Capture both inspectors before.
- [ ] **T1 — Interface file (orchestrator).** Write `IEventMarkerAccessor.cs` (the interface only)
  so the wave compiles against it. Gate. Commit `A86-T1`.
- [ ] **T2 — Two adapters [parallel-safe]** — Files: new `ClipEventMarkerAccessor.cs`, new
  `CutsceneEventMarkerAccessor.cs` (both `Editor/ClipEditor/Components/`).
- [ ] **T3 — Inspector element [parallel-safe]** — Files: new `EventMarkerInspectorElement.cs`.
  Read `EventPayloadFieldBuilder.cs`, `VocabularyPicker.cs` (grep public surface).
- [ ] **T4 — `EventLaneStyle` + cutscene lane adopts it [parallel-safe]** — Files: new
  `EventLaneStyle.cs`, `CutsceneMomentLaneElement.cs` (event-drawing range only).
- [ ] **T5 — `AnimEventValidation` + fixture [parallel-safe]** — Files: new
  `AnimEventValidation.cs`, new `Tests/EditMode/AnimEventValidationTests.cs`. Fixture:
  `WindowOnPulseOnlyKey_IsAWarning` (key 80, window 0.2 → one Warning) and
  `KeyBelowFirstUserKey_IsAnError` (key 3 → Error). Revert-to-fail: remove each rule.
- [ ] **T6 — Docs + changelog [parallel-safe]** — Files: `Documentation~/animation-events.md`
  ("Cutscene events use the same inspector" paragraph), `CHANGELOG.md` `## [0.33.0]`.
- **Gate the wave.** `AnimEventValidationTests`. Commit `A86-T2..T6`.
- [ ] **T7 — Orchestrator edits (sequential, two files that the wave could not own).**
  `ClipInspectorPane.cs`: the two event builders become one `EventMarkerInspectorElement.Bind`
  call with a `ClipEventMarkerAccessor`. `CutsceneEventInspectorProviders.cs`: likewise with the
  cutscene adapter. `TrackLaneElement.cs`: pin drawing calls `EventLaneStyle`. `ClipValidation` and
  the cutscene validator call `AnimEventValidation` and drop their own rules. Gate;
  `ClipEditorAddEventTests`, `ClipValidationTests` (grep the fixture's real name), the cutscene
  validation fixture. `Conformance_G` allowlist for `EventLaneStyle`. `package.json`.
- [ ] **T8 — Drive.** Full suites. Open a clip and a cutscene each with an event; edit key and
  payload on both; reload both from disk; confirm. Capture both inspectors after.
- [ ] **T9 — Close.** HANDOFF §4, vault note ("Event lanes are per-name" section gains: one
  inspector, one validation), roadmap checkbox.
- [ ] **T10 — ⏸ owner checkpoint.** Message: "Select an event on a clip, then on a cutscene. Same
  inspector, same pin. The cutscene one has Fire on skip / Hold rows; the clip one has Window. Say
  if the cutscene pin should have kept its old glyph."

---

## 6. Deliberately out of scope

- Merging the two serialized types (D1). Revisit only if a schema migration is ever scheduled.
- Hold markers, cues, mark lanes — untouched.

## 7. Build log

_(empty)_
