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
    public struct ActionLockSettings
    {
        public float maxActionDuration;
        public float stuckThreshold;
        public float stuckTime;
        public float decisionInterval;
    }
    
    public static void AddRequirements<T>(Baker<T> baker, Entity entity, ActionLockSettings actionLockSettings) where T : UnityEngine.Component
    {
        baker.AddBuffer<ActionOption>(entity);
        baker.AddComponent<SelectedAction>(entity);
        baker.AddComponent(entity, new NeedsAction());
        baker.SetComponentEnabled<NeedsAction>(entity, false);
        baker.AddBuffer<Hurt>(entity);
    }

    public static void AddHumanMotivations<T>(Baker<T> baker, Entity entity) where T : UnityEngine.Component
    {
        baker.AddComponent<Hunger>(entity);
        baker.AddComponent<Energy>(entity);
        baker.AddComponent<Fun>(entity);
        baker.AddComponent<Social>(entity);
        baker.AddComponent<Comfort>(entity);
        baker.AddComponent<Bladder>(entity);
        baker.AddComponent<Safety>(entity);
        baker.AddComponent<Movement>(entity);
    }
    
    public static void AddRandomMotivations<TAuthoring>(this Baker<TAuthoring> baker, Entity entity, uint seed) 
        where TAuthoring : MonoBehaviour
    {
        var traitPool = new List<System.Action<Baker<TAuthoring>, Entity>>
        {
            (Baker<TAuthoring> bakerInstance, Entity targetEntity) => bakerInstance.AddComponent(targetEntity, new Bookworm { value = 0 }),
            (Baker<TAuthoring> bakerInstance, Entity targetEntity) => bakerInstance.AddComponent(targetEntity, new Work { value = 0 }),
            (Baker<TAuthoring> bakerInstance, Entity targetEntity) => bakerInstance.AddComponent(targetEntity, new NightOwl { value = 0 }),
            (Baker<TAuthoring> bakerInstance, Entity targetEntity) => bakerInstance.AddComponent(targetEntity, new EarlyBird { value = 0 }),
            (Baker<TAuthoring> bakerInstance, Entity targetEntity) => bakerInstance.AddComponent(targetEntity, new Glutton { value = 0 }),
            (Baker<TAuthoring> bakerInstance, Entity targetEntity) => bakerInstance.AddComponent(targetEntity, new Grumpy { value = 0 }),
            (Baker<TAuthoring> bakerInstance, Entity targetEntity) => bakerInstance.AddComponent(targetEntity, new Depressed { value = 0 }),
            (Baker<TAuthoring> bakerInstance, Entity targetEntity) => bakerInstance.AddComponent(targetEntity, new Lazy { value = 0 }),
            (Baker<TAuthoring> bakerInstance, Entity targetEntity) => bakerInstance.AddComponent(targetEntity, new Nervous { value = 0 })
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