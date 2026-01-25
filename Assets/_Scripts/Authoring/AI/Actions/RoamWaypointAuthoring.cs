using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class RoamWaypointAuthoring : MonoBehaviour
{
    public class Baker : Baker<RoamWaypointAuthoring>
    {
        public override void Bake(RoamWaypointAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<RoamWaypoint>(entity);
        }
    }
}

public struct RoamWaypoint : IComponentData { }