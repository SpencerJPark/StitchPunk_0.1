using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

// Define the signature that all behavior functions must follow
public delegate void SelectingAction(Entity entity, int index, EntityCommandBuffer.ParallelWriter ecb);

public static class SelectingFunctions
{
    [BurstCompile]
    public static void EnableAttack(Entity entity, int index, EntityCommandBuffer.ParallelWriter ecb)
        => ecb.SetComponentEnabled<AttackAction>(index, entity, true);

    [BurstCompile]
    public static void EnablePatrol(Entity entity, int index, EntityCommandBuffer.ParallelWriter ecb)
        => ecb.SetComponentEnabled<PatrolAction>(index, entity, true);
        
    [BurstCompile]
    public static void EnableIdle(Entity entity, int index, EntityCommandBuffer.ParallelWriter ecb)
        => ecb.SetComponentEnabled<IdleAction>(index, entity, true);
}