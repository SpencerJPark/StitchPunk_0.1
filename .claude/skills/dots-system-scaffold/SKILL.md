---
name: dots-system-scaffold
description: Scaffold a new Unity DOTS ISystem + IJobEntity file for the Stitch Punk project following its strict conventions — no `var`, explicit types everywhere, `[BurstCompile]` on the struct and every method, `[ReadOnly]` imported from `Unity.Collections`, `state.RequireForUpdate<GameSceneTag>()`, `ScheduleParallel` with `state.Dependency`, and the correct `[UpdateInGroup]` from `SystemGroups.cs`. Use this skill whenever the user says "add a system", "write an ECS system for X", "create a new job that does Y", "I need a system that Z", or asks to create, scaffold, or draft anything under `Assets/_Scripts/Systems/`. Also use when fixing an existing system that violates these conventions. Skip only for pure refactors of an already-correct file.
---

# dots-system-scaffold

## What this skill does

Writes a new `ISystem` + `IJobEntity` file (or pair of files) for the Stitch Punk project. This is the single most repeated pattern in the codebase — every new gameplay rule lives in a system, and forgetting a `[BurstCompile]` attribute or missing the `Unity.Collections` import for `[ReadOnly]` breaks Burst silently.

## When to use it

Trigger for any request that creates a new system file. Examples:
- "Add a system that decays Thirst by 0.1 per second."
- "Write a job that reads the Health buffer and enables Dead when it hits 0."
- "I need an ECS system that ticks FactoryGrid cells."
- "Scaffold a pre-pass system for Curiosity."

Don't trigger for pure edits to non-system files or for MonoBehaviour-only work.

## What to read first

Before writing the file, consult these in order:

1. `Assets/_Vault/Memories/Code/RULES.md` — hard rules (no `var`, no single-letter names, `[ReadOnly]` from `Unity.Collections`, etc.). These are non-negotiable.
2. `Assets/_Vault/Memories/Code/Systems.md` — the **full system group execution order** and a map of every existing system. Use this to pick the right `[UpdateInGroup]`.
3. `Assets/_Scripts/Systems/SystemGroups.cs` — the actual group declarations (truth source).
4. Sub-group docs if relevant: `Systems_AI.md`, `Systems_Animation.md`, `Systems_Movement.md`.
5. `Assets/_Vault/Memories/Code/Gotchas.md` — scan if the system interacts with spawning, ECB, or cross-entity lookups.

## How to decide which group

Ask yourself: "What phase of the frame does this logic belong in?"

- Awareness/perception/context boost → `UtilityAwarenessSystemGroup`
- Scoring AI options → `AIScoringSystemGroup`
- Selecting a single chosen action → `AISelectionSystemGroup`
- Executing the chosen action (move, attack, interact) → `AIExecutionSystemGroup`
- Per-frame mutation of a ScriptableObject-derived blob at bake → `PostBakingSystemGroup`
- Player input → input event components → `PlayerInputSystemGroup`
- Combat resolution → `CombatResolutionSystemGroup`; damage application → `CombatReactionSystemGroup`
- Life/death/heal/revive → `HealthSystemGroup`
- Spawning/despawning/save → `LateSimulationSystemGroup` and its sub-groups (see `Systems.md`)
- Factory production → `BuildingsSystemGroup`
- Visual-only after transforms settle → `PresentationSystemGroup`

If still unclear, **ask the user** instead of guessing.

## Templates

Pick the shape that matches the job. Copy the template, rename, fill in the logic. All templates are tested against the conventions in `RULES.md`.

### Template A — minimal ISystem with a single IJobEntity

Use when you iterate one component kind, optionally with an enableable trigger. This is the baseline shape — start here 80% of the time.

```csharp
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

[BurstCompile]
[UpdateInGroup(typeof(ReplaceWithGroup))]
public partial struct ReplaceWithSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        state.Dependency = new ReplaceWithJob
        {
            deltaTime = SystemAPI.Time.DeltaTime,
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(TriggerComponent))]
public partial struct ReplaceWithJob : IJobEntity
{
    public float deltaTime;

    public void Execute(ref TargetComponent targetComp, EnabledRefRW<TriggerComponent> triggerEnabled)
    {
        // logic here
        triggerEnabled.ValueRW = false;
    }
}
```

Exemplar in the codebase: `Assets/_Scripts/Systems/HealthSystemGroup/HealSystem.cs`.

### Template B — buffer-iteration job with in-place mutation

Use when you iterate a `DynamicBuffer` and mutate its entries. **Remember to write the element back** — `buffer[i] = element` — or the change vanishes.

```csharp
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

[BurstCompile]
[UpdateInGroup(typeof(ReplaceWithGroup))]
partial struct ReplaceWithSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;

        state.Dependency = new ReplaceWithJob
        {
            deltaTime = deltaTime,
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(GatingComponent))]
public partial struct ReplaceWithJob : IJobEntity
{
    public float deltaTime;

    public void Execute(ref DynamicBuffer<BufferElement> buffer)
    {
        for (int bufferIndex = 0; bufferIndex < buffer.Length; bufferIndex++)
        {
            BufferElement element = buffer[bufferIndex];
            element.value = math.clamp(element.value - element.decayRate * deltaTime, -100f, 100f);
            buffer[bufferIndex] = element;
        }
    }
}
```

Exemplar: `Assets/_Scripts/Systems/AISystemGroup/UtilityAwarenessSystemGroup/MotivationDecaySystem.cs`.

### Template C — cross-entity mutation via ComponentLookup

Use when the job needs to read or write components on a *different* entity than the one it iterates. Read `references/lookup-patterns.md` before using this — there are three attribute rules that get confused often.

### Template D — pre-pass system (AI context boost)

Use for AI pre-pass systems that scan the `Behaviour` buffer on `ActiveBrain` units and mutate `contextMultiplier`. These always `[UpdateAfter(typeof(MotivationDecaySystem))]` because decay resets the multiplier to 1.0. See `references/system-templates.md`.

## Naming rules (from RULES.md — non-negotiable)

- **Never `var`.** Always the explicit type.
- **Never single-character names.** `for (int i = ...)` is fine in this codebase because it is a well-understood index idiom, but for anything semantic use the domain name — `unitEntity`, `attackerBody`, `bufferIndex`.
- **`[ReadOnly]` comes from `Unity.Collections`.** Always `using Unity.Collections;` on jobs that have it, even if nothing else from that namespace is used.
- **`[BurstCompile]` on the struct AND every method** (`OnCreate`, `OnUpdate`, `OnDestroy`). Missing any one breaks Burst silently.
- **Never allocate managed memory inside a Burst job.** No `new List<>`, no `string`, no boxing.
- **No `#region` blocks.**

## Scheduling

- `ScheduleParallel(state.Dependency)` is the default.
- Use `Schedule(state.Dependency)` only when the job has cross-entity writes that can't be parallelised safely.
- Always assign back: `state.Dependency = newJob.ScheduleParallel(state.Dependency);`

## Singleton guards

- `state.RequireForUpdate<GameSceneTag>()` in `OnCreate` for any system that should only run in-game.
- `state.RequireForUpdate<FooLibrary>()` if the system reads a baked blob library singleton.
- In `OnUpdate`, read the library with `SystemAPI.GetSingleton<FooLibrary>()`. If the library is not a true singleton (e.g., `UnitPrefabEntry` — see `Gotchas.md`), use `SystemAPI.GetSingletonEntity<...>()` + `SystemAPI.GetComponent<...>()`.

## Common mistakes — check before finishing

- [ ] `[BurstCompile]` on the `ISystem` struct AND on `OnCreate` + `OnUpdate` + `OnDestroy` **if those methods have a body**. Do NOT write empty `OnCreate` / `OnDestroy` methods — omit them entirely if there's nothing to do. Empty methods are noise and mislead readers into thinking state is being managed.
- [ ] `using Unity.Collections;` if anywhere in the file uses `[ReadOnly]`.
- [ ] `state.Dependency = newJob.ScheduleParallel(state.Dependency)` — never drop the dependency.
- [ ] **`ScheduleParallel` by default, not `Schedule`.** Only fall back to single-threaded `Schedule` when workers can collide on the same target (e.g., many attackers writing to the same victim's `Health`). Per-entity toggles, buffer mutations, and unique-pairing cross-entity writes are all parallel-safe.
- [ ] Buffer in-place mutation: `buffer[i] = element` after modifying.
- [ ] `lookup.Update(ref state)` in `OnUpdate` for every `ComponentLookup` / `BufferLookup` field before scheduling.
- [ ] `[UpdateInGroup(typeof(...))]` on the `ISystem`, pointing at a real group in `SystemGroups.cs`.
- [ ] No `var`. No single-letter semantic names.
- [ ] No managed allocations inside `Execute` — no `new List<>`, no `string`.
- [ ] If save/load or I/O is involved, OMIT `[BurstCompile]` (see `SaveSystem.cs`).

## Query filtering — prefer narrow queries

Add query attributes on the `IJobEntity` to filter chunks at the archetype level instead of running `Execute` and then skipping inside. This cuts iteration cost and makes intent explicit.

- `[WithAll(typeof(X))]` — entity must have X enabled.
- `[WithDisabled(typeof(X))]` — entity must have X present but disabled. Useful for "flip from off to on" systems: if you're about to enable `AtStation`, filter on `[WithDisabled(typeof(AtStation))]` so you don't run on entities already in that state.
- `[WithNone(typeof(X))]` — entity must not have X at all (structural, not enable-state).
- `[WithAbsent(typeof(X))]` — enable-state-aware "absent" (disabled or missing).

Rule of thumb: if `Execute` starts with an early-return that checks a component's presence or enable state, that check probably belongs as a query attribute instead.

## Where to put the file

`Assets/_Scripts/Systems/<SystemGroupName>/<YourSystemName>.cs` — the path mirrors the group.

Examples:
- `Systems/AISystemGroup/UtilityAwarenessSystemGroup/CuriosityPrePassSystem.cs`
- `Systems/HealthSystemGroup/BleedSystem.cs`
- `Systems/BuildingsSystemGroup/WorkerAssignmentSystem.cs`

## Update the docs

After writing the system, update the table in the matching section of `Assets/_Vault/Memories/Code/Systems.md` (or `Systems_AI.md` etc.) with one row describing the new system. This keeps the map current for future work — the vault is the single source of truth for system order.

## Deeper references

- `references/system-templates.md` — the full set of templates including Template C (ComponentLookup) and Template D (pre-pass) with every convention annotated.
- `references/lookup-patterns.md` — the `[ReadOnly]` / `[NativeDisableParallelForRestriction]` decision table and common cross-entity mutation shapes.

Read these if the user's request doesn't map cleanly to Template A or B above.
