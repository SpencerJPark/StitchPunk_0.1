using Unity.Entities;
using UnityEngine;

// Add to every unit prefab (citizens, enemies, minions — all AI units).
// Replaces CitizenBrainAuthoring and ZombieBrainAuthoring.
//
// Brain personality is determined by the BrainType enum — NOT by the authoring class.
// To change how a unit thinks, change initialBrain. No new authoring components needed.
//
// MotivationState is baked zeroed — SwapBrainSystem (or UnitSpawnerSystem) sets initial
// values from BrainLibraryBlob when the unit is spawned or transitions states.
public class BrainAuthoring : MonoBehaviour
{
    [SearchableEnum]
    [Tooltip("Which brain this unit starts with. Can be swapped at runtime via SwapBrainRequest.")]
    public BrainType initialBrain;

    public class Baker : Baker<BrainAuthoring>
    {
        public override void Bake(BrainAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent(entity, new Brain { activeBrain = authoring.initialBrain });
            AddBuffer<ActionOption>(entity);
            AddComponent(entity, new SelectedAction());
            AddComponent(entity, new NeedsAction());
            SetComponentEnabled<NeedsAction>(entity, true);
            AddComponent(entity, new SwapBrainRequest());
            SetComponentEnabled<SwapBrainRequest>(entity, false);
            AddComponent(entity, new PlayerControlled());
            SetComponentEnabled<PlayerControlled>(entity, false); // required by ActionSelectionSystem [WithDisabled]
            AddComponent(entity, new CombatTarget());
            SetComponentEnabled<CombatTarget>(entity, false);
        }
    }
}
