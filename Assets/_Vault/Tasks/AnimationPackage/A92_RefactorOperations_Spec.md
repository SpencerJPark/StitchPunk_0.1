# Amendment A92 — Project-wide refactor operations

> **Status:** 📝 specced 2026-09-10, not built. Takes `0.39.0`.
> **Roadmap:** [`AnimationPackage_Roadmap.md`](AnimationPackage_Roadmap.md) Phase 1, after A84.
> **Predecessors:** A84 (the index says what each operation will touch), A86 (event lane context
> menu is where "Change key everywhere" lives), A77 (rename in place — renames are display-only and
> need no refactor, because tags and keys are stable ids).
> **Executor:** one orchestrator; `worker` subagents in **one wave of five**, each ≤ 2 files.

---

## 0. Session prompt (paste into a fresh session)

You are running **Amendment A92** on the DOTS Animation Toolkit package (head `0.38.0` or later,
**A84 built**). Spec: `Assets/_Vault/Tasks/AnimationPackage/A92_RefactorOperations_Spec.md`. Read
it, the roadmap §3 protocol, then only what §3 here names. T0 and T1 yours; one wave (T2–T6); one
gate; T7–T9 yours. Stop at T10.

---

## 1. Goal

Renaming a tag or an event key is free — names are display, ids are stable. What is not free is
changing which id something uses: re-keying every `Footstep` marker to `FootstepHeavy`, merging two
keys that turned out to mean the same thing, moving every clip track bound to tag `Hand_L` onto
`Hand`, or retiring a key that a dozen assets still reference. Today each of those is a hand edit
across clips, cutscenes and profiles, with the delete confirmation (A84) the only thing that even
lists them.

After this amendment three operations exist as `RefactorEditing` methods, each one Undo group
across every touched asset, each previewed with A84's reference list before it runs, and each
reachable from where the thing being refactored is shown: the event lane context menu, the event
registry inspector, the tag registry inspector, and the Rigs tab target row.

---

## 2. Decisions (recorded — do not re-ask). ⚠ = interpretation not yet confirmed.

- **A92-D1 — Three operations, no more this round:**
  `RekeyEvent(uint fromKey, uint toKey)` — every `EventMarker`, `CutsceneEventMarker` and
  `ActorAnimationDefinition.ragdollAtEventKey` equal to `fromKey` becomes `toKey`; the registry
  entry for `fromKey` is left in place (the user deletes it, seeing zero references).
  `MergeEventKeys(uint fromKey, uint intoKey)` — `RekeyEvent` then delete the `fromKey` registry
  entry, in one undo group.
  `ReplaceTrackTag(uint fromTagId, uint toTagId)` — every `TransformTrack`/`SpriteTrack`/billboard
  track with `tagId == fromTagId` gets `toTagId`; rig targets are **not** retagged (a rig's tag is
  the roster fact; the clip is what moves).
- **A92-D2 — Preview before commit.** Each operation has a `Preview(...)` returning A84's
  `List<AssetReference>`; the UI shows `AssetReferenceIndex.SummarizeForDialog` in a confirm dialog;
  Cancel does nothing. No silent operations.
- **A92-D3 — One undo group per operation**, `Undo.SetCurrentGroupName("Re-key event")`,
  `Undo.RecordObject` on every touched asset before its write, `EditorUtility.SetDirty` after,
  `AssetDatabase.SaveAssets` once at the end. Ctrl+Z reverts every asset.
- **A92-D4 — Payload conflicts on merge:** if `fromKey` and `intoKey` have different A85 payload
  schemas, the dialog says so and the markers keep their raw `intParam` — never remapped by name.
  ⚠ (Remapping by matching value names is possible; the owner decides whether it is wanted.)
- **A92-D5 — Entry points:** event lane right-click → "Change key everywhere…" (opens a
  `VocabularyPicker` for the target key, then the preview dialog); event registry inspector row →
  "Merge into…"; tag registry inspector row → "Replace in clips with…"; Rigs tab target row's Tag
  button menu → "Move clip tracks to another tag…". All four call the same two methods.
- **A92-D6 — Static class `RefactorEditing`** (`Editing` suffix: editor-only clip-editing
  operations), `Editor/ClipEditor/Editing/`.

---

## 3. Read first

- `Editor/ClipUtilities/AssetReferenceIndex.cs` (A84) in full.
- `Editor/ClipEditor/Editing/ClipTransformEditing.cs` lines 1–60 — the `Editing` class idiom and
  its undo pattern.
- `Editor/ClipUtilities/TargetTagBindingUtility.cs` and `AnimEventBindingUtility.cs` in full —
  existing partial tag/key operations (reuse; do not duplicate).
- `Editor/Inspectors/AnimEventKeyRegistryEditor.cs`, `TargetTagRegistryEditor.cs` — grep the
  per-row context menu or buttons.
- `Editor/ClipEditor/Authoring/RigTargetRowBuilder.cs` — grep `Tag` for the button's menu.
- The event lane context menu: grep `DropdownMenu` / `ContextualMenuPopulateEvent` in
  `Editor/ClipEditor/TrackLaneElement.cs` (post-A86 it may be in `EventLaneStyle`'s host).

---

## 4. Design

### 4.1 `Editor/ClipEditor/Editing/RefactorEditing.cs` (T1 surface, T2 body)

```csharp
public static class RefactorEditing
{
    public static List<AssetReference> PreviewRekeyEvent(uint fromKey);
    public static int RekeyEvent(uint fromKey, uint toKey);          // returns touched asset count
    public static int MergeEventKeys(uint fromKey, uint intoKey);
    public static List<AssetReference> PreviewReplaceTrackTag(uint fromTagId);
    public static int ReplaceTrackTag(uint fromTagId, uint toTagId);
}
```

### 4.2 Pure core — `Editor/ClipEditor/Editing/RefactorTargetResolver.cs` (T2)

Given a `ClipAsset`, returns the marker indices / track indices that an operation would touch, so
the fixture runs on in-memory clips with no `AssetDatabase`.

### 4.3 `Editor/ClipEditor/Components/RefactorConfirmDialog.cs` (T3)

A UI Toolkit `EditorWindow` (modal via `ShowModalUtility`) listing the preview summary with
Cancel / Apply. `Conformance_E`: no `EditorUtility.DisplayDialog` — that is IMGUI-adjacent but
allowed? T0 checks the conformance test's banned list; if `DisplayDialog` is allowed, use it and
skip this file.

---

## 5. Tasks

- [ ] **T0 — Baseline (orchestrator).** Gate; totals. Check `Conformance_E`'s banned API list for
  `EditorUtility.DisplayDialog` (decides whether T3 exists). Grep the four entry points.
- [ ] **T1 — Surface (orchestrator).** `RefactorEditing.cs` with stubs; gate; commit `A92-T1`.
- [ ] **T2 — Body + resolver + fixture [parallel-safe]** — Files: `RefactorEditing.cs`, new
  `RefactorTargetResolver.cs`. Fixture (orchestrator adds the third file from the report):
  `Tests/EditMode/RefactorTargetResolverTests.cs` — `RekeyTouchesOnlyMatchingMarkers` (clip with
  keys 16, 17, 16 → indices `[0, 2]` for `fromKey 16`) and `ReplaceTagLeavesUntaggedTracksAlone`
  (`tagId 0` never matches even when `fromTagId` is 0 — guard). Revert-to-fail: drop the `tagId != 0`
  guard.
- [ ] **T3 — Confirm dialog [parallel-safe]** — Files: new `RefactorConfirmDialog.cs` (or skipped
  per T0).
- [ ] **T4 — Registry inspector entry points [parallel-safe]** — Files:
  `AnimEventKeyRegistryEditor.cs` ("Merge into…"), `TargetTagRegistryEditor.cs` ("Replace in
  clips with…").
- [ ] **T5 — Lane + Rigs entry points [parallel-safe]** — Files: the lane context menu file T0
  named ("Change key everywhere…"), `RigTargetRowBuilder.cs` (the Tag button menu).
- [ ] **T6 — Docs + changelog [parallel-safe]** — Files: `Documentation~/animation-events.md`
  ("Re-keying and merging events") and `Documentation~/sharing-clips.md` ("Moving tracks to another
  tag"); `CHANGELOG.md` `## [0.39.0]` is the orchestrator's (T7) to keep this at two files.
- **Gate the wave.** `RefactorTargetResolverTests`. Commit `A92-T2..T6`.
- [ ] **T7 — Orchestrator edits.** `CHANGELOG.md`; `package.json`; vault note "Refactor operations
  (A92)" with D3's undo shape.
- [ ] **T8 — Drive.** Full suites. On `Assets/A92Scratch` copies (two clips, one cutscene, one
  profile with a ragdoll trigger, all on key 16): Preview lists four assets; Apply re-keys to 17;
  reload all four from disk and confirm; Ctrl+Z once → all four back to 16 (reload again). Replace a
  tag across two clips likewise. Delete scratch; `git status` clean.
- [ ] **T9 — Close.** HANDOFF §4, roadmap checkbox.
- [ ] **T10 — ⏸ owner checkpoint.** Message: "Right-click an event pin → Change key everywhere.
  The dialog lists what it will touch; Apply; Ctrl+Z reverts all of it. Same for Merge in the Event
  Keys settings and Replace-in-clips on Target Tags. ⚠ D4: on merge, should int payloads be remapped
  by matching value names, or left raw as now?"

---

## 6. Deliberately out of scope

- Time offsets / scaling of keys across clips; mirror-all; batch retiming.
- Retagging rig targets (the rig is the roster fact).
- Scene/prefab references (A84's scope rule).

## 7. Build log

_(empty)_
