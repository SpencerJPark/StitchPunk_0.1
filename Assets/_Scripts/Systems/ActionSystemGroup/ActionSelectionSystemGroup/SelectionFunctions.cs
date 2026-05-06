using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

// Both structs must be passed by 'in' (read-only reference) for Burst compatibility
public delegate void ActionActivationDelegate(in Entity entity, int index, ref EntityCommandBuffer.ParallelWriter ecb, bool enabled);

[BurstCompile]
public static class SelectionFunctions
{
    [BurstCompile]
    public static void NullEnable(in Entity entity, int index, ref EntityCommandBuffer.ParallelWriter ecb, bool enabled) { }
    
    [BurstCompile]
    public static void IdleEnable(in Entity entity, int index, ref EntityCommandBuffer.ParallelWriter ecb, bool enabled)
        => ecb.SetComponentEnabled<IdleAction>(index, entity, enabled);
    
    [BurstCompile]
    public static void WanderEnable(in Entity entity, int index, ref EntityCommandBuffer.ParallelWriter ecb, bool enabled)
        => ecb.SetComponentEnabled<WanderAction>(index, entity, enabled);
    
    [BurstCompile]
    public static void InteractEnable(in Entity entity, int index, ref EntityCommandBuffer.ParallelWriter ecb, bool enabled)
        => ecb.SetComponentEnabled<InteractAction>(index, entity, enabled);
    
    [BurstCompile]
    public static void MeleeEnable(in Entity entity, int index, ref EntityCommandBuffer.ParallelWriter ecb, bool enabled)
        => ecb.SetComponentEnabled<MeleeContinuousAction>(index, entity, enabled);
    
    [BurstCompile]
    public static void MeleeSingleEnable(in Entity entity, int index, ref EntityCommandBuffer.ParallelWriter ecb, bool enabled)
        => ecb.SetComponentEnabled<MeleeSingleAction>(index, entity, enabled);

    [BurstCompile]
    public static void FleeEnable(in Entity entity, int index, ref EntityCommandBuffer.ParallelWriter ecb, bool enabled)
        => ecb.SetComponentEnabled<FleeAction>(index, entity, enabled);

    [BurstCompile]
    public static void SitEnable(in Entity entity, int index, ref EntityCommandBuffer.ParallelWriter ecb, bool enabled)
        => ecb.SetComponentEnabled<SitAction>(index, entity, enabled);
}