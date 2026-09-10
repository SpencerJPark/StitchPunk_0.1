# Amendment A97 — Retarget tab: clip × rig binding table and roster coverage

> **Status:** 📝 specced 2026-09-10, not built. Takes `0.44.0`.
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

- [ ] **T0 — Baseline (orchestrator).** Gate; totals. Read `ValidateBind`'s T2/T3 conditions and
  paste them into T1's brief so the resolver matches the validator exactly.
- [ ] **T1 — Resolver + fixture [parallel-safe]** — Files: new `RetargetBindingResolver.cs`, new
  `Tests/EditMode/RetargetBindingResolverTests.cs` (`TagOnRegistryButNotRig_IsSkipped_NotDangling`,
  `TagNotInRegistry_IsDangling`). Revert-to-fail: collapse the two states.
- [ ] **T2 — Track table [parallel-safe]** — Files: new `RetargetTrackTableElement.cs`.
- [ ] **T3 — Roster strip [parallel-safe]** — Files: new `RosterCoverageStripElement.cs`.
- [ ] **T4 — Preview element [parallel-safe]** — Files: new `RetargetPreviewElement.cs`. Read
  `VatBakePanel.cs`'s preview hosting range.
- [ ] **T5 — Panel [parallel-safe]** — Files: new `RetargetPanel.cs`.
- [ ] **T6 — Docs [parallel-safe]** — Files: `Documentation~/sharing-clips.md` (a "Seeing coverage"
  section with the three states), new `Documentation~/retarget-tab.md` (short; may fold into the
  former — worker's call, say which).
- **Gate the wave.** `RetargetBindingResolverTests`. Commit `A97-T1..T6`.
- [ ] **T7 — Window wiring (orchestrator).** Tab; `index.md`; `CHANGELOG.md` `## [0.44.0]`;
  `package.json`; `Conformance_G`. Gate; `ClipEditorLayoutTests`.
- [ ] **T8 — Drive.** Full suites. Walk on MaleCitizen → all Bound; on a scratch rig missing one
  tag → one Skipped, preview shows the hole; remap the row → Bound, clip on disk carries the new
  `tagId`; Ctrl+Z reverts. Roster shows both rigs. Capture.
- [ ] **T9 — Vault + HANDOFF.**
- [ ] **T10 — Close.** Roadmap checkbox.
- [ ] **T11 — ⏸ owner checkpoint.** Message: "Retarget tab: pick a clip, switch rigs with the
  roster chips, watch rows go ✓/●/✗ and the preview lose parts. ⚠ Should a ● row offer 'add this
  tag to the rig' (edits the rig), or stay clip-side only?"

---

## 6. Deliberately out of scope

- Editing rigs from this tab (D6 ⚠ pending).
- Automatic tag guessing by name similarity.
- Bone-name remapping for VAT clips (names come from the DCC tool; out of the package's hands).

## 7. Build log

_(empty)_
