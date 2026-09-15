# Amendment A97 — Retarget tab: clip × rig binding table and roster coverage

> **Status:** ✅ built 2026-09-14 as `0.47.0` in the A96–A98 parallel worktree batch (merged `4b0c352e`, integrated `e7ae55f9`); ⏸ T11 owner checkpoint open. The specced `0.44.0` went to A94F; see §7.
> **Roadmap:** [`AnimationPackage_Roadmap.md`](AnimationPackage_Roadmap.md) Phase 2.
> **Predecessors:** Phase E (tag rules T2 lenient / T3 error), A82 (column, split view), A84
> (`TrackTargetMatchResolver`), A92 (`ReplaceTrackTag` — used when the user remaps a whole tag).
> **Executor:** one orchestrator; `worker` subagents in **one wave of six**, each ≤ 2 files.

---

## 0. Session prompt (paste into a fresh session)

You are running **Amendment A97 — Retarget tab** on the DOTS Animation Toolkit package (head
`0.43.0` or later; **A82, A84 built**). Spec:
`Assets/_Vault/Tasks/AnimationPackage/A97_RetargetTab_Spec.md`. Read it, the roadmap §3 protocol,
then only what §3 here names. T0 yours; one wave (T1–T6); one gate; T7–T10 yours. Stop at T11.

---

## 1. Goal

"One clip covers a roster of differing rigs" is the package's sharing model: a track binds by tag;
a rig lacking that tag skips the track with a warning (T2, lenient); a tag missing from the
registry is an error (T3). The only surface for this is the validation badge's message list. An
author cannot see, for a clip, which tracks land on which rig, nor fix a miss without opening the
clip's every track. After this amendment a Retarget tab shows a clip against the shared rig as a
table — one row per track: tag, the rig target it binds to or why not — with a per-row remap
dropdown that rewrites that track's tag, a roster strip showing coverage across every rig in the
project, and the preview posing the clip on the selected rig.

```
┌ Retarget ───────────────────────────────────────────────────────────────────────────────────────┐
│ Clip Set [CitizenClips ▾]  Clip [Walk ▾]          Rig [GuardRig ▾]                                │
│ ┌ Tracks (9) ──────────────────────────────────────────────┐ ┌ Preview ───────────────────────┐ │
│ │ ✓ Torso     → Torso                                       │ │                                │ │
│ │ ✓ Head      → Head                                        │ │        (clip on GuardRig)      │ │
│ │ ● Hand_L    (no tagged part on GuardRig)   remap [▾]      │ │                                │ │
│ │ ✗ Jaw       (tag not in registry)          remap [▾]      │ │                                │ │
│ └───────────────────────────────────────────────────────────┘ └────────────────────────────────┘ │
│ Roster: MaleCitizen 9/9 ▮▮▮▮▮  GuardRig 7/9 ▮▮▮▮░  DogRig 2/9 ▮░░░░                                │
└─────────────────────────────────────────────────────────────────────────────────────────────────┘
```

---

## 2. Decisions (recorded — do not re-ask). ⚠ = interpretation not yet confirmed.

- **A97-D1 — `ClipEditorTab.Retarget`, after Materials.** Toggle `tab-retarget`, text "Retarget",
  pane `retarget-pane`. **Joins the shared selection** for both Clip Set and Rig; the Clip dropdown
  is local (the set's clips).
- **A97-D2 — One row per track, three states:** `Bound` (✓, the target's display name), `Skipped`
  (●, `Warning`, "no tagged part on <rig>" — T2), `Dangling` (✗, `Error`, "tag not in registry" —
  T3). Transform, sprite, billboard and bone tracks all appear; bone tracks bind by name and show
  `Bound`/`Skipped` by `FindHierarchyIndexByName` on the preview controller.
- **A97-D3 — Remap is per track, this clip only.** The dropdown lists the rig's tags (via
  `VocabularyPicker`'s data); choosing one writes `track.tagId` with undo. A second menu item,
  "Remap in every clip…", calls A92's `ReplaceTrackTag` if built, else is absent.
- **A97-D4 — Roster is every `RigAsset` in the project** (from `AssetReferenceIndex`'s scan), each
  with `bound / total` and a five-block bar; clicking a roster chip sets the shared Rig. The clip
  set's own rig is listed first.
- **A97-D5 — Preview reuses `ClipPreviewController`** exactly as the VAT Bake tab does (A74/A78):
  `SetRig`, `SetClipSet`, `SamplePose`, its own `PreviewCameraNavigation`. Skipped tracks pose
  nothing, which is the point — the author sees the hole.
- **A97-D6 — The binding computation is pure:** `RetargetBindingResolver.Resolve(ClipAsset,
  RigAsset, TargetTagRegistry) → List<TrackBinding>`, built on A84's `TrackTargetMatchResolver`.
  ⚠ Whether `Skipped` rows should offer "add this tag to the rig" (a rig edit) — omitted; the
  checkpoint asks.

---

## 3. Read first

- `Docs/AnimationToolkit/Phase_E_TargetTags_Spec.md` §4.2 and §6.1 — the T2/T3 rules (owner
  directives).
- `Authoring/Validation/ClipValidation.cs` lines 73–120 (`ValidateBind`) — where T2/T3 are emitted
  today; the resolver must agree with it.
- `Editor/ClipUtilities/TrackTargetMatchResolver.cs` (A84) in full.
- `Editor/VatBaking/VatBakePanel.cs` lines 50–200 — hosting a `ClipPreviewController` in a cover
  pane, and its `Dispose` (vault: "A cover pane that owns a PreviewRenderUtility must be disposed").
- `Editor/ClipEditor/Components/VocabularyPicker.cs` — public surface (grep `public`).
- `Documentation~/sharing-clips.md` in full.

---

## 4. Design

### 4.1 `Editor/Retarget/RetargetBindingResolver.cs` (T1)

```csharp
public enum TrackBindingState : byte { Bound, Skipped, Dangling }
public readonly struct TrackBinding { public readonly string trackName; public readonly uint tagId;
    public readonly TrackBindingState state; public readonly string targetDisplayName; }
public static class RetargetBindingResolver
{
    public static List<TrackBinding> Resolve(ClipAsset clip, RigAsset rig, TargetTagRegistry registry);
    public static int CountBound(List<TrackBinding> bindings);
}
```

### 4.2 `Editor/Retarget/RetargetTrackTableElement.cs` (T2) — `ListView` rows per D2/D3.
### 4.3 `Editor/Retarget/RosterCoverageStripElement.cs` (T3) — D4.
### 4.4 `Editor/Retarget/RetargetPreviewElement.cs` (T4) — D5 host.
### 4.5 `Editor/Retarget/RetargetPanel.cs` (T5) — header fields, split, strip, `Dispose`.

---

## 5. Tasks

- [x] **T0 — Baseline (orchestrator).** Gate; totals. Read `ValidateBind`'s T2/T3 conditions and
  paste them into T1's brief so the resolver matches the validator exactly.
- [x] **T1 — Resolver + fixture [parallel-safe]** — Files: new `RetargetBindingResolver.cs`, new
  `Tests/EditMode/RetargetBindingResolverTests.cs` (`TagOnRegistryButNotRig_IsSkipped_NotDangling`,
  `TagNotInRegistry_IsDangling`). Revert-to-fail: collapse the two states.
- [x] **T2 — Track table [parallel-safe]** — Files: new `RetargetTrackTableElement.cs`.
- [x] **T3 — Roster strip [parallel-safe]** — Files: new `RosterCoverageStripElement.cs`.
- [x] **T4 — Preview element [parallel-safe]** — Files: new `RetargetPreviewElement.cs`. Read
  `VatBakePanel.cs`'s preview hosting range.
- [x] **T5 — Panel [parallel-safe]** — Files: new `RetargetPanel.cs`.
- [x] **T6 — Docs [parallel-safe]** — Files: `Documentation~/sharing-clips.md` (a "Seeing coverage"
  section with the three states), new `Documentation~/retarget-tab.md` (short; may fold into the
  former — worker's call, say which).
- **Gate the wave.** `RetargetBindingResolverTests`. Commit `A97-T1..T6`.
- [x] **T7 — Window wiring (orchestrator).** Tab; `index.md`; `CHANGELOG.md` `## [0.44.0]`;
  `package.json`; `Conformance_G`. Gate; `ClipEditorLayoutTests`.
- [x] **T8 — Drive.** Full suites. Walk on MaleCitizen → all Bound; on a scratch rig missing one
  tag → one Skipped, preview shows the hole; remap the row → Bound, clip on disk carries the new
  `tagId`; Ctrl+Z reverts. Roster shows both rigs. Capture.
- [x] **T9 — Vault + HANDOFF.**
- [x] **T10 — Close.** Roadmap checkbox.
- [ ] **T11 — ⏸ owner checkpoint.** Message: "Retarget tab: pick a clip, switch rigs with the
  roster chips, watch rows go ✓/●/✗ and the preview lose parts. ⚠ Should a ● row offer 'add this
  tag to the rig' (edits the rig), or stay clip-side only?"

---

## 6. Deliberately out of scope

- Editing rigs from this tab (D6 ⚠ pending).
- Automatic tag guessing by name similarity.
- Bone-name remapping for VAT clips (names come from the DCC tool; out of the package's hands).

## 7. Build log

### Phase 0 (stage, 2026-09-14)

- **Version:** the status line said `0.44.0`; A93F–A95F took `0.43.0`–`0.45.0`, so this spec takes `0.47.0`
  (roadmap rule). CHANGELOG's top section is `## [0.45.0]` at `7d036585`.
- **Baseline at `7d036585`:** compile clean; `DotsAnimationToolkit.Tests.EditMode` 851 run, 850 passed, the one failure
  the standing `Conformance_A` (asmdef reference list); `DotsAnimationToolkit.Tests.PlayMode` 285 run, 285 passed.
- **Registry sha256:** `DotsAnimationToolkitAnimEventKeyRegistry.asset`
  `3bdb420d55b808ecfd9251ab144ac89645c4d6f903b4a8a3498a42aa76d14701`; `DotsAnimationToolkitTargetTagRegistry.asset`
  `dbec3d5f6d31db02891682e7f88e6011f7317658f1d29753a0185ff2ebd1eb4f`. Drives must leave both unchanged.
- **Stage untracked files:** none. Nothing on the stage names a type this spec removes or renames.
- **Runs as** a spec-lead (opus) with sonnet workers in a Worktree Toolkit batch beside the other two of A96–A98; the
  stage owns window wiring, `index.md`, CHANGELOG, `package.json`, the conformance pin, drives and the close.

### T0 grounding (spec-lead, worktree `spec/a97`, 2026-09-14)

Names confirmed by grep: `ClipValidation.ValidateBind` (line 73), `TrackTargetMatchResolver.TrackBindsTarget`,
`RefactorEditing.ReplaceTrackTag(uint, uint)` plus `RefactorPromptEditing.PickTagThenReplaceTrackTag(host, anchor,
fromTagId, onApplied)`, `VocabularyPicker.Open` / `VocabularyPickerConfig.ForTrackTagRebind`,
`ClipPreviewController.FindHierarchyIndexByName` (returns -1 on a miss), `AssetReferenceIndex.Rigs`,
`ActiveAssetSelection`, `CoverPaneSplitView`, `PreviewCameraNavigation.AttachTo(Image)`.

T2/T3 as the validator emits them (`ValidateTrackBindingInto`, not `ValidateBind` itself, which only calls it):

```csharp
if (tagId == 0u) { /* V38 Warning */ if (resolutionRig != null && !RigContainsTarget(resolutionRig, targetId)) ...; return; }
if (tagRegistry != null && !tagRegistry.ContainsId(tagId)) { /* V36 Error, dangling */ return; }
if (resolutionRig == null) { return; }
if (RigContainsTagTarget(resolutionRig, tagId)) { return; }
/* V35 Warning: the tag exists but this rig has no target carrying it, skipped */
```

Drift and decisions (8):

1. The T2/T3 checks live in `ValidateTrackBindingInto` (ClipValidation.cs ~1150-1220), reached from `ValidateClipInto`,
   not in lines 73-120. The resolver follows it: a registry-unknown tag is Dangling even when a rig target still wears it;
   a null registry never yields Dangling; an untagged track binds by raw id (V38 maps to Skipped).
2. `TrackBinding` carries three fields beyond section 4.1: `kind` (`RetargetTrackKind` Transform/Sprite/Bone/Billboard),
   `trackIndex` and `reason`, since a per-row remap needs to address the track. `RosterCoverageEntry` and
   `RetargetBindingResolver.BuildRoster` added for D4.
3. D5 says reuse `ClipPreviewController` "exactly as the VAT Bake tab does": the VAT Bake tab hosts a `VatPreviewElement`
   with its own `PreviewRenderUtility`, not the controller. The Actor Editor panel is the controller-hosting model
   (`Render(w, h)` into an `Image`, `cameraNavigation.Rig = controller`); T4 follows that.
4. D2 bone tracks: the pure resolver cannot hold a preview, so `Resolve` takes an optional `Func<string, bool>`; null
   searches the rig's `sourcePrefab` by first name (the rule `FindHierarchyIndexByName` uses). The panel passes null
   for both table and roster so the two never disagree; the preview element exposes `FindHierarchyIndexByName` for the drive.
5. Billboard tracks bind by `rootStableId` against `rig.billboardRoots[].Id.Value`; no tag, so no remap.
6. D4 "the clip set's own rig is listed first": `ClipSetAsset` has no rig field. The shared selection's Rig is listed first.
7. D3 remap: a `GenericDropdownMenu` of the rig's tagged targets (names from the registry), not a `VocabularyPicker`
   overlay. A remap onto a tag another track in the clip already carries merges into it via
   `ClipComponentModel.Merge*Tracks`, the timeline picker's rule; the write is `RetargetRemapEditing.RemapTrackTag`
   (one new static class, suffix Editing, no allowlist). "Remap in every clip…" calls `PickTagThenReplaceTrackTag`.
8. A92 is built, so the "Remap in every clip…" item is present.

### Wave T1-T6 (spec-lead)

- Commits `35839077` (T1 resolver, remap write, fixture) and `cce7b6c9` (T2-T6 elements, panel, docs). T6 wrote
  `retarget-tab.md` as its own page plus a "Seeing coverage" section in `sharing-clips.md`.
- **Gate at `cce7b6c9`:** compile clean, no Burst errors; 14 named tests run (2 `RetargetBindingResolverTests` + 12
  `PackagingConformanceTests`), 13 passed, the one failure the standing `Conformance_A`.
- **Revert-to-fail:** mutation `d128511d` made Dangling return Skipped; the gate (after one "Unity is compiling" refusal)
  failed `TagNotInRegistry_IsDangling` ("Expected: Dangling, But was: Skipped") beside the standing `Conformance_A`,
  12 passed. `git reset --hard HEAD~1`; resolver sha256 `10f4101f82eee808ffc3f8624aab92c9488a765716e6fbafb47ab584499743af`
  matches the committed blob. The opposite mutation (Skipped returning Dangling) was not run, so
  `TagOnRegistryButNotRig_IsSkipped_NotDangling` has not been seen failing; its Assert on `Skipped` would catch it by reading.
- Unverified: nothing in the UI has run (no drive); `PopupField<ClipAsset>` with a null first choice and the ListView row
  recycling are compile-checked only.

### For integration

**CHANGELOG `## [0.47.0]`:**

```
## [0.47.0]
### Added
- Retarget tab: pick a clip set, a clip and a rig to see every track as a row — Bound (the part it lands on),
  Skipped (the rig has no part wearing the tag) or Dangling (the tag is gone from the project's tag list).
- A row's remap menu rewrites that track's tag in this clip, with undo; a tag another track already uses merges the
  two. "Remap in every clip…" runs the project-wide replace.
- Roster strip: bound/total coverage for the clip on every rig in the project; click a rig to switch to it.
- Preview poses the clip on the picked rig, so skipped tracks show as holes.
- `RetargetBindingResolver.Resolve(ClipAsset, RigAsset, TargetTagRegistry)` for the same answer from code.
```

**Conformance_G allowlist:** none. New static classes are `RetargetBindingResolver` (Resolver) and
`RetargetRemapEditing` (Editing).

**Wiring (stage):**
- `ClipEditorTab.Retarget`, the member after A96's `Materials` (A96 takes the next number; Retarget the one after).
- UXML: `<uie:ToolbarToggle name="tab-retarget" text="Retarget" class="clip-editor__tab"/>` after the Materials toggle;
  `<ui:VisualElement name="retarget-pane" class="clip-editor__cover-pane clip-editor--hidden"/>` after the Materials pane.
- Show path, lazily as VAT Bake does: `retargetPanel = new RetargetPanel(); retargetPanel.Bind(selection); retargetPane.Add(retargetPanel);`
- Teardown beside `vatBakePanel.Dispose()`: `retargetPanel?.Dispose(); retargetPanel = null;` (the preview owns a
  `ClipPreviewController` and so a `PreviewRenderUtility`; the panel's Dispose disposes it).
- No transport target: the preview has its own play button.

**Drive surface (detached):** `new RetargetPanel()`, then `Bind(ClipSetAsset, ClipAsset, RigAsset, TargetTagRegistry,
IReadOnlyList<RigAsset>)`; read `ResolvedBindings` and `RosterEntries`; `RemapTrack(TrackBinding, uint newTagId)`; `SelectClip`;
`Refresh`; `Dispose`. Element names: `retarget-panel`, `retarget-clip-set-field`, `retarget-clip-field`, `retarget-rig-field`,
`retarget-track-table`, `retarget-track-list`, `retarget-remap-button`, `retarget-roster-strip`, `retarget-roster-chip`,
`retarget-preview`, `retarget-preview-status`, `retarget-preview-play-button`. Split divider pref suffix `Retarget.Tracks`.
Use scratch clips and a scratch rig with a `CreateInstance` registry, never the project registry.

**Vault traps (AnimationToolkit.md):**
- The tag rules live in `ClipValidation.ValidateTrackBindingInto`: a registry-unknown tag is Dangling even if a rig
  target still wears it, and a null registry can never produce Dangling. Anything that reports coverage must keep that order.
- Retagging a track onto a tag another track in the same clip carries must merge (`ClipComponentModel.Merge*Tracks`),
  never leave two tracks with one tag; `RetargetRemapEditing` and the timeline picker both follow this.
- The resolver judges bone tracks against `rig.sourcePrefab` by first name, not the preview's skeleton mirror, so table
  and roster agree without a live preview.

**HANDOFF draft:** A97 (0.47.0) adds the Retarget tab under `Editor/Retarget/`: `RetargetBindingResolver` turns a clip and
a rig into one `TrackBinding` per transform, sprite, bone and billboard track (Bound / Skipped / Dangling, the validator's
V35/V36/V38 order), `RetargetTrackTableElement` lists them with a per-row remap menu writing through
`RetargetRemapEditing.RemapTrackTag` (undo, merge on a duplicate tag), `RosterCoverageStripElement` shows bound/total for
every rig in `AssetReferenceIndex.Rigs`, and `RetargetPreviewElement` hosts its own `ClipPreviewController` posing the clip
on the picked rig. `RetargetPanel` binds the shared Clip Set and Rig and disposes the preview. Owner checkpoint open:
should a Skipped row offer "add this tag to the rig"?

### Close (stage, 2026-09-14)

- **Merge:** rebased onto trunk (head `4b0c352e`), pushed; worktree and branch removed cleanly. No allowlist names.
- **Integration `e7ae55f9`:** `ClipEditorTab` Materials 10, Retarget 11, Capture 12; toggles and cover panes after Health;
  `tabToggles` sized 13; `MaterialsPanel` built with the window (`Bind(selection)`, `Refresh()` on show), `RetargetPanel`
  and `CapturePanel` built lazily on first show with `Bind(selection)`; all three disposed in teardown; layout test lists,
  `index.md`, CHANGELOG 0.46.0–0.48.0, `package.json` and the conformance pin at 0.48.0. Not driven in the real window (the
  docked Clip Editor is the owner's).
- **Gates:** integration fixtures 24 of 25 (`MaterialContractValidationTests`, `RetargetBindingResolverTests`,
  `PngSequenceWriterTests`, `GifEncodingTests`, `ClipEditorLayoutTests`, `PackagingConformanceTests`; Conformance_A the only
  failure); full EditMode 857 run, 856 passed (Conformance_A only; 851 + the batch's six new tests); PlayMode 285 of 285.
- **T8 drive (scratch only, `Assets/A97Scratch/`):** `Walk.asset` copied as `A97ScratchWalk`, `NewRig.asset` copied twice
  (`A97ScratchRig`, `A97ScratchGuardRig`); the registry is an `EditorJsonUtility` copy of the project `TargetTagRegistry`
  (20 entries) on a `CreateInstance`; the clip set is an in-memory `ClipSetAsset` listing the scratch clip.
  - `RetargetBindingResolver.Resolve(Walk, full rig copy)`: **16 rows, all Bound** (Walk has 16 transform tracks).
  - The guard rig copy lost the tag of the target `UpperLeftLeg` wears (`0x46ADC5AC`). Detached `RetargetPanel.Bind(set, clip,
    guardRig, registryCopy, [fullRig, guardRig])`: 16 rows, **one Skipped**, `UpperLeftLeg` "no tagged part on A97ScratchGuardRig".
    Roster: `A97ScratchGuardRig 15/16`, `A97ScratchRig 16/16`, two `retarget-roster-chip` elements.
  - `RemapTrack(skipped row, 0x4C38DAB8)` (the `Eyes` tag, one no track in the clip used, so no merge): true; after `Refresh`
    16 of 16 Bound; `Undo.GetCurrentGroupName()` "Remap Track Tag". Saved with `SaveAssetIfDirty` on the scratch clip only: its
    YAML has `tagId: 1278794424` and no `tagId: 1185793452`, while the project's `Walk.asset` still has the old id.
  - `Undo.PerformUndo()` put `0x46ADC5AC` back on the track in memory (and removed `0x4C38DAB8`).
  - Dangling: with `UpperLeftLeg` removed from the in-memory registry copy (20 → 19), the full rig gives 15 Bound and
    **one Dangling** row "tag not in registry" (its name reads "Missing tag"). The project registry kept 20 entries.
  - Drift: detached, the track `ListView` realises no rows, so `retarget-remap-button` count was 0 and the preview status label
    was empty (no panel, no layout, no render tick); both need the real window.
  - Scratch deleted; registry sha256s unchanged; the project `Walk.asset` and `NewRig.asset` untouched.
- **Not seen by eye:** the drawn table and its ✓/●/✗ glyphs, the remap menu, the chips' bars, and the preview's missing part.
