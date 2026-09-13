# Amendment A85 — Event payload schema: a key says what its parameters mean

> **Status:** ✅ built 2026-09-13 as `0.32.0` (commits `743d0fab`, `d78e21f2`, `5169daf0`, close-out); ⏸ T10 owner checkpoint open.
> **Roadmap:** [`AnimationPackage_Roadmap.md`](AnimationPackage_Roadmap.md) Phase 1.
> **Predecessors:** A55 (event authoring), A83 (the inspector pane).
> **Executor:** one orchestrator; `worker` subagents in **one wave of five**, each ≤ 2 files.

---

## 0. Session prompt (paste into a fresh session)

You are running **Amendment A85** on the DOTS Animation Toolkit package (head `0.31.0` or later).
Spec: `Assets/_Vault/Tasks/AnimationPackage/A85_EventPayloadSchema_Spec.md`. Read it, the roadmap
§3 protocol, then only what §3 here names. T0 and T1 are yours; then one wave (T2–T6); one gate;
T7–T9 yours. Stop at T10.

---

## 1. Goal

An `EventMarker` carries `intParam` and `floatParam`, and nothing anywhere says what they mean for
a given key. The Clip Editor's event inspector shows two raw number fields; the generated
`AnimEvents` constants say `Footstep = 0x10` and nothing more; a host reading `AnimEventOutput`
guesses. After this amendment an `AnimEventKeyEntry` declares a payload schema — a label for each
parameter, an optional named-value list for the int — the inspector renders a dropdown or a labelled
field accordingly, and the generated constants file carries the meaning in its XML doc and a nested
`Values` class per enumerated key.

---

## 2. Decisions (recorded — do not re-ask). ⚠ = interpretation not yet confirmed.

- **A85-D1 — Schema lives on the registry entry, not on the marker.** Four new fields on
  `AnimEventKeyEntry`: `string intParamLabel`, `string floatParamLabel`, `List<string>
  intParamValueNames` (index = value; empty = free integer), `string floatParamUnit` (display
  suffix, e.g. "m/s"). Empty labels mean "unused" and the inspector hides that field.
- **A85-D2 — Registry files under `ProjectSettings/` are edited in place;** no migration. Missing
  fields deserialize to empty, which is "no schema" — today's behaviour.
- **A85-D3 — The inspector renders from the schema:** int with value names → `DropdownField`
  over the names (stores the index); int with a label only → `IntegerField` with that label; no
  label → hidden. Float likewise with `FloatField` and the unit as a suffix label. A marker whose
  `intParam` is out of the names' range shows the raw number in the dropdown's text with a warning
  tint (`ToolkitPalette.Warning`) — it is never silently clamped.
- **A85-D4 — Generated constants gain meaning.** `ConstantsGenerator` emits, per key with a
  schema, a `<summary>` naming the labels and, when `intParamValueNames` is non-empty, a nested
  `public static class <Key>Values { public const int <Name> = i; }`. Identifier sanitising reuses
  `SanitizeIdentifier` / `MakeUniqueName`.
- **A85-D5 — Cutscene event markers get the same inspector** — but the shared inspector element is
  A86's; here A85 changes only the clip-side builder and leaves a one-line note for A86.
- **A85-D6 — The registry inspector and the Quick Edit window show the schema fields** under a
  "Payload" foldout per entry. ⚠ The owner has not seen the Quick Edit window with extra rows;
  the checkpoint asks.

---

## 3. Read first

- `Authoring/Assets/AnimEventKeyRegistry.cs` lines 150–175 (`AnimEventKeyEntry`).
- `Editor/ClipUtilities/ConstantsGenerator.cs` lines 40–130 (`BuildVocabularyConstantsSource`),
  170–260 (sanitising).
- The clip event inspector builders: grep `EventMarker marker, AnimEventKeyRegistry registry` in
  `Editor/ClipEditor/Panes/ClipInspectorPane.cs` (pre-A83: `ClipEditorWindow.cs:7180`, `:7265`).
- `Editor/Inspectors/AnimEventKeyRegistryEditor.cs` in full; `VocabularyQuickEditWindow.cs` — grep
  `AnimEventKeyEntry`.
- `Tests/EditMode/ConstantsGeneratorTests.cs` — the fixture being extended.
- `Documentation~/animation-events.md` "Naming your events".

---

## 4. Design

### 4.1 `AnimEventKeyEntry` additions (T1)

```csharp
[Tooltip("What intParam means for this event. Empty hides the field in the Clip Editor.")]
public string intParamLabel = string.Empty;
[Tooltip("Named values for intParam, index = value. Empty means any integer.")]
public List<string> intParamValueNames = new List<string>();
[Tooltip("What floatParam means for this event. Empty hides the field.")]
public string floatParamLabel = string.Empty;
[Tooltip("Display suffix for floatParam, e.g. m/s. Display only.")]
public string floatParamUnit = string.Empty;
```

### 4.2 `Editor/ClipEditor/Components/EventPayloadFieldBuilder.cs` (T2)

`public static class EventPayloadFieldBuilder` (`Builder` suffix — it builds elements from
authoring data; if `Conformance_G` disagrees, allowlist as a plain noun `EventPayloadFields`).
`public static VisualElement BuildIntField(AnimEventKeyEntry entry, int currentValue, Action<int>
onChanged)` and `BuildFloatField(...)` per D3. Pure element construction; both event inspectors
call it.

### 4.3 `ConstantsGenerator` (T3)

`BuildVocabularyConstantsSource` gains an optional `Func<int, string> summaryForRow` and
`Func<int, IReadOnlyList<string>> nestedValueNamesForRow`; `AnimEventKeyRegistryEditor`'s
generate button passes closures over the entries. Other registries pass null and emit as today.

---

## 5. Tasks

- [x] **T0 — Baseline (orchestrator).** Gate; totals. Open `ProjectSettings/` and note the event
  registry file's name and current entry count in §7.
- [x] **T1 — Entry fields (orchestrator).** §4.1 by hand; gate; commit `A85-T1`.
- [x] **T2 — `EventPayloadFieldBuilder` [parallel-safe]** — Files: new file. Read §4.2, the
  inspector builders' ranges (for the element idiom: how labels and fields are styled today).
- [x] **T3 — Generator + fixture [parallel-safe]** — Files: `ConstantsGenerator.cs`,
  `Tests/EditMode/ConstantsGeneratorTests.cs`. Fixture:
  `EnumeratedKey_EmitsNestedValuesClass` — two names `["Left","Right"]` on one row → output
  contains `public static class FootstepValues` with `Left = 0` and `Right = 1`, and the summary
  line contains the int label. Revert-to-fail: drop the nested emission.
- [x] **T4 — Clip inspector uses the builder [parallel-safe]** — Files: `ClipInspectorPane.cs`
  (the two builder ranges only). Replace the two raw fields with `EventPayloadFieldBuilder` calls;
  keep undo recording as it is.
- [x] **T5 — Registry inspector + Quick Edit foldout [parallel-safe]** — Files:
  `AnimEventKeyRegistryEditor.cs`, `VocabularyQuickEditWindow.cs`. A "Payload" foldout per entry
  with the four fields; persist through `VocabularyRegistryProvider.Persist`.
- [x] **T6 — Docs + changelog [parallel-safe]** — Files: `Documentation~/animation-events.md`
  (a "Payload schema" subsection under "Naming your events", with the generated-constants example),
  `CHANGELOG.md` `## [0.32.0]`.
- **Gate the wave.** `ConstantsGeneratorTests`, `ClipEditorAddEventTests`. Commit `A85-T2..T6`.
- [x] **T7 — Orchestrator edits.** `package.json`; `Conformance_G` allowlist if T2 needed it; vault
  note "The vocabulary pattern" gains the schema fields.
- [x] **T8 — Drive.** Full suites. Give one real key two value names, save, reload the registry
  from disk (`VocabularyRegistryProvider.AnimEventKeys` after a domain reload) and confirm the
  names persisted; open a clip with that key and confirm the dropdown; generate constants and open
  the generated file. Capture the inspector.
- [x] **T9 — Close.** HANDOFF §4, roadmap checkbox.
- [ ] **T10 — ⏸ owner checkpoint.** Message: "In Project Settings ▸ DOTS Animation ▸ Event Keys,
  give Footstep an int label 'Foot' with values Left/Right. Open a clip with a Footstep marker: the
  inspector shows a dropdown. Regenerate constants and read `AnimEvents.FootstepValues`. ⚠ The
  Quick Edit window grew a Payload foldout per row — say if it should stay collapsed or go."

---

## 6. Deliberately out of scope

- Typed payloads beyond int + float (a third parameter, a string, an asset reference) — the
  runtime struct is not changing.
- Validation that a marker's `intParam` is within the names' range (a warning tint only; A94 may add
  a Health rule).

## 7. Build log

**T0 (2026-09-13, head `ececae16`).** Compile gate clean. Suite totals inherited from A84 and not
re-run: EditMode 826 (one standing failure, `Conformance_A` asmdef reference list), PlayMode 283.
Event registry file: `ProjectSettings/DotsAnimationToolkitAnimEventKeyRegistry.asset`
(`EditorJsonUtility` JSON), **4 entries**: Sound 16, Damage 17, Attack 18, Dialogue 19. Generated
file `Assets/Generated/DotsAnimationToolkit/AnimEvents.cs` (tracked). Probe: a `DropdownField`
given `SetValueWithoutNotify("7")` outside its choices keeps value `"7"` with index −1, so D3's
raw-number display needs no workaround.

Drifts:
- **`VocabularyQuickEditWindow` never names `AnimEventKeyEntry`**: it hosts the registry editor
  through `Editor.CreateEditor`. T5 therefore edits only `AnimEventKeyRegistryEditor.cs` for the
  foldout, and the Quick Edit window inherits it.
- **The generate path is `VocabularyConstantsSection.RegenerateIfConfigured`**, not a button on
  `AnimEventKeyRegistryEditor` (there is no button; A-era "no button, no dialog, ever"). The
  closures ride on two optional constructor parameters of `VocabularyConstantsSection`, which took
  T5's second file slot. Payload edits regenerate when the inspector closes (`OnDisable`), like a
  rename.
- **The spec's §3 builder ranges (`EventMarker marker, AnimEventKeyRegistry registry`) are the key
  and window fields;** the two raw payload fields live in `AddSelectedEventMarkerFields`
  (`ClipInspectorPane.cs:550–576`). T4 replaced those.
- **There is no Footstep key in this project's registry**, and the settings page is **Project
  Settings ▸ DOTS Animation Toolkit ▸ Event Names**, not "DOTS Animation ▸ Event Keys". The T10
  message is given verbatim with that correction attached.

Interpretations (⚠, asked at T10):
- ⚠ **D1 against D2.** D1 says an empty label hides the field; D2 says no schema is today's
  behaviour, and today every key shows both raw fields. Built: an entry with no schema at all
  (both labels empty, no value names) shows today's raw "Int Param" / "Float Param"; once any
  schema field is set, an unlabelled parameter hides.
- ⚠ **A hidden parameter that still stores a non-zero value is shown**, as "Int Param (unused)" /
  "Float Param (unused)" in `ToolkitPalette.Warning`, so a value that fires at runtime never
  becomes invisible (D3's "never silently clamped", extended).
- **D5 note for A86:** the payload rendering is `EventPayloadFieldBuilder.BuildIntField` /
  `BuildFloatField` (`Editor/ClipEditor/Components/`), pure over an `AnimEventKeyEntry`; the
  cutscene event inspector still binds raw "Int Param" / "Float Param" fields
  (`CutsceneEditorPanel.cs` `AddBoundField(eventProperty, "intParam", …)`) and should call the same
  two methods when A86 unifies the surfaces.

**T1 (743d0fab).** The four fields by hand; gate clean.

**Wave T2–T6 (d78e21f2).** Five workers in parallel against pinned signatures; all finished well
under the cap (52–72k tokens, 5–18 tool uses). Orchestrator fixes before the gate: T2's new file
lacked `using DotsAnimationToolkit.Authoring;`; T5's `DescribePayloadForConstants` and nested-names
closure were not null-safe on a null registry row. One gate: compile clean,
`ConstantsGeneratorTests` + `ClipEditorAddEventTests` 6/6. **Revert-to-fail:** with the nested
emission disabled, `EnumeratedKey_EmitsNestedValuesClass` failed on its class assertion; restored,
it passes. Kept.

**T7 (5169daf0).** `package.json` and the `PackagingConformanceTests` pin at `0.32.0`; no
`Conformance_G` allowlist entry (the `Builder` suffix passes); vault "The vocabulary pattern" gained
the schema paragraph and its traps.

**T8 — suites and drive (2026-09-13).** EditMode **827** (826 + 1; only the standing
`Conformance_A` failure), PlayMode **283/283**. Drive against the real registry: the Sound key (16)
was given `intParamLabel = "Variant"` and names Soft/Loud and persisted; the registry editor was
created and destroyed so `OnDisable` regenerated `AnimEvents.cs` through the real path. The file
read `/// <summary>Event 'Sound'. intParam: Variant (0 = Soft, 1 = Loud).</summary>` followed by
`public static class SoundValues { Soft = 0; Loud = 1 }`. After a forced domain reload,
`VocabularyRegistryProvider.AnimEventKeys` re-read label and both names from disk, and
`StitchPunk.Generated` had compiled `AnimEvents+SoundValues` (Soft=0, Loud=1).

**The live Clip Editor could not be driven and nothing was captured:** the Editor was unfocused
for the whole drive, and the owner's docked DOTS Animator window had a null panel with `CreateGUI`
not yet re-run after the reload (Unity rebuilds a docked window's GUI only when it paints). Forcing
it visible would rearrange the owner's layout without fixing OS focus, so the inspector was proven
one level down instead: `EventPayloadFieldBuilder` over the persisted Sound entry and `NewClip 1`'s
real first marker (key 16, intParam 0, floatParam 0) gave a `DropdownField` labelled Variant on
Soft (index 0, choices Soft/Loud) and a hidden float field; intParam 7 gave value "7", index −1,
label in `ToolkitPalette.Warning`; a null entry gave today's "Int Param" `IntegerField`. The owner's
window selection was never changed.

**Restore.** The Sound entry's schema was cleared, persisted and regenerated; `AnimEvents.cs` is
byte-identical to HEAD. `ProjectSettings/DotsAnimationToolkitAnimEventKeyRegistry.asset` now
serializes the four new fields empty on all four entries (no value changed) — the shape any later
persist writes, committed with the close-out rather than reverted.
