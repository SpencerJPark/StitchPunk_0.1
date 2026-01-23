using Unity.Entities;
using UnityEngine;

public class AwarenessAuthoring : MonoBehaviour
{
    public float radius = 10f;

    public class Baker : Baker<AwarenessAuthoring>
    {
        public override void Bake(AwarenessAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new Awareness());
            AddComponent(entity, new AwarenessRadius { radius = authoring.radius });
            AddBuffer<SensedEntity>(entity);
        }
    }
}

public struct Awareness : IComponentData
{
    public Entity nearestFood;
    public Entity nearestBed;
    public Entity nearestWork;
    public Entity nearestEntertainment;
    public Entity nearestSmokeSpot;
    public Entity nearestBar;

    public bool hasFood;
    public bool hasBed;
    public bool hasWork;
    public bool hasEntertainment;
    public bool hasSmokeSpot;
    public bool hasBar;
}

public struct AwarenessRadius : IComponentData
{
    public float radius;
}