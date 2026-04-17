using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Static helper used by all brain startingNeedsSettings Bakers to register the full set
/// of enableable action components. Every brain needs all of these (disabled by default)
/// so downstream systems can enable them at runtime.
///
/// WHEN ADDING A NEW ActiveXxx COMPONENT:
/// Add a single line here and it automatically applies to all brain types.
/// </summary>
public static class BrainBakeHelper
{
    
    public static void AddRequirements<T>(Baker<T> baker, Entity entity, float awarenessRange) where T : UnityEngine.Component
    {
        baker.AddComponent<ActiveBrain>(entity);
        baker.SetComponentEnabled<ActiveBrain>(entity, true);

        baker.AddBuffer<ActionOption>(entity);
        baker.AddComponent<SelectedAction>(entity);
        baker.AddComponent<NeedsAction>(entity);
        baker.SetComponentEnabled<NeedsAction>(entity, true);
        baker.AddComponent(entity, new Awareness { range = awarenessRange });
        baker.AddComponent(entity, new ActionTimer { time = 0f });

        // Player control — baked on all brains (disabled by default). Allows
        // MoveToTargetSystem's [WithDisabled(PlayerControlled)] to work for every brain type.
        baker.AddComponent<PlayerControlled>(entity);
        baker.SetComponentEnabled<PlayerControlled>(entity, false);

        // Combat
        baker.AddComponent<Target>(entity);
        baker.SetComponentEnabled<Target>(entity, false);

        // Shared move-to-target
        baker.AddComponent<MoveToTargetRequest>(entity);
        baker.SetComponentEnabled<MoveToTargetRequest>(entity, false);
        baker.AddComponent<ArrivedAtTarget>(entity);
        baker.SetComponentEnabled<ArrivedAtTarget>(entity, false);

        // Behavioral states (all disabled by default)
        baker.AddComponent<AggressiveState>(entity); baker.SetComponentEnabled<AggressiveState>(entity, false);
        baker.AddComponent<DrunkState>(entity);      baker.SetComponentEnabled<DrunkState>(entity, false);
        baker.AddComponent<StunnedState>(entity);    baker.SetComponentEnabled<StunnedState>(entity, false);
        baker.AddComponent<PanickedState>(entity);   baker.SetComponentEnabled<PanickedState>(entity, false);
        baker.AddComponent<BerserkState>(entity);    baker.SetComponentEnabled<BerserkState>(entity, false);
    }

    // Adds the 9 standard citizen motivations as a unified DynamicBuffer<Motivation>.
    // All are Interaction mode (require a spatial query to find an entity to act on).
    // Returns the buffer so callers can append additional entries (e.g. AddCitizenBehaviours).
    public static DynamicBuffer<Behaviour> AddHumanMotivations<T>(Baker<T> baker, Entity entity) where T : UnityEngine.Component
    {
        DynamicBuffer<Behaviour> motivations = baker.AddBuffer<Behaviour>(entity);
        motivations.Add(new Behaviour { behaviourType = BehaviourType.Hunger,           value = 50f,  decayRate = 2.0f, contextMultiplier = 1f, scoringMode = ScoringMode.Interaction, actionCategory = ActionCategory.Interaction });
        motivations.Add(new Behaviour { behaviourType = BehaviourType.Energy,           value = 100f, decayRate = 1.5f, contextMultiplier = 1f, scoringMode = ScoringMode.Interaction, actionCategory = ActionCategory.Interaction });
        motivations.Add(new Behaviour { behaviourType = BehaviourType.Fun,              value = 50f,  decayRate = 1.0f, contextMultiplier = 1f, scoringMode = ScoringMode.Interaction, actionCategory = ActionCategory.Interaction });
        motivations.Add(new Behaviour { behaviourType = BehaviourType.Social,           value = 50f,  decayRate = 0.8f, contextMultiplier = 1f, scoringMode = ScoringMode.Interaction, actionCategory = ActionCategory.Interaction });
        motivations.Add(new Behaviour { behaviourType = BehaviourType.Comfort,          value = 50f,  decayRate = 0.5f, contextMultiplier = 1f, scoringMode = ScoringMode.Interaction, actionCategory = ActionCategory.Interaction });
        motivations.Add(new Behaviour { behaviourType = BehaviourType.Bladder,          value = 100f, decayRate = 0.5f, contextMultiplier = 1f, scoringMode = ScoringMode.Interaction, actionCategory = ActionCategory.Interaction });
        motivations.Add(new Behaviour { behaviourType = BehaviourType.Safety,           value = 100f, decayRate = 0f,   contextMultiplier = 1f, scoringMode = ScoringMode.Interaction, actionCategory = ActionCategory.Interaction });
        motivations.Add(new Behaviour { behaviourType = BehaviourType.Movement,         value = 50f,  decayRate = 3.0f, contextMultiplier = 1f, scoringMode = ScoringMode.Interaction, actionCategory = ActionCategory.Interaction });
        motivations.Add(new Behaviour { behaviourType = BehaviourType.SelfPreservation, value = 100f, decayRate = 0f,   contextMultiplier = 1f, scoringMode = ScoringMode.Interaction, actionCategory = ActionCategory.Interaction });
        return motivations;
    }

    // Appends self-directed Action motivations to an existing Motivation buffer.
    // These score directly (no spatial query). Execution systems resolve targeting
    // from the Target component when needed.
    //
    // Wander reuses the Movement curve — high movement urgency makes both seeking
    // a MovementInteraction and wandering score higher; interactions win if nearby.
    // Flee shares SelfPreservation so pre-pass systems amplify both together.
    public static void AddCitizenBehaviours(ref DynamicBuffer<Behaviour> motivations)
    {
        motivations.Add(new Behaviour
        {
            behaviourType  = BehaviourType.Movement,
            scoringMode     = ScoringMode.Action,
            actionCategory  = ActionCategory.Wander,
            value           = 100f,
            decayRate       = 0f,
            contextMultiplier = 1f,
        });
        motivations.Add(new Behaviour
        {
            behaviourType  = BehaviourType.SelfPreservation,
            scoringMode     = ScoringMode.Action,
            actionCategory  = ActionCategory.Flee,
            value           = 100f,
            decayRate       = 0f,
            contextMultiplier = 1f,
        });
    }
    
    // Reactive combat — scores ActionCategory.Attack when CombatAwarenessSystem detects a hostile.
    // contextMultiplier is set to 3.0 by CombatAwarenessSystem; decay resets it to 1.0 each frame.
    public static void AddSelfDefence(ref DynamicBuffer<Behaviour> motivations)
    {
        motivations.Add(new Behaviour
        {
            behaviourType    = BehaviourType.SelfDefence,
            scoringMode       = ScoringMode.Action,
            actionCategory    = ActionCategory.Attack,
            value             = 100f,
            decayRate         = 0f,
            contextMultiplier = 1f,
        });
    }

    // Aggressive combat — scores ActionCategory.Attack when any hostile is in range.
    // contextMultiplier is set to 2.5 by CombatAwarenessSystem; decay resets it to 1.0 each frame.
    public static void AddBloodLust(ref DynamicBuffer<Behaviour> motivations)
    {
        motivations.Add(new Behaviour
        {
            behaviourType    = BehaviourType.BloodLust,
            scoringMode       = ScoringMode.Action,
            actionCategory    = ActionCategory.Attack,
            value             = 100f,
            decayRate         = 0f,
            contextMultiplier = 1f,
        });
    }

    public static void AddRandomMotivations<TAuthoring>(this Baker<TAuthoring> baker, Entity entity, uint seed) 
        where TAuthoring : MonoBehaviour
    {
        var traitPool = new List<System.Action<Baker<TAuthoring>, Entity>>
        {
            (Baker<TAuthoring> bakerInstance, Entity targetEntity) => bakerInstance.AddComponent(targetEntity, new BookwormMotivation { value = 0 }),
            (Baker<TAuthoring> bakerInstance, Entity targetEntity) => bakerInstance.AddComponent(targetEntity, new WorkMotivation { value = 0 }),
            (Baker<TAuthoring> bakerInstance, Entity targetEntity) => bakerInstance.AddComponent(targetEntity, new NightOwlMotivation { value = 0 }),
            (Baker<TAuthoring> bakerInstance, Entity targetEntity) => bakerInstance.AddComponent(targetEntity, new EarlyBirdMotivation { value = 0 }),
            (Baker<TAuthoring> bakerInstance, Entity targetEntity) => bakerInstance.AddComponent(targetEntity, new GluttonMotivation { value = 0 }),
            (Baker<TAuthoring> bakerInstance, Entity targetEntity) => bakerInstance.AddComponent(targetEntity, new GrumpyMotivation { value = 0 }),
            (Baker<TAuthoring> bakerInstance, Entity targetEntity) => bakerInstance.AddComponent(targetEntity, new DepressedMotivation { value = 0 }),
            (Baker<TAuthoring> bakerInstance, Entity targetEntity) => bakerInstance.AddComponent(targetEntity, new LazyMotivation { value = 0 }),
            (Baker<TAuthoring> bakerInstance, Entity targetEntity) => bakerInstance.AddComponent(targetEntity, new NervousMotivation { value = 0 })
        };

        // Initialize Unity Mathematics Random with the provided seed
        Unity.Mathematics.Random randomGenerator = new Unity.Mathematics.Random(seed);

        int traitsAddedCount = 0;
        const int targetTraitCount = 5;

        // Loop until we have added 5 traits or run out of available traits in the pool
        while (traitsAddedCount < targetTraitCount && traitPool.Count > 0)
        {
            // Pick a random index from the remaining list
            int randomIndex = randomGenerator.NextInt(0, traitPool.Count);
            
            // Execute the action at that index
            System.Action<Baker<TAuthoring>, Entity> addTraitAction = traitPool[randomIndex];
            addTraitAction.Invoke(baker, entity);

            // Remove the trait from the pool so it cannot be picked again (ensures uniqueness)
            traitPool.RemoveAt(randomIndex);
            traitsAddedCount++;
        }
    }
}