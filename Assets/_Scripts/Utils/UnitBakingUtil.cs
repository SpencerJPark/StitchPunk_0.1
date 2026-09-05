using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using System.Collections.Generic;

public static class UnitBakingUtil
{
    public static void BakeRequirements<T>(Baker<T> baker, Entity entity, bool active, UnitSO unitSo)
        where T : UnityEngine.Component
    {
        baker.AddComponent(entity, new UtilityBrain { unitType = unitSo.unitType });
        baker.SetComponentEnabled<UtilityBrain>(entity, active);

        baker.AddBuffer<UtilityActions>(entity);
        baker.AddComponent<CurrentAction>(entity);
        baker.AddComponent<ActionRequest>(entity);
        baker.SetComponentEnabled<ActionRequest>(entity, active);
        baker.AddComponent<ActionSelectionValidationRequest>(entity);
        baker.SetComponentEnabled<ActionSelectionValidationRequest>(entity, false);

        baker.AddComponent(entity, new Awareness { range = unitSo.awarenessRange });

        // UnitSO is the source of truth for brain-unit speeds. MovementAuthoring adds the
        // Movement component; UnitSpeedBakingSystem overwrites its values with these.
        baker.AddComponent(entity, new UnitSpeedBakingData
        {
            moveSpeed     = unitSo.moveSpeed,
            runSpeed      = unitSo.runSpeed,
            rotationSpeed = unitSo.rotationSpeed,
        });

        baker.AddComponent<AggressiveState>(entity);
        baker.SetComponentEnabled<AggressiveState>(entity, false);

        DynamicBuffer<Motivation> behaviourBuffer = baker.AddBuffer<Motivation>(entity);
        if (unitSo.motivations != null)
            PopulateBehaviours(behaviourBuffer, unitSo.motivations);

        if (unitSo.randomMotivations != null && unitSo.randomMotivations.Length > 0 && unitSo.randomMotivationsTotal > 0)
            PopulateRandomBehaviours(behaviourBuffer, unitSo.randomMotivations, unitSo.randomMotivationsTotal);

        if (unitSo.motivationDecayRates != null && unitSo.motivationDecayRates.Count > 0)
            ApplyDecayRates(behaviourBuffer, unitSo.motivationDecayRates);

        DynamicBuffer<AttackFaction> attackBuffer = baker.AddBuffer<AttackFaction>(entity);
        if (unitSo.attackFactions != null)
        {
            for (int i = 0; i < unitSo.attackFactions.Length; i++)
                attackBuffer.Add(new AttackFaction { faction = unitSo.attackFactions[i] });
        }

        if (unitSo.canBePlayerControlled)
        {
            AddPlayerControlled(baker, entity, false);
        }

        baker.AddComponent<ActionInterruptRequest>(entity);
        baker.SetComponentEnabled<ActionInterruptRequest>(entity, false);

        baker.AddComponent<CutsceneActor>(entity);
        baker.SetComponentEnabled<CutsceneActor>(entity, false);

        baker.AddBuffer<MotivationChangeRequest>(entity);
        baker.AddBuffer<RecentWaypoint>(entity);
        baker.AddBuffer<RecentInteraction>(entity);

        baker.AddComponent<SwapBrainRequest>(entity);
        baker.SetComponentEnabled<SwapBrainRequest>(entity, false);

        baker.AddComponent<ZombifyRequest>(entity);
        baker.SetComponentEnabled<ZombifyRequest>(entity, false);


        baker.AddComponent<SocialInvite>(entity);
        baker.SetComponentEnabled<SocialInvite>(entity, false);
        

        DynamicBuffer<AvailableAttack> availableAttackBuffer = baker.AddBuffer<AvailableAttack>(entity);
        if (unitSo.attacks != null)
            foreach (AttackActionMapping mapping in unitSo.attacks)
                availableAttackBuffer.Add(new AvailableAttack { actionType = mapping.action, damageSource = mapping.attack });

        System.Random braveRng       = new System.Random();
        float         braveBase      = unitSo.bravery        + (float)(braveRng.NextDouble() * 2.0 - 1.0) * unitSo.braveryVariance;
        float         socialBase     = unitSo.socialAffinity + (float)(braveRng.NextDouble() * 2.0 - 1.0) * unitSo.socialAffinityVariance;
        float         wanderlustBase = unitSo.wanderlust     + (float)(braveRng.NextDouble() * 2.0 - 1.0) * unitSo.wanderlustVariance;
        float         gluttonyBase   = unitSo.gluttony       + (float)(braveRng.NextDouble() * 2.0 - 1.0) * unitSo.gluttonyVariance;

        baker.AddComponent(entity, new StateMachine
        {
            action              = ActionType.Idle,
            activeBehavior      = BehaviorType.None,
            targetEntity        = Entity.Null,
            currentPhase        = BehaviorPhase.Execute,
            CurrentCommandIndex = 0,
            CommandTimer        = 0f,
            currentStance       = StanceType.Normal,
        });

        DynamicBuffer<PersonalityAttributes> personalityBuffer = baker.AddBuffer<PersonalityAttributes>(entity);
        personalityBuffer.Add(new PersonalityAttributes { personality = PersonalityTypes.Bravery,  value = braveBase });
        personalityBuffer.Add(new PersonalityAttributes { personality = PersonalityTypes.Friendly, value = socialBase });
    }

    public static void AddAction<TAuthoring, TTask>(Baker<TAuthoring> baker, Entity entity)
        where TAuthoring : UnityEngine.Component
        where TTask : unmanaged, IComponentData, IEnableableComponent
    {
        baker.AddComponent<TTask>(entity);
        baker.SetComponentEnabled<TTask>(entity, false);
    }

    public static void PopulateBehaviours(DynamicBuffer<Motivation> buffer, NeedType[] types)
    {
        for (int i = 0; i < types.Length; i++)
            buffer.Add(DefaultBehaviour(types[i]));
    }

    public static void PopulateRandomBehaviours(DynamicBuffer<Motivation> buffer, NeedType[] pool, int amount)
    {
        if (pool == null || pool.Length == 0 || amount <= 0)
            return;

        if (amount >= pool.Length)
        {
            for (int i = 0; i < pool.Length; i++)
                buffer.Add(DefaultBehaviour(pool[i]));
            return;
        }

        NeedType[] copy = new NeedType[pool.Length];
        System.Array.Copy(pool, copy, pool.Length);

        System.Random rng = new System.Random();
        for (int i = 0; i < amount; i++)
        {
            int j = rng.Next(i, copy.Length);
            NeedType temp = copy[i];
            copy[i] = copy[j];
            copy[j] = temp;
            buffer.Add(DefaultBehaviour(copy[i]));
        }
    }

    public static void PopulateRandomBehaviours(
        DynamicBuffer<Motivation> buffer,
        ref BlobArray<NeedType> pool,
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

        NativeArray<NeedType> copy = new NativeArray<NeedType>(poolLength, Allocator.Temp);
        for (int i = 0; i < poolLength; i++)
            copy[i] = pool[i];

        for (int i = 0; i < amount; i++)
        {
            int j = random.NextInt(i, poolLength);
            NeedType temp = copy[i];
            copy[i] = copy[j];
            copy[j] = temp;
            buffer.Add(DefaultBehaviour(copy[i]));
        }

        copy.Dispose();
    }

    public static void ApplyDecayRates(DynamicBuffer<Motivation> buffer, List<MotivationDecayConfig> rates)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            Motivation m = buffer[i];
            for (int r = 0; r < rates.Count; r++)
            {
                if (rates[r].needType != m.needType)
                    continue;
                m.decayRate = rates[r].decayRate;
                buffer[i]   = m;
                break;
            }
        }
    }

    public static void AddPlayerControlled<T>(Baker<T> baker, Entity entity, bool startControlled)
        where T : UnityEngine.Component
    {
        baker.AddComponent<PlayerUnitBrain>(entity);
        baker.SetComponentEnabled<PlayerUnitBrain>(entity, startControlled);

        // Per-minion command slots — UnitSelectionManager fans commands out to selected minions;
        // MinionActionSelectionSystem consumes them one-shot (disables after emitting an option).
        baker.AddComponent<OnMinionMoveCommand>(entity);
        baker.SetComponentEnabled<OnMinionMoveCommand>(entity, false);
        baker.AddComponent<OnMinionAttackCommand>(entity);
        baker.SetComponentEnabled<OnMinionAttackCommand>(entity, false);
        baker.AddComponent<OnMinionInteractCommand>(entity);
        baker.SetComponentEnabled<OnMinionInteractCommand>(entity, false);
        baker.AddComponent<OnMinionFollowCommand>(entity);
        baker.SetComponentEnabled<OnMinionFollowCommand>(entity, false);
        baker.AddComponent<OnMinionDefendCommand>(entity);
        baker.SetComponentEnabled<OnMinionDefendCommand>(entity, false);
    }

    public static Motivation DefaultBehaviour(NeedType type)
    {
        return new Motivation
        {
            needType          = type,
            value             = 100f,
            decayRate         = 0f,
        };
    }
}
