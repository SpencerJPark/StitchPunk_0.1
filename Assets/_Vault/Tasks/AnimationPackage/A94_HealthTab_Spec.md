# Amendment A94 — Health tab: one project-wide findings list

> **Status:** 📝 specced 2026-09-10, not built. Takes `0.41.0`.
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
  unbaked (Warning / Error — A89's resolver; Fix: Rebake jumps to VAT Bake with the set selected);
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

- [ ] **T0 — Baseline (orchestrator).** Gate; totals. Time a full six-type `FindAssets` + load;
  D4 call. Grep `ClipSetAssetUtility` for D6's method names.
- [ ] **T1 — Shared types (orchestrator).** §4.1; expose A84's cached asset lists if needed. Gate.
  Commit `A94-T1`.
- [ ] **T2 — `HealthScan` + fixture [parallel-safe]** — Files: new `HealthScan.cs`, new
  `Tests/EditMode/HealthScanTests.cs` (`Run_OrdersErrorsFirst`: a context producing one Note and
  one Error → Error first). Revert-to-fail: drop the sort.
- [ ] **T3 — Rules H01, H02, H05 [parallel-safe]** — Files: new `HealthRules/ClipMembershipRules.cs`
  (H01, H02), new `HealthRules/RigUsageRule.cs` (H05). Fixture is T9's.
- [ ] **T4 — Rules H03, H04 [parallel-safe]** — Files: new `HealthRules/ProfileRules.cs`.
- [ ] **T5 — Rules H06, H09 [parallel-safe]** — Files: new `HealthRules/VatFreshnessRule.cs`, new
  `HealthRules/StableIdRule.cs`.
- [ ] **T6 — Rules H07, H08, H10 [parallel-safe]** — Files: new `HealthRules/TagAndKeyRules.cs`.
- [ ] **T7 — List element [parallel-safe]** — Files: new `HealthFindingListElement.cs`.
- [ ] **T8 — Panel [parallel-safe]** — Files: new `HealthPanel.cs`.
- [ ] **T9 — Rule fixture + docs [parallel-safe]** — Files: new `Tests/EditMode/HealthRulesTests.cs`
  (two tests: `H04_FlagsProfileWhoseClipSetHasAnotherRig`, `H10_FlagsClipWithNoTagOnRoster`; both on
  in-memory assets; revert-to-fail by disabling each rule's comparison), new
  `Documentation~/health-tab.md` (the rule table with codes and what each means).
- **Gate the wave.** `HealthScanTests`, `HealthRulesTests`. Commit `A94-T2..T9`.
- [ ] **T10 — Window wiring (orchestrator).** `ClipEditorTab.Health`; UXML; `BindTab`;
  `ShowHealthTab`; `ClipEditorLayoutTests`; `index.md`; `CHANGELOG.md` `## [0.41.0]`;
  `package.json`; `Conformance_G` allowlist (`HealthScan`, rule classes end in `Rule`/`Rules` —
  plain nouns, allowlist them or rename to `…Validation`; T0 decides one way for all). Gate.
- [ ] **T11 — Drive.** Full suites. Scan this project; record the counts in §7 (they are real
  findings — list them for the owner). Trigger H09 by touching a scratch clip's stable id and
  confirm the Save fix clears it on reload.
- [ ] **T12 — Vault + HANDOFF.** Vault note "Health tab (A94)": D4's timing, the rule-file layout.
  HANDOFF §4; §7 loses any bullet a rule now covers.
- [ ] **T13 — Close.** Roadmap checkbox.
- [ ] **T14 — ⏸ owner checkpoint.** Message: "Open Health and press Scan. The findings on your
  project are: [paste §7's list]. Click a row to ping. Say which rules are noise and whether H01
  should offer Delete."

---

## 6. Deliberately out of scope

- Scene/prefab checks (`ActorAuthoring` pointing at a deleted profile) — assets only, A84's rule.
- The camera-data check (A90 does it at run time; edit time cannot know).
- `Samples~` compile and asmdef truth — release readiness, in the handoff's item 4.

## 7. Build log

_(empty)_
