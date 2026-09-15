# Amendment A97F — Retarget tab: a Skipped row can add its tag to a rig part

> **Status:** 📝 specced 2026-09-14 from the owner's A97 T11 answer, not built. Takes `0.50.0`.
> **Roadmap:** [`AnimationPackage_Roadmap.md`](AnimationPackage_Roadmap.md), Phase 2 follow-up to A97.
> **Predecessors:** A97 (`0.47.0`), A84 (`AssetReferenceIndex`).
> **Executor:** one lead; `worker` subagents in **one wave of three**, each ≤ 2 files; the stage does the drive and the close
> (no window wiring changes).

---

## 0. Session prompt

You are running **A97F — Skipped row adds its tag to the rig** on the DOTS Animation Toolkit (head `0.48.0` or later). Spec:
`Assets/_Vault/Tasks/AnimationPackage/A97F_SkippedRowAddsTag_Spec.md`. Read it, the roadmap §3 protocol, then only what §3 names.
§2 is settled. T0 and T1 are the lead's; one wave (T2–T4); one gate; T5–T7 belong to the stage orchestrator.

---

## 1. Goal

The owner's A97 T11 answer (2026-09-14): "yes". A ● Skipped row means the rig has no part wearing the track's tag. Today the only
fix is clip-side (remap the track). After this amendment the row's menu can also put that tag on a rig part, which edits the rig,
and the row turns ✓ Bound.

---

## 2. Decisions (owner answer + orchestrator calls, 2026-09-14 — do not re-ask). ⚠ = interpretation for the checkpoint.

- **R-D1 — Skipped rows only.** The remap menu of a Skipped row gains a submenu "Add tag to rig part ▸" listing the rig's targets
  by display name, untagged parts first, then tagged parts as "Torso (wears Chest)". Dangling rows (tag not in the registry) and
  bone tracks (bind by name, `CanRemapTag` false) get no such item.
- **R-D2 — The write is one undo step on the rig** through a new `RetargetRemapEditing.AddTagToRigPart(RigAsset rig, uint
  targetStableId, uint tagId, out string failureMessage)`, built on `RigAssetUtility.SetTargetTag`. It refuses (false, message)
  when another target already wears the tag, which a Skipped row rules out but a stale table could not.
- **R-D3 — A part that already wears a tag asks first.** `EditorUtility.DisplayDialog` naming the rig, the part, the tag it loses
  and how many clips bind that tag (`AssetReferenceIndex`); the dialog lives only in `RetargetPanel`, so drives call the editing
  method beneath it. An untagged part is written without a dialog. ⚠ Whether "wears another tag" should be offered at all.
- **R-D4 — After the write** the panel refreshes the table, the roster and the preview, and the Rigs tab picks the change up
  through the asset (no new event).

---

## 3. Read first

- `Editor/Retarget/RetargetPanel.cs` lines 320–365 (`OnTrackRemapRequested`, the menu) and grep `Refresh`.
- `Editor/Retarget/RetargetRemapEditing.cs` (public surface and `RecordClip` / `FinishEdit`).
- `Editor/ClipUtilities/RigAssetUtility.cs` lines 110–140 (`SetTargetTag`).
- `Editor/ClipEditor/ClipEditorWindow.cs` lines 4146–4168 (`WriteRigPartTag`, read only: the one-wearer rule).
- `Editor/Retarget/RetargetBindingResolver.cs` (`TrackBinding`, `CanRemapTag`); grep `ReferencesToTag` in `AssetReferenceIndex`.
- `Documentation~/retarget-tab.md`.

---

## 4. Design

```csharp
public static bool AddTagToRigPart(RigAsset rig, uint targetStableId, uint tagId, out string failureMessage);
```

`RetargetPanel.OnTrackRemapRequested` adds the R-D1 submenu for `TrackBindingState.Skipped`, R-D3's dialog for a tagged part, then
`AddTagToRigPart` and `Refresh()`. A public `AddTagToRigPart(TrackBinding binding, uint targetStableId)` on the panel (no dialog)
serves drives.

---

## 5. Tasks

- [ ] **T0 — Grounding (lead).** Verify §3's names and ranges; confirm the menu type in `OnTrackRemapRequested` supports submenus
  (`GenericMenu` "A/B" paths or `DropdownMenu`); log drift in §7.
- [ ] **T1 — Stub (lead).** The §4 signature returning false, committed before the wave.
- [ ] **T2 — Editing + fixture [parallel-safe]** — Files: `Editor/Retarget/RetargetRemapEditing.cs`, new
  `Tests/EditMode/RetargetAddTagToRigPartTests.cs`. `AddTagToUntaggedPart_SkippedTrackBecomesBound` (`CreateInstance` clip, rig and
  registry; `RetargetBindingResolver.Resolve` before and after) and `AddTagWornByAnotherPart_IsRefused`. Revert-to-fail: skip the
  `SetTargetTag` call.
- [ ] **T3 — Panel menu [parallel-safe]** — Files: `Editor/Retarget/RetargetPanel.cs`. R-D1 submenu, R-D3 dialog, refresh, and the
  drive method.
- [ ] **T4 — Docs [parallel-safe]** — Files: `Documentation~/retarget-tab.md`. A Skipped row's two fixes: remap the track (clip
  side) or add the tag to a part (rig side).
- **Gate the wave.** `RetargetAddTagToRigPartTests`, `RetargetBindingResolverTests`, `PackagingConformanceTests`.
- [ ] **T5 — Drive (stage).** Full suites. Scratch copies in `Assets/A97FScratch/`: `Walk.asset`, and `NewRig.asset` with
  `UpperLeftLeg`'s tag cleared; a `CreateInstance` registry copy. Detached `RetargetPanel`: one Skipped row; `AddTagToRigPart` on
  the untagged `UpperLeftLeg` part → 16/16 Bound; the rig copy's YAML carries the tag; `Undo.PerformUndo` clears it; scratch deleted;
  registry sha256s unchanged.
- [ ] **T6 — Vault + HANDOFF + close (stage).** CHANGELOG, `package.json` and the conformance pin `0.50.0`.
- [ ] **T7 — ⏸ owner checkpoint.** "Retarget: on a rig missing a part's tag, open the ● row's menu, Add tag to rig part ▸ pick the
  part; the row turns ✓ and the roster fills. ⚠ Should parts that already wear another tag be offered (with a confirm), or only
  untagged parts?"

---

## 6. Out of scope

- Creating new rig targets or prefab nodes from this tab.
- Adding a Dangling row's tag to the registry (Events/registry surfaces own that).

## 7. Build log

### Phase 0

Phase 0 (stage, 2026-09-15, head `2d53ae6f`): doctor clean (git 2.43.0, hooks installed, broker alive, no stage blockers); compile clean; EditMode baseline 857 (856 passed, standing Conformance_A only); PlayMode baseline 285 (285 passed); CHANGELOG top section `## [0.48.1]`; registry sha256 AnimEventKey `3bdb420d…14701`, TargetTag `dbec3d5f…eb4f`. Lead opus, workers sonnet; merges authorized once ready with gates green (owner, 2026-09-14/15). Owner is away: checkpoints close by the standing rule (assume pass unless game breaking); this batch is followed by A101 on trunk.

### T0 — Grounding (lead, 2026-09-15)

Every §3 name resolves. `OnTrackRemapRequested` sits at `Editor/Retarget/RetargetPanel.cs` 326-364 (the file ends at 365);
`RigAssetUtility.SetTargetTag` at 116-139; `ClipEditorWindow.WriteRigPartTag` at 4148-4167; `TrackBinding`/`CanRemapTag` and
`TrackBindingState` at the head of `RetargetBindingResolver.cs`; `AssetReferenceIndex.ReferencesToTag(uint)` at 449. Five drifts:

- **D-1 (menu type).** The row menu is a `GenericDropdownMenu`, not `GenericMenu`, and `GenericDropdownMenu` has no submenu
  machinery in 6000.5: its only `AddItem` overloads take a flat name, and `subMenuPath` exists solely on `DropdownMenu` and
  `DropdownMenuSeparator` (checked against the shipped `UnityEngine.UIElementsModule` metadata and XML docs). A `"A/B"` name would
  render as one literal item. R-D1's "▸" therefore becomes a **two-stage menu**: the row menu gains one item
  `Add tag to rig part…` that opens a second `GenericDropdownMenu` on the same anchor listing the rig's parts, untagged first, then
  `Torso (wears Chest)`. This is the package's existing second-stage pattern (`RefactorPromptEditing.PickTagThenReplaceTrackTag`)
  and keeps Conformance_E (UI Toolkit only; `GenericMenu` is IMGUI and would break it).
- **D-2 (the one-wearer rule is not in `SetTargetTag`).** `RigAssetUtility.SetTargetTag` writes the tag unconditionally; only
  `ClipEditorWindow.WriteRigPartTag` clears a previous wearer. R-D2's refusal is therefore `AddTagToRigPart`'s own check through
  `ClipComponentModel.FindTargetByTag(rig, tagId)` before the write, so the rig can never hold two wearers of one tag.
- **D-3 (`SetTargetTag` saves).** It ends in `EditorUtility.SetDirty` + `AssetDatabase.SaveAssetIfDirty(rig)` — one asset, not
  `SaveAssets`, so it stays inside the lead contract; noted because a fixture's `CreateInstance` rig has no path and is unaffected.
- **D-4 (a Skipped row can be untagged).** `ResolveTaggedTrack` returns `Skipped` with `tagId == 0` for a track that matches no
  target by raw id. There is no tag to add, so the new item is offered only when `binding.tagId != 0u` alongside
  `binding.CanRemapTag` and `TrackBindingState.Skipped`.
- **D-5 (no target id on the row).** `TrackBinding` carries no rig-target stable id, which is what makes R-D1's second stage — and
  the panel's `AddTagToRigPart(TrackBinding, uint targetStableId)` drive signature — the shape the data allows.

Unchanged: `AddTagToRigPart` keeps §4's signature, `RigTargetDefinition.Id.Value` is the stable id the write keys on, and because
`TrackTargetMatchResolver.TrackBindsTarget` lets a non-zero track tag win over its raw target id, tagging the part is enough to turn
the row `Bound`.

### T1-T4 build (lead, 2026-09-15, worktree `spec/a97f`)

Two commits. `bac6c237` (T0/T1) carries the grounding above and the section 4 signature as a stub returning false, so the wave's
three workers compiled against one shape. `bddf8257` (T2/T3/T4) is the wave, three sonnet workers in parallel, each on its own
files: the editing method plus its fixture, the panel menu, and the tab doc.

`RetargetRemapEditing.AddTagToRigPart(RigAsset, uint targetStableId, uint tagId, out string failureMessage)` refuses in order - no
rig, no targets list, `tagId == 0u`, a different target already wearing the tag (D-2's own check), the target no longer in the rig -
then opens an undo group named `Add Tag To Rig Part`, calls `RigAssetUtility.SetTargetTag` and marks the reference index dirty.

`RetargetPanel.OnTrackRemapRequested` adds `Add tag to rig part…` last in the row menu, gated on
`state == Skipped && CanRemapTag && tagId != 0u`. It calls `ShowAddTagToRigPartMenu`, the D-1 second menu: untagged parts first,
then tagged ones as `Torso (wears Chest)`, with `(unnamed part)` for a blank display name and `(wears another tag)` when the
registry cannot name the worn tag. A tagged part goes through `EditorUtility.DisplayDialog("Replace this part's tag", …, "Replace",
"Cancel")` naming the rig, the part, the tag it loses and `AssetReferenceIndex.ReferencesToTag(existingTagId).Count`. The public
`RetargetPanel.AddTagToRigPart(TrackBinding binding, uint targetStableId)` beneath it carries no dialog, logs the failure message
as a warning and calls `Refresh()` - R-D4's table, roster and preview in one call, no new event.

`Tests/EditMode/RetargetAddTagToRigPartTests.cs` builds clip, rig and registry with `CreateInstance` only.
`AddTagToUntaggedPart_SkippedTrackBecomesBound` resolves Skipped, adds the tag to the untagged part and resolves Bound with
`CountBound == 1`; `AddTagWornByAnotherPart_IsRefused` gets false, a non-empty message and both targets untouched. The fixture sets
`LogAssert.ignoreFailingMessages` in setup for the same reason `RigAssetUtilityTests` does: `SetTargetTag` ends in
`AssetDatabase.SaveAssetIfDirty`, which warns on a rig instance that has no asset path.

**Gate** (`bddf8257`): 16 tests across the three named fixtures, 15 passed, one failure - the standing
`Conformance_A_AsmdefReferenceLists_MatchSection13Exactly`. Compile and Burst clean.

**Revert-to-fail.** Commit `e49f2c13` alone replaced `RigAssetUtility.SetTargetTag(rig, targetStableId, tagId)` with `true`;
the gate came back 14 passed, 2 failed, the new one being
`AddTagToUntaggedPart_SkippedTrackBecomesBound` - `Expected: Bound But was: Skipped`. `git reset --hard HEAD~1` restored
`RetargetRemapEditing.cs` to sha256 `472cc1a593411ce3a60bd1075b0be9d39bbc8cddfe530e171c75839e288d8285`. The refusal test keeps
passing under that mutation by design: it returns before the write.

Unverified by this worktree: nothing in the panel was run, since the menu, the dialog and the refresh need an Editor. T5's drive
still owns the second menu opening on the anchor, the dialog text, the rig YAML carrying the tag and `Undo.PerformUndo` clearing it.

### For integration

**CHANGELOG, `## [0.50.0]`:**

> **Retarget: a Skipped row can add its tag to a rig part.** A Skipped row means the rig has no part wearing that track's tag, and
> until now the only fix was clip-side. The row's menu now also offers `Add tag to rig part…`, which opens a second menu of the
> rig's parts - untagged first, then parts that already wear a tag shown as `Torso (wears Chest)` - and writes the track's tag onto
> the part you pick, turning the row Bound. The rig changes, the clip does not, and it is one undo step. Picking a part that already
> wears another tag confirms first, naming the tag it would lose and how many clip references use it; a tag another part already
> wears is refused, so a rig never holds two wearers of one tag. The item appears only on Skipped rows carrying a tag - Dangling
> rows and bone tracks, which bind by name, do not get it. New `RetargetRemapEditing.AddTagToRigPart` and
> `RetargetPanel.AddTagToRigPart`, the latter free of the modal dialog so a drive can call it.

**`Conformance_G` allowlist:** nothing new. The write lives on the existing `RetargetRemapEditing` (the `Editing` suffix), and no
new static class was added.

**Vault-note traps:**

- `GenericDropdownMenu` has no submenu support in Unity 6000.5 - only `DropdownMenu` and `DropdownMenuSeparator` carry
  `subMenuPath`, and a `"Parent/Child"` name renders as one literal item. Nesting a UI Toolkit dropdown means opening a second
  `GenericDropdownMenu` on the same anchor. `GenericMenu` would work but is IMGUI, which `Conformance_E` bans in this package.
- `RigAssetUtility.SetTargetTag` does **not** enforce one wearer per tag; only `ClipEditorWindow.WriteRigPartTag` does. Any new
  caller must check `ClipComponentModel.FindTargetByTag` itself or the rig quietly ends up with two parts wearing one tag.
- `SetTargetTag` ends in `AssetDatabase.SaveAssetIfDirty`, which warns on a `CreateInstance` rig with no asset path. An EditMode
  fixture that calls it needs `LogAssert.ignoreFailingMessages = true`, as `RigAssetUtilityTests` already sets.
- A Skipped row can carry `tagId == 0`: `ResolveTaggedTrack` returns Skipped for an untagged track matching no target by raw id.
  Any "fix this row by its tag" affordance has to exclude that case.

**HANDOFF draft.** A97F closes the A97 T11 answer: the Retarget tab's Skipped rows now fix in both directions. The clip-side remap
is unchanged, and the row menu gains `Add tag to rig part…`, a second dropdown of the rig's parts that writes the track's tag onto
the part you choose as one undo step, turning the row Bound without touching the clip; parts that already wear a tag are offered
behind a confirm that names the tag they would lose and its clip-reference count, and the one-wearer rule is enforced in the new
`RetargetRemapEditing.AddTagToRigPart` rather than in `RigAssetUtility.SetTargetTag`, which never had it. The panel's public
`AddTagToRigPart(TrackBinding, uint)` keeps the modal dialog out of the drive path. Built by one lead and three parallel workers
against a committed stub, gated at 15 of 16 with only the standing `Conformance_A` failing, with the fixture's revert-to-fail
proven. No window wiring changed. The drive still owes the second menu, the dialog and the rig YAML round trip, and the T7
checkpoint asks whether already-tagged parts should be offered at all.
