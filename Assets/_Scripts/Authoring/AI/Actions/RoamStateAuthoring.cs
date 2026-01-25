using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class RoamStateAuthoring : MonoBehaviour
{
    public float arrivalThreshold = 2f;
    public float minWaitTime = 0f;
    public float maxWaitTime = 3f;

    public class Baker : Baker<RoamStateAuthoring>
    {
        public override void Bake(RoamStateAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<RoamState>(entity, new RoamState
            {
                currentWaypoint = Entity.Null,
                arrivalThreshold = authoring.arrivalThreshold,
                minWaitTime = authoring.minWaitTime,
                maxWaitTime = authoring.maxWaitTime,
                waitTimer = 0f
            });
        }
    }
}

public struct RoamState : IComponentData
{
    public Entity currentWaypoint;
    public float arrivalThreshold;
    public float minWaitTime;
    public float maxWaitTime;
    public float waitTimer;
}