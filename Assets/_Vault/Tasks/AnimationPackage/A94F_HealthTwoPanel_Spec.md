# Amendment A94F — Health tab rework: a big Scan, a findings list and a detail panel

> **Status:** ✅ built 2026-09-14 as `0.44.0` in the A93F–A95F parallel worktree batch (merged `19ec21f7`, integrated `f67b47e3`); ⏸ T12 owner checkpoint open. Specced the same day from the owner's A94 T14 answer and widened with
> the "Health (n)" count and the badge removal (H-D8 to H-D10).
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
- **H-D8 — The tab reads "Health (n)"** (owner: yes). `n` is the number of **Error** findings, including the
  pinned H06; at zero the tab reads "Health". The tab text is drawn in `ToolkitPalette.Error` while `n > 0`. So the
  count exists before the tab is first opened, the window builds and binds `HealthPanel` at window creation (not
  lazily). That costs one scan at open (~130 ms on this project, A94 §7). `HealthPanel` gains `public int ErrorCount`
  and raises the existing `FindingsChanged` after every scan.
- **H-D9 — The Clip Editor's error badge goes** (owner: "remove the error part next to it since Health will now
  handle all that info"). That means the `ValidationBadgeElement` in the toolbar's `validation-badge-slot` and its
  message panel on the viewport overlay. The Actor Profiles panel's own badge (profile rules P1–P7, inside that
  panel) stays.
  - **Nothing it caught may disappear:** the badge ran `ClipValidation.ValidateBind(rig, { clipSet }, …)` and
    `SharedClipBindingUtility.ValidateSharedClipBinding(clip)`, so Health gains H-D10's two rules.
  - **Edits must still re-validate:** the badge refreshed on every committed clip edit, and `AssetReferenceIndex.Dirtied`
    only fires on asset changes. So `HealthPanel` gains `public void RequestRescan()` (the existing 500 ms debounce).
    The window calls it at the four places it called `validationBadge.Refresh` (`ClipEditorWindow.cs` ~875, ~1613,
    ~3100, ~3163 at `f51cad62`).
- **H-D10 — Two rules absorb the badge.**
  - **H11 "Clip set doesn't bind cleanly to its profile's rig":** for each `ActorProfileAsset`, run
    `ClipValidation.ValidateBind(profile rig, profile clip sets, tagRegistry: context.targetTags, eventKeyRegistry:
    context.eventKeys)`. Emit one finding per `ValidationMessage`: severity mapped one to one, the message's code
    (for example `V03`) shown in the title, its text as the detail, `assetContext` as the target. It **skips** `V08`
    (H06 owns stale bakes) and every `ValidationCode` that H07 or H08 already reports (T0 lists them), so nothing
    shows twice.
  - **H12 "Shared clip binding problem":** `SharedClipBindingUtility.ValidateSharedClipBinding(clip)` for every clip,
    one finding per message.
  - A clip set no profile uses has no rig to validate against; H01 and H05 already speak for orphans.

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
| H11 | `<V-code>`: clip set doesn't bind to its profile's rig | The validator's own message, verbatim. | Locate clip; Locate profile |
| H12 | Shared clip binding problem | The shared-binding validator's message, verbatim. | Locate clip |

Element names: `health-scan-button`, `health-scan-status`, `health-filter-errors|warnings|notes`,
`health-finding-row`, `health-finding-detail`, `health-finding-action`.

---

## 5. Tasks

- [x] **T0 — Grounding (lead).** Grep every name in §2–§3; confirm nothing outside `Editor/Health/` reads `fix` or
  `fixLabel`; confirm how `VatTextureSetBuilder` stores part textures (sub-assets of the set, or separate files) and
  write the answer into H-D5's resolver brief. A sub-asset texture is trashed with its set, so the resolver still
  matters only for separate files. Confirm `DeleteRig` never calls `SaveAssets`. Log drift in §7.
- [x] **T1 — Shared types (lead).** `HealthFindingAction`; the `HealthFinding` field change (remove `fix`, `fixLabel`);
  `HealthPanel.ErrorCount` and `HealthPanel.RequestRescan()` as stubs; the `BindValidation` stub and its call in
  `HealthScan`; committed stubs for `HealthFindingDetailElement` (`SetFinding(HealthFinding)`), `ClipAssetUtility.TrashClip`,
  `VatTextureSetAssetUtility.TrashTextureSet`, `VatTextureOwnershipResolver.FindTexturesSafeToTrash`. Temporarily
  delete the three rules' `fix` assignments and the row's fix button so T1 compiles (the wave rewrites them). Gate
  `HealthScanTests`, `HealthRulesTests`, `PackagingConformanceTests`. Commit `A94F-T1`.
- [x] **T2 — Detail element [parallel-safe]** — Files: new `Editor/Health/HealthFindingDetailElement.cs`. H-D3 detail
  panel and H-D5 confirmation flow (the element runs `buildConfirmation`; a null builder runs `run` directly).
- [x] **T3 — List element [parallel-safe]** — Files: `Editor/Health/HealthFindingListElement.cs`. Two-line rows, no
  buttons, `public event Action<HealthFinding> FindingSelected`, `SelectFinding(HealthFinding)`.
- [x] **T4 — Panel [parallel-safe]** — Files: `Editor/Health/HealthPanel.cs`. H-D1, H-D2, H-D3 split, selection
  survival across rescans, rescan after an action runs.
- [x] **T5 — Rules H01, H02, H05 [parallel-safe]** — Files: `HealthRules/ClipMembershipValidation.cs`,
  `HealthRules/RigUsageValidation.cs`. Titles, details, related assets, actions (including both deletes).
- [x] **T6 — Rules H03, H04, H06 [parallel-safe]** — Files: `HealthRules/ProfileHealthValidation.cs`,
  `HealthRules/VatFreshnessValidation.cs`. H-D7 wording; H06's delete only for an existing set.
- [x] **T7 — Rules H07, H08, H10, H09 [parallel-safe]** — Files: `HealthRules/TagAndKeyValidation.cs`,
  `HealthRules/StableIdValidation.cs`.
- [x] **T8 — Trash utilities + ownership fixture [parallel-safe]** — Files: new
  `Editor/ClipUtilities/VatTextureSetAssetUtility.cs` (holding `TrashTextureSet` and the static class
  `VatTextureOwnershipResolver` in its own new file `Editor/ClipUtilities/VatTextureOwnershipResolver.cs`), new
  `Tests/EditMode/VatTextureOwnershipResolverTests.cs`:
  - `SharedTexture_IsNotSafeToTrash`: two in-memory sets sharing one texture and each with one of its own → only the
    target's own texture is returned.
  - Revert-to-fail: return every texture of the target.
  - `ClipAssetUtility.TrashClip` goes in this worker's brief as a third, tiny edit only if T0 finds the file under
    300 lines of change context; otherwise the lead adds it in T1 as a real body.
- [x] **T9 — Docs [parallel-safe]** — Files: `Documentation~/health-tab.md` (the new layout, the actions per code,
  what each Delete removes and what it leaves, the tab count, H11 and H12, and a line that the Clip Editor's error
  badge is gone). The badge mentions in `Documentation~/clip-editor.md` and `index.md` are the stage's (T9b).
- [x] **T9a — Bind rules H11, H12 + fixture [parallel-safe]** — Files: new `Editor/Health/HealthRules/BindValidation.cs`,
  new `Tests/EditMode/BindValidationTests.cs`:
  - `H11_SkipsStaleVatBakeAndCodesOtherRulesReport`: an in-memory profile, rig and clip set that produce a `V08`, a
    tag message H07 already reports, and one other binding message. Only the other message becomes an H11. (T0 names
    a message code that is cheap to provoke in memory.)
  - Revert-to-fail: drop the skip list.
  - `HealthScan` calls `BindValidation` after the existing rules; the one-line call goes in the T1 stub so this
    worker touches no other file.
- [x] **T9b — Window: tab count and badge removal (stage, at integration).** In `ClipEditorWindow.cs`:
  - delete the `validationBadge` field, its creation (~1023–1027), `AttachMessagePanel` (~2114) and the four
    `Refresh` calls, replacing each with `healthPanel?.RequestRescan()`;
  - build and bind `healthPanel` at window creation, not in `ShowHealthTab`;
  - subscribe `FindingsChanged` to set `tab-health`'s text and colour (H-D8), unsubscribed in teardown.

  Also:
  - delete `validation-badge-slot` from the UXML and from `ClipEditorLayoutTests.RequiredElementNames`;
  - fix the badge mentions in `Documentation~/clip-editor.md` and `index.md` (grep "validation badge");
  - keep `ValidationBadgeElement` itself (Actor Profiles uses it).

  Gate `ClipEditorLayoutTests`.
- **Gate the wave.** `HealthScanTests`, `HealthRulesTests`, `VatTextureOwnershipResolverTests`, `BindValidationTests`,
  `PackagingConformanceTests` (all namespace-qualified). Revert-to-fail. Commit `A94F-T2..T9`. For-integration block
  (CHANGELOG `## [0.44.0]`, traps, HANDOFF draft; no wiring change expected). `worktree.py status a94f ready`.
- [x] **T10 — Drive (stage).** Full suites. Detached `HealthPanel` scan: three findings, first selected, detail shows
  H06's title, detail and actions. On scratch copies only: an orphan scratch clip shows H01 with "Delete clip…";
  run `TrashClip` beneath the dialog and confirm the file left and the rescan dropped H01. Never press a real Delete.
- [x] **T11 — Vault + HANDOFF + close (stage).** CHANGELOG, `package.json` and conformance pin `0.44.0`.
- [ ] **T12 — ⏸ owner checkpoint.** "The tab reads Health (2) in red before you open it, and the error badge beside
  the tabs is gone. Press Scan project and pick each finding: the right panel explains it and lists the fixes, and
  deletes ask first and name what uses the asset. Break a clip's binding in the Clip Editor: within a second Health
  shows it as H11. Readable now?"

---

## 6. Out of scope

- New rules beyond H11 and H12.
- The Actor Profiles panel's own badge (P1–P7) — it stays.
- Undo for trashed assets (the OS trash is the undo, as everywhere else in the package).

## 7. Build log

- **2026-09-14 — Phase 0 (stage, parallel batch A93F-A95F).** Head `eb60b150`, package `0.42.0`, CHANGELOG top section `## [0.42.0]`. Baseline: compile clean; EditMode 850 (Conformance_A the one standing failure), PlayMode 285. Registry sha256: AnimEventKeyRegistry `3bdb420d…d14701`, TargetTagRegistry `dbec3d5f…eb4f`. Preflight: broker alive, hooks installed, stage blockers only the owner's five uncommitted files. Lead opus, worker sonnet.
- **2026-09-14 — T0 grounding (lead, worktree `spec/a94f`).** Names in §2–§3 all exist. Nothing outside `Editor/Health/` read `fix`/`fixLabel` (grep clean). `DeleteRig` never calls `SaveAssets` (RigAssetUtility 214–234). Drift:
  - **D1 — part files are separate assets, not sub-assets.** `VatTextureSetBuilder` calls `CreateAsset` per part at `<set>Vat<Part>Bone|Position|Normal.asset` and `<…>RuntimeMesh.asset`, so the resolver matters. It also covers `runtimeMesh` (a leftover mesh would be junk too); name kept as `FindTexturesSafeToTrash`, returns `List<UnityEngine.Object>`.
  - **D2 — the H11 skip list is `V08`, `V36`, `V41`.** H07 reports `V36` only (via `ValidateClip`); H08 is the `V41` condition (unregistered event key). H10 has no V-code of its own (`V35` is per track and stays in H11).
  - **D3 — `V08` can never fire in H11.** `ValidateBind` emits `V08` only when `vatSourceHashRecomputed` is true, and H11 passes the spec's arguments only. The skip is defensive; the fixture asserts `IsCodeReportedByAnotherRule(V08)` directly and provokes `V36` (a transform track bound to a tag missing from an empty in-memory `TargetTagRegistry`) plus `V01` (clip duration 0) as the "other" message.
  - **D4 — H12 uses the pure overload** `ValidateSharedClipBinding(clip, context.clipSets)`; the one-argument overload scans the project and would break fixture purity.
  - **D5 — `HealthFinding.message` stays.** It is the specific one-line sentence (search matches it, the detail panel shows it under the title); `title` and `detail` are added beside it.
  - **D6 — `ClipAssetUtility.TrashClip` written by the lead in T1** (file is 317 lines, over the worker read guard). `VatTextureSetAssetUtility.TrashTextureSet` also written by the lead (keeps T8 at two files); per-path `MoveAssetToTrash`, no `SaveAssets`.
  - **D7 — shared helpers on `HealthFindingAction`:** `static Locate(label, description, asset)` and `static BuildDeleteConfirmation(assetPath, List<AssetReference>)` (lists up to 12 owners), so the three rule workers phrase Locate and Delete the same way.
  - **D8 — H11 dedupes across profiles:** an identical (code, assetContext, text) message from a second profile sharing the same clip set and rig is emitted once.
- **2026-09-14 — T1 (lead).** Commit `4e6af49a`. Gate `HealthScanTests`, `HealthRulesTests`, `PackagingConformanceTests`: compile clean, 15 passed / 1 failed of 16, the failure the standing Conformance_A.
- **2026-09-14 — Wave T2–T9a (nine sonnet workers, disjoint files).** T2 detail element, T3 list, T4 panel, T5 H01/H02/H05, T6 H03/H04/H06, T7 H07–H10, T8 resolver + `VatTextureOwnershipResolverTests`, T9 `Documentation~/health-tab.md`, T9a `BindValidation` + `BindValidationTests`. Lead fix after T7: removed a `using System;` that made the bare `Object` in StableIdValidation ambiguous with `UnityEngine.Object` (CS0104). Commit `03e94592`. Wave gate (`HealthScanTests`, `HealthRulesTests`, `VatTextureOwnershipResolverTests`, `BindValidationTests`, `PackagingConformanceTests`): compile clean, 17 passed / 1 failed of 18, the failure the standing Conformance_A. (A first attempt exited 3 on a transient broker heartbeat gap; two later "Unity is compiling" refusals were retried.)
- **2026-09-14 — Revert-to-fail (lead).** Mutation commit `0f389818` alone: `IsCodeReportedByAnotherRule` returns false, and the resolver's other-set reference check is `false && (…)`. Gate on the two new fixtures: 0 passed / 2 failed (`H11_SkipsStaleVatBakeAndCodesOtherRulesReport`: V36 title present; `SharedTexture_IsNotSafeToTrash`: 2 returned, expected 1). `git reset --hard HEAD~1` back to `03e94592`; sha256 of both files matches the pre-mutation values (`9a1e5a73…db80e5`, `4d7a7df2…397e47`).
- **Unverified (stage T10):** the panel has never been drawn; the rich-text coloured dots in the `ToolbarToggle` chips, the list's selection survival and the detail panel layout need the drive. No real delete was run.

### For integration

**CHANGELOG `## [0.44.0]`**
```
### Changed
- Health tab reworked: a large "Scan project" button with last-scan status, three severity chips that count and filter, and a findings list beside a detail panel (title, explanation, affected assets with ping and Open, and one button per fix).
- Findings carry several actions. H02 Remove missing, H06 Rebake and H09 Save are unchanged in behaviour; every finding gains Locate actions.
- The Clip Editor tab reads "Health (n)" in red while there are Error findings; the panel is built at window creation so the count is there before the tab is opened.
- H06 no longer reads "on rig 'no rig'" for a never-baked set.
### Added
- Delete actions behind a confirmation that names the asset path and what still references it: H01 Delete clip (ClipAssetUtility.TrashClip), H05 Delete rig, H06 Delete VAT textures (VatTextureSetAssetUtility.TrashTextureSet; part textures and runtime meshes no other VAT set uses go with it, decided by VatTextureOwnershipResolver).
- H11: each actor profile's clip sets bind-validated against its rig (V08, V36 and V41 skipped, reported by H06, H07 and H08). H12: shared clips still bound by target id.
- HealthPanel.ErrorCount and HealthPanel.RequestRescan().
### Removed
- The Clip Editor toolbar's validation badge and its message panel; Health reports everything it caught. The Actor Profiles badge stays.
- HealthFinding.fix and fixLabel (replaced by actions).
```

**Conformance_G allowlist:** none. New static classes `BindValidation` (Validation), `VatTextureOwnershipResolver` (Resolver), `VatTextureSetAssetUtility` (Utility, in `Editor/ClipUtilities/`). `HealthFindingAction` is not static.

**Wiring (T9b; no enum, UXML toggle or pane changes — the Health slot exists):**
- Members as built on `HealthPanel`: `public int ErrorCount { get; }` (every `HealthSeverity.Error` finding, pinned H06 included); `public event Action FindingsChanged` (raised at the end of every `Scan()`, including the one `Bind()` runs); `public void RequestRescan()` (the existing 500 ms debounce). Unchanged: `new HealthPanel()`, `Bind()`, `Dispose()`, `RebakeRequested`, `LatestFindings`, `StaleVatBakeCount`.
- `HealthPanel` needs **no change** to be built eagerly: move `new HealthPanel()`, `RebakeRequested +=`, `healthPane.Add`, `Bind()` from `ShowHealthTab` (~1536–1541) into window creation; subscribe `FindingsChanged` **before** `Bind()` so the first scan sets the tab text; unsubscribe beside the existing `Dispose` (~758–762).
- Tab text: `errorCount > 0 ? "Health (" + errorCount + ")" : "Health"`, colour `ToolkitPalette.Error` while `> 0`, else clear the inline colour (`StyleKeyword.Null`).
- Replace the four `validationBadge.Refresh` calls with `healthPanel?.RequestRescan()`.

**Vault-note traps (AnimationToolkit.md):**
- Health rules stay pure: `AssetReferenceIndex` and `AssetDatabase.GetAssetPath` are called only inside an action's `buildConfirmation`/`run` lambdas, never while a rule evaluates, so fixtures run on `CreateInstance` assets.
- `ValidateBind` emits V08 only when a recomputed hash is passed; H11 never passes one, H06 owns staleness.
- `SharedClipBindingUtility.ValidateSharedClipBinding(clip)` (one argument) scans the project; Health uses the `(clip, clipSets)` overload.
- VAT bake output is separate `.asset` files per part (Bone/Position/Normal/RuntimeMesh), not sub-assets; trashing the set alone leaves them behind.
- Trash utilities never `SaveAssets` (it would flush the owner's unrelated unsaved edits); `DeleteClipFromSet` still does.
- A detail panel action runs, then the panel rescans immediately; selection survives when the same code + target still exists, otherwise the row at the old index.

**HANDOFF draft (0.44.0):** A94F reworked the Health tab into a readable two-panel layout: a big Scan project button with last-scan status, severity chips that count and filter, a findings list of two-line rows with no buttons, and a detail panel that explains the selected finding, lists every affected asset and offers one button per fix. Findings now carry several actions; H01, H05 and H06 gain Delete behind a confirmation that names the path and its remaining references (VAT deletes take only part files no other set uses). Two new rules, H11 (profile bind validation, V08/V36/V41 skipped) and H12 (shared clip binding), absorb the Clip Editor's toolbar badge, which is gone; the tab reads "Health (n)" in red from window open, and committed clip edits trigger a debounced rescan. Real deletes were exercised only on the stage's scratch copies.

### Close (stage, 2026-09-14)

- **Lead:** stopped at 69 turns waiting on its wave gate (the gate had exited 3 on a transient broker heartbeat gap).
  The stage's earlier "retry" message resumed it; it gated the wave (17/18), ran revert-to-fail and marked ready.
  A gate the stage started by hand in the meantime was refused ("commit before gating"), so nothing collided.
- **Merge:** rebased onto trunk as `997725d6`, `8b3512f6`, `19ec21f7`; pushed; worktree and branch removed cleanly.
- **T9b (integration `f67b47e3`):** the `validationBadge` field, its creation and `AttachMessagePanel` are gone.
  Drift: there were **eight** `validationBadge.Refresh` sites, not four. Besides ~875, ~1613, ~3100 and ~3163 there
  were `OnPaneRequestedRebuild`, the debounced preview refresh, the event-edit commit and `FinishRigTagEdit`; all
  eight now call `healthPanel?.RequestRescan()`. `BuildHealthPanel` runs right after `BindTabs` in `BindToolbar`: it
  subscribes `FindingsChanged` before `Bind`, so the first scan labels the tab. `RefreshHealthTabLabel` writes
  "Health (n)" and `ToolkitPalette.Error`, or "Health" and `StyleKeyword.Null`. Teardown unsubscribes. The UXML
  slot, the USS `.clip-editor__badge-slot` block and the `ClipEditorLayoutTests` name are removed. Badge mentions are
  fixed in `clip-editor.md`, `index.md` (which also reads H01–H12 now), `rigged-characters.md` and
  `sharing-clips.md`. `ValidationBadgeElement` stays for Actor Profiles.
- **Gates:** as A93F's close (fixtures 32/33, EditMode 850/851, PlayMode 285/285, Conformance_A the only failure).
- **T10 drive:** detached `HealthPanel.Bind` scan in 22 ms, `ErrorCount` 2:
  - H06 Error "VAT bake is not baked yet", `VatSampleTentacleClips`, actions Rebake and Locate;
  - H02 Error "Clip set lists missing clips", `NewClipSet`, Remove missing and Locate;
  - three H11 Warnings "V38: clip set doesn't bind to its profile's rig" on `NewClip 1` against rig `NewRig` (tracks
    0–2 target ids the rig does not declare; `NewClip 1.asset` carries the owner's uncommitted edits);
  - H05 Note "Rig is used by no profile", `VatSampleTentacleRig`, Locate and "Delete rig…" (destructive).

  Drift: the spec's "three findings" is six now that H11 exists. H06 is selected first; the detail panel read H06,
  Error, the title, the explanation, the runtime consequence, Affected (`▸ VatSampleTentacleClips`) and How to fix
  (Rebake, Locate, each with its one-line description). On scratch only: `Assets/A94FScratch/A94FScratchOrphanClip.asset`
  raised H01 (Warning, "Clip is in no clip set", Locate and "Delete clip…"). Its confirmation read "This moves
  'Assets/A94FScratch/A94FScratchOrphanClip.asset' to the OS trash. Nothing in the project references it." Running
  the action beneath the dialog removed the asset from the AssetDatabase and the disk, the rescan dropped to six
  findings with no H01, and the scratch folder was deleted. No real Delete, Remove missing or Rebake was run. Registry
  sha256s unchanged.
- **Not seen by eye:** the drawn layout, the rich-text dots on the chips, the tab label's colour in the real window,
  and a clip edit reaching H11 within a second.
