using Unity.Entities;
using UnityEngine;

// Adds the health/life components every unit needs. The NUMBERS here are a fallback, not the
// authority: UnitHealthInitSystem overwrites both from UnitSO.maxHealth at spawn, so a spawned unit
// always carries the blob's value and per-prefab tuning would silently do nothing. They still
// matter for an entity that never passes through the spawner — a unit placed directly in a subscene
// — which is the only reason they are still serialized.
public class HealthAuthoring : MonoBehaviour {

    [Tooltip("Fallback only — a spawned unit gets UnitSO.maxHealth instead. Used by units placed " +
             "directly in a scene, which never run the spawn-init health stamp.")]
    public int healthAmount;

    [Tooltip("Fallback only — see Health Amount.")]
    public int healthAmountMax;

    public class Baker : Baker<HealthAuthoring> {
        public override void Bake(HealthAuthoring authoring) {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new Health {
                healthAmount = authoring.healthAmount,
                healthAmountMax = authoring.healthAmountMax,
            });
            AddComponent<Dead>(entity);
            SetComponentEnabled<Dead>(entity, false);
            AddComponent<HealRequest>(entity);
            SetComponentEnabled<HealRequest>(entity, false);
        }

    }

}
