# Amendment A94F — Health tab rework: a big Scan, a findings list and a detail panel

> **Status:** 📝 specced 2026-09-14 from the owner's A94 T14 answer. Takes `0.44.0`.
> **Roadmap:** [`AnimationPackage_Roadmap.md`](AnimationPackage_Roadmap.md), Phase 2 follow-up to A94.
> **Predecessors:** A94 (`0.41.0`), A82 (`CoverPaneSplitView`), A84 (reference index).
> **Executor:** one lead; `worker` subagents in **one wave of eight**, each ≤ 2 files; the stage does the drive and
> the close (no window wiring changes: the tab slot already exists).

---

## 0. Session prompt

You are running **A94F — Health tab rework** on the DOTS Animation Toolkit (head `0.42.0` or later). Spec:
`Assets/_Vault/Tasks/AnimationPackage/A94F_HealthTwoPanel_Spec.md`. Read it, the roadmap §3 protocol, then only
what §3 names. §2 is settled by the owner (2026-09-14). T0 and T1 are the lead's; one wave (T2–T9); one gate;
T10–T12 belong to the stage orchestrator.

---

## 1. Goal

The owner's A94 checkpoint answer: Scan should be a much bigger, more obvious button; the tab is cramped and hard to
read; Delete is fine where it makes sense, behind a warning; the list should share the tab with a panel that shows the
detail and the ways to deal with the issue.

```
┌ Health ───────────────────────────────────────────────────────────────────────────────────────────┐
│ [  ⟳  Scan project  ]   last scan 19:42 · 3 findings     (● 2 Errors) (● 0 Warnings) (● 1 Note)  🔍 │
│ ┌ Findings ────────────────────────────┐ ┌ H06 · Error ─────────────────────────────────────────┐ │
│ │ ● H06  VAT bake is stale             │ │ VAT textures for VatSampleTentacleClips are unbaked   │ │
│ │        VatSampleTentacleClips        │ │                                                       │ │
│ │ ● H02  Clip set lists missing clips  │ │ What's wrong / why it matters at run time (wrapped)   │ │
│ │        NewClipSet                    │ │                                                       │ │
│ │ ○ H05  Rig used by no profile        │ │ Affected: [VatSampleTentacleClips ↗] [no baked rig]   │ │
│ │        VatSampleTentacleRig          │ │                                                       │ │
│ │                                      │ │ How to fix                                            │ │
│ │                                      │ │ [ Rebake — opens VAT Bake with this set and rig    ]  │ │
│ │                                      │ │ [ Locate — selects and pings the clip set          ]  │ │
│ └──────────────────────────────────────┘ └───────────────────────────────────────────────────────┘ │
└────────────────────────────────────────────────────────────────────────────────────────────────────┘
```

---

## 2. Decisions (owner, 2026-09-14 — do not re-ask)

- **H-D1 — Scan is the primary control.** A full-height button (at least 32 px tall, 150 px wide), icon `d_Refresh`
  plus the words "Scan project", accent background (`ToolkitPalette.Accent`), first in the toolbar. Beside it, a
  status label: `last scan HH:mm · N findings` (or "not scanned yet").
- **H-D2 — Counts and filters merge into three chips.** Each severity is one `ToolbarToggle` reading `● 2 Errors`
  (dot in the severity colour); toggling filters. The search field sits right-aligned.
- **H-D3 — Two panels.** `CoverPaneSplitView("Health.Findings", 0, 360f, Horizontal)`: the left is
  `HealthFindingListElement`, the right is the new `HealthFindingDetailElement`.
  - **List rows are two-line boxed rows** (the catalog-column look): severity dot, code, short title; second line the
    asset name. **No buttons in rows.** Selection drives the detail panel; the first finding is selected after a
    scan; the selection survives a rescan when the same code + target still exists, otherwise the next row.
  - **The detail panel** shows: a header (code, severity word in its colour), the title, a wrapped explanation
    (what's wrong, why it matters at run time), **Affected** (every related asset as a ping button, with an open
    button where the window can open it), and **How to fix**: one full-width button per action with its label and a
    one-line description beneath.
- **H-D4 — Findings carry several actions.** `HealthFinding` loses `fix` and `fixLabel` and gains
  `string title`, `string detail`, `List<UnityEngine.Object> relatedAssets` and `List<HealthFindingAction> actions`.
  `HealthFindingAction` (new, `Editor/Health/HealthFindingAction.cs`) is
  `{ string label; string description; bool isDestructive; Func<string> buildConfirmation; Action run; }`. No
  migration: nothing outside `Editor/Health/` reads the old fields (verified 2026-09-14).
- **H-D5 — Delete, where it makes sense, always behind a confirmation.** A destructive action draws in
  `ToolkitPalette.Error`; pressing it shows `EditorUtility.DisplayDialog` with `buildConfirmation()`. That text names
  the asset path and quotes its remaining usage from the reference index. Cancel does nothing.
  - **H01 clip in no set → "Delete clip…"**: new `ClipAssetUtility.TrashClip(ClipAsset clip)`
    (`AssetDatabase.MoveAssetToTrash` only; never `SaveAssets`, unlike `DeleteClipFromSet`). The confirmation lists
    `AssetReferenceIndex.ReferencesToClip` (cutscenes and profiles can still name a clip that is in no set).
  - **H05 rig used by no profile → "Delete rig…"**: existing `RigAssetUtility.DeleteRig`; the confirmation lists
    `ReferencesToRig`.
  - **H06 stale VAT textures → "Delete VAT textures…"**, only when a `VatTextureSetAsset` exists (a never-baked set has
    nothing to delete): new `VatTextureSetAssetUtility.TrashTextureSet(VatTextureSetAsset set)` in
    `Editor/ClipUtilities/`. It trashes the set and every texture its `parts` reference **that no other VAT set
    references**, decided by the pure `VatTextureOwnershipResolver.FindTexturesSafeToTrash(VatTextureSetAsset target,
    IReadOnlyList<VatTextureSetAsset> allSets)`. The set's clip set is then unbaked, so H06 stays, now offering Rebake
    only.
- **H-D6 — The existing one-click fixes become actions.** H02 "Remove missing", H06 "Rebake" and "Locate", H09 "Save",
  unchanged in behaviour.
- **H-D7 — Wording.** Every rule writes a short `title` and a `detail` paragraph (§4 gives the gist). An unbaked set no
  longer reads "on rig 'no rig'"; it reads "no baked rig yet".
- **Still open (not in this amendment):** the "Health (n)" tab-strip count, which the owner has not answered.

---

## 3. Read first

- `Editor/Health/HealthPanel.cs` (285 lines; the toolbar is 58–115, filters 162–250, debounce 255–285).
- `Editor/Health/HealthFindingListElement.cs` (174 lines; rows 60–160).
- `Editor/Health/HealthFinding.cs` (45 lines, whole).
- The fix sites: `HealthRules/ClipMembershipValidation.cs` ~74, `HealthRules/StableIdValidation.cs` 40–56,
  `HealthRules/VatFreshnessValidation.cs` 30–60.
- `Editor/ClipUtilities/ClipAssetUtility.cs` 215–268; `Editor/ClipUtilities/RigAssetUtility.cs` 210–240.
- `Authoring/Assets/VatTextureSetAsset.cs` lines 15–60 and the `VatPartTextures` class;
  `Editor/VatBaking/VatTextureSetBuilder.cs` ~140–175 (how the part textures are written).
- An existing boxed two-line row: `ToolkitCatalogColumn`'s row construction (grep `MakeItem` in `Editor/ClipEditor/Shared/`).

---

## 4. Design notes — title and detail per rule (workers write the final strings)

| Code | Title | Detail gist | Actions |
|---|---|---|---|
| H01 | Clip is in no clip set | No set registers it, so no actor can play it. | Locate; **Delete clip…** |
| H02 | Clip set lists missing clips | The set has empty slots; baking skips them and indices shift. | Remove missing; Locate |
| H03 | Profile names an unregistered animation | Play-by-name fails silently for that name. | Locate profile |
| H04 | Profile rig differs from its clip set's baked rig | VAT textures were baked for another rig; the actor deforms wrongly. | Locate profile; Locate clip set |
| H05 | Rig is used by no profile | Probably leftover. | Locate; **Delete rig…** |
| H06 | VAT bake is stale / not baked | Actors play old motion (or none) with no run-time error. | Rebake; Locate; **Delete VAT textures…** (stale only) |
| H07 | Track tag is not registered | The track binds to nothing on every rig. | Locate clip |
| H08 | Event key is not registered | Markers fire a key no system names. | Locate clip |
| H09 | Stable id not saved | The id re-mints on next load and breaks references. | Save |
| H10 | Clip poses nothing on this rig | None of its tags exist on the set's rig. | Locate clip; Locate rig |

Element names: `health-scan-button`, `health-scan-status`, `health-filter-errors|warnings|notes`,
`health-finding-row`, `health-finding-detail`, `health-finding-action`.

---

## 5. Tasks

- [ ] **T0 — Grounding (lead).** Grep every name in §2–§3; confirm nothing outside `Editor/Health/` reads `fix` or
  `fixLabel`; confirm how `VatTextureSetBuilder` stores part textures (sub-assets of the set, or separate files) and
  write the answer into H-D5's resolver brief. A sub-asset texture is trashed with its set, so the resolver still
  matters only for separate files. Confirm `DeleteRig` never calls `SaveAssets`. Log drift in §7.
- [ ] **T1 — Shared types (lead).** `HealthFindingAction`; the `HealthFinding` field change (remove `fix`, `fixLabel`);
  committed stubs for `HealthFindingDetailElement` (`SetFinding(HealthFinding)`), `ClipAssetUtility.TrashClip`,
  `VatTextureSetAssetUtility.TrashTextureSet`, `VatTextureOwnershipResolver.FindTexturesSafeToTrash`. Temporarily
  delete the three rules' `fix` assignments and the row's fix button so T1 compiles (the wave rewrites them). Gate
  `HealthScanTests`, `HealthRulesTests`, `PackagingConformanceTests`. Commit `A94F-T1`.
- [ ] **T2 — Detail element [parallel-safe]** — Files: new `Editor/Health/HealthFindingDetailElement.cs`. H-D3 detail
  panel and H-D5 confirmation flow (the element runs `buildConfirmation`; a null builder runs `run` directly).
- [ ] **T3 — List element [parallel-safe]** — Files: `Editor/Health/HealthFindingListElement.cs`. Two-line rows, no
  buttons, `public event Action<HealthFinding> FindingSelected`, `SelectFinding(HealthFinding)`.
- [ ] **T4 — Panel [parallel-safe]** — Files: `Editor/Health/HealthPanel.cs`. H-D1, H-D2, H-D3 split, selection
  survival across rescans, rescan after an action runs.
- [ ] **T5 — Rules H01, H02, H05 [parallel-safe]** — Files: `HealthRules/ClipMembershipValidation.cs`,
  `HealthRules/RigUsageValidation.cs`. Titles, details, related assets, actions (including both deletes).
- [ ] **T6 — Rules H03, H04, H06 [parallel-safe]** — Files: `HealthRules/ProfileHealthValidation.cs`,
  `HealthRules/VatFreshnessValidation.cs`. H-D7 wording; H06's delete only for an existing set.
- [ ] **T7 — Rules H07, H08, H10, H09 [parallel-safe]** — Files: `HealthRules/TagAndKeyValidation.cs`,
  `HealthRules/StableIdValidation.cs`.
- [ ] **T8 — Trash utilities + ownership fixture [parallel-safe]** — Files: new
  `Editor/ClipUtilities/VatTextureSetAssetUtility.cs` (holding `TrashTextureSet` and the static class
  `VatTextureOwnershipResolver` in its own new file `Editor/ClipUtilities/VatTextureOwnershipResolver.cs`), new
  `Tests/EditMode/VatTextureOwnershipResolverTests.cs`:
  - `SharedTexture_IsNotSafeToTrash`: two in-memory sets sharing one texture and each with one of its own → only the
    target's own texture is returned.
  - Revert-to-fail: return every texture of the target.
  - `ClipAssetUtility.TrashClip` goes in this worker's brief as a third, tiny edit only if T0 finds the file under
    300 lines of change context; otherwise the lead adds it in T1 as a real body.
- [ ] **T9 — Docs [parallel-safe]** — Files: `Documentation~/health-tab.md` (the new layout, the actions per code,
  what each Delete removes and what it leaves).
- **Gate the wave.** `HealthScanTests`, `HealthRulesTests`, `VatTextureOwnershipResolverTests`,
  `PackagingConformanceTests` (all namespace-qualified). Revert-to-fail. Commit `A94F-T2..T9`. For-integration block
  (CHANGELOG `## [0.44.0]`, traps, HANDOFF draft; no wiring change expected). `worktree.py status a94f ready`.
- [ ] **T10 — Drive (stage).** Full suites. Detached `HealthPanel` scan: three findings, first selected, detail shows
  H06's title, detail and actions. On scratch copies only: an orphan scratch clip shows H01 with "Delete clip…";
  run `TrashClip` beneath the dialog and confirm the file left and the rescan dropped H01. Never press a real Delete.
- [ ] **T11 — Vault + HANDOFF + close (stage).** CHANGELOG, `package.json` and conformance pin `0.44.0`.
- [ ] **T12 — ⏸ owner checkpoint.** "Health: press Scan project. Pick each finding: the right panel explains it and
  lists the fixes; deletes ask first and name what uses the asset. Readable now? And the open question: should the tab
  read Health (1) while a VAT bake is stale?"

---

## 6. Out of scope

- The tab-strip count (unanswered).
- New rules.
- Undo for trashed assets (the OS trash is the undo, as everywhere else in the package).

## 7. Build log

_(empty)_
