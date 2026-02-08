using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class AwarenessAuthoring : MonoBehaviour
{
    public float radius = 15f;

    public class Baker : Baker<AwarenessAuthoring>
    {
        public override void Bake(AwarenessAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<Awareness>(entity);
            AddComponent<AwarenessRadius>(entity, new AwarenessRadius { radius = authoring.radius });
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
    public Entity nearestBathroom;
    public Entity nearestSeat;

    public float nearestFoodDistance;
    public float nearestBedDistance;
    public float nearestWorkDistance;
    public float nearestEntertainmentDistance;
    public float nearestSmokeSpotDistance;
    public float nearestBarDistance;
    public float nearestBathroomDistance;
    public float nearestSeatDistance;

    public bool hasFood;
    public bool hasBed;
    public bool hasWork;
    public bool hasEntertainment;
    public bool hasSmokeSpot;
    public bool hasBar;
    public bool hasBathroom;
    public bool hasSeat;
}

public struct AwarenessRadius : IComponentData
{
    public float radius;
}

public struct SensedEntity : IBufferElementData
{
    public Entity entity;
    public InteractableType type;
    public float distance;
    public float3 position;
}