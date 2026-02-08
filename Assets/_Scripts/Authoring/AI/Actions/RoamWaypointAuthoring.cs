using Unity.Entities;
using UnityEngine;

public class RoamWaypointAuthoring : MonoBehaviour
{
    public float arrivalThreshold = 3f;

    public class Baker : Baker<RoamWaypointAuthoring>
    {
        public override void Bake(RoamWaypointAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<RoamWaypoint>(entity, new RoamWaypoint
            {
                arrivalThreshold = authoring.arrivalThreshold
            });
        }
    }
}

public struct RoamWaypoint : IComponentData
{
    public float arrivalThreshold;
}