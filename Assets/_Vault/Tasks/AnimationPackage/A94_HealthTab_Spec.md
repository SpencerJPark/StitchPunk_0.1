# Amendment A94 — Health tab: one project-wide findings list

> **Status:** ✅ built 2026-09-14 as `0.41.0` in the parallel worktree batch A93–A95; ⏸ T14 owner checkpoint open.
> **Roadmap:** [`AnimationPackage_Roadmap.md`](AnimationPackage_Roadmap.md) Phase 2, second.
> **Predecessors:** A82 (split view), A84 (reference index), A89 (VAT freshness), A91
> (`ProfileP2Scan`), A86 (`AnimEventValidation`).
> **Executor:** one orchestrator; `worker` subagents in **one wave of eight**, each ≤ 2 files.

---

## 0. Session prompt (paste into a fresh session)

You are running **Amendment A94 — Health tab** on the DOTS Animation Toolkit package (head
`0.40.0` or later; **A84, A89, A91 built**). Spec:
`Assets/_Vault/Tasks/AnimationPackage/A94_HealthTab_Spec.md`. Read it, the roadmap §3 protocol,
then only what §3 here names. T0 and T1 yours; one wave (T2–T9); one gate; T10–T13 yours. Stop
at T14.

---

## 1. Goal

The validation badge answers "is this clip against this rig OK". Nothing answers "is this project
OK": clips in no set, rigs no profile uses, profiles whose rig differs from their clip sets' rig,
stale VAT bakes, dangling tags, animation names nobody registered, VAT parts with no baked texture,
event keys used but not in the registry, assets with unpersisted stable ids. Each is a silent
failure at play time. After this amendment a Health tab runs every rule over every toolkit asset
and lists findings by severity, each row pinging its asset and, where a fix is one click, offering
it.

```
┌ Health ─────────────────────────────────────────────────────────────────────────────────────┐
│ [⟳ Scan]  ● 2 errors  ● 5 warnings  ● 3 notes      🔍 filter   [Errors][Warnings][Notes]      │
│ ┌──────────────────────────────────────────────────────────────────────────────────────────┐ │
│ │ ● H03 Profile Guard names Walk, not in Animation Names registry        ▸ Guard.asset       │ │
│ │ ● H06 VAT set for CitizenClips is stale — rig changed          [Rebake]▸ CitizenClips      │ │
│ │ ● H01 Clip Idle_Old is in no clip set                          [Delete]▸ Idle_Old.asset    │ │
│ │ ● H08 Key 23 used by 2 clips but not in Event Keys registry            ▸ Walk.asset        │ │
│ │ ○ H05 Rig OldRig is used by no profile                                  ▸ OldRig.asset      │ │
│ └──────────────────────────────────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────────────────────────────────┘
```

---

## 2. Decisions (recorded — do not re-ask). ⚠ = interpretation not yet confirmed.

- **A94-D1 — `ClipEditorTab.Health`, after Events.** Toggle `tab-health`, text "Health", pane
  `health-pane`.
- **A94-D2 — Rules live in `Editor/Health/HealthRules/`, one static class per rule**, each
  `public static void Evaluate(HealthScanContext context, List<HealthFinding> output)`. The
  context carries the six asset lists (from `AssetReferenceIndex`'s scan — expose its cached lists
  in A84's class if not already public; a one-line addition the orchestrator makes in T1) and the
  three registries. Rules are pure over the context and testable with in-memory assets.
- **A94-D3 — The rule set this round (codes are display, not `ValidationCode`):**
  `H01` clip in no set (Warning; Fix: none); `H02` clip set lists a null clip (Error; Fix: Remove
  null); `H03` profile names an animation not in the registry (Error — A91's scan); `H04` profile
  rig ≠ a listed clip set's rig (Error); `H05` rig used by no profile (Note); `H06` VAT stale /
  unbaked (**Error for both** — owner 2026-09-13, matching A89's V08 Error; A89's resolver; Fix:
  Rebake jumps to VAT Bake with the set and its baked rig selected, the same jump as A89's Clip Sets
  Rebake button; see D8);
  `H07` clip track tag absent from the tag registry (Error — the T3 analogue, via `ClipValidation`);
  `H08` event key used but not in the event registry (Error — `AnimEventValidation`); `H09` asset
  has an unpersisted stable id (Warning; Fix: Save — calls `MarkStableIdPersisted` and `SaveAssets`);
  `H10` clip in a set whose rig lacks every tag the clip's tracks use (Warning — the clip poses
  nothing on that roster).
- **A94-D4 — Scan is on demand (button) and after any asset import** (debounced 500 ms via
  `AssetReferenceIndex.Rebuilt`). Never per frame. T0 times a full scan; if it exceeds one second
  on this project the automatic rescan is dropped and the button alone remains — log the call.
- **A94-D5 — Finding row = severity dot (`ToolkitPalette.Error/Warning/Accent`), code, message,
  optional Fix button, ping button.** Filter toggles per severity; the text field filters on message
  and asset name.
- **A94-D6 — Fix actions are the existing utilities, never new write paths:** `ClipSetAssetUtility`
  (grep for a remove-null or add-clip method; if none, the Fix is omitted this round),
  `IStableIdMintReporter.MarkStableIdPersisted`, and A89's "jump to VAT Bake". ⚠ Whether a Delete
  fix for H01 belongs here (it trashes an asset) — omitted unless the owner asks.
- **A94-D7 — The badge is not replaced.** Per-clip, per-rig validation stays where it is; Health
  is cross-asset.
- **A94-D8 — A stale or unbaked VAT bake is the first thing Health says, and it is easy to find**
  (owner, 2026-09-13, answering A89's checkpoint).
  - **Pinned first.** H06 findings sort above every other finding, errors included. This is a
    silent failure: the actor plays old motion with no error at run time.
  - **Row text** names the clip set, the rig, and the resolver's reason ("rig changed" or "clips
    changed").
  - **Row actions:** **Rebake** (D6's jump) and **Locate**, which selects and pings the texture
    set, then the clip set.
  - **Rig lookup.** Use the same rig lookup the Clip Sets tab uses: the rig whose `StableId`
    equals `sourceRigKey`. It is private `ClipSetsPanel.FindRigTheSetWasBakedFrom` today; T5 lifts
    it into `VatSourceHashResolver` or `AssetReferenceIndex` rather than copying it.
  - ⚠ Whether the tab strip shows a count ("Health (1)") while any H06 exists. The checkpoint asks.

---

## 3. Read first

- `Editor/ClipUtilities/AssetReferenceIndex.cs` (A84) in full.
- `Editor/VatBaking/VatSourceHashResolver.cs` (A89), `Editor/ClipUtilities/ProfileP2Scan.cs`
  (A91), `Authoring/Validation/AnimEventValidation.cs` (A86) — surfaces only.
- `Authoring/Validation/ClipValidation.cs` lines 20–80 (`ValidateRig`, `ValidateClip`,
  `ValidateBind` signatures) and grep `T3` for the registry-membership rule.
- `Authoring/Assets/IStableIdMintReporter.cs` in full.
- `Editor/ClipEditor/ValidationBadgeElement.cs` lines 78–140 — the message panel's row idiom.
- `Editor/TexturePacker/TexturePackerPanel.cs` lines 1–80 — panel idiom.

---

## 4. Design

### 4.1 `Editor/Health/HealthFinding.cs` + `HealthScanContext.cs` (T1, orchestrator)

```csharp
public enum HealthSeverity : byte { Note, Warning, Error }
public sealed class HealthFinding
{
    public HealthSeverity severity; public string code; public string message;
    public UnityEngine.Object target; public Action fix; public string fixLabel;
}
public sealed class HealthScanContext { /* six IReadOnlyList<T> + three registries */ }
```

### 4.2 `Editor/Health/HealthScan.cs` (T2) — `public static List<HealthFinding> Run(HealthScanContext)`
calling every rule in code order; plain noun, allowlist.

### 4.3 Rules (T3–T6, two or three rule files per worker) — §2 D3.

### 4.4 `Editor/Health/HealthFindingListElement.cs` (T7) — `ListView` of findings, D5 row.

### 4.5 `Editor/Health/HealthPanel.cs` (T8) — toolbar (Scan, counts, filter), the list, D4 wiring,
`Dispose`.

---

## 5. Tasks

- [x] **T0 — Baseline (orchestrator).** Gate; totals. Time a full six-type `FindAssets` + load;
  D4 call. Grep `ClipSetAssetUtility` for D6's method names.
- [x] **T1 — Shared types (orchestrator).** §4.1; expose A84's cached asset lists if needed. Gate.
  Commit `A94-T1`.
- [x] **T2 — `HealthScan` + fixture [parallel-safe]** — Files: new `HealthScan.cs`, new
  `Tests/EditMode/HealthScanTests.cs`:
  - `Run_OrdersErrorsFirst`: a context producing one Note and one Error → the Error comes first.
  - `Run_PinsStaleVatBakesAboveOtherErrors` (D8): an H02 Error and an H06 → H06 comes first.
  - Revert-to-fail: drop the sort, then drop the H06 pin.
- [x] **T3 — Rules H01, H02, H05 [parallel-safe]** — Files: new `HealthRules/ClipMembershipRules.cs`
  (H01, H02), new `HealthRules/RigUsageRule.cs` (H05). Fixture is T9's.
- [x] **T4 — Rules H03, H04 [parallel-safe]** — Files: new `HealthRules/ProfileRules.cs`.
- [x] **T5 — Rules H06, H09 [parallel-safe]** — Files: new `HealthRules/VatFreshnessRule.cs`, new
  `HealthRules/StableIdRule.cs`.
  - H06 follows D8: an Error for stale and for unbaked, the row text names set + rig + reason, and
    the row carries Locate.
  - The rig lookup is lifted from `ClipSetsPanel` at T1 (orchestrator) so this worker only calls
    it.
- [x] **T6 — Rules H07, H08, H10 [parallel-safe]** — Files: new `HealthRules/TagAndKeyRules.cs`.
- [x] **T7 — List element [parallel-safe]** — Files: new `HealthFindingListElement.cs`.
- [x] **T8 — Panel [parallel-safe]** — Files: new `HealthPanel.cs`.
- [x] **T9 — Rule fixture + docs [parallel-safe]** — Files: new `Tests/EditMode/HealthRulesTests.cs`
  (two tests: `H04_FlagsProfileWhoseClipSetHasAnotherRig`, `H10_FlagsClipWithNoTagOnRoster`; both on
  in-memory assets; revert-to-fail by disabling each rule's comparison), new
  `Documentation~/health-tab.md` (the rule table with codes and what each means).
- **Gate the wave.** `HealthScanTests`, `HealthRulesTests`. Commit `A94-T2..T9`.
- [x] **T10 — Window wiring (orchestrator).** `ClipEditorTab.Health`; UXML; `BindTab`;
  `ShowHealthTab`; `ClipEditorLayoutTests`; `index.md`; `CHANGELOG.md` `## [0.41.0]`;
  `package.json`; `Conformance_G` allowlist (`HealthScan`, rule classes end in `Rule`/`Rules` —
  plain nouns, allowlist them or rename to `…Validation`; T0 decides one way for all). Gate.
- [x] **T11 — Drive.** Full suites. Scan this project; record the counts in §7 (they are real
  findings — list them for the owner). Trigger H09 by touching a scratch clip's stable id and
  confirm the Save fix clears it on reload.
- [x] **T12 — Vault + HANDOFF.** Vault note "Health tab (A94)": D4's timing, the rule-file layout.
  HANDOFF §4; §7 loses any bullet a rule now covers.
- [x] **T13 — Close.** Roadmap checkbox.
- [ ] **T14 — ⏸ owner checkpoint.** Message: "Open Health and press Scan. The findings on your
  project are: [paste §7's list]. Click a row to ping. Say which rules are noise and whether H01
  should offer Delete."

---

## 6. Deliberately out of scope

- Scene/prefab checks (`ActorAuthoring` pointing at a deleted profile) — assets only, A84's rule.
- The camera-data check (A90 does it at run time; edit time cannot know).
- `Samples~` compile and asmdef truth — release readiness, in the handoff's item 4.

## 7. Build log

- **2026-09-14 — stage Phase 0 (parallel batch A93–A95, stage orchestrator).** Baseline at `bdd439b9`: compile clean; EditMode 840 (standing `Conformance_A` failure only), PlayMode 285. CHANGELOG top is `## [0.39.0]`. A88 T9 and A92 T10 were unanswered at batch start. Registry sha256: event keys `3bdb420d…d14701`, tags `dbec3d5f…d1eb4f`. T0 (no-Unity part), T1, the wave and the fixtures run under a `spec-lead` in its own worktree; window wiring, CHANGELOG, `package.json`, conformance pin, drive, vault, HANDOFF and close stay with the stage orchestrator.
- **T0 scan timing (stage, `execute_code`).** The combined six-type `FindAssets` + `GUIDToAssetPath` + `LoadAllAssetsAtPath` over every hit, the same shape as `AssetReferenceIndex.RebuildIfDirty`: 24 assets (11 `ClipAsset`, 2 `ClipSetAsset`, 2 `RigAsset`, 2 `ActorProfileAsset`, 7 `CutsceneAsset`, **0 `VatTextureSetAsset`**); cold 78 ms (FindAssets 68 ms), warm 23–24 ms. Far under D4's one-second bar, so the debounced automatic rescan stays. With no VAT texture sets in the project, H06 has nothing to find on the real scan; its fixture carries the proof.
- **2026-09-14 — spec-lead (opus lead, sonnet workers, worktree `spec/a94`).** T0 no-Unity grounding, T1 and the one wave (T2–T9, eight parallel workers) built and gated; T10–T14 stay with the stage.
  - **D4 decision:** the stage's T0 timing (78 ms cold, 23 ms warm over 24 assets) is far under one second, so the debounced automatic rescan stays alongside the Scan button.
  - **Commits:** `7182a89b` A94-T1 (shared types, every stub, index accessors, rig lookup lift); `1df86bda` A94-T2..T9 (rules, scan, list, panel, fixtures, `health-tab.md`).
  - **Gates:** T1's bare gate exited 3 (heartbeat gap), folded into the wave gate per the stage. Wave gate with bare names `HealthScanTests`/`HealthRulesTests` said pass with 1 test — a false green (see trap below); re-gated with namespace-qualified names: **pass, 4/4, compile clean**.
  - **Revert-to-fail:** M1 (`f952a45b`: pin dropped, H04 and H10 comparisons disabled) gated test-failures, 3 failed (`Run_PinsStaleVatBakesAboveOtherErrors` got H02 first; `H04_FlagsProfileWhoseClipSetHasAnotherRig` and `H10_FlagsClipWithNoTagOnRoster` expected 1, was 0) while `Run_OrdersErrorsFirst` still passed, so each failure has one cause. M2 (`b36ecb11`: sort dropped) gated test-failures, `Run_OrdersErrorsFirst` failed (first error at index 2, after the note) along with the pin test. Both were removed with `git reset --hard HEAD~1`; after each reset `HealthScan.cs` (c8978c96...), `ProfileHealthValidation.cs` (fdffdbae...) and `TagAndKeyValidation.cs` (931a7d1a...) sha256 match the `1df86bda` blobs. Several gate attempts were refused with "Unity is compiling" (other leads' gates) and retried; refusals are not verdicts.
- **T0/T1 drifts (15):**
  1. **D6:** `ClipSetAssetUtility` does not exist. H02's Remove missing uses `ClipAssetUtility.RemoveClipFromSet(ClipSetAsset, int)` for every null index (highest first) inside one collapsed undo group, then `AssetDatabase.SaveAssetIfDirty(set)`. A null element takes one `DeleteArrayElementAtIndex` there (`RemoveClipEntry`).
  2. **D3 H09:** Save is `EditorUtility.SetDirty` + `AssetDatabase.SaveAssetIfDirty(asset)` on that one asset, and `MarkStableIdPersisted` only once the asset is no longer dirty — never `SaveAssets` (batch rule: it flushes the owner's unsaved editor state).
  3. **D3 H04:** a clip set names no rig (`ClipSetAsset.cs` says so outright), so "a listed clip set's rig" is the rig its VAT textures were baked for: H04 fires when `vatTextures.sourceRigKey` is non-zero and differs from `profile.rig.StableId`. Sets without VAT textures, or with key 0, are not judged.
  4. **D3 H10:** "a set whose rig" is the rig of each profile that lists the set. One finding per (rig, clip). Only transform and sprite tracks carry `tagId`; a clip with no tagged track is skipped (it binds by target id).
  5. **D2:** one static class per rule became six classes with one `Evaluate…` method per code (ten methods), so `HealthScan` calls them strictly H01..H10. Class names end in `Validation` (T0's Conformance_G call, one way for all): `ClipMembershipValidation` (H01, H02), `ProfileHealthValidation` (H03, H04), `RigUsageValidation` (H05), `VatFreshnessValidation` (H06), `TagAndKeyValidation` (H07, H08, H10), `StableIdValidation` (H09). Only `HealthScan` needs the plain-noun allowlist.
  6. **D4 wiring:** `AssetReferenceIndex.Rebuilt` fires only when a query rebuilds, never on an import alone, so T1 added `AssetReferenceIndex.Dirtied` (raised by `MarkDirty`, which `AssetReferenceIndexPostprocessor` calls on import, delete and move). The panel debounces 500 ms on `EditorApplication.update`, not the element scheduler, so it rescans while detached.
  7. **D2 exposure:** `AssetReferenceIndex` gained read-only `Clips`, `ClipSets`, `Rigs`, `Profiles`, `Cutscenes`, `VatTextureSets` (each runs `RebuildIfDirty`); `HealthScanContext.FromProject` copies them and takes the three project registries from `VocabularyRegistryProvider`.
  8. **D8 rig lookup:** lifted into `VatSourceHashResolver.FindRigByStableId(IReadOnlyList<RigAsset>, ulong)`; `ClipSetsPanel.FindRigTheSetWasBakedFrom` now calls it with `AssetReferenceIndex.Rigs` (its own per-call `FindAssets` scan is gone). H06 falls back, when the set has no texture set or key 0, to the rig of the first profile listing the set (the tab falls back to its shared selection instead).
  9. **D8 Locate:** `HealthFinding` gained `secondaryTarget`. Every row's locate button (`▸ <asset>`) is Locate: select + ping `target`, then ping `secondaryTarget` 800 ms later. H06: target the texture set, secondary the clip set; unbaked: target the clip set. No separate Locate button.
  10. **§4.1 shape:** `HealthFinding` also carries the ten code constants (`ClipInNoSetCode` … `ClipPosesNothingOnRigCode`) and `IsPinnedFirst` (code H06).
  11. **Null registries skip their rule** (H03, H07, H08) instead of falling back to the project registry — `ProfileP2Scan.ScanProfile` does fall back on null, so H03 returns early first. `FromProject` always supplies them.
  12. **H07/H08 sources:** H07 re-reports `ClipValidation.ValidateClip`'s V36 messages; H08 walks clip event markers only (cutscene markers not included), skips keys below `ReservedEventKeys.FirstUserKey`, and emits one finding per unregistered key naming every clip using it.
  13. **H01/H05 scope as written:** H01 counts set membership only (a clip used only by a cutscene still reports); H05 counts profiles only (a rig used only by a cutscene slot still reports as a Note).
  14. **Panel surface:** `HealthPanel` gained `Bind()` (the spec named only `Dispose`), `Scan()`, `LatestFindings`, `StaleVatBakeCount` and `FindingsChanged` — the last two exist only so the stage can answer D8's ⚠ tab-count question without reopening the panel.
  15. **Gate trap:** `worktree.py gate --edit-mode <BareFixtureName>` matches nothing: `TestCapture.cs` line 28 anchors `^<name>(\.|$)` against the full name, which starts with the namespace, and the childless root result counts as 1 passed. Use `--edit-mode DotsAnimationToolkit.Tests.EditMode.<Fixture>`. Reported to the stage; the toolkit fix is the stage's.
- **Unverified:** no drive — the panel and list have never been rendered or clicked; the H02 Remove missing, H06 Rebake and H09 Save fixes have never run; H03, H07 and H09 have no fixture (H02, H05, H06, H08 are exercised through `HealthScanTests`, H04 and H10 through `HealthRulesTests`); the real-project counts are T11's.

### For integration

**CHANGELOG `## [0.41.0]`:**

```
## [0.41.0]

### Added
- Health tab: one project-wide list of cross-asset problems, sorted by severity, each row locating its asset and, where the fix is one click, offering it. Scan on demand; rescans about half a second after toolkit assets change.
- Ten rules: H01 clip in no set, H02 clip set lists a missing clip (Remove missing), H03 profile animation not in the Animation Names registry, H04 profile rig differs from the rig a listed set's VAT textures were baked for, H05 rig used by no profile, H06 VAT texture set stale or unbaked (always listed first; Rebake, Locate), H07 track tag not in the Target Tags registry, H08 event key not in the Event Keys registry, H09 unsaved stable id (Save), H10 clip whose tags the profile's rig has no target for.
- `AssetReferenceIndex` exposes its six cached asset lists read-only and raises `Dirtied` whenever the index is marked dirty.
- `VatSourceHashResolver.FindRigByStableId`; the Clip Sets tab's baked-rig lookup now uses it.
- Documentation: `health-tab.md`.
```

**Conformance_G:** add `"HealthScan"` to `PlainNounStaticClasses`. Nothing else — the six rule classes end in `Validation`.

**Wiring (all names final):**
- `ClipEditorTab.Health = 8`.
- UXML: tab toggle `name="tab-health"` text `Health`; pane `<ui:VisualElement name="health-pane" class="clip-editor__cover-pane clip-editor--hidden"/>` beside `texture-packer-pane`.
- Construct lazily like the Texture Packer (ClipEditorWindow.cs ~1432): `healthPanel = new HealthPanel(); healthPanel.RebakeRequested += OnClipSetRebakeRequested; healthPane.Add(healthPanel); healthPanel.Bind();` — `OnClipSetRebakeRequested(ClipSetAsset, RigAsset)` (ClipEditorWindow.cs:1496) already has the right signature and is the Clip Sets tab's Rebake jump, so Health's Rebake is the same jump.
- Dispose beside `texturePackerPanel.Dispose()` (~738): `healthPanel.RebakeRequested -= OnClipSetRebakeRequested; healthPanel.Dispose(); healthPanel = null;`
- Optional, only if the owner answers yes to the tab count: subscribe `healthPanel.FindingsChanged` and set the toggle text to `"Health (" + healthPanel.StaleVatBakeCount + ")"` when it is above zero. The panel only feeds it once built, so a count before first open would need an eager `new HealthPanel()` + `Bind()`.
- `index.md`: one link line to `health-tab.md`.
- **Merge note:** this branch also edits `ClipSetsPanel.cs` (the lookup body only), `AssetReferenceIndex.cs` and `VatSourceHashResolver.cs` (additions only).

**Vault-note traps ("Health tab (A94)"):**
- D4 timing: six-type scan 78 ms cold / 23 ms warm over 24 assets, so the automatic rescan stays.
- Layout: `Editor/Health/` holds `HealthFinding`, `HealthScanContext`, `HealthScan`, `HealthFindingListElement`, `HealthPanel`; `Editor/Health/HealthRules/` holds six `…Validation` classes, one `Evaluate…` per code, called in code order by `HealthScan`.
- `AssetReferenceIndex.Rebuilt` never fires on an import by itself — listen to `Dirtied`.
- Every `CreateInstance`'d toolkit asset reports an unpersisted stable id (H09); a fixture that runs `HealthScan.Run` must `MarkStableIdPersisted()` on what it creates.
- Rules are pure over `HealthScanContext`; a null registry skips its rule, whereas `ProfileP2Scan` alone falls back to the project registry on null.
- Gate fixture names must be namespace-qualified (bare names report a false 1-passed).

**HANDOFF §4 draft:** A94 adds the Health tab (0.41.0): one project-wide findings list over every toolkit asset, ten rules H01–H10 in `Editor/Health/HealthRules/`, run by `HealthScan` with stale or unbaked VAT bakes pinned above everything else. Rows ping their asset; H02 (Remove missing), H06 (Rebake, the Clip Sets tab's own jump) and H09 (Save, `SaveAssetIfDirty` on that one asset) offer one-click fixes. The panel rescans 500 ms after `AssetReferenceIndex.Dirtied`, a new event, because `Rebuilt` never fires on an import alone. Owner checkpoint open: which rules are noise, whether H01 should offer Delete, and whether the tab shows a count while an H06 exists.

### Close (stage orchestrator, 2026-09-14)

- **Merged** `spec/a94` (3 commits, `daf000c5`), second; worktree removed.
- **Integration** `ffdc6754`: `ClipEditorTab.Health = 8`, `tab-health` / `health-pane`, `ShowHealthTab` (lazy `new HealthPanel()`, `RebakeRequested += OnClipSetRebakeRequested`, `Bind()`; unsubscribed and disposed with the window), `HealthScan` on the Conformance_G plain-noun allowlist, CHANGELOG `## [0.41.0]`. The optional "Health (n)" tab count is not wired; it stays a checkpoint question.
- **Correction to the Phase 0 entry above:** H06 is not silent here. With zero `VatTextureSetAsset`s, an unbaked clip set still reports.
- **Suites (T11):** EditMode 850 (standing Conformance_A only), PlayMode 285.
- **Real project scan (T11)**, `HealthScanContext.FromProject` + `HealthScan.Run`, 132 ms: **3 findings, 2 errors, 0 warnings, 1 note**, in this order:
  1. **Error H06**: VAT set for clip set `VatSampleTentacleClips` on rig "no rig" is unbaked (`Assets/ScriptableObjects/Animations/VatSampleTentacle/VatSampleTentacleClips.asset`).
  2. **Error H02**: clip set `NewClipSet` lists 3 missing clips; fix "Remove missing" (`Assets/ScriptableObjects/Animations/NewClipSet.asset`).
  3. **Note H05**: rig `VatSampleTentacleRig` is used by no actor profile.
- **H09 fix (T11)** on `Assets/A94Scratch/A94DriveClip.asset`: created with an unpersisted id, H09 found with fix "Save", fix run, 0 H09 in memory and the asset not dirty; after `Resources.UnloadAsset` + reload still 0 H09, `HasUnpersistedStableId` false, serialized JSON identical across the reload. Scratch deleted.
- **Panel:** detached `new HealthPanel()` + `Bind()` built (55 elements), reported `StaleVatBakeCount` 1, disposed.
- **Not verified:** Remove missing (it would edit the real `NewClipSet`) and Rebake (it jumps the docked window) were not clicked; H03, H07 and H08 have no fixture and find nothing on this project. Wording for the checkpoint: an unbaked set reads "on rig 'no rig'". No capture (docked window).
