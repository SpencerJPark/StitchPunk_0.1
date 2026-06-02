using Unity.Entities;
using UnityEngine;

// Adds the three v2-pipeline components to a unit prefab. Place alongside UnitAuthoring — UnitType
// comes from UnitData (baked by UnitAuthoring) and is read by ActionEntitySpawnSystem at spawn.
public class UtilityBrainV2Authoring : MonoBehaviour
{
    [Tooltip("Seconds between decision polls. Lower = more responsive, higher = cheaper.")]
    public float decideInterval = 0.5f;

    public class Baker : Baker<UtilityBrainV2Authoring>
    {
        public override void Bake(UtilityBrainV2Authoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.None);

            AddComponent<UtilityBrainV2>(entity);
            SetComponentEnabled<UtilityBrainV2>(entity, true);

            AddComponent<StateMachine>(entity);

            AddComponent(entity, new DecideCadence
            {
                interval  = authoring.decideInterval,
                remaining = 0f,
            });
        }
    }
}
