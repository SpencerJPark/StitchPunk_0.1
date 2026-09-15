# Next session prompt — A93 + A94 + A95 in parallel worktrees (written 2026-09-14, after A92)

Before you paste:
- Keep the Unity Editor open, all scenes saved, and not in Play mode.
- While the leads run, don't author anything in the Editor. Every gate swaps a spec branch into the
  folder your Editor has open, then swaps trunk back.
- Usage: three Opus leads, each spawning 7–10 Sonnet workers. If usage is tight, set the A95 lead to
  sonnet, or delete every A95 line to run two specs.
- Fill in or delete the two answer slots.

Paste everything below the line into a fresh session.

---

You are the **stage orchestrator** for a parallel batch on the DOTS Animation Toolkit package
(Packages/com.dotsanimationtoolkit, version 0.39.0 after A92). Three specs run at once in their own git
worktrees, each under a `spec-lead`, through the Worktree Toolkit (`/worktree-run`,
Packages/com.worktreetoolkit). You alone touch mcp__UnityMCP__*, merge, integrate, drive and close.

| Spec | Path | Version | Tab slot |
|---|---|---|---|
| a93 | Assets/_Vault/Tasks/AnimationPackage/A93_EventsTab_Spec.md | 0.40.0 | `ClipEditorTab.Events = 7` |
| a94 | Assets/_Vault/Tasks/AnimationPackage/A94_HealthTab_Spec.md | 0.41.0 | `ClipEditorTab.Health = 8` |
| a95 | Assets/_Vault/Tasks/AnimationPackage/A95_SpriteSheetsTab_Spec.md | 0.42.0 | `ClipEditorTab.SpriteSheets = 9` |

Why these three: the roadmap puts A93 and A94 first, and A95 needs nothing either of them builds (only
the tab order). Their only shared files are the window wiring and the close files, and those are yours.
A96 (checks A95's sheets) and A97 wait for the next batch. A98 and A100 have Unity-probe T0s, and A99
waits on three open ragdoll checkpoints.

Read, in order:
1. The Worktree Toolkit instructions: .claude/skills/worktree-run/SKILL.md, .claude/agents/spec-lead.md,
   and Assets/_Vault/Memories/Code/WorktreeToolkit.md (rules and traps).
2. The roadmap's section 3 protocol: Assets/_Vault/Tasks/AnimationPackage/AnimationPackage_Roadmap.md.
3. The three specs' sections 0, 2 and 5 only. Leads read their specs in full; you do not need to.

**Models (pre-answered, skip the skill's Ask step):** lead `opus`, worker `sonnet` for all three.
**Merge authorization:** I authorize `worktree.py merge` for a93, a94 and a95 once each reports
`ready` with its gates green. No review swap is needed. Delete this line to require my word per merge.

## Phase 0 — stage prep (you, on the stage, before any lead exists)

1. **Owner answers** (apply each as its own commit; delete a line if unanswered):
   - Owner's A88 T9 answer: <PASTE HERE>
     - A88 (0.38.0) is the Layer Events strip under the Actor Editor preview. Its questions: does
       dimmed/hollow read as "will not fire"; should the strip start collapsed?
     - "Start collapsed" is `EditorPrefs.GetBool(ExpandedPrefKey, true)` → false in
       Editor/ClipEditor/ActorEditor/LayerEventStripElement.cs.
     - Pins are drawn in `LayerEventRowElement.DrawLane`: `InactiveAlpha = 0.35f` is the dim, and the
       hollow ghost is a 0-alpha fill with a 35% outline.
     - Gate: `LayerEventRowResolverTests` + `ActorEditorPanelTests`. Commit A88-T10 (precedent 071c361c).
   - Owner's A92 T10 answer: <PASTE HERE>
     - A92 (0.39.0) is refactor operations. Its question: do the dialog wording and the four entry
       points read right?
     - Strings are in Editor/ClipEditor/Editing/RefactorPromptEditing.cs. Entry points:
       EventMarkerContextMenu.cs, AnimEventKeyRegistryEditor.cs, TargetTagRegistryEditor.cs,
       RigsPanel.cs `PopulateTagButtonContextMenu`.
     - Gate: `RefactorTargetResolverTests`. Commit A92-T11.
   - For each answer: add it to that version's CHANGELOG section, record it in the spec's status and
     section 7, update HANDOFF section 4 ("T9/T10 answered"), and tick the roadmap box and remove its
     to-do line.
2. **Preflight:** `python Packages/com.worktreetoolkit/Tools~/worktree.py doctor --json`.
   - Stop and tell me if git fails, hooks aren't installed, or `brokerAlive` is false.
   - `stageBlockers` listing only my uncommitted files (below) is expected (trap 13).
3. **Baseline:** compile gate, then full suites (`DotsAnimationToolkit.Tests.EditMode`, then
   `.PlayMode`). Expected at A92's close (fbd15d45): EditMode 840 with only the standing Conformance_A
   failure, and PlayMode 285.
4. **Unity-bound T0 work** (leads cannot do it). Do it here and paste the results into that lead's
   prompt:
   - A94 T0: the full six-type `FindAssets` + load timing (read A94 section 5 T0 in full). A84 measured
     65 ms for the combined FindAssets alone.
   - A95 T0: the `execute_code` Texture2DArray probe (read A95 section 5 T0 in full).
   - All three: confirm CHANGELOG's top section is `## [0.39.0]`.
5. Record the sha256 of ProjectSettings/DotsAnimationToolkitAnimEventKeyRegistry.asset and
   ProjectSettings/DotsAnimationToolkitTargetTagRegistry.asset. Commit Phase 0 and push, so the worktrees
   branch from it.

## Phase 1 — spawn (skill "Spawn" step)

Spawn three `Agent`s in one message: `subagent_type: spec-lead`, `model: opus`, background. Each prompt
contains:
- the spec id;
- the spec path;
- `worker model: sonnet`;
- that spec's Phase 0 probe results;
- the **lead contract** below, verbatim.

> **Lead contract**
> - **Your scope:** the spec's T0 grounding that needs no Unity (verify names by grep, make the
>   code-reading decisions, log drift in your spec's section 7), T1 shared types, every
>   `[parallel-safe]` worker wave, and the fixtures with their revert-to-fail.
> - **The stage orchestrator owns these. Never edit them:**
>   - `Editor/ClipEditor/ClipEditorTab.cs`
>   - `ClipEditorWindow.uxml` and every `ClipEditorWindow*.cs` (BindTab / Show…Tab)
>   - `ClipEditorLayoutTests`
>   - `Documentation~/index.md`
>   - `CHANGELOG.md`
>   - `package.json`
>   - `PackagingConformanceTests.cs`
>   - `Docs/AnimationToolkit/HANDOFF.md`
>   - the roadmap
>   - `Assets/_Vault/Memories/Code/AnimationToolkit.md`
>   - anything under `ProjectSettings/`
>   - and the spec's window-wiring, drive, vault, close and checkpoint tasks.
>   Brief your docs worker to write only its own new page.
> - **Your spec's section 7 is yours.** End it with a `### For integration` block holding:
>   - the CHANGELOG section text for your version;
>   - the `Conformance_G` allowlist names you need;
>   - the exact wiring: enum member, UXML toggle and pane names, and the panel's constructor,
>     Bind and Dispose calls;
>   - vault-note traps;
>   - a one-paragraph HANDOFF draft.
> - **Build the panel so it works detached** (`new …Panel()` plus the spec's Bind/Dispose), so it can
>   be wired and proven without the window.
> - **Gates:** `python Packages/com.worktreetoolkit/Tools~/worktree.py gate --edit-mode <fixtures>`,
>   in the format `gate --help` shows. Gate only the fixtures your spec names, and only after every
>   worker in the wave has reported (trap 8).
> - **Revert-to-fail:** commit the mutation alone, then gate it and expect a failure. Then run
>   `git reset --hard HEAD~1` in your worktree and check the file's sha256 matches.
> - **No drives, full suites or execute_code.** Fixtures build everything with `CreateInstance`, never
>   real assets or registries.
> - **Conventions:** CLAUDE.md and roadmap section 3 (with MCP replaced by `worktree.py gate`).
>   - Worker briefs: at most two files, named line ranges, exact signatures pasted, and "add NO
>     <summary> blocks" for existing files. Commit committed stubs for every new shared type first.
>   - No `var` and no single-letter names.
>   - Conformance_F: one <summary> per file, max 3 lines, and no "§", "amendment A<n>", "Phase A-G",
>     "rule V<nn>", <remarks> or <para> anywhere, strings included.
>   - Conformance_E: UI Toolkit only; `DisplayDialog` is allowed.
>   - Conformance_G: static class suffixes are Api, Builder, Sampler, Resolver, Math, Validation,
>     Utility (Editor/ClipUtilities only) and Editing. Report anything that needs the allowlist.
> - **Git:** `git commit` takes the whole index, so run `git diff --cached --stat` before every commit.
>   Stage paths explicitly and never push.
> - **Settled owner calls, do not re-ask:**
>   - no package-side event handlers (A93's routes are data; its stub is host code);
>   - no sound mixing;
>   - names, never numbers, in every editor surface;
>   - A95 builds Texture2DArrays, never atlases.
> - **Finish** with `worktree.py status <id> ready` and a report of 30 lines or fewer: commits, gate
>   verdicts, revert-to-fail result, drift count, where the For-integration block is, and what's
>   unverified. At turn 70, stop and report what's left.

## Phase 2 — while running

- Stay quiet: the broker resolves gates. If a lead sends "gate needed", follow the skill's four steps.
- Never run execute_code, tests or refresh on the stage while a gate may be running; the stage is
  swapped. Track progress with `worktree.py list`.
- A capped lead is never resumed or re-spawned as a spec-lead (that makes a new worktree, trap 5).
  Spawn a `worker` with the worktree's absolute path (from `list --json`) for the remaining scope.

## Phase 3 — merge and integrate (you, on trunk)

1. Merge in tab order, a93 → a94 → a95, each with `worktree.py merge <id>` then push.
   - On a conflict (exit 2), use the skill's worker-rebase flow.
   - After all three are merged: `worktree.py remove <id>` for each. Never `git worktree remove --force`.
2. **One integration commit** "A93-A95 integration", built from the three For-integration blocks:
   - enum members 7/8/9;
   - UXML toggles and panes after Cutscene, in that order;
   - BindTab / Show…Tab;
   - `ClipEditorLayoutTests`;
   - `index.md`;
   - CHANGELOG `## [0.42.0]`, `## [0.41.0]` and `## [0.40.0]`, newest on top;
   - `package.json` 0.42.0;
   - the conformance pin, with its comment gaining 0.40.0/0.41.0/0.42.0 and the Assert at 0.42.0;
   - the Conformance_G allowlist.
   Then the compile gate, the three specs' fixtures and `ClipEditorLayoutTests`.
3. **Full suites once.** Totals must not drop below 840/285 plus the new fixtures.
4. **Drives:** A93 T13, A94 T11 and A95 T12, one at a time, following the cautions below. Record the
   results in each spec's section 7.
5. **Close:**
   - each spec's status line, a section 7 close log, and ticked task boxes;
   - HANDOFF section 4: one paragraph per spec at the top of the queue, A95 on top;
   - three sections appended at the end of AnimationToolkit.md, "(A93, 0.40.0)", "(A94, 0.41.0)" and
     "(A95, 0.42.0)", without editing older sections;
   - the roadmap status line plus three checkpoint lines in the owner to-do block;
   - commit and push.
6. **Write the next batch prompt** in Assets/_Vault/Spencer/. Evaluate A96 (needs A95), A97 (needs A92)
   and A98 the same way this batch was chosen. Keep Unity probes as stage work, and write a parallel
   prompt only where the specs can run in parallel.
7. **End with one owner checkpoint message** covering all three specs' checkpoint questions (A93 T16,
   A94 T14, A95 T15). Adapt it to the drift you settled, name real assets to try, and say exactly what I
   should see.

## Cautions (verify, log drift in section 7, escalate rather than quietly re-spec)

- **My uncommitted work** (never stage, overwrite or write):
  - Assets/ScriptableObjects/Animations/A63CheckpointCutscene.asset, NewClip.asset, "NewClip 1.asset"
  - ProjectSettings/DotsAnimationToolkitAnimEventKeyRegistry.asset
  - ProjectSettings/EditorBuildSettings.asset
  - Assets/SceneDependencyCache/*
  - the untracked screenshot in Tasks/AnimationPackage/
  - untracked .meta files under Assets/_Vault/Spencer/ and Tasks/NewPlans/
  - Re-check both registry sha256s after every drive.
- **A93** edits the event vocabulary. Drive new-key, payload and route flows against a `CreateInstance`
  registry (`VocabularyRegistryProvider.Persist` is a no-op for it). "Generate consumer stub…" writes
  only into an Assets/ scratch folder, which you delete afterwards. A92 is built, so the Merge row menu
  can use `RefactorPromptEditing.ShowMergeIntoMenu`, which always acts on the project registry.
- **A94** scans the real project; record its real counts. Any fix action runs only on scratch copies.
- **A95** bakes from project PNGs into a scratch folder only. Its `ClipInspectorPane` sprite-key edit is
  proven on a scratch clip.
- **Never call `AssetDatabase.SaveAssets()`**; save only scratch assets, with `SaveAssetIfDirty`.
- **Never run a player build.**
- **ClipEditorWindow** is single-instance and my copy is docked. Don't drive, rearrange or load into it.
  Don't open scenes or enter Play mode. Prove tabs on detached panels. Capture only if
  `EditorApplication.isFocused` is true; otherwise say so.
- **`EditorUtility.DisplayDialog` is modal:** never reach one from execute_code; call the operation
  beneath it instead.
- **Settled owner calls** (do not re-ask):
  - A87 D1/D5, A90, and A91 ("works for now");
  - A88's and A92's section 7 drifts;
  - A92: cutscene part tracks included, merge payloads left raw;
  - no sound mixing, no package-side event handlers, names never numbers.

## Inherited mechanics (do not re-derive)

- **Compile gate:** `refresh_unity` (compile: request), then poll mcpforunity://editor/state until
  `is_compiling` is false and `last_domain_reload_after_unix_ms` is after your edit, then
  `read_console` (errors). A `stale_status` block clears by itself; "ping not answered" means retry.
- **Tests:** never run execute_code while a test job runs, and foreground sleeps are blocked.
- **execute_code** is CodeDom C# 6:
  - no usings, no out var, no local functions (`System.Func` delegates are fine);
  - find package types by scanning AppDomain assemblies and use reflection;
  - `GetInstanceID()` fails to compile on 6.5;
  - pass `safety_checks=false` only to delete your own scratch.
- **Commits:** one per phase step, prefixed "A9x-Tn:" or "A93-A95 integration:". Stage paths
  explicitly, check `git diff --cached --stat` first, and push when green.
