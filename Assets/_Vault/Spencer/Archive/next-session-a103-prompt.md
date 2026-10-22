You are the **stage orchestrator** for one package spec run through the Worktree Toolkit (`/worktree-run`,
`Packages/com.worktreetoolkit`): **A103 — Unified authoring and the whole-animation VAT preview**, one `spec-lead` in its
own git worktree. You alone touch `mcp__UnityMCP__*`, merge, wire the window, drive and close.

| Spec id | Path | What it is | Version / suites |
|---|---|---|---|
| `a103` | `Assets/_Vault/Tasks/AnimationPackage/A103_UnifiedAuthoringAndVatPreview_Spec.md` | UA P1–P5 + A79. A shared `RegistryTargetPoser`, so a targetless rig previews. Baked VAT in the Clip Editor viewport, plus a VAT binding row and read-only imported-clip lanes. The VAT Bake preview plays every part and poses the cutout half behind *VAT parts* / *Other parts* toggles. | `0.55.0`; `DotsAnimationToolkit.Tests.*` |

Why this one: `Code_Audit_2026-09.md` §3 item 2 and §5 item 3. It is the owner's own directive ("I should be animating
everything in the same window, vat bones, object transforms, flipbooks") and the last package gap the audit lists
before HANDOFF hygiene. The package is at `0.54.0` after A102: EditMode 868 with **zero failures**, PlayMode 285.

Read, in order:
1. `CLAUDE.md` (root), `.claude/skills/worktree-run/SKILL.md`, `.claude/agents/spec-lead.md`,
   `Assets/_Vault/Memories/Code/WorktreeToolkit.md` (all 26 traps; 17–26 are the batch ones).
2. `Assets/_Vault/Tasks/AnimationPackage/AnimationPackage_Roadmap.md` §3 (the execution protocol) and §2.
3. The A103 spec **whole**. Its §1.1 lists ten drifts in the two source specs. Do not read
   `UnifiedClipAuthoring_System.md` or `Amendment_A79_VatPreviewModes_Spec.md` for instructions; A103 supersedes both.
4. `Assets/_Vault/Memories/Code/AnimationToolkit.md` lines 590–700: the VAT preview's source-copy ownership sweep,
   the two registry gates, and cover-pane disposal.
5. The prompt chain the lead contract comes from: `next-session-parallel-a102-despawn-minionorders-prompt.md` →
   `next-session-parallel-a96f-a97f-a99-prompt.md` → `next-session-parallel-a96-a98-prompt.md`. The contract below is
   copied verbatim from the last; its gate syntax, cautions and inherited mechanics carry over unless this file says
   otherwise.

**Owner pre-answers (do not ask):**
- Models: lead `opus`, worker `sonnet`. Skip the `AskUserQuestion` step of the skill.
- Merge: `worktree.py merge a103` is authorized once the lead reports `ready` with W1–W3 gates green. Push after it.
  Delete this line to require my word.
- Checkpoint: A103 §6 S6 has three questions. If I am away, close it under the standing rule (accepted unless game
  breaking) and keep the questions in HANDOFF §4 with the captures' paths.
- Play mode: **not authorized.** No scene edits, no `SaveAssets`, no Play. PlayMode fixtures through the Test Runner
  are fine.

## Phase 0 — stage prep (A103 §6 S0)

1. **Preflight:** `ListAgents` (trap: a clean `git status` is not a free trunk); `python
   Packages/com.worktreetoolkit/Tools~/worktree.py doctor --json`. Stop on git failure, missing hooks or
   `brokerAlive` false; retry once first, since the broker's heartbeat can blip. `git status` must be clean apart
   from what you create. Delete any empty `.claude/worktrees/*` folder that is no longer locked.
2. **Baseline:** compile gate, then `DotsAnimationToolkit.Tests.EditMode` (expected 868, **zero failures**) and
   `.PlayMode` (285). Anything red now is real — record it and stop if it touches a §4 regression fixture.
3. **Versions:** CHANGELOG top section `## [0.54.0]`, `package.json` `0.54.0`.
4. **Registry sha256s:** record `ProjectSettings/DotsAnimationToolkitAnimEventKeyRegistry.asset` and
   `ProjectSettings/DotsAnimationToolkitTargetTagRegistry.asset`.
5. **Sample assets:** confirm `Assets/ScriptableObjects/Animations/VatSampleTentacle/` still holds
   `VatSampleTentacleRig.asset` (zero targets) and `VatSampleTentacleClips.asset` — the S4(a) drive depends on both.
6. Record Phase 0 in A103 §7; commit only that file; push, so the worktree branches from it.

## Phase 1 — spawn

One `spec-lead`, `model: opus`, background. Its prompt holds: spec id `a103`, the spec path, `worker model: sonnet`,
the Phase 0 results, the gate syntax, the lead contract below verbatim, and these **A103 additions**:

- The waves are the spec's: **W1** is six workers (T1–T6), **W2** five (T7–T11), **W3** three (T12–T14); T15 is
  the lead's close text. Commit `RegistryTargetPoser` stubs before W1 spawns. W2 briefs paste its real signatures.
- **Revert-to-fail:** one mutation commit per wave carries every fixture's revert; the gate must fail exactly those
  tests (W1: F2–F5; W2: F1).
- **Additional stage-owned files for this spec:** `ClipEditorWindow.uss`,
  `Docs/AnimationToolkit/Amendment_A78_VatBakeSourceFromRig_Spec.md` and `Code_Audit_2026-09.md`. `VatBakePanel.cs`
  and `VatPreviewElement.cs` are the lead's (T12). `ClipEditorWindow.ComponentStack.cs` is **not** (it matches
  `ClipEditorWindow*.cs`): T10 builds the binding row as a standalone element that works detached, and you wire it.
- **No PlayMode gate is expected:** A103 changes no runtime or `Authoring/` file (A103-D13). A lead that finds it
  must, stops and asks.
- **Conformance_I** (A101) bans inline visual styles in editor sources: new elements use existing `toolkit-` USS
  classes, and a needed new class goes into the For-integration block for you.

**Gate syntax** (repeat the flag per fixture; namespace-qualified names; always add the conformance fixture):
`python Packages/com.worktreetoolkit/Tools~/worktree.py gate --edit-mode DotsAnimationToolkit.Tests.EditMode.<Fixture> --edit-mode DotsAnimationToolkit.Tests.EditMode.PackagingConformanceTests`.
A gate is real only when its passed count equals the tests named. **Since A102 there is no expected failure:** a red
`Conformance_A` is now a real failure.

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

- Stay quiet; the broker resolves EditMode gates. If the lead sends "gate needed", follow the skill's four steps, only
  while `list --json` says `stage.busyWith` is null, then `git status` for stray folder metas (trap 26).
- Never run `execute_code`, tests or refresh on the stage while a gate may be running.
- A capped lead is never resumed or re-spawned as a `spec-lead` (trap 5). Read its diff, then spawn a `worker` with
  the worktree's absolute path for the remaining scope; W2 and W3 are already cut into two-file tasks.

## Phase 3 — merge, wire, drive, close (A103 §6 S2–S6)

1. **Merge** `a103` (while `stage.busyWith` is null), push, `worktree.py remove a103` (trap 24 if WinError 32).
2. **Integration commit "A103 integration"** from the lead's For-integration block. Wire:
   - UXML `baked-vat-preview-toggle` + `baked-vat-preview-icon` after `ragdoll-preview-toggle`;
   - the toggle's binding in `ClipEditorWindow.cs` (`VatPartsGlyph`; value →
     `previewController.BakedVatPreviewEnabled`; disabled with a tooltip without `clipSet.vatTextures`);
   - the `VatBinding` case and picker entry in `ClipEditorWindow.ComponentStack.cs`;
   - the USS modifier;
   - `ClipEditorLayoutTests`' toggle list.

   Then compile gate, the five §4 fixtures + the regression set + `ClipEditorLayoutTests` +
   `PackagingConformanceTests`.
3. **Full suites once:** EditMode ≥ 868 + the new tests with zero failures; PlayMode 285.
4. **Drives** — A103 §6 S4 (a)–(e), one at a time, scratch only (`Assets/A103Scratch/`), results in §7.
   - Never drive the docked Clip Editor: use a detached `ClipPreviewController`, a floating `VatBakeWindow` (open,
     drive, close), and detached elements in a temporary utility window.
   - Never touch `MaleCitizen.prefab` or `NewRig.asset`.
   - Read posed transforms in a **separate** `execute_code` call from the one that scrubbed (vault trap).
   - Captures only when `EditorApplication.isFocused` (scale by `pixelsPerPoint`), into `Library/A103Captures/`.
   - Delete scratch with `safety_checks=false`; re-check both registry sha256s.
5. **Close** — A103 §6 S5 exactly:
   - CHANGELOG `## [0.55.0] — Unified authoring and VAT preview`;
   - `package.json` and the conformance pin at `0.55.0`;
   - HANDOFF §4 paragraph on top;
   - A78's status line (T8 closed under the standing rule, A103-D5);
   - A103 status and boxes;
   - roadmap Phase 5 box ticked;
   - `Code_Audit_2026-09.md` §3.2 and §5.3 done;
   - the `AnimationToolkit.md` section.

   Write the close texts with a scratchpad Python file (a long heredoc broke the shell wrapper before). Commit; push.
6. **End** with one message for the owner:
   - the lead's ready report, condensed;
   - the drive results;
   - S6's three questions, naming the captures and the real assets to open;
   - the next prompt: HANDOFF hygiene (audit §3.3) or the game queue's Ranged spec (audit §5.2), whichever the
     owner has not started.

## Cautions (verify, log drift in §7, escalate rather than quietly re-spec)

- **Owner's uncommitted work:** never stage, overwrite or write whatever `git status` shows at start that you did
  not create.
- **Never call `AssetDatabase.SaveAssets()`**; save only scratch assets, with `SaveAssetIfDirty`. Never run a player
  build.
- **`EditorUtility.DisplayDialog` is modal:** never reach one from `execute_code`; call the operation beneath it.
- **The VAT preview's source copy is `HideAndDontSave`** and outlives a domain reload. After the drives,
  `Resources.FindObjectsOfTypeAll<GameObject>()` must show no `VatPreviewSourceCopy` that no live preview owns.
- **A `ClipPreviewController` or `VatPreviewElement` you construct owns a `PreviewRenderUtility` and a `Persistent`
  blob.** Dispose every one you create in the same drive.

## Inherited mechanics (do not re-derive)

- **Compile gate:** `refresh_unity` (compile: request, mode force), then read `mcpforunity://editor/state`
  (`is_compiling` false, `last_domain_reload_after_unix_ms` after your edit), then `read_console` (errors).
- **Tests:** `run_tests` with `group_names` as one regex, e.g.
  `^DotsAnimationToolkit\.Tests\.EditMode\.(FixtureA|FixtureB)\.`, then `get_test_job` with `wait_timeout` 60.
  Never `execute_code` while a test job runs.
- **`execute_code`** is CodeDom C# 6:
  - no usings, no `out var`, no local functions;
  - find package types by scanning AppDomain assemblies and use reflection;
  - UI Toolkit extensions are called statically;
  - a blob's `ref` API cannot be called by reflection (prove it by fixture);
  - `GetInstanceID()` fails to compile on 6.5.
- **Commits:** one per phase step, prefixed `A103-S<n>:` or `A103 integration:`. Stage paths explicitly, check
  `git diff --cached --stat` first, and push when green.

## Settled owner calls (do not re-ask)

- **Product:** names never numbers; no manual asset wiring; no package-side event handlers; no sound mixing; sprite
  sheets are Texture2DArrays.
- **Process:** new rigs are created fresh, with no migration paths; unseen checkpoints close as accepted unless game
  breaking; visual-first editor tools.
- **A103's own:** A103-D1–D13 as written, including UA's three: a targetless rig is fine with bone or VAT content;
  empty-target registry; imported clips are read-only lanes with no import action.
