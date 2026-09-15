You are the **stage orchestrator** for a parallel batch on the DOTS Animation Toolkit package
(Packages/com.dotsanimationtoolkit, version 0.45.0 after the A93F–A95F follow-up batch; run
`Assets/_Vault/Spencer/next-session-parallel-a93f-a95f-prompt.md` first). Three specs run at once in their own
git worktrees, each under a `spec-lead`, through the Worktree Toolkit (`/worktree-run`,
Packages/com.worktreetoolkit). You alone touch mcp__UnityMCP__*, merge, integrate, drive and close.

| Spec | Path | Version | Tab slot |
|---|---|---|---|
| a96 | Assets/_Vault/Tasks/AnimationPackage/A96_MaterialsTab_Spec.md | 0.46.0 | `ClipEditorTab.Materials = 10` |
| a97 | Assets/_Vault/Tasks/AnimationPackage/A97_RetargetTab_Spec.md | 0.47.0 | `ClipEditorTab.Retarget = 11` |
| a98 | Assets/_Vault/Tasks/AnimationPackage/A98_CaptureTab_Spec.md | 0.48.0 | `ClipEditorTab.Capture = 12` |

Why these three:
- **Versions moved:** the three specs' status lines still say 0.43.0–0.45.0; take 0.46.0–0.48.0 and correct each
  status line (roadmap rule).
- **A96** needed A95 (built 0.42.0); it only reads sheet-bound sprite tracks. Its T0 is greps, so the lead can
  do it.
- **A97** needed A84 and A92 (both built); its T0 reads `ValidateBind`, also a grep.
- **A98** needed A74 and A71 (both built). Its T0 has a Unity timing probe (D3, the GIF encode), which is stage
  work and done in Phase 0 below.
- They share only the window wiring and the close files, which are yours.
- **Still waiting:** A99 on its three open ragdoll checkpoints. A100 has a Play-mode T0 of its own; it goes in
  the next batch, or alone.

Read, in order:
1. `.claude/skills/worktree-run/SKILL.md`, `.claude/agents/spec-lead.md`, `Assets/_Vault/Memories/Code/WorktreeToolkit.md`.
2. `Assets/_Vault/Tasks/AnimationPackage/AnimationPackage_Roadmap.md` §3.
3. The three specs' §0, §2 and §5 only.
4. The last three sections of `Assets/_Vault/Memories/Code/AnimationToolkit.md`, the A93, A94 and A95 tabs and
   the "Parallel batch A93–A95" lessons.

**Models (pre-answered, skip the skill's Ask step):** lead `opus`, worker `sonnet` for all three.
**Merge authorization:** I authorize `worktree.py merge` for a96, a97 and a98 once each reports `ready` with its
gates green. No review swap is needed. Delete this line to require my word per merge.

## Phase 0 — stage prep (you, on the stage, before any lead exists)

1. **Owner answers.** Apply each answer as its own commit, and delete any line left unanswered.
   - **For each answer:** add it to that version's CHANGELOG section; record it in the spec's status line and §7;
     update HANDOFF §4; tick the roadmap box and remove its to-do line.
   - Owner's A88 T9 answer: <PASTE HERE>
     - Covers the Layer Events strip. "Start collapsed" is `EditorPrefs.GetBool(ExpandedPrefKey, true)` → false
       in `Editor/ClipEditor/ActorEditor/LayerEventStripElement.cs`; the dim is `InactiveAlpha` in
       `LayerEventRowElement.DrawLane`.
     - Gate: `LayerEventRowResolverTests`, `ActorEditorPanelTests`. Commit A88-T10.
   - Owner's A92 T10 answer: <PASTE HERE>
     - Strings are in `Editor/ClipEditor/Editing/RefactorPromptEditing.cs`.
     - Gate: `RefactorTargetResolverTests`. Commit A92-T11.
   - A93 T16, A94 T14 and A95 T15 were answered 2026-09-14 and reworked as A93F–A95F. If the follow-up
     batch left its own checkpoint answers unrecorded, add slots for A93F T10, A94F T12 and A95F T10 here.
2. **Preflight:** `python Packages/com.worktreetoolkit/Tools~/worktree.py doctor --json`.
   - Stop if git fails, hooks aren't installed, or `brokerAlive` is false.
   - `stageBlockers` listing only the owner's uncommitted files is expected (trap 13).
3. **Baseline:** compile gate, then full suites (`DotsAnimationToolkit.Tests.EditMode`, then `.PlayMode`).
   - Expected: the totals the A93F–A95F close recorded (A93–A95 closed at EditMode 850, standing Conformance_A
     only, and PlayMode 285; A93F removes three fixtures and adds one).
4. **Unity-bound T0 work** (leads cannot do it); paste the results into that lead's prompt:
   - **A98 T0:** the D3 GIF timing probe (read A98 §2 D3 and §5 T0 in full; `execute_code` is CodeDom C# 6, no
     local functions). Also record whether `PreviewRenderUtility` owns the camera's target (grep `targetTexture`
     and `BeginPreview` in the preview controllers). The probe decides whether A98's GIF task runs; tell the lead
     the verdict.
   - **All three:** confirm CHANGELOG's top section is `## [0.45.0]` (or the newest owner-answer version).
5. Record the sha256 of `ProjectSettings/DotsAnimationToolkitAnimEventKeyRegistry.asset` and
   `ProjectSettings/DotsAnimationToolkitTargetTagRegistry.asset`. Record the results in each spec's §7 (commit only
   those files), push, so the worktrees branch from it.

## Phase 1 — spawn

Spawn three `Agent`s in one message: `subagent_type: spec-lead`, `model: opus`, background. Each prompt holds:
- the spec id and path;
- `worker model: sonnet`;
- that spec's Phase 0 results;
- the gate syntax;
- the lead contract below, verbatim.

**Gate syntax** (repeat the flag per fixture; use namespace-qualified names and always add the conformance
fixture):
`python Packages/com.worktreetoolkit/Tools~/worktree.py gate --edit-mode DotsAnimationToolkit.Tests.EditMode.<Fixture> --edit-mode DotsAnimationToolkit.Tests.EditMode.PackagingConformanceTests`.
A gate is real only when its passed count equals the tests named. Conformance_A is the one expected failure.

> **Lead contract**
> - **Your scope:** the spec's T0 grounding that needs no Unity (verify names by grep, make the code-reading
>   decisions, log drift in your spec's section 7), T1 shared types, every `[parallel-safe]` worker wave, and the
>   fixtures with their revert-to-fail.
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
>   - the exact wiring: enum member, UXML toggle and pane names, and the panel's constructor, Bind and Dispose
>     calls;
>   - vault-note traps;
>   - a one-paragraph HANDOFF draft.
> - **Build the panel so it works detached** (`new …Panel()` plus the spec's Bind/Dispose), so it can be wired
>   and proven without the window.
> - **Gates:** the syntax above. Gate only the fixtures your spec names plus `PackagingConformanceTests`, and only
>   after every worker in the wave has reported (trap 8). A "Unity is compiling" refusal means retry, not fail.
> - **Package files may not name `Assets/<Folder>`** except `Assets/Generated` (Conformance_D), strings, comments
>   and docs included. A fixture that must write under Assets creates a GUID-named folder and reads its path back.
> - **Revert-to-fail:** commit the mutation alone, then gate it and expect a failure. Then run
>   `git reset --hard HEAD~1` in your worktree and check the file's sha256 matches.
> - **No drives, full suites or execute_code.** Fixtures build everything with `CreateInstance`, never real
>   assets or registries.
> - **Conventions:** CLAUDE.md and roadmap section 3 (with MCP replaced by `worktree.py gate`).
>   - Worker briefs: at most two files, named line ranges, exact signatures pasted, and "add NO <summary> blocks"
>     for existing files. Commit committed stubs for every new shared type first.
>   - No `var` and no single-letter names.
>   - Conformance_F: one <summary> per file, max 3 lines, and no "§", "amendment A<n>", "Phase A-G",
>     "rule V<nn>", <remarks> or <para> anywhere, strings included.
>   - Conformance_E: UI Toolkit only; `DisplayDialog` is allowed.
>   - Conformance_G: static class suffixes are Api, Builder, Sampler, Resolver, Math, Validation, Utility
>     (Editor/ClipUtilities only) and Editing. Report anything that needs the allowlist.
> - **Git:** `git commit` takes the whole index, so run `git diff --cached --stat` before every commit. Stage paths
>   explicitly and never push.
> - **Settled owner calls, do not re-ask:**
>   - no package-side event handlers;
>   - no sound mixing;
>   - names, never numbers, in every editor surface;
>   - sprite sheets are Texture2DArrays, never atlases;
>   - nobody auto-writes arrays into materials (A95 §6).
> - **Finish** with `worktree.py status <id> ready` and a report of 30 lines or fewer: commits, gate verdicts with
>   pass counts, revert-to-fail result, drift count, where the For-integration block is, and what's unverified.
>   At turn 70, stop and report what's left.

## Phase 2 — while running

- Stay quiet; the broker resolves gates. If a lead sends "gate needed", follow the skill's four steps.
- Never run execute_code, tests or refresh on the stage while a gate may be running. Check
  `worktree.py list --json` `stage.busyWith` is null first.
- A capped lead is never resumed or re-spawned as a spec-lead (trap 5); spawn a `worker` with the worktree's
  absolute path for the remaining scope.

## Phase 3 — merge and integrate (you, on trunk)

1. **Merge** in tab order a96 → a97 → a98, each with `worktree.py merge <id>` then push, only while
   `stage.busyWith` is null.
   - On a conflict (exit 2), use the skill's worker-rebase flow.
   - Then `worktree.py remove <id>` for each.
2. **One integration commit** "A96-A98 integration", built from the three For-integration blocks:
   - enum members 10/11/12;
   - UXML toggles and panes at the end of the strip, after Health (the owner reordered the strip in `f51cad62`);
   - fields, pane lookups, BindTab, ApplyActiveTab Show calls, Show…Tab methods, and teardown in
     `ClipEditorWindow.cs`;
   - `tabToggles` sized 13;
   - both tab-name lists and the pane list in `ClipEditorLayoutTests`;
   - `index.md`;
   - CHANGELOG `## [0.48.0]`, `## [0.47.0]` and `## [0.46.0]`, newest on top;
   - `package.json` 0.48.0;
   - the conformance pin (comment gains 0.46.0–0.48.0, Assert at 0.48.0);
   - the Conformance_G allowlist.

   Then the compile gate, the three specs' fixtures + `ClipEditorLayoutTests` + `PackagingConformanceTests`.
3. **Full suites once.** Totals must not drop below the A93F–A95F close totals plus the new fixtures.
4. **Drives:** A96, A97 and A98's drive tasks, one at a time, following the cautions below. Record the results in
   each spec's §7. Proven patterns from A93–A95:
   - `EditorJsonUtility` round trip of a `CreateInstance` registry copy;
   - scratch assets + `Resources.UnloadAsset` + reload + YAML text checks;
   - detached panel construct / Bind / Dispose;
   - popups hosted in a temporary utility window, because a detached `PopupField` never dispatches `ChangeEvent`;
   - delete scratch with `safety_checks=false`.
5. **Close:**
   - each spec's status line, ticked task boxes, and a §7 close log;
   - HANDOFF §4 paragraphs at the top of the queue, A98 on top;
   - three sections appended to `AnimationToolkit.md`;
   - the roadmap status line and three checkpoint lines in the owner to-do block;
   - commit and push.

   Write the close texts to a scratchpad Python file and run it. A long Bash heredoc with apostrophes broke the
   shell wrapper in the A93–A95 close.
6. **Write the next batch prompt** in Assets/_Vault/Spencer/ (A99 if its ragdoll checkpoints are answered, A100).
7. **End with one owner checkpoint message** covering the three specs' checkpoints. Adapt it to settled drift,
   name real assets, and say exactly what the owner should see.

## Cautions (verify, log drift in section 7, escalate rather than quietly re-spec)

- **Owner's uncommitted work** (never stage, overwrite or write): whatever `git status` shows at start that you
  did not create. At the A93–A95 close that was:
  - `A63CheckpointCutscene.asset`, `NewClip.asset`, `NewClip 1.asset`;
  - `ProjectSettings/DotsAnimationToolkitAnimEventKeyRegistry.asset`, `ProjectSettings/EditorBuildSettings.asset`;
  - `Assets/SceneDependencyCache/*`, a screenshot in Tasks/AnimationPackage, and untracked `.meta` files.

  Re-check both registry sha256s after every drive.
- **A96** scans real materials; any create-from-template runs into an `Assets/A96Scratch/` folder that you delete.
- **A97** remaps tags. Every remap or `ReplaceTrackTag` drive runs on scratch clips and a scratch rig, never the
  project tag registry.
- **A98** writes PNG sequences (and a GIF if the probe passed) into `Assets/A98Scratch/` only. The capture
  renders through a preview camera, never the Scene or Game view. No Play mode.
- **Never call `AssetDatabase.SaveAssets()`**; save only scratch assets, with `SaveAssetIfDirty`.
- **Never run a player build.**
- **ClipEditorWindow** is single-instance and the owner's copy is docked. Don't drive it, rearrange it or load into
  it; don't open scenes or enter Play mode. Prove tabs on detached panels. Capture only if
  `EditorApplication.isFocused` is true; otherwise say so.
- **`EditorUtility.DisplayDialog` is modal:** never reach one from execute_code; call the operation beneath it.
- **Settled owner calls** (do not re-ask):
  - A87 D1/D5, A90, A91 ("works for now");
  - every A88, A92, A93, A94 and A95 §7 drift the owner has not overturned in Phase 0;
  - no sound mixing, no package-side event handlers, names never numbers.

## Inherited mechanics (do not re-derive)

- **Compile gate:** `refresh_unity` (compile: request, mode force), then read `mcpforunity://editor/state`
  (`is_compiling` false, `last_domain_reload_after_unix_ms` after your edit; `stale_status` clears by itself),
  then `read_console` (errors). "Refresh recovered after Unity disconnect" is normal.
- **Tests:** `run_tests` with `group_names` as one regex, e.g.
  `^DotsAnimationToolkit\.Tests\.EditMode\.(FixtureA|FixtureB)\.`, then `get_test_job` with `wait_timeout` 60.
  Never execute_code while a test job runs.
- **execute_code** is CodeDom C# 6:
  - no usings, no out var, no local functions (`System.Func` delegates are fine);
  - find package types by scanning AppDomain assemblies and use reflection; `out` parameters come back through
    the `Invoke` argument array;
  - UI Toolkit extension methods must be called statically: `UQueryExtensions.Query<T>(element, (string)null, (string)null)`;
  - a blob's `ref` API cannot be called by reflection (struct copies break offsets), so prove it by fixture;
  - `GetInstanceID()` fails to compile on 6.5.
- **Commits:** one per phase step, prefixed "A9x-Tn:" or "A96-A98 integration:". Stage paths explicitly, check
  `git diff --cached --stat` first, and push when green.
