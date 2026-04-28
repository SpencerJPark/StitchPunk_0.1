using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

// Both structs must be passed by 'in' (read-only reference) for Burst compatibility
public delegate void ActionActivationDelegate(in Entity entity, int index, ref EntityCommandBuffer.ParallelWriter ecb);

[BurstCompile]
public static class SelectionFunctions
{
    [BurstCompile]
    public static void NullEnable(in Entity entity, int index, ref EntityCommandBuffer.ParallelWriter ecb) { }
    
    [BurstCompile]
    public static void IdleEnable(in Entity entity, int index, ref EntityCommandBuffer.ParallelWriter ecb)
        => ecb.SetComponentEnabled<IdleAction>(index, entity, true);
    
    [BurstCompile]
    public static void WanderEnable(in Entity entity, int index, ref EntityCommandBuffer.ParallelWriter ecb)
        => ecb.SetComponentEnabled<WanderAction>(index, entity, true);
    
    [BurstCompile]
    public static void InteractEnable(in Entity entity, int index, ref EntityCommandBuffer.ParallelWriter ecb)
        => ecb.SetComponentEnabled<InteractAction>(index, entity, true);
    
    [BurstCompile]
    public static void MeleeEnable(in Entity entity, int index, ref EntityCommandBuffer.ParallelWriter ecb)
        => ecb.SetComponentEnabled<MeleeContinuousAction>(index, entity, true);
    
    [BurstCompile]
    public static void MeleeSingleEnable(in Entity entity, int index, ref EntityCommandBuffer.ParallelWriter ecb)
        => ecb.SetComponentEnabled<MeleeSingleAction>(index, entity, true);

    [BurstCompile]
    public static void FleeEnable(in Entity entity, int index, ref EntityCommandBuffer.ParallelWriter ecb)
        => ecb.SetComponentEnabled<FleeAction>(index, entity, true);
}