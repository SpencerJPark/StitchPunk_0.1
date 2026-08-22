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
- **Tests:** EditMode fixtures in `Assets/_Scripts/Tests/`; run via `mcp__UnityMCP__run_tests` (poll `mcp__UnityMCP__get_test_job`). No headless CLI. No PlayMode coverage of the AI spine yet.

## Architecture

`Assets/_Scripts/` splits into `StitchPunk.*` assemblies by folder: `Components/` (data only, no logic), `Authoring/` (MonoBehaviour + nested `Baker`, no game logic), `Data/` (SOs + blob structs), `Systems/` (all gameplay), `MonoBehaviours/` (hybrid bridge), plus `UI/ Core/ Utils/ Editor/ Tests/`. `Core/Unused/` is legacy parking — never reference it.

**Absolute rules** (full set in `RULES.md`): never `var`, never single-letter names — explicit types, names read like docs. Never `.Run()` a job — `.Schedule()` / `.ScheduleParallel()` into `state.Dependency`.

**Every group is declared in [`Assets/_Scripts/Systems/SystemGroups.cs`](Assets/_Scripts/Systems/SystemGroups.cs)** — the single ordering manifest. Place new systems with `[UpdateInGroup]`, never ad-hoc ordering, and put the file in the folder named after its group. Scene gating is group-level; do not add per-system `RequireForUpdate<GameSceneTag>`.

Sim order: `GameManager → Player → UtilityAI → MinionActionSelection → StateMachine → Item → Movement → Buildings → Combat → Health → Design → Animation`. LateSim: `Spawn → SpawnInit → Ragdoll → Sound → Despawn → Save`.

**AI is a decision/execution split:** awareness systems in `UtilityAISystemGroup` score options into a `UtilityActions` buffer → the winner is written into the `StateMachine` component → `BehaviorExecutionSystem` interprets the chosen `BehaviorSO`'s blob-baked command sequence. Read `Systems_AI.md` before touching AI.
