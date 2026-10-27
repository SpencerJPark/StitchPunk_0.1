using DotsMovementToolkit;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// Walks a fresh summon out of the caravan's back and hops it down to the ground (Caravan_System.md §4). The unit
// is scripted, not steered: CutsceneActor keeps its AI and facing out of the way (CV-Q2), and it is placed after
// gravity every frame. On landing it gets its rally move order, which WorkstationSummonSpawnSystem left disabled.
[BurstCompile]
[UpdateInGroup(typeof(MovementExecutionSystemGroup))]
[UpdateAfter(typeof(UnitGravitySystem))]
public partial struct CaravanExitWalkSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<CaravanExitWalker>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;
        // Main-thread lookup reads/writes on other entities wait for nothing on their own (Gotchas.md).
        state.CompleteDependency();
        EntityCommandBuffer commandBuffer = new EntityCommandBuffer(Allocator.Temp);
        NativeList<Entity> landedUnits = new NativeList<Entity>(4, Allocator.Temp);

        foreach ((RefRW<LocalTransform> unitTransform, RefRW<CaravanExitWalker> exitWalker, Entity unitEntity)
                 in SystemAPI.Query<RefRW<LocalTransform>, RefRW<CaravanExitWalker>>().WithEntityAccess())
        {
            CaravanExitWalker walker = exitWalker.ValueRO;
            walker.elapsedSeconds += deltaTime;
            exitWalker.ValueRW.elapsedSeconds = walker.elapsedSeconds;

            float3 position;
            if (walker.elapsedSeconds < walker.walkOutSeconds)
            {
                float walkProgress = walker.elapsedSeconds / math.max(walker.walkOutSeconds, 0.0001f);
                position = math.lerp(walker.interiorPoint, walker.tailgatePoint, walkProgress);
            }
            else
            {
                float hopProgress = math.saturate((walker.elapsedSeconds - walker.walkOutSeconds) / math.max(walker.hopSeconds, 0.0001f));
                position = math.lerp(walker.tailgatePoint, walker.landingPoint, hopProgress);
                position.y += walker.hopArcHeight * 4f * hopProgress * (1f - hopProgress);
            }

            float3 walkDirection = walker.landingPoint - walker.interiorPoint;
            walkDirection.y = 0f;
            unitTransform.ValueRW.Position = position;
            if (math.lengthsq(walkDirection) > 0.0001f)
                unitTransform.ValueRW.Rotation = quaternion.LookRotationSafe(math.normalize(walkDirection), math.up());

            bool landed = walker.elapsedSeconds >= walker.walkOutSeconds + walker.hopSeconds;
            if (landed)
            {
                landedUnits.Add(unitEntity);
                commandBuffer.RemoveComponent<CaravanExitWalker>(unitEntity);
            }

            if (SystemAPI.HasComponent<Movement>(unitEntity))
                SystemAPI.GetComponentRW<Movement>(unitEntity).ValueRW.targetPosition = position;
            if (SystemAPI.HasComponent<Gravity>(unitEntity))
                SystemAPI.GetComponentRW<Gravity>(unitEntity).ValueRW.verticalVelocity = 0f;
            // Re-asserted every frame: spawn init clears it on the unit's first frame.
            if (SystemAPI.HasComponent<CutsceneActor>(unitEntity))
                SystemAPI.SetComponentEnabled<CutsceneActor>(unitEntity, !landed);
        }

        for (int landedIndex = 0; landedIndex < landedUnits.Length; landedIndex++)
        {
            if (SystemAPI.HasComponent<OnMinionMoveCommand>(landedUnits[landedIndex]))
                SystemAPI.SetComponentEnabled<OnMinionMoveCommand>(landedUnits[landedIndex], true);
        }

        commandBuffer.Playback(state.EntityManager);
        commandBuffer.Dispose();
        landedUnits.Dispose();
    }
}
