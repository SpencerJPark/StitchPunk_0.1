# A102 — Release readiness: the three "before 1.0" checks and a green Conformance_A

> **Status:** 📝 specced 2026-09-15 from `Code_Audit_2026-09.md` §3.1. Takes `0.54.0` (CHANGELOG top is `0.53.1`).
> **Executor:** a `spec-lead` in its own worktree for the code half (§5 T1–T5); the **stage orchestrator** for the
> Editor-bound half (§6 S1–S4), because a player build and a batch-mode import cannot run through the broker.
> **Why now:** every roadmap box A82–A101 is ticked; `package.json` has said "remaining before 1.0: a clean-project
> import check, a player build, and a compile pass over Samples~" since 0.9.0; and every gate since July has
> reported "Conformance_A the standing failure". A gate that is always red is a gate nobody reads.

## 0. Session prompt

See `Assets/_Vault/Spencer/next-session-parallel-a102-despawn-minionorders-prompt.md`.

## 1. Decisions (recorded 2026-09-15 under the standing delegation — do not re-ask)

- **A102-D1 — The Editor assembly's URP reference is legitimate and stays.** `Editor/Capture/CutsceneCaptureSource.cs`
  and `Editor/ClipEditor/Cutscene/CutsceneViewportElement.cs` use `UnityEngine.Rendering.Universal`;
  `package.json` already depends on `com.unity.render-pipelines.universal`. Fix the *expectation*: the Editor row of
  `PackagingConformanceTests.AsmdefExpectations` gains `Unity.RenderPipelines.Universal.Runtime` as its last entry,
  and `Phase_B_Architecture.md` §1.3's Editor row gains the same reference with an "Amendment A102" sentence in the
  amendment style already used there (A17, A33).
- **A102-D2 — Samples~ get a compile check the suite can run.** A new EditMode fixture `SamplesCompileConformanceTests`
  does **not** compile C# (it cannot); it asserts the cheap invariants that caught the 2026-08-16 rot: every
  `Samples~/<Name>/**/*.asmdef` references only assemblies the package's own asmdefs reference or define; every
  sample `.cs` references only namespaces that resolve inside those assemblies' public types (regex over
  `using` lines against a list built from the package's own `namespace` declarations plus the Unity assemblies
  named); and every sample folder has exactly one asmdef. The real compile happens on the stage (§6 S1) and any
  error it finds is fixed by the lead (§5 T3).
- **A102-D3 — A clean-project import is a batch-mode Unity run, recorded in §7, not a test.** The stage creates a
  throwaway project, points its manifest at this package by `file:` path, lets the package's own `dependencies`
  pull Entities/URP, runs `-quit`, and greps the log. Pass = zero `error CS` and the package's Runtime, Authoring
  and Editor assemblies listed as compiled.
- **A102-D4 — The player build is the *game's* build**, Windows64, development, output outside the repo. Pass = the
  build finishes, or fails only on game-side errors the stage fixes mechanically (an Editor-only `using` in a
  runtime assembly, a missing `#if UNITY_EDITOR`). Package-side player errors go back to the lead as a wave.
  While at it, the A91 hook fires if a profile has a name error — leave `MaleCitizen.profile` intact and do not
  create the broken profile; the A91 check is the owner's (`verify-a91-player-build.md`).
- **A102-D5 — `package.json`'s description drops the "remaining before 1.0" sentence** once S1–S3 pass, replaced by
  one sentence naming what was checked and when. `README.md` gains a three-line "Verified" block with the same.

## 2. Files

**Lead (worktree):**
- `Tests/EditMode/PackagingConformanceTests.cs` — the Editor expectation row (D1); nothing else in this file.
- `Docs/AnimationToolkit/Phase_B_Architecture.md` — §1.3 Editor row + one amendment paragraph (D1). *(Exception to
  the "package files only" rule: this doc is the package's architecture record and lives outside it.)*
- `Tests/EditMode/SamplesCompileConformanceTests.cs` — new (D2).
- `Samples~/**` — only what the stage's compile report (§6 S1) names.
- `Documentation~/getting-started.md` — an "Importing into another project" paragraph (the `file:` and git-URL
  forms, the URP requirement, the Camera Sync sample).
- `package.json` description and `README.md` — D5, **last**, after the stage reports S1–S3 green.

**Stage only:** `Assets/Generated/DotsAnimationToolkit/SamplesCompileCheck/` (temporary, deleted after S1), the
throwaway project under `%TEMP%`, the build output under `%TEMP%`.

## 3. Read (line ranges)

- `Tests/EditMode/PackagingConformanceTests.cs`: the `AsmdefExpectations` array (grep `assemblyName =`), and
  the `Conformance_C` scan (grep `Samples~`) for the file-walk pattern to reuse.
- `Phase_B_Architecture.md` lines 91–120 (§1.3 and its amendments A17, A33).
- `Samples~/*/` — the four asmdefs and four `.cs` files, whole (each under 200 lines).
- `Assets/_Vault/Memories/Code/AnimationToolkit.md` — grep `Samples~`.

## 4. Fixtures

- `PackagingConformanceTests.Conformance_A_AsmdefReferenceLists_MatchSection13Exactly` — the existing test goes
  green. Revert-to-fail: drop the new entry, it fails on the Editor row (it does today).
- `SamplesCompileConformanceTests.EverySampleAsmdef_ReferencesOnlyKnownAssemblies` — revert-to-fail: add a
  fake reference to one sample asmdef in the mutation commit.
- `SamplesCompileConformanceTests.EverySampleSource_UsesOnlyResolvableNamespaces` — revert-to-fail: add
  `using DotsAnimationToolkit.DoesNotExist;` to one sample.

## 5. Tasks — lead

- [ ] **T0 — Ground.** `git rev-parse --show-toplevel`; claim `a102`; grep the names in §2 and §3; read the stage's
  S1 report pasted into your prompt. Log drift in §7.
- [ ] **T1 — Conformance_A green** `[parallel-safe]` (one worker, two files): the expectation row and the §1.3
  amendment. Gate: `PackagingConformanceTests`. This is the first gate in months where Conformance_A must pass.
- [ ] **T2 — SamplesCompileConformanceTests** `[parallel-safe]` (one worker, one new file). Build the allowed-assembly
  list from the package's four asmdefs (Runtime, Runtime.Physics, Authoring, Editor) plus their references; the
  namespace list from a regex over `namespace ` lines in `Runtime/`, `Authoring/`, `Editor/`. Gate it with its
  two revert-to-fail mutations.
- [ ] **T3 — Sample rot** (after T0; one worker per sample that the S1 report names, at most two files each). Fix
  only what the compile error says. If S1 reported clean, tick this and say so.
- [ ] **T4 — getting-started paragraph** `[parallel-safe]` (docs worker, one file).
- [ ] **T5 — Close text.** Wait for the stage's message that S1–S3 are green (or its list of what is not), then one
  worker edits `package.json` description and `README.md` (D5). Write §7's `### For integration` block: the
  0.54.0 CHANGELOG text, a HANDOFF paragraph, the vault trap(s). `status a102 ready`.

## 6. Tasks — stage orchestrator (Editor-bound, never a lead)

- [ ] **S1 — Samples~ compile.** Before spawning the lead: copy each `Samples~/<Name>/` into
  `Assets/Generated/DotsAnimationToolkit/SamplesCompileCheck/<Name>/` (asmdef included, `name` field suffixed
  `.Check`), `refresh_unity` with compile, `read_console` for `error CS`/`DC`, record every line in this spec's §7
  and in the lead's prompt, then delete the folder and its `.meta`, refresh again, `git status` clean.
- [ ] **S2 — Player build.** Phase 0, stage on trunk, before any lead gates (a gate swaps the stage under a running
  build): `manage_build` action `build`, target `windows64`, development `true`, output
  `%TEMP%\A102Build\StitchPunk.exe`. Poll `status`. Record the result and every error line in §7. Fix mechanical
  game-side errors on the stage with a `worker` (commit with an `A102-S2:` prefix); send package-side ones to the
  lead as a T3-style wave.
- [ ] **S3 — Clean-project import**, in the background while leads run (it is a separate project and process):
  `"C:\Program Files\Unity\Hub\Editor\6000.5.0f1\Editor\Unity.exe" -batchmode -nographics -createProject
  %TEMP%\A102Import -quit -logFile %TEMP%\A102Import-create.log`; then write its `Packages/manifest.json` with
  `"com.dotsanimationtoolkit": "file:<absolute repo path>/Packages/com.dotsanimationtoolkit"` beside the
  default manifest entries; run again with `-projectPath %TEMP%\A102Import -quit -logFile
  %TEMP%\A102Import-open.log`; grep the open log for `error CS`, `Failed to resolve` and the three assembly
  names. Record in §7. Delete both folders after.
- [ ] **S4 — Integration and close.** Merge `a102` after `despawn` and `minion-orders` (it touches no game file, so
  order is free; last keeps its `package.json` bump on top). CHANGELOG `## [0.54.0] — Release readiness` from the
  lead's block; `package.json` and the conformance pin at `0.54.0`; HANDOFF §4 paragraph on top; the roadmap gains
  a "Phase 4 — release readiness" box, ticked; `AnimationToolkit.md` traps; `Code_Audit_2026-09.md` §3.1 marked
  done. Full suites once: EditMode must report **zero** failures for the first time (Conformance_A green);
  PlayMode 285 or more. Commit, push.

## 7. Build log

*(T0 grounding, S1–S3 results, drift, the For-integration block — written by the sessions that run this.)*

**Phase 0 (stage, 2026-09-15).** Trunk `faebc8eb`; `doctor` clean, `brokerAlive` true, no worktrees. CHANGELOG top
`## [0.53.1]`, `package.json` `0.53.1`. Registry sha256: `DotsAnimationToolkitAnimEventKeyRegistry.asset`
`3bdb420d55b808ecfd9251ab144ac89645c4d6f903b4a8a3498a42aa76d14701`; `DotsAnimationToolkitTargetTagRegistry.asset`
`dbec3d5f6d31db02891682e7f88e6011f7317658f1d29753a0185ff2ebd1eb4f`. Baseline: EditMode 866 (865 passed, Conformance_A
the only failure: Editor row expected 7 references, actual 8, extra `Unity.RenderPipelines.Universal.Runtime`), PlayMode
285 of 285. Game baseline in the Despawn and Minion Order specs' §12.4 (the owner's damage stub needed an
`[UpdateInGroup]`, `529e8bcc`).

**S1 — Samples~ compile: clean.** All four samples (`CameraSync`, `CompositeActor`, `Cutscene`, `QuickStartActor`) copied
to `Assets/Generated/DotsAnimationToolkit/SamplesCompileCheck/<Name>/` with asmdef names suffixed `.Check`; forced
refresh with compile; zero `error CS` / `DC` lines; all four `.Check` assemblies loaded in the AppDomain (9, 6, 9, 6
types) and present in `Library/ScriptAssemblies`. Folder and `.meta` deleted, refreshed, `git status` clean. T3 has no
rot to fix.

**S2 — Player build: succeeded, first round, no fixes.** `manage_build` build, `windows64`, development, output
`%TEMP%\A102Build\StitchPunk.exe`, scenes passed as build options only (`["Assets/Scenes/Game.unity"]`: the Build
Settings list holds just `Main.unity`, disabled, and was not changed). 392 s, **0 errors**, 488 warnings, 257.1 MB. No
`error CS`, no Burst `BC` errors; the Burst player compile peaked near 9 GB and left the machine with ~200 MB free (two
background log watchers were killed for memory — nothing else was). The A91 build preprocessor raised nothing
(`MaleCitizen.profile` untouched). Warnings worth the owner's eyes, not package-side: `Game.unity`'s `GameInitiator`
has a missing script; `Core/BaseClasses/RegulatorSingleton.cs(41)` uses the obsolete
`FindObjectsByType<T>(FindObjectsSortMode)` (CS0618). The Performance Testing package writes
`Assets/Resources/PerformanceTestRun*.json` during a build and removes them after; the tree was clean afterwards. The
A91 check (`verify-a91-player-build.md`) stays the owner's, but its old blocker is gone. Nothing for T3.
