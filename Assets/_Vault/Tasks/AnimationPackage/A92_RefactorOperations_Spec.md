# Amendment A92 — Project-wide refactor operations

> **Status:** ✅ built 2026-09-14 as `0.39.0`; **T10 accepted 2026-09-14 (owner: assume pass).** 13 T0 drifts in §7 (cutscene part
> tracks included and merge payloads left raw are owner calls of 2026-09-14).
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

- [x] **T0 — Baseline (orchestrator).** Gate; totals. Check `Conformance_E`'s banned API list for
  `EditorUtility.DisplayDialog` (decides whether T3 exists). Grep the four entry points.
- [x] **T1 — Surface (orchestrator).** `RefactorEditing.cs` with stubs; gate; commit `A92-T1`.
- [x] **T2 — Body + resolver + fixture [parallel-safe]** — Files: `RefactorEditing.cs`, new
  `RefactorTargetResolver.cs`. Fixture (orchestrator adds the third file from the report):
  `Tests/EditMode/RefactorTargetResolverTests.cs` — `RekeyTouchesOnlyMatchingMarkers` (clip with
  keys 16, 17, 16 → indices `[0, 2]` for `fromKey 16`) and `ReplaceTagLeavesUntaggedTracksAlone`
  (`tagId 0` never matches even when `fromTagId` is 0 — guard). Revert-to-fail: drop the `tagId != 0`
  guard.
- [x] **T3 — Confirm dialog [parallel-safe]** — Files: new `RefactorConfirmDialog.cs` (or skipped
  per T0).
- [x] **T4 — Registry inspector entry points [parallel-safe]** — Files:
  `AnimEventKeyRegistryEditor.cs` ("Merge into…"), `TargetTagRegistryEditor.cs` ("Replace in
  clips with…").
- [x] **T5 — Lane + Rigs entry points [parallel-safe]** — Files: the lane context menu file T0
  named ("Change key everywhere…"), `RigTargetRowBuilder.cs` (the Tag button menu).
- [x] **T6 — Docs + changelog [parallel-safe]** — Files: `Documentation~/animation-events.md`
  ("Re-keying and merging events") and `Documentation~/sharing-clips.md` ("Moving tracks to another
  tag"); `CHANGELOG.md` `## [0.39.0]` is the orchestrator's (T7) to keep this at two files.
- **Gate the wave.** `RefactorTargetResolverTests`. Commit `A92-T2..T6`.
- [x] **T7 — Orchestrator edits.** `CHANGELOG.md`; `package.json`; vault note "Refactor operations
  (A92)" with D3's undo shape.
- [x] **T8 — Drive.** Full suites. On `Assets/A92Scratch` copies (two clips, one cutscene, one
  profile with a ragdoll trigger, all on key 16): Preview lists four assets; Apply re-keys to 17;
  reload all four from disk and confirm; Ctrl+Z once → all four back to 16 (reload again). Replace a
  tag across two clips likewise. Delete scratch; `git status` clean.
- [x] **T9 — Close.** HANDOFF §4, roadmap checkbox.
- [x] **T10 — ⏸ owner checkpoint.** Message: "Right-click an event pin → Change key everywhere.
  The dialog lists what it will touch; Apply; Ctrl+Z reverts all of it. Same for Merge in the Event
  Keys settings and Replace-in-clips on Target Tags. ⚠ D4: on merge, should int payloads be remapped
  by matching value names, or left raw as now?"

---

## 6. Deliberately out of scope

- Time offsets / scaling of keys across clips; mirror-all; batch retiming.
- Retagging rig targets (the rig is the roster fact).
- Scene/prefab references (A84's scope rule).

## 7. Build log

### T0 drift (2026-09-14, head 2ad594a2; package unchanged since 1cad5221, CHANGELOG top still 0.38.0 so A92 takes 0.39.0)

1. **No confirm window (T3 as specced is skipped).** `Conformance_E` bans only `OnGUI`, `GUILayout`
   and `Handles.`; `EditorUtility.DisplayDialog` is allowed and already used by six package files.
   T3 is re-purposed as `Editor/ClipEditor/Editing/RefactorPromptEditing.cs`: the shared
   pick-a-target → preview → `DisplayDialog` → run step all four entry points call.
2. **D3 `AssetDatabase.SaveAssets` is not used.** Standing rule: it flushes the owner's unsaved
   editor state. Each touched asset is saved with `AssetDatabase.SaveAssetIfDirty(asset)` instead.
   Ctrl+Z reverts every asset in memory (one collapsed group) and leaves them dirty; disk follows on
   the next save, as for any Unity undo.
3. **Billboard tracks carry no tag** (`BillboardTrack.rootStableId` only). `ReplaceTrackTag` moves
   `TransformTrack` and `SpriteTrack`.
4. **Cutscene part tracks are included** (owner call 2026-09-14, "use your recommendations"):
   `CutsceneKeyedTrack.tagId` moves with the clip tracks. Rig targets are still never retagged.
5. **Preview filters A84's list.** `ReferencesToTag` also returns `RigTargetTag` rows, which the
   operation never touches; `PreviewReplaceTrackTag` drops them, so the dialog lists only what changes.
6. **Ragdoll re-key matches the index**: only definitions with `ragdollTrigger != None` are re-keyed,
   the same filter `ReferencesToEventKey` applies, so nothing is touched that the preview did not list.
7. **D4 settled** (owner call 2026-09-14): merge leaves `intParam` raw; the dialog warns when the two
   entries' payload schemas (int label, int value names, float label, float unit) differ. A85's schema
   lives on `AnimEventKeyEntry`.
8. **Lane menu is `EventMarkerContextMenu`**, shared by `TimelinePane` and `CutsceneEditorPanel`.
   T1 added a `changeKeyEverywhere` parameter after `openKeyPicker` and wired both callers (the cutscene
   one calls `serializedObject.Update()` before rebuilding, since the operation writes behind it), so T5
   shrinks to the Rigs tab.
9. **Rigs tab Tag button has no menu** (left-click opens the tag picker, in `RigsPanel.cs`, not
   `RigTargetRowBuilder.cs`, which is data only). "Move clip tracks to another tag…" is a right-click
   `ContextualMenuManipulator` item on the Tag button, enabled when the row has a tag.
10. **Registry inspectors use a name menu, not the overlay picker.** `VocabularyPicker` needs a host
    it can add itself to; the inspectors get a `GenericDropdownMenu` of the other entries' names via
    `RefactorPromptEditing.ShowMergeIntoMenu` / `ShowReplaceTagMenu`.
11. **`MergeEventKeys` gains a registry overload** `(fromKey, intoKey, AnimEventKeyRegistry)` so the
    drive can merge against a scratch registry; `VocabularyRegistryProvider.Persist` is already a no-op
    for anything but the project instance.
12. **Drive keys.** The operations are project-wide, so the drive uses an event key and a tag id no
    real asset uses, and previews before applying to prove only scratch assets are listed.
13. **One wave of five**: T2 (operations + resolver), T3 (prompts), T4 (inspectors), T5 (Rigs tab),
    T6 (docs).

### Close (2026-09-14)

- **Commits:** `9b66dd17` (T0/T1 surface), `3437aee5` (T2..T6 wave), then the close commit.
- **Wave shape (revises drift 13):** four workers (T2, T3, T4, T6) at 55–69k tokens each; T5 was ten lines in
  `RigsPanel.cs`, so the orchestrator wrote it.
- **Gate:** compile clean after T1, after the wave and after the restore. `RefactorTargetResolverTests` 2/2.
  Revert-to-fail in one compile: the marker match widened to `!= 0u` failed `RekeyTouchesOnlyMatchingMarkers`,
  and dropping both `tagId != 0u` guards failed `ReplaceTagLeavesUntaggedTracksAlone`; restored, sha256 matched.
- **Suites:** EditMode 840 (838 + 2; the standing Conformance_A failure only), PlayMode 285.
- **Drive** on `Assets/A92Scratch` (two clips, one cutscene with a part track, one profile with a Start ragdoll
  trigger), key 4000001 → 4000002 and tag 0x7A920001 → 0x7A920002, both asserted unused project-wide first:
  - Re-key: preview 4 rows, "Referenced by 2 clips, 1 cutscene, 1 profile."; touched 4; all four files on disk
    carried the new key and were not dirty. One `Undo.PerformUndo` put all four back in memory (dirty);
    saving them restored the old key on disk.
  - Replace tag: preview 3 rows (both clips, the cutscene; no rig rows); touched 3; the untagged tracks and the
    profile were untouched; one undo reverted all three.
  - Merge against a `CreateInstance` registry: payload mismatch detected (int label on one side only); touched 4;
    `A92From` removed; one undo restored the four assets and both entries.
  - Scratch folder deleted with no stray `.meta`; both `ProjectSettings` registry files sha256-unchanged.
- **Not driven:** the four UI entry points. `DisplayDialog` is modal and would block the Editor under MCP, the
  Clip Editor window is the owner's docked copy, and no capture was taken. The owner checkpoint covers them.
- **Commit slip:** `9b66dd17` also carries a deletion of `Assets/_Vault/Tasks/Claude/Cult-of-the-Lamb…jpg` that
  was already staged by another session before A92 began (`git commit` takes the whole index).

### Owner checkpoint answer (2026-09-14)

- **Accepted without a hands-on look.** The owner's instruction for the open checkpoints: "let's just assume they
  pass unless there's something really game breaking I need to check". Nothing in this amendment's §7 is game
  breaking, so the checkpoint is closed as built with no follow-up.
