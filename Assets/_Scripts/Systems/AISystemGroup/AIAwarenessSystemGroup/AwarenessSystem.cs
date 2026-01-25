using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

[BurstCompile]
[UpdateInGroup(typeof(AIAwarenessSystemGroup))]
public partial struct AwarenessSystem : ISystem
{
    private ComponentLookup<Interactable> interactableLookup;
    private ComponentLookup<LocalTransform> transformLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PhysicsWorldSingleton>();
        interactableLookup = SystemAPI.GetComponentLookup<Interactable>(true);
        transformLookup = SystemAPI.GetComponentLookup<LocalTransform>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        interactableLookup.Update(ref state);
        transformLookup.Update(ref state);

        PhysicsWorldSingleton physicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>();

        state.Dependency = new AwarenessJob
        {
            physicsWorld = physicsWorld,
            interactableLookup = interactableLookup,
            transformLookup = transformLookup
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
public partial struct AwarenessJob : IJobEntity
{
    [ReadOnly] public PhysicsWorldSingleton physicsWorld;
    [ReadOnly] public ComponentLookup<Interactable> interactableLookup;
    [ReadOnly] public ComponentLookup<LocalTransform> transformLookup;

    public void Execute(
        ref Awareness awareness,
        ref DynamicBuffer<SensedEntity> sensedEntities,
        in BrainLink brainLink,
        in AwarenessRadius awarenessRadius)
    {
        sensedEntities.Clear();
        awareness = default;

        if (!transformLookup.TryGetComponent(brainLink.body, out LocalTransform bodyTransform))
            return;

        float3 position = bodyTransform.Position;

        NativeList<DistanceHit> hits = new NativeList<DistanceHit>(Allocator.Temp);

        CollisionFilter filter = new CollisionFilter
        {
            BelongsTo = ~0u,
            CollidesWith = 1u << GameAssets.UNITS_LAYER | 1u << GameAssets.OBJECTS_LAYER | 1u << GameAssets.INTERACTABLE_LAYER,
            GroupIndex = 0
        };

        bool hasHits = physicsWorld.OverlapSphere(position, awarenessRadius.radius, ref hits, filter);

        if (!hasHits)
        {
            hits.Dispose();
            return;
        }

        float nearestFoodDist = float.MaxValue;
        float nearestBedDist = float.MaxValue;
        float nearestWorkDist = float.MaxValue;
        float nearestEntertainmentDist = float.MaxValue;
        float nearestSmokeDist = float.MaxValue;
        float nearestBarDist = float.MaxValue;

        for (int i = 0; i < hits.Length; i++)
        {
            DistanceHit hit = hits[i];

            if (!interactableLookup.TryGetComponent(hit.Entity, out Interactable interactable))
                continue;

            float dist = hit.Distance;
            float3 hitPosition = hit.Position;

            sensedEntities.Add(new SensedEntity
            {
                entity = hit.Entity,
                type = interactable.type,
                distance = dist,
                position = hitPosition
            });

            switch (interactable.type)
            {
                case InteractableType.Food:
                    if (dist < nearestFoodDist)
                    {
                        nearestFoodDist = dist;
                        awareness.nearestFood = hit.Entity;
                        awareness.hasFood = true;
                    }
                    break;

                case InteractableType.Bed:
                    if (dist < nearestBedDist)
                    {
                        nearestBedDist = dist;
                        awareness.nearestBed = hit.Entity;
                        awareness.hasBed = true;
                    }
                    break;

                case InteractableType.Workstation:
                    if (dist < nearestWorkDist)
                    {
                        nearestWorkDist = dist;
                        awareness.nearestWork = hit.Entity;
                        awareness.hasWork = true;
                    }
                    break;

                case InteractableType.Entertainment:
                    if (dist < nearestEntertainmentDist)
                    {
                        nearestEntertainmentDist = dist;
                        awareness.nearestEntertainment = hit.Entity;
                        awareness.hasEntertainment = true;
                    }
                    break;

                case InteractableType.SmokingSpot:
                    if (dist < nearestSmokeDist)
                    {
                        nearestSmokeDist = dist;
                        awareness.nearestSmokeSpot = hit.Entity;
                        awareness.hasSmokeSpot = true;
                    }
                    break;

                case InteractableType.Bar:
                    if (dist < nearestBarDist)
                    {
                        nearestBarDist = dist;
                        awareness.nearestBar = hit.Entity;
                        awareness.hasBar = true;
                    }
                    break;
            }
        }

        hits.Dispose();
    }
}