using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

// Define the signature that all behavior functions must follow
public delegate void ActionActivationDelegate(Entity entity, int index, EntityCommandBuffer.ParallelWriter ecb);

public static class SelectionFunctions
{
    [BurstCompile]
    public static void NullEnable(Entity entity, int index, EntityCommandBuffer.ParallelWriter ecb) { }
    
    [BurstCompile]
    public static void IdleEnable(Entity entity, int index, EntityCommandBuffer.ParallelWriter ecb)
        => ecb.SetComponentEnabled<IdleAction>(index, entity, true);
    
    [BurstCompile]
    public static void WanderEnable(Entity entity, int index, EntityCommandBuffer.ParallelWriter ecb)
        => ecb.SetComponentEnabled<WanderAction>(index, entity, true);
    
    [BurstCompile]
    public static void InteractEnable(Entity entity, int index, EntityCommandBuffer.ParallelWriter ecb)
        => ecb.SetComponentEnabled<InteractAction>(index, entity, true);
    
    [BurstCompile]
    public static void PunchEnable(Entity entity, int index, EntityCommandBuffer.ParallelWriter ecb)
        => ecb.SetComponentEnabled<PunchAction>(index, entity, true);
    
    [BurstCompile]
    public static void FleeEnable(Entity entity, int index, EntityCommandBuffer.ParallelWriter ecb)
        => ecb.SetComponentEnabled<FleeAction>(index, entity, true);
}