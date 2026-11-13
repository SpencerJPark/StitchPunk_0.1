# Next session: readability pass, part 3 (the two Burst refactors left over)

Paste everything below the line into a fresh session.

---

Finish the readability refactor of Stitch Punk's gameplay code. Two items are left from the audit. Both touch Burst jobs at the centre of the AI, so each one ends with a compile in the Editor and a Play check, not just the offline gate.

**Context (already done, don't redo):** commits `a91c39b2` and `15019d8b` on main parked dead prototype files, trimmed comments on 63 files, split long methods into named steps (PlayerInputManager, UnitSelectionManager, AttackRequestSystem, MinionActionSelectionSystem, ItemAwarenessSystem, two menu UIs), added `EntityUtils.IsDead/IsAlive`, and added `MinionOrderWriter`. Roadmap: `Assets/_Vault/Tasks/Roadmap.md`.

**How to work (owner's standing instructions):**
- Be token conscious. You are the observer and never edit code yourself. Pick the lowest model tier that can do each job:
  - **`model: haiku`** (`verifier` type) for pure lookups: grep inventories, "list every lookup in these files", line counts, finding call sites. Ask for a table, 20 lines max.
  - **`model: sonnet`** (`worker` type) for every edit, and (`verifier` type) for behaviour-equivalence and Burst-hazard reviews.
  - Never `general-purpose`, and never your own model for subagents.
- Each brief names at most two files, pastes the line ranges and snippets the agent needs so it does not rediscover them, says "stop at turn 30 and report", and caps the report at 15–25 lines.
- Run independent agents in parallel waves, and gate once per wave. Read diffs yourself only when they're under ~60 lines; otherwise send them to a Sonnet verifier.
- If a 429 rate limit kills a wave, check the disk (`git status`, `git diff --stat`) before respawning; the edits are usually there. Respawn fresh agents with only the remaining scope, and never resume a capped one.
- Run `ListAgents` first. If a peer session is busy on main, work in a worktree: `git worktree add .claude/worktrees/readability-3 -b refactor/readability-3`. Never `cd` the session.
- Gate the worktree like this:
  - Junction `<worktree>\Library` to main's `Library`.
  - Backdate the asmdefs: `find <worktree>/Assets/_Scripts <worktree>/Packages -name '*.asmdef' -exec touch -d "2026-10-30" {} +`.
  - Run `(cd <worktree> && python Tools/devloop/devloop.py gate game)`.
  - Use `gate game`, not a single assembly. Single-assembly gates report stale CS0246 errors in terrain files here.
  - Before removing the worktree, remove the junction with `cmd /c rmdir`.
- Ask the peer sessions before using the Editor via Unity MCP, and tell them when you are done.
- Every refactor must be behaviour-identical. Afterwards a `verifier` compares old (`git show HEAD:<path>`) against new for side-effect order, struct field values, comparison strictness, and Burst hazards. The Burst hazards to check: lookups passed by value, `in` params with non-readonly calls dropping writes, and missing `unmanaged` constraints.
- Project rules: no `var`, no single-letter names, never `.Run()`, Burst static helpers take structs by `in`/`ref`/`out`, names are the documentation, comments are one-line whys only.

## Item 1: BehaviorExecutionSystem builds its command context once per job, not once per unit

File: `Assets/_Scripts/Systems/StateMachineSystemGroup/ActionExecutionSystemGroup/BehaviorExecutionSystem.cs` (~426 lines). Read `Assets/_Vault/Memories/Code/Systems_AI.md` first.

The audit found two problems:
- Around lines 295-324, `Execute` builds a 27-field `BehaviorCommandContext` for every unit every frame, copying 20+ lookups each time.
- Around lines 163-196, the job declares the same lookups the context already holds.

Agents for this item:
- A Haiku `verifier` maps the context struct: where it is defined, its fields, and every consumer with file and line.
- One Sonnet `worker` makes the change.
- A Sonnet `verifier` checks equivalence.

Goal:
- Build the shared part of the context once, as a job field filled in `OnUpdate`. Set only the per-entity values (entity, entityIndex/sort key, ECB if it's per chunk) per unit, on a local copy or through `ref`.
- Drop the duplicated job fields.
- Find the context struct's definition and its consumers under `Assets/_Scripts/Utils/BehaviorCommands/`. Change the consumers' signatures only if the struct's shape changes, and prefer not to change them.

Watch for:
- `[NativeDisableParallelForRestriction]` and `[ReadOnly]` must stay correct on every lookup.
- A `ComponentLookup` copied into a struct still aliases the same data, so the copy is fine, but the job's safety attributes must sit on the field the job actually schedules with.
- If this is `ScheduleParallel`, the per-unit values must not be written into a shared job field.

Verify:
1. Gate.
2. A `verifier` checks equivalence.
3. Editor: `refresh_unity`, poll `isCompiling`, then `read_console` for errors including Burst `BC####`.
4. Run the AI PlayMode fixtures: `run_tests` in PlayMode with `group_names` matching the AI/SmartHumanAi suites. Never the full ~700 toolkit suite.
5. Measure before and after: one `manage_profiler` capture, or `ProfilerMarker` timing of `BehaviorExecutionSystem` in `Scenes/CampLoop.unity` with the bandit camp active. Report both numbers.
6. Add to the owner's checklist: "bandits still patrol, sit, throw, shoot and heal as before".

## Item 2: shared lookup bundles for the awareness systems

Files: the systems in `Assets/_Scripts/Systems/UtilityAISystemGroup/UtilityAwarenessSystemGroup/`. About 14 files declare, create, `.Update(ref state)` and wire the same lookups (`Dead`, `LocalTransform`, `Faction`, and others), about 55 update lines in total.

Steps:
1. A Haiku `verifier` greps every awareness system's lookups and reports which set appears in 4 or more systems, as a table.
2. Make one `[ReadOnly]`-safe struct in `Assets/_Scripts/Utils/`, e.g. `UnitStateLookups { ComponentLookup<Dead> dead; ComponentLookup<LocalTransform> transform; ComponentLookup<Faction> faction; ... }`.
   - Give it `static Create(ref SystemState state)` and `Update(ref SystemState state)`.
   - Put `[ReadOnly]` on each field inside the struct. Confirm in `Library/PackageCache/com.unity.entities*` that nested `[ReadOnly]` lookups are honoured by the job safety system. If they are not, stop and report: keep the bundle for the declaration and update boilerplate only, and pass its fields to the job individually.
3. Convert one system first, then gate, compile in the Editor, and run its fixture.
4. Only then send 2-3 systems per Sonnet worker, in parallel waves. Then a Sonnet verifier checks each wave for equivalence.

Leave alone:
- `MinionGatherSystem`: the World Features session owns it.
- Any `Terrain*` file.
- Systems whose lookups differ from the bundle. Partial adoption is fine.

Verify: the same chain as item 1, plus one Play run in CampLoop with the console checked for safety-system exceptions (`InvalidOperationException ... job ... reads/writes`).

## Finish

- Merge to main with `--no-ff`, then run `gate game` on main.
- Tell the peers what changed.
- Report to the owner:
  - lines before and after
  - the profiler numbers for item 1
  - which awareness systems took the bundle and which didn't, and why
  - the Play checks that still need the owner's eyes
- Commit with explicit paths. Never `git add -A`. Don't push unless asked.
