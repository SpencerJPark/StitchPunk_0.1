// =====================================
// APPLY ANIMATED POSE SYSTEM
// =====================================

using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(AnimationSamplingSystem))]
public partial struct ApplyAnimatedPoseSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        new ApplyPoseJob().ScheduleParallel();
        new ApplyAnimatedImageIndexJob().ScheduleParallel();
    }
}

[BurstCompile]
public partial struct ApplyPoseJob : IJobEntity
{
    public void Execute(
        in AnimationTargetPose animatedPose,
        ref LocalTransform transform)
    {
        transform.Position = animatedPose.localPosition;
        transform.Rotation = quaternion.Euler(0, 0, math.radians(animatedPose.rotation));
        transform.Scale = 1f;
    }
}

[BurstCompile]
public partial struct ApplyAnimatedImageIndexJob : IJobEntity
{
    public void Execute(
        in AnimationTargetPose animatedPose,
        ref ImageIndex imageIndex)
    {
        imageIndex.index = animatedPose.imageIndex;
        imageIndex.onUpdate = true;
    }
}