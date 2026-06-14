# Full System Templates (Stitch Punk DOTS conventions)

Five templates, progressively more advanced. Every template follows `RULES.md`: no `var`, explicit types, `[BurstCompile]` on the struct + every method, `[ReadOnly]` imported from `Unity.Collections`, `state.Dependency` scheduling.

---

## Template A — minimal ISystem + IJobEntity

The baseline shape. Start here unless you have a specific reason not to.

```csharp
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

[BurstCompile]
[UpdateInGroup(typeof(HealthSystemGroup))]
[UpdateBefore(typeof(DeathSystem))]
public partial struct HealSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        state.Dependency = new HealJob().ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(Heal))]
public partial struct HealJob : IJobEntity
{
    public void Execute(ref Health health, ref Heal heal, EnabledRefRW<Heal> healEnabled)
    {
        health.healthAmount = math.min(health.healthAmount + heal.healAmount, health.healthAmountMax);
        heal.healAmount = 0;
        healEnabled.ValueRW = false;
    }
}
```

Exemplar: `Assets/_Scripts/Systems/HealthSystemGroup/HealSystem.cs`.

---

## Template B — buffer in-place mutation

Iterate a `DynamicBuffer` and mutate each entry. The canonical gotcha: you must re-assign the element back to the buffer slot after mutating it, otherwise the write vanishes.

```csharp
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

[BurstCompile]
[UpdateInGroup(typeof(AIAwarenessSystemGroup))]
partial struct MotivationDecaySystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;

        state.Dependency = new MotivationDecayJob
        {
            deltaTime = deltaTime,
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(ActiveBrain))]
public partial struct MotivationDecayJob : IJobEntity
{
    public float deltaTime;

    public void Execute(ref DynamicBuffer<Behaviour> motivations)
    {
        for (int motivationIndex = 0; motivationIndex < motivations.Length; motivationIndex++)
        {
            Behaviour motivation = motivations[motivationIndex];
            motivation.contextMultiplier = 1f;
            motivation.value = math.clamp(motivation.value - motivation.decayRate * deltaTime, -100f, 100f);
            motivations[motivationIndex] = motivation;
        }
    }
}
```

Exemplar: `Assets/_Scripts/Systems/AISystemGroup/AIAwarenessSystemGroup/MotivationDecaySystem.cs`.

---

## Template C — cross-entity mutation via ComponentLookup

Use when the job must touch an entity other than the iterated one. Read `lookup-patterns.md` for attribute rules before adopting.

```csharp
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

[BurstCompile]
[UpdateInGroup(typeof(HealthSystemGroup))]
[UpdateAfter(typeof(HealSystem))]
public partial struct DeathPropagateSystem : ISystem
{
    private ComponentLookup<ActiveBrain> activeBrainLookup;
    private ComponentLookup<NeedsAction> needsActionLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        activeBrainLookup = state.GetComponentLookup<ActiveBrain>(isReadOnly: false);
        needsActionLookup = state.GetComponentLookup<NeedsAction>(isReadOnly: false);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        activeBrainLookup.Update(ref state);
        needsActionLookup.Update(ref state);

        state.Dependency = new DeathPropagateJob
        {
            activeBrainLookup = activeBrainLookup,
            needsActionLookup = needsActionLookup,
        }.Schedule(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(Dead))]
public partial struct DeathPropagateJob : IJobEntity
{
    [NativeDisableParallelForRestriction] public ComponentLookup<ActiveBrain> activeBrainLookup;
    [NativeDisableParallelForRestriction] public ComponentLookup<NeedsAction> needsActionLookup;

    public void Execute(Entity bodyEntity, in BrainLink brainLink)
    {
        Entity brainEntity = brainLink.brain;

        if (activeBrainLookup.HasComponent(brainEntity))
            activeBrainLookup.SetComponentEnabled(brainEntity, false);

        if (needsActionLookup.HasComponent(brainEntity))
            needsActionLookup.SetComponentEnabled(brainEntity, false);
    }
}
```

Exemplar: `DeathSystem.cs` under `HealthSystemGroup`.

---

## Template D — pre-pass system (AI context boost)

Pre-pass systems mutate `Behaviour.contextMultiplier` on the AI buffer. They must run **after** `MotivationDecaySystem` (which resets the multiplier to 1.0 each frame) and **before** `BehaviourScoringSystem` (which reads it).

```csharp
using Unity.Burst;
using Unity.Entities;

[BurstCompile]
[UpdateInGroup(typeof(AIAwarenessSystemGroup))]
[UpdateAfter(typeof(MotivationDecaySystem))]
public partial struct CuriosityPrePassSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        state.Dependency = new CuriosityPrePassJob().ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(ActiveBrain))]
public partial struct CuriosityPrePassJob : IJobEntity
{
    public void Execute(ref DynamicBuffer<Behaviour> behaviours, in UnexploredAreaNearby unexplored)
    {
        for (int behaviourIndex = 0; behaviourIndex < behaviours.Length; behaviourIndex++)
        {
            Behaviour behaviour = behaviours[behaviourIndex];
            if (behaviour.behaviourType != BehaviourType.Curiosity) continue;

            behaviour.contextMultiplier = 1.8f;
            behaviours[behaviourIndex] = behaviour;
        }
    }
}
```

Exemplar: `SafetyPrePassSystem.cs` under `AIAwarenessSystemGroup`.

---

## Template E — system that reads a baked blob library

```csharp
using Unity.Burst;
using Unity.Entities;

[BurstCompile]
[UpdateInGroup(typeof(BuildingsSystemGroup))]
public partial struct ProductionSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<FactoryLibrary>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        FactoryLibrary library = SystemAPI.GetSingleton<FactoryLibrary>();

        state.Dependency = new TickProductionJob
        {
            deltaTime = SystemAPI.Time.DeltaTime,
            recipes = library.blob,
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(FactoryStation))]
public partial struct TickProductionJob : IJobEntity
{
    public float deltaTime;
    [Unity.Collections.ReadOnly] public Unity.Entities.BlobAssetReference<FactoryLibraryBlob> recipes;

    public void Execute(ref ProductionProgress progress, EnabledRefRW<ProductionProgress> enabled /* etc. */)
    {
        // read recipes.Value.xxx — always by reference, never copied to managed memory
    }
}
```

Exemplar: `Assets/_Scripts/Systems/BuildingsSystemGroup/ProductionSystem.cs`.

---

## When to OMIT `[BurstCompile]`

If the system uses managed APIs (`JsonUtility`, `System.IO`, `Resources`, anything that isn't a `NativeContainer` or unmanaged struct), omit `[BurstCompile]` entirely. Do NOT just omit it on one method — Burst will still try to compile the struct and fail silently.

Exemplar: `SaveSystem.cs` / `LoadSystem.cs` under `SaveSystemGroup`.
