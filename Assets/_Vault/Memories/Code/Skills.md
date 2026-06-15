# Project Skills — Index

> **Skills are version-controlled in the repo at `.claude/skills/`** (repo root, outside `Assets/`). That folder is the single source of truth — Claude Code loads project skills only from there, and it commits/syncs across computers via git.
> This page is a **read-only index** for browsing in Obsidian. To edit a skill, change its `SKILL.md` in `.claude/skills/` and commit. (Links below point outside the Obsidian vault root, so they may not be clickable in Obsidian — open the path in your editor / file browser.)

These are custom skills for Stitch Punk. The `dots-*` scaffolding skills generate code; `dots-task-creator` authors plans and `execute-plan` builds them. Reference the scaffolders by name in plan docs under a **`Skills Needed`** heading so the right one is used at build time.

---

## dots-task-creator *(workflow skill — not a scaffolder)*
`.claude/skills/dots-task-creator/SKILL.md`

Authors a **self-contained system design/plan doc** in `Assets/_Vault/Tasks/Plans/`. Runs a batched Q&A to lock architecture decisions, flags which scaffolding skills the build will use (under `Skills Needed`), and writes the spec from a template with `← DECISION` markers + build phases. **Planning only — writes no code** (`execute-plan` builds an approved plan).

**Use when:** "plan the X system", "flesh out X", "spec out Y", "make a plan for Z". **Not for:** building/implementing (use `execute-plan`), or scaffolding individual C# files (use the `dots-*` skills).
**References:** `planning-questions.md` (question checklist), `plan-template.md` (spec skeleton)

---

## execute-plan *(workflow skill — the execution counterpart to dots-task-creator)*
`.claude/skills/execute-plan/SKILL.md`

**Builds** an approved plan from `Assets/_Vault/Tasks/Plans/`. Asks clarifying questions until every `← DECISION` marker + ambiguity is resolved, builds phase-by-phase using the `dots-*` skills the plan lists under `Skills Needed`, does full vault housekeeping (plan status + Plans/README + memory docs), then `git mv`s the completed plan into `Assets/_Vault/Tasks/Verification/` with a `verify-<system>.md` steps file and commits + pushes to `main`. (No Unity compile here — correctness is static review; compile + play-test live in the moved verification steps.)

**Use when:** "execute / build / enact / implement the X plan", "build out the approved spec". **Not for:** planning (use `dots-task-creator`), scaffolding a single file (use the `dots-*` skills), or debugging.

---

## dots-system-scaffold
`.claude/skills/dots-system-scaffold/SKILL.md`

Scaffolds a new DOTS **`ISystem` + `IJobEntity`** file following the project's strict conventions: no `var`, explicit types, `[BurstCompile]` on the struct and every method, `[ReadOnly]` from `Unity.Collections`, `state.RequireForUpdate<GameSceneTag>()`, `ScheduleParallel` with `state.Dependency`, and the correct `[UpdateInGroup]` from `SystemGroups.cs`.

**Use when:** "add a system", "write an ECS system for X", "create a job that does Y", or any new file under `_Scripts/Systems/`. Also for fixing a system that violates these conventions.
**References:** `system-templates.md`, `lookup-patterns.md` · **Evals:** `evals/evals.json`

## dots-authoring-baker
`.claude/skills/dots-authoring-baker/SKILL.md`

Scaffolds a DOTS **MonoBehaviour + nested `Baker`** pair following authoring conventions: correct `TransformUsageFlags`, explicit `AddComponent` / `SetComponentEnabled` / `AddBuffer` ordering, `DependsOn(so)` for referenced ScriptableObjects, and a cross-entity baking system in `PostBakingSystemGroup` when the baker touches child entities.

**Use when:** "add authoring for X", "write a baker for Y", "bake a MonoBehaviour", "wire a new prefab into ECS", or any new file under `_Scripts/Authoring/`. Also for fixing `TransformUsageFlags` misuse or the "entity doesn't belong to the current authoring component" error.
**References:** `cross-entity-bake.md` · **Evals:** `evals/evals.json`

## dots-blob-library
`.claude/skills/dots-blob-library/SKILL.md`

Scaffolds the full **SO → BlobAsset library pipeline**: `FooSO` + `FooLibrarySO` + `FooLibraryBlob` + `FooLibrary`/`FooLibraryReference` components + `FooLibraryAuthoring` + `FooLibraryBakingSystem` in `PostBakingSystemGroup`. Five files; the most repetitive, silently-bug-prone pattern in the codebase.

**Use when:** "make a new library", "bake a list of SOs into a blob", "expose X to systems via blob", "add a FooLibrary", "new blob asset".
**References:** `canonical-blob-library.md`, `nested-blob-arrays.md` · **Evals:** `evals/evals.json`

## dots-unit-ai
`.claude/skills/dots-unit-ai/SKILL.md`

Scaffolds or extends a **unit AI decision behaviour**: awareness systems that emit `ActionOption` entries, wiring new `ActionType`/`MotivationType` enums, Burst function pointers in `SelectionFunctions`, action-execution systems driving `PathRequest`/`AttackRequest`/`AnimationRequest`, and `ActionInterruptRequest` for urgent reactions.

**Use when:** "make units react to X", "add a daily schedule", "units should panic when Y", "add an awareness system", "wire up ActionType.Foo", "add an interrupt for W". **Not for:** blob libraries (use `dots-blob-library`), bakers (use `dots-authoring-baker`), or generic non-AI systems (use `dots-system-scaffold`).

---

*Maintained alongside the code it scaffolds. When you add or rename a skill in `.claude/skills/`, update this index.*
