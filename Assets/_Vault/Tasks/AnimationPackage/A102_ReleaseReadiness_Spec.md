# A102 — Release readiness: the three "before 1.0" checks and a green Conformance_A

> **Status:** ✅ built 2026-09-15 as `0.54.0` (A102 / Despawn / Minion Orders parallel batch); no owner checkpoint. Specced from `Code_Audit_2026-09.md` §3.1.
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

- [x] **T0 — Ground.** `git rev-parse --show-toplevel`; claim `a102`; grep the names in §2 and §3; read the stage's
  S1 report pasted into your prompt. Log drift in §7.
- [x] **T1 — Conformance_A green** `[parallel-safe]` (one worker, two files): the expectation row and the §1.3
  amendment. Gate: `PackagingConformanceTests`. This is the first gate in months where Conformance_A must pass.
- [x] **T2 — SamplesCompileConformanceTests** `[parallel-safe]` (one worker, one new file). Build the allowed-assembly
  list from the package's four asmdefs (Runtime, Runtime.Physics, Authoring, Editor) plus their references; the
  namespace list from a regex over `namespace ` lines in `Runtime/`, `Authoring/`, `Editor/`. Gate it with its
  two revert-to-fail mutations.
- [x] **T3 — Sample rot** (after T0; one worker per sample that the S1 report names, at most two files each). Fix
  only what the compile error says. If S1 reported clean, tick this and say so.
- [x] **T4 — getting-started paragraph** `[parallel-safe]` (docs worker, one file).
- [ ] **T5 — Close text.** Wait for the stage's message that S1–S3 are green (or its list of what is not), then one
  worker edits `package.json` description and `README.md` (D5). Write §7's `### For integration` block: the
  0.54.0 CHANGELOG text, a HANDOFF paragraph, the vault trap(s). `status a102 ready`.

## 6. Tasks — stage orchestrator (Editor-bound, never a lead)

- [x] **S1 — Samples~ compile.** Before spawning the lead: copy each `Samples~/<Name>/` into
  `Assets/Generated/DotsAnimationToolkit/SamplesCompileCheck/<Name>/` (asmdef included, `name` field suffixed
  `.Check`), `refresh_unity` with compile, `read_console` for `error CS`/`DC`, record every line in this spec's §7
  and in the lead's prompt, then delete the folder and its `.meta`, refresh again, `git status` clean.
- [x] **S2 — Player build.** Phase 0, stage on trunk, before any lead gates (a gate swaps the stage under a running
  build): `manage_build` action `build`, target `windows64`, development `true`, output
  `%TEMP%\A102Build\StitchPunk.exe`. Poll `status`. Record the result and every error line in §7. Fix mechanical
  game-side errors on the stage with a `worker` (commit with an `A102-S2:` prefix); send package-side ones to the
  lead as a T3-style wave.
- [x] **S3 — Clean-project import**, in the background while leads run (it is a separate project and process):
  `"C:\Program Files\Unity\Hub\Editor\6000.5.0f1\Editor\Unity.exe" -batchmode -nographics -createProject
  %TEMP%\A102Import -quit -logFile %TEMP%\A102Import-create.log`; then write its `Packages/manifest.json` with
  `"com.dotsanimationtoolkit": "file:<absolute repo path>/Packages/com.dotsanimationtoolkit"` beside the
  default manifest entries; run again with `-projectPath %TEMP%\A102Import -quit -logFile
  %TEMP%\A102Import-open.log`; grep the open log for `error CS`, `Failed to resolve` and the three assembly
  names. Record in §7. Delete both folders after.
- [x] **S4 — Integration and close.** Merge `a102` after `despawn` and `minion-orders` (it touches no game file, so
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

**S3 — Clean-project import: green.** `Unity.exe -batchmode -nographics -createProject %TEMP%\A102Import -quit` (exit 0), then `Packages/manifest.json` gained `"com.dotsanimationtoolkit": "file:C:/Users/spenc/Documents/GitHub/Stitch_Punk/Packages/com.dotsanimationtoolkit"` beside the 35 default entries, then `-projectPath %TEMP%\A102Import -quit` (exit 0). The package's own `dependencies` resolved Entities/URP with no manual entries: **0 `error CS`, 0 `Failed to resolve`**; `DotsAnimationToolkit.Runtime`, `.Authoring` and `.Editor` each Csc-compiled and copied to `Library/ScriptAssemblies` (plus `.Runtime.Physics` imported). The toolkit logged nothing on first import; the only files written into the fresh project were URP's own defaults (`UniversalRenderPipelineGlobalSettings.asset`, `DefaultVolumeProfile.asset`, with URP's standard "Global Settings Asset has been created for you" warning). Remaining log errors were batch-mode licensing handshake noise only. Throwaway project deleted.

**T0 — Grounding (lead, worktree `spec/a102`, 2026-09-15).** Claimed `a102` (opus lead, sonnet workers). Every name in §2/§3 resolved: `AsmdefExpectations` Editor row at `PackagingConformanceTests.cs` ~130 held 7 references, the asmdef 8 (URP last); `PackageRootPath`, `StripComments` and `ToPackageRelativePath` are `internal static` on that fixture, so the new fixture reuses them. Package namespaces on disk: `DotsAnimationToolkit` (Runtime), `DotsAnimationToolkit.Physics` (Runtime.Physics), `DotsAnimationToolkit.Authoring`, `DotsAnimationToolkit.Editor`. Samples: `CameraSync` and `Cutscene` asmdefs at the folder root (all platforms), `CompositeActor` and `QuickStartActor` under `Editor/` (Editor platform). T3: S1 reported clean, nothing to fix — ticked. Drift (5):
1. §3 says the four sample `.cs` files are each under 200 lines; `CompositeActorBuilder.cs` is 406 and `QuickStartActorBuilder.cs` 220. Not read whole (S1 clean made it unnecessary).
2. §1.3's closing sentence said samples reference "Runtime+Authoring only"; `CameraSync` and `Cutscene` reference no Authoring and every sample references Unity assemblies. Corrected in T1.
3. D2 names three invariants, §4 names two fixtures: the exactly-one-asmdef check is folded into `EverySampleAsmdef_ReferencesOnlyKnownAssemblies` so the fixture stays two tests with two mutations.
4. T2's namespace regex list names `Runtime/`, `Authoring/`, `Editor/`; `Runtime.Physics/` declares `DotsAnimationToolkit.Physics` and is scanned too (it is one of the four asmdefs the allowed-assembly list is built from).
5. `README.md` "Not shipped yet" still says "Two samples ship" and that no `VatCrowd` sample is packaged; four ship today (Camera Sync and Cutscene were added). Outside D5's three-line block — left for the stage (noted under For integration).

**T1/T2/T4 wave (lead).** Workers: T1 (expectation row + §1.3 row, Amendment A102 paragraph, samples sentence), T2
(new fixture + `.meta`), T4 (getting-started section). Lead review caught one T2 defect before the gate: the
known-assembly list held only the four package assembly *names*, not their references, so every sample's
`Unity.Entities` reference would have failed; fixed with `BuildKnownAssemblyNames()`. Commit `9d8ed2d8`.
- **Gate 1 (`9d8ed2d8`): pass, 14 of 14** (PackagingConformanceTests 12, SamplesCompileConformanceTests 2).
  Conformance_A green for the first time since July.
- **Revert-to-fail:** mutation commit (fake `DotsAnimationToolkit.FakeMutationReference` in the CameraSync asmdef;
  `using DotsAnimationToolkit.DoesNotExist;` in `CutsceneSampleHost.cs`). First gate refused ("Unity is compiling"),
  retry: **test-failures, 12 passed, exactly the two new tests failed**, each naming its mutation. Hard-reset one
  commit; sha256 of both files matched the pre-mutation values (CameraSync asmdef `51f26aba...`, CutsceneSampleHost
  `e270c161...`). Conformance_A's own revert-to-fail is the Phase 0 baseline (it failed on exactly this row).
- **T3:** S1 clean, nothing to fix.

### For integration

**CHANGELOG `## [0.54.0] — Release readiness`:**
- Conformance_A is green: the Editor assembly's `Unity.RenderPipelines.Universal.Runtime` reference (used by the
  cutscene viewport and capture source; `package.json` has always depended on URP) is now in the expected reference
  list and the architecture record. The EditMode suite has no standing failure.
- New `SamplesCompileConformanceTests`: `Samples~` is excluded from Unity compilation, so the suite now checks on disk
  that every sample has exactly one asmdef referencing only assemblies the package defines or references, and that
  every sample `using` directive resolves in the assemblies its asmdef references (package namespaces matched
  exactly).
- Release checks run 2026-09-15 on Unity 6000.5.0f1: all four samples compiled as copied assemblies with zero errors;
  a Windows64 development player build of the host game finished with zero errors; a clean project referencing the
  package by `file:` path resolved its dependencies and compiled Runtime, Authoring and Editor with zero errors.
- `package.json` description: the "remaining before 1.0" sentence is replaced by the checks above; `README.md` gains a
  Verified section; getting-started gains "Importing into another project" (`file:` and git `?path=` forms, the URP
  pipeline-asset requirement, the Camera Sync sample).

**Conformance_G allowlist:** none (no static classes added). **Wiring:** none (no tab, panel, enum member or UXML).
**Pin/version:** the stage bumps `package.json` `version`, the conformance pin and CHANGELOG to 0.54.0.

**Vault traps (`AnimationToolkit.md`, next to the `Samples~` compile-check line):**
- `SamplesCompileConformanceTests` catches asmdef-reference and `using` rot only, never an API signature change inside
  a resolvable namespace. The copy-and-compile check (S1) is still the real compile; run it before any release.
- Package namespaces are matched exactly, Unity namespaces by prefix. A new package sub-namespace is picked up from
  `namespace` declarations automatically, but a sample referencing a Unity assembly whose namespace differs from its
  name (like `Unity.Entities.Graphics` -> `Unity.Rendering`) needs a row in `MapNonPackageAssemblyToNamespacePrefix`.
- A new sample must keep exactly one asmdef somewhere under its folder, or the fixture flags it.

**Drift for the stage:** `README.md` "Not shipped yet" still says two samples ship; four do (drift 5 above) — a
one-line README fix at integration.

**HANDOFF §4 paragraph (draft):** A102 Release readiness (0.54.0, 2026-09-15). The three "before 1.0" checks the
package description carried since 0.9.0 are done: the four `Samples~` compiled clean as copied assemblies, the game's
Windows64 development player build finished with zero errors, and a batch-mode clean project importing the package by
`file:` path compiled Runtime, Authoring and Editor with zero errors. Conformance_A is green (the Editor row now lists
URP, recorded as Amendment A102 in the architecture doc), so a package EditMode run reports zero failures for the first
time since July, and any red conformance test is now real. `SamplesCompileConformanceTests` guards sample asmdef
references and `using` directives on disk (revert-to-fail proven for both); it does not replace a real sample compile.
Getting-started documents importing into another project. Unverified: the owner's A91 player-build check stays his;
no Play mode was run.

**S4 — Integration and close (stage, 2026-09-15).** Merged `despawn` → `minion-orders` (integration `f3dc6619`) → `a102`
(`75909632`), each pushed and its worktree removed. CHANGELOG `## [0.54.0] — Release readiness` from the lead's block;
`package.json` and the conformance pin at `0.54.0`; README's sample list now names all four shipped samples (lead drift
5); HANDOFF §4 paragraph on top; roadmap Phase 4 box ticked and status line brought past A100; `AnimationToolkit.md`
gains a Release readiness section (the lead's three traps plus the build and import mechanics); `Code_Audit_2026-09.md`
§3.1 marked done. Compile gate clean. Full suites: **`DotsAnimationToolkit.Tests.EditMode` 868 of 868 — zero failures**
(Conformance_A green; +2 `SamplesCompileConformanceTests`), `.PlayMode` 285 of 285, `StitchPunk.Tests` 68 of 68,
`StitchPunk.Tests.PlayMode` 19 of 19. Registry sha256s unchanged; `%TEMP%\A102Build` and `%TEMP%\A102Import` deleted.
