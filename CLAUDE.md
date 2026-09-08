# CLAUDE.md

Before editing a folder, read its context note in `Assets/_Vault/Memories/Code/` (named after the folder). `RULES.md` = hard conventions, `Contracts.md` = cross-feature request/event index, `Gotchas.md` = silent-failure traps. Game overview and status: [`Assets/CLAUDE.md`](Assets/CLAUDE.md). Prefer the repo's `dots-*` scaffolding skills over hand-written boilerplate.

Ask follow-up questions until you are ~95% confident before making changes. After solving a non-obvious problem or adding a system/folder, update the matching `_Vault/Memories/Code/*.md` so the next session skips the rediscovery.

## Environment

- **Unity 6000.5.0f1 (6.5), DOTS:** Entities/Physics 6.5, Burst, Collections, Mathematics, Jobs; URP 17.5 (2.5D); Cinemachine 3.1, Input System 1.19, UniTask, Reflex (DI), Rive (UI). Verify real API signatures in `Library/PackageCache` before calling — there is no compiler mid-task.
- **Unity MCP is live** (`mcp__UnityMCP__*`), and only while the Editor is open.

## Commands

Editor-driven; there is no CLI build for the game.

- **Compile gate (after every `.cs` change):** save → `mcp__UnityMCP__refresh_unity` → poll `editor_state.isCompiling` until false → `mcp__UnityMCP__read_console` for `error CS####` / Burst `BC####`. Editor closed? Grep the **project-relative** `Logs/Editor.log` and confirm its mtime is newer than your edit — the `%LOCALAPPDATA%` copy is a stub that always greps clean. If it hasn't recompiled, say so and fall back to static review rather than claiming it compiles. Ignore the root `*.csproj` files; Unity regenerates them.
- **Rebake:** authoring/baker/SO changes need a re-bake — reopen the subscene or re-enter Play mode. "Compile + rebake + play" is the standard verification pass.
- **Play-test:** user-driven. Main scene `Assets/Scenes/Game.unity`; DOTS sandbox `Assets/Scenes/SubScenes/DOTSTestScene.unity`. Anything on-screen needs the user to look or share a screenshot.
- **Tests:** EditMode fixtures in `Assets/_Scripts/Tests/`; PlayMode fixtures (need a `World`/`EntityManager`) in `Assets/_Scripts/Tests/PlayMode/` (`StitchPunk.Tests.PlayMode.asmdef`, added 2026-08-29 — mirrors `com.dotsanimationtoolkit`'s own PlayMode assembly: manual `World` + `GetOrCreateSystem<T>().Update(...)`, no scene/GameObjects needed). Run via `mcp__UnityMCP__run_tests` (poll `mcp__UnityMCP__get_test_job`). No headless CLI. Test only what actually needs testing — real logic, real invariants, real regressions. No coverage-chasing, no fixtures for trivial accessors or for Unity's own behaviour. If you cannot revert the fix and watch the test fail, delete the test.

## Subagent Delegation

- **Spawn `worker` (edits) or `verifier` (read-only checks), never `general-purpose`.** Both are Sonnet, both carry a read guard that refuses whole-file reads over 300 lines, and both have a hard turn cap (40 / 25). The cap is the budget: a task that cannot finish in 40 turns is scoped wrong, not under-resourced. Measured 2026-09-08: the median agent crosses 100k tokens at turn 32.
- **A capped agent is never resumed.** The harness result will invite you to SendMessage it to continue; do not, that grows the same context. Read its diff, then spawn a fresh `worker` with only the remaining scope.
- **Brief checklist before every spawn:** at most two files to edit, named; reading limited to named line ranges (grep the member, read 40 lines either side); the snippets it needs pasted into the brief rather than rediscovered; no Unity MCP compile or test gate inside the agent unless the gate is the task; "at turn 30 stop editing and write your report"; report of 30 lines or fewer.
- **Every finished agent gets a ledger line** injected into your context by `.claude/hooks/agent_result_ledger.py` (rows in `.claude/subagent-ledger.tsv`): peak tokens, turns, guard denials, verdict. HIGH or OVER means the next brief in that family gets split further.
- **If a subagent's output needs checking, spawn a `verifier`** rather than re-deriving the work in the orchestrator's own context.

## Architecture

`Assets/_Scripts/` splits into `StitchPunk.*` assemblies by folder: `Components/` (data only, no logic), `Authoring/` (MonoBehaviour + nested `Baker`, no game logic), `Data/` (SOs + blob structs), `Systems/` (all gameplay), `MonoBehaviours/` (hybrid bridge), plus `UI/ Core/ Utils/ Editor/ Tests/`. `Core/Unused/` is legacy parking — never reference it.

**Absolute rules** (full set in `RULES.md`): never `var`, never single-letter names — explicit types, names read like docs. Never `.Run()` a job — `.Schedule()` / `.ScheduleParallel()` into `state.Dependency`.

**Names are the documentation.** Be verbose when naming — a long, explicit method or field name that states exactly what it does communicates better than a short one propped up by a comment. Do not write a `<summary>` on every function; most need none, and a summary that just restates the signature is noise. Comment only what the code cannot say: a *why*, a non-obvious constraint, a silent-failure trap. One or two lines, never a multi-paragraph `<remarks>` essay. Code should read like the logic it is.

**Every group is declared in [`Assets/_Scripts/Systems/SystemGroups.cs`](Assets/_Scripts/Systems/SystemGroups.cs)** — the single ordering manifest. Place new systems with `[UpdateInGroup]`, never ad-hoc ordering, and put the file in the folder named after its group. Scene gating is group-level; do not add per-system `RequireForUpdate<GameSceneTag>`.

Sim order: `GameManager → Player → UtilityAI → MinionActionSelection → StateMachine → Item → Movement → Buildings → Combat → Health → Design → Animation`. LateSim: `Spawn → SpawnInit → Ragdoll → Sound → Despawn → Save`.

**AI is a decision/execution split:** awareness systems in `UtilityAISystemGroup` score options into a `UtilityActions` buffer → the winner is written into the `StateMachine` component → `BehaviorExecutionSystem` interprets the chosen `BehaviorSO`'s blob-baked command sequence. Read `Systems_AI.md` before touching AI.
