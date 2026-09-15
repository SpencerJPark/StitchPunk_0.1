You are the **stage orchestrator** for a parallel batch: one package spec and two game specs run at once in their own
git worktrees, each under a `spec-lead`, through the Worktree Toolkit (`/worktree-run`, `Packages/com.worktreetoolkit`).
You alone touch `mcp__UnityMCP__*`, merge, integrate, drive and close. The owner is away from the computer: every
owner checkpoint closes under the standing rule (accepted unless game breaking), and the merges below are
pre-authorized.

| Spec id | Path | What it is | Version / suites |
|---|---|---|---|
| `a102` | `Assets/_Vault/Tasks/AnimationPackage/A102_ReleaseReadiness_Spec.md` | Package release readiness: Conformance_A green, a Samples~ conformance fixture, sample rot fixed, import docs; **you** run the Samples~ compile, the player build and the clean-project import (§6 S1–S4) | `0.54.0`; `DotsAnimationToolkit.Tests.*` |
| `despawn` | `Assets/_Vault/Tasks/Plans/Despawn_System.md` (§12 is the build plan) | `DespawnMode` + `Lifetime`, `LifetimeSystem` + `DespawnSystem` in `Systems/DespawnSystemGroup/`, one PlayMode fixture | game; `StitchPunk.Tests`, `StitchPunk.Tests.PlayMode` |
| `minion-orders` | `Assets/_Vault/Tasks/Plans/MinionOrderRobustness_System.md` (§12 is the build plan) | order-time attack resolution via `AIUtils.ResolveOrderedAttack`, Stop (X) and ReturnToPlayer (R) verbs, one EditMode fixture | game; `StitchPunk.Tests` |

Why these three: `Code_Audit_2026-09.md` §5 items 1 and 2. A102 is the only thing between "roadmap complete" and
"sellable". Despawn is the queue's first prerequisite (projectile pooling); Minion Orders is the second (the
hard-coded `MeleeSingle` order breaks on the first ranged minion). Zombie Conversion is already built through phase 1
and is not in this batch. The three touch disjoint files: `a102` edits only package tests, `Docs/AnimationToolkit/`,
`Samples~` and `Documentation~`; `despawn` edits `Components/Spawners`, `Data/Enums`, `Authoring/`,
`Systems/DespawnSystemGroup/`, `Tests/PlayMode/`; `minion-orders` edits `Utils/AIUtils.cs`, `Utils/UnitBakingUtil.cs`,
`Components/Player/`, `Systems/MinionActionSelectionSystemGroup/`, `MonoBehaviours/Managers/UnitSelectionManager.cs`,
`Tests/`. The three vault notes both game leads touch (`Systems.md`, `Components.md`, `Contracts.md`) are the one
expected merge conflict — take both sides.

Read, in order:
1. `CLAUDE.md` (root), `.claude/skills/worktree-run/SKILL.md`, `.claude/agents/spec-lead.md`,
   `Assets/_Vault/Memories/Code/WorktreeToolkit.md` (all 26 traps; 17–26 are the batch ones).
2. `Assets/_Vault/Tasks/AnimationPackage/AnimationPackage_Roadmap.md` §3 (the execution protocol) and §2.
3. `Assets/_Vault/Tasks/Claude/Code_Audit_2026-09.md` §1, §3, §5.
4. The three specs: A102 whole; the two game specs' §12 whole and §1–§2 for context. `Assets/_Vault/Memories/Code/RULES.md`.
5. `Assets/_Vault/Spencer/next-session-parallel-a96f-a97f-a99-prompt.md` and the a96–a98 prompt it points at: the
   lead contract, gate syntax, cautions and inherited mechanics carry over unless this file says otherwise. Copy the
   lead contract into each lead's prompt verbatim, with the game-side substitutions below.

**Owner pre-answers (do not ask):**
- Models: lead `opus`, worker `sonnet` for all three. Skip the `AskUserQuestion` step of the skill.
- Merges: `worktree.py merge` authorized for `despawn`, `minion-orders` and `a102`, in that order, once each reports
  ready with its gates green. Push after each.
- Checkpoints: none of the three has an owner checkpoint; what needs eyes goes into each spec's log and the closing
  message, not a stop.
- Play mode: **not authorized.** No scene edits, no `SaveAssets`, no Play. PlayMode *fixtures* through the Test
  Runner are fine.

## Phase 0 — stage prep (all yours, before any lead)

1. **Preflight:** `ListAgents` (a peer session may be on trunk — trap: a clean `git status` is not a free trunk);
   `python Packages/com.worktreetoolkit/Tools~/worktree.py doctor --json` (stop on git failure, missing hooks or
   `brokerAlive` false — the broker had one `IOException` heartbeat blip on 2026-09-15, retry before declaring it
   dead); `git status` clean apart from what you create; delete any empty `.claude/worktrees/*` folder no longer locked.
2. **Baseline:** compile gate, then the four suites once: expected `DotsAnimationToolkit.Tests.EditMode` 866
   (865 passed, Conformance_A the standing failure — for the last time), `.PlayMode` 285; record the two
   `StitchPunk.Tests` / `StitchPunk.Tests.PlayMode` totals (unknown at write time, they are this batch's floor).
3. **A102 S1 — Samples~ compile** exactly as the spec says; paste every error line into a102's lead prompt (or
   "S1 clean").
4. **A102 S2 — player build**, now, while the stage is on trunk and no lead has gated (a gate swaps the stage under
   a running build). `manage_build` action `build`, target `windows64`, development `true`, output
   `%TEMP%\A102Build\StitchPunk.exe`; poll `status`. Record the outcome and every error line in A102 §7. Game-side
   errors that are mechanical (an Editor `using` without `#if UNITY_EDITOR`, an Editor-only type in a runtime
   assembly): fix on the stage with a `worker`, commit `A102-S2:`, rebuild once. Anything package-side goes into
   a102's prompt as a T3 wave. Do not spend more than two rebuild rounds; log what is left.
5. **CHANGELOG** top section is `## [0.53.1]`; `package.json` is `0.53.1`. Record both registry sha256s
   (`DotsAnimationToolkitAnimEventKeyRegistry.asset`, `DotsAnimationToolkitTargetTagRegistry.asset`).
6. Record Phase 0 in A102 §7 and in each game spec's §12 log. Commit only those files; push.
7. **A102 S3 — clean-project import** starts now in the background (`run_in_background`), as the spec's S3 says;
   it is a separate project and process and never touches the stage. Read its logs when it exits.

## Phase 1 — spawn

Three `spec-lead` agents at once, `model: opus`, background. Each prompt = spec id, spec path, worker model `sonnet`,
the lead contract, and:

- **Game-side substitutions to the lead contract** (for `despawn` and `minion-orders`): the "stage owns" list becomes
  `SystemGroups.cs`, every `CLAUDE.md`, `Tasks/Plans/README.md`, `Tasks/Verification/*`, `Tasks/Claude/*`, anything
  under `ProjectSettings/` and `Assets/Scenes/`; the conformance fixture in every gate is the pair
  `StitchPunk.Tests.SystemPlacementConformanceTests` + `StitchPunk.Tests.SystemGroupOrderTests` (not
  `PackagingConformanceTests`); Conformance_D/E/F/G do not apply to game code, RULES.md does (no `var`, no
  single-letter names, explicit types, never `.Run()`, `EnabledRefRW` params named `<component>Enabled`, the
  `dots-*` skills for scaffolding). Gate syntax is unchanged:
  `python Packages/com.worktreetoolkit/Tools~/worktree.py gate --edit-mode <FullName> --edit-mode <FullName>`.
- **PlayMode fixtures:** the broker refuses them (trap 25). `despawn`'s T5 sends "gate needed: despawn <sha>
  StitchPunk.Tests.PlayMode.DespawnSystemTests"; you gate it by hand (`stage-commit`, compile, the fixture,
  `restore-trunk`) only while `list --json` says `stage.busyWith` is null, then `git status` for stray folder metas
  (trap 26). A rebake is implied by both game specs' archetype changes; PlayMode fixtures bake nothing, so that is
  the owner's later Play, not yours.
- **a102's prompt** also carries the S1 report and the S2 package-side errors (if any), and the instruction to wait
  for your "S1–S3 green" (or "S1–S3: <what failed>") message before its T5.

## Phase 2 — while running

Stay quiet while the broker resolves EditMode gates. Hand-gate PlayMode requests. When S3 exits, grep its logs and
message a102's lead the S1–S3 verdict. `worktree.py list` for progress. A capped lead is never resumed: read its
diff, then spawn a fresh `worker` with the worktree's absolute path (never a second `spec-lead`, whose isolation
would create a new worktree — trap 5) for what is left.

## Phase 3 — merge, integrate, close

- Merge order `despawn` → `minion-orders` → `a102`; push after each; `worktree.py remove` each.
- **Integration on the stage after the game merges:** compile gate; the four suites once (totals must not drop
  below the floor; `StitchPunk.Tests` gains `AttackResolutionTests`, `.PlayMode` gains `DespawnSystemTests`);
  `Tasks/Plans/README.md` rows for Despawn and Minion Order Robustness → 🔨 built, verify pending; move both specs
  to `Tasks/Verification/` with `verify-despawn.md` and `verify-minion-orders.md` built from their §10 (the owner's
  play checks, including the rebake note and the X/R keys); `Systems.md`/`Systems_AI.md` truth; `Gotchas.md` DS-D5.
- **Integration after a102:** A102 §6 S4 exactly — CHANGELOG `## [0.54.0] — Release readiness`, `package.json` and the
  conformance pin at `0.54.0`, HANDOFF §4 paragraph on top, roadmap "Phase 4" box ticked, `AnimationToolkit.md` traps,
  `Code_Audit_2026-09.md` §3.1 marked done. The final EditMode run must show **zero** failures — say so in the commit.
- **Cleanup:** `%TEMP%\A102Build` and `%TEMP%\A102Import` deleted; `git status` clean; both registry sha256s unchanged.
- **End** with one message for the owner: the three ready reports condensed, the S1–S3 verdicts with any error lines
  that remain, what needs his eyes (the play checks in the two verify files, the A91 build check he still owns),
  and the next prompt: UA + A79 as one spec (audit §3.2) and, if S2 failed on something structural, an A103 for it.

## Settled owner calls (do not re-ask)

Names never numbers; no manual asset wiring; no package-side event handlers; no sound mixing; sprite sheets are
Texture2DArrays; unseen checkpoints close as accepted unless game breaking; the URP dependency in the Editor
assembly is legitimate (A102-D1); Despawn DS-D1–D7 and Minion Orders MO-D1–D7 as written in each spec's §12.
