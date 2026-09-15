You are the **stage orchestrator** for a parallel follow-up batch on the DOTS Animation Toolkit package
(Packages/com.dotsanimationtoolkit, version 0.42.0). The owner answered the A93, A94 and A95 checkpoints on 2026-09-14
and asked for three reworks. Each runs in its own git worktree under a `spec-lead` through the Worktree Toolkit
(`/worktree-run`). You alone touch mcp__UnityMCP__*, merge, integrate, drive and close.

| Spec | Path | Version | Tab |
|---|---|---|---|
| a93f | Assets/_Vault/Tasks/AnimationPackage/A93F_EventsTabRework_Spec.md | 0.43.0 | Events (exists) |
| a94f | Assets/_Vault/Tasks/AnimationPackage/A94F_HealthTwoPanel_Spec.md | 0.44.0 | Health (exists) |
| a95f | Assets/_Vault/Tasks/AnimationPackage/A95F_SheetsFromArrays_Spec.md | 0.45.0 | Sprite Sheets (exists) |

Why parallel:
- The three touch disjoint folders (`Editor/Events/` + `Runtime/Api/`, `Editor/Health/` + `Editor/ClipUtilities/` VAT
  and clip trash, `Editor/SpriteSheets/` + the sheet picker).
- None adds a tab, so the window, UXML and layout test change only for A93F's `FocusClip` and `OpenOwnerRequested`
  wiring, which is yours.
- The only Unity probe (A95F S-D4, layer thumbnails) was run on 2026-09-14 and is recorded in that spec.

**This file gives what differs from `Assets/_Vault/Spencer/next-session-parallel-a96-a98-prompt.md`.** Use that
file's **Lead contract** (verbatim, including the gate syntax with namespace-qualified names and
`PackagingConformanceTests` in every wave gate), **Phase 2**, **Cautions** and **Inherited mechanics** sections
unchanged. Read its Phase 3 for the merge, integration and close shape.

Read, in order:
1. The Worktree Toolkit instructions (`.claude/skills/worktree-run/SKILL.md`, `.claude/agents/spec-lead.md`,
   `Assets/_Vault/Memories/Code/WorktreeToolkit.md`).
2. Roadmap §3.
3. The three follow-up specs' §0, §2 and §5.
4. The A93, A94 and A95 specs' "Owner checkpoint answer" blocks at the end of their §7.

**Models (pre-answered):** lead `opus`, worker `sonnet` for all three.
**Merge authorization:** I authorize `worktree.py merge` for a93f, a94f and a95f once each reports `ready` with its
gates green. Delete this line to require my word per merge.

## Phase 0 — stage prep

1. **Owner answers** (own commit each; delete a line if unanswered):
   - Owner's A88 T9 answer: <PASTE HERE> (details in the A96–A98 prompt's Phase 0).
   - Owner's A92 T10 answer: <PASTE HERE> (details in the A96–A98 prompt's Phase 0).
   - Owner's A95 leftovers (compressed arrays? retire `TextureArrayBuilder.cs`?): <PASTE HERE>. Each "yes" is a new
     task note in `Assets/_Vault/Tasks/`, not code in this batch.
2. **Preflight:** `worktree.py doctor --json`. Stop if git fails, hooks are missing or `brokerAlive` is false.
3. **Baseline:** compile gate, full suites. Expected: EditMode 850 (standing Conformance_A only), PlayMode 285.
4. **Unity-bound T0:** none beyond A95F's recorded probe. Confirm CHANGELOG's top section is `## [0.42.0]`.
5. **Registry hashes:** record the sha256 of both vocabulary registries.
6. **The owner's untracked game file:** confirm `Assets/_Scripts/Systems/CombatSystemGroup/DamageEventSystemAnimEventSystem.cs`
   still exists. A93F's removal breaks its compile on the stage the moment a93f merges; T7 converts it in the same
   integration pass, before any compile gate. Never commit it.
7. Commit Phase 0 (spec §7 entries only) and push.

## Phase 1 — spawn

Three `spec-lead` agents in one message, `model: opus`, background, each prompt holding:
- the spec id and path;
- `worker model: sonnet`;
- the gate syntax;
- the A96–A98 prompt's Lead contract, verbatim.

Add per lead:
- **a93f:** "Your T1 `git rm` removes types the owner's untracked stub uses; that file exists only on the stage, so
  your gates cannot see it. Do not create or edit anything under `Assets/_Scripts/`."
- **a94f:** "The only real project deletes are the stage's drive, on scratch copies. Your fixtures never trash a
  real asset."
- **a95f:** "The ten project arrays live under `Assets/Textures/Units/`. Nothing in your code or fixtures writes
  beside them, and fixtures build arrays in a GUID-named scratch folder."

## Phase 3 — merge and integrate

1. **Merge** in the order a93f → a94f → a95f, only while `stage.busyWith` is null; push after each; then `remove`.
2. **One integration commit** "A93F-A95F integration":
   - A93F T6: `ClipEditorWindow.FocusClip(ClipAsset)`, and the `OpenOwnerRequested` routing in `ShowEventsTab`,
     unsubscribed in teardown.
   - CHANGELOG `## [0.45.0]`, `## [0.44.0]` and `## [0.43.0]`, newest on top, from the three For-integration blocks
     (A93F's has a **Removed** list).
   - `package.json` 0.45.0; the conformance pin (comment gains 0.43.0–0.45.0, Assert at 0.45.0).
   - Any `Conformance_G` allowlist names.
   - `ClipEditorLayoutTests` only if a block names new UXML elements (none expected).
3. **A93F T7**, the owner's stub conversion. Stage only, not committed.
4. **Compile gate**, then:
   - the fixtures `AnimEventBufferApiTests`, `HealthScanTests`, `HealthRulesTests`, `VatTextureOwnershipResolverTests`,
     `SpriteSheetArrayNamesTests`, `SpriteSheetValidationTests`, `SpriteSheetBakerTests`, `ClipEditorAddEventTests`,
     `ClipEditorLayoutTests`, `PackagingConformanceTests`;
   - full suites once. EditMode should be 850 − 3 (removed routing fixtures) + the new fixtures; PlayMode 285.
5. **Drives:** A93F T8, A94F T10, A95F T8, one at a time, on scratch only. Re-check the registry hashes after each.
6. **Close:**
   - each spec's status line, boxes and §7 close log;
   - HANDOFF §4 paragraphs (A95F on top);
   - `AnimationToolkit.md` sections "(A93F, 0.43.0)", "(A94F, 0.44.0)", "(A95F, 0.45.0)", and the A93 section's routing
     traps marked as removed in a new line rather than edited away;
   - roadmap ticks and status line;
   - write the close texts through a scratchpad Python file.
   Update `next-session-parallel-a96-a98-prompt.md` only if totals or versions moved.
7. **One owner checkpoint message** covering A93F T10, A94F T12 and A95F T10. Name real assets: `MeleeContinuous`
   and `Attack`; `VatSampleTentacleClips`, `NewClipSet` and `VatSampleTentacleRig`; `EyeArray.png`.

## Batch-specific cautions (on top of the A96–A98 prompt's Cautions)

- **Health drive:** never press a real Delete, Remove missing or Rebake. Run each action's `run` beneath its dialog,
  on scratch copies only.
- **Sprite Sheets drive:** copy `EyeArray.png` into a scratch folder with `AssetDatabase.CopyAsset`, never write
  `_Sheet.asset` beside a real array, and delete scratch after.
- **Events drive:** the usage column reads the real project (read-only). Invoke open requests on a detached panel
  only; the docked window stays untouched.
