using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

public static class BrainUtil
{
    public static void BakeRequirements<T>(Baker<T> baker, Entity entity, bool active, BrainSO brainSo)
        where T : UnityEngine.Component
    {
        baker.AddComponent(entity, new Brain { activeBrain = brainSo.brainType });
        baker.AddComponent<ActiveBrain>(entity);
        baker.SetComponentEnabled<ActiveBrain>(entity, active);

        baker.AddBuffer<ActionOption>(entity);
        baker.AddComponent<SelectedAction>(entity);
        baker.AddComponent<NeedsAction>(entity);
        baker.SetComponentEnabled<NeedsAction>(entity, active);

        baker.AddComponent(entity, new Awareness { range = brainSo.awarenessRange });
        baker.AddComponent(entity, new ActionTimer { time = 0f });

        baker.AddComponent<AggressiveState>(entity);
        baker.SetComponentEnabled<AggressiveState>(entity, false);

        DynamicBuffer<Behaviour> behaviourBuffer = baker.AddBuffer<Behaviour>(entity);
        if (brainSo.behaviours != null)
            PopulateBehaviours(behaviourBuffer, brainSo.behaviours);

        if (brainSo.randomBehaviours != null && brainSo.randomBehaviours.Length > 0 && brainSo.amount > 0)
            PopulateRandomBehaviours(behaviourBuffer, brainSo.randomBehaviours, brainSo.amount);

        DynamicBuffer<AttackFaction> attackBuffer = baker.AddBuffer<AttackFaction>(entity);
        if (brainSo.attackFactions != null)
        {
            for (int i = 0; i < brainSo.attackFactions.Length; i++)
                attackBuffer.Add(new AttackFaction { faction = brainSo.attackFactions[i] });
        }

        if (brainSo.canBePlayerControlled)
            AddPlayerControlled(baker, entity, false);

        baker.AddComponent<SwapBrainRequest>(entity);
        baker.SetComponentEnabled<SwapBrainRequest>(entity, false);

        // Interaction plumbing — CurrentInteraction holds the active target + driving
        // behaviour; per-kind task enableables gate the small execution systems that
        // run only for the kind of interaction the unit is currently performing.
        baker.AddComponent(entity, new CurrentInteraction
        {
            interaction      = Entity.Null,
            drivingBehaviour = BehaviourType.None,
        });
        AddInteractionTask<T, PickupTask>(baker, entity);
        AddInteractionTask<T, BuildTask>(baker, entity);
        AddInteractionTask<T, DrinkTask>(baker, entity);
        AddInteractionTask<T, EatTask>(baker, entity);
        AddInteractionTask<T, TouchAnimateTask>(baker, entity);
        AddInteractionTask<T, WanderAreaTask>(baker, entity);
        AddInteractionTask<T, SitTask>(baker, entity);
        AddInteractionTask<T, RepairTask>(baker, entity);
    }

    private static void AddInteractionTask<TAuthoring, TTask>(Baker<TAuthoring> baker, Entity entity)
        where TAuthoring : UnityEngine.Component
        where TTask : unmanaged, IComponentData, IEnableableComponent
    {
        baker.AddComponent<TTask>(entity);
        baker.SetComponentEnabled<TTask>(entity, false);
    }

    public static void PopulateBehaviours(DynamicBuffer<Behaviour> buffer, BehaviourType[] types)
    {
        for (int i = 0; i < types.Length; i++)
            buffer.Add(DefaultBehaviour(types[i]));
    }

    public static void PopulateRandomBehaviours(DynamicBuffer<Behaviour> buffer, BehaviourType[] pool, int amount)
    {
        if (pool == null || pool.Length == 0 || amount <= 0)
            return;

        if (amount >= pool.Length)
        {
            for (int i = 0; i < pool.Length; i++)
                buffer.Add(DefaultBehaviour(pool[i]));
            return;
        }

        BehaviourType[] copy = new BehaviourType[pool.Length];
        System.Array.Copy(pool, copy, pool.Length);

        System.Random rng = new System.Random();
        for (int i = 0; i < amount; i++)
        {
            int j = rng.Next(i, copy.Length);
            BehaviourType temp = copy[i];
            copy[i] = copy[j];
            copy[j] = temp;
            buffer.Add(DefaultBehaviour(copy[i]));
        }
    }

    public static void PopulateRandomBehaviours(
        DynamicBuffer<Behaviour> buffer,
        ref BlobArray<BehaviourType> pool,
        int amount,
        ref Random random)
    {
        int poolLength = pool.Length;
        if (poolLength == 0 || amount <= 0)
            return;

        if (amount >= poolLength)
        {
            for (int i = 0; i < poolLength; i++)
                buffer.Add(DefaultBehaviour(pool[i]));
            return;
        }

        NativeArray<BehaviourType> copy = new NativeArray<BehaviourType>(poolLength, Allocator.Temp);
        for (int i = 0; i < poolLength; i++)
            copy[i] = pool[i];

        for (int i = 0; i < amount; i++)
        {
            int j = random.NextInt(i, poolLength);
            BehaviourType temp = copy[i];
            copy[i] = copy[j];
            copy[j] = temp;
            buffer.Add(DefaultBehaviour(copy[i]));
        }

        copy.Dispose();
    }

    public static void AddPlayerControlled<T>(Baker<T> baker, Entity entity, bool startControlled)
        where T : UnityEngine.Component
    {
        baker.AddComponent<PlayerControlled>(entity);
        baker.SetComponentEnabled<PlayerControlled>(entity, startControlled);
    }

    public static Behaviour DefaultBehaviour(BehaviourType type)
    {
        return new Behaviour
        {
            behaviourType     = type,
            value             = 100f,
            decayRate         = 0f,
            contextMultiplier = 1f,
            scoringMode       = ScoringMode.Action,
            actionCategory    = DefaultActionCategory(type),
        };
    }

    private static ActionCategory DefaultActionCategory(BehaviourType type)
    {
        switch (type)
        {
            case BehaviourType.Movement:         return ActionCategory.Wander;
            case BehaviourType.SelfPreservation: return ActionCategory.Flee;
            case BehaviourType.Safety:           return ActionCategory.Flee;
            case BehaviourType.BloodLust:        return ActionCategory.Attack;
            case BehaviourType.SelfDefence:      return ActionCategory.Attack;
            case BehaviourType.Work:             return ActionCategory.Interaction;
            default:                             return ActionCategory.None;
        }
    }
}
