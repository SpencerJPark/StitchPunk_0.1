using Unity.Entities;
using UnityEngine;

public class RoamStateAuthoring : MonoBehaviour
{
    public class Baker : Baker<RoamStateAuthoring>
    {
        public override void Bake(RoamStateAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<RoamState>(entity, new RoamState
            {
                currentWaypoint = Entity.Null,
                previousWaypoint = Entity.Null
            });
        }
    }
}

public struct RoamState : IComponentData
{
    public Entity currentWaypoint;
    public Entity previousWaypoint;
}