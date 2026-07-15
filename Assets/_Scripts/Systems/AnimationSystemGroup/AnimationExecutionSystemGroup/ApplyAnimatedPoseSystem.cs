// =====================================
// APPLY ANIMATED POSE SYSTEM
// =====================================

using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
[UpdateInGroup(typeof(AnimationExecutionSystemGroup))]
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

// CameraVisible gate (part-level): off-screen parts keep their last pose — no transform writes, no
// chunk dirtying. Sampling refreshes the pose the frame the rig re-enters view.
[BurstCompile]
[WithAll(typeof(CameraVisible))]
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
[WithAll(typeof(CameraVisible))]
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