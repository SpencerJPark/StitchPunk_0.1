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
        baker.AddComponent(entity, new Awareness
        {
            range = awarenessRange
        });
        //baker.AddBuffer<Hurt>(entity);
    }

    public static void AddPlayerControllable<T>(Baker<T> baker, Entity entity) where T : UnityEngine.Component
    {
        baker.AddComponent<PlayerControlled>(entity);
        baker.SetComponentEnabled<PlayerControlled>(entity, false);
    }

    public static void AddHumanMotivations<T>(Baker<T> baker, Entity entity) where T : UnityEngine.Component
    {
        baker.AddComponent<HungerMotivation>(entity);
        baker.AddComponent<EnergyMotivation>(entity);
        baker.AddComponent<FunMotivation>(entity);
        baker.AddComponent<SocialMotivation>(entity);
        baker.AddComponent<ComfortMotivation>(entity);
        baker.AddComponent<BladderMotivation>(entity);
        baker.AddComponent<SafetyMotivation>(entity);
        baker.AddComponent<MovementMotivation>(entity);
        baker.AddComponent<SelfPreservationMotivation>(entity);
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