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
    }
}

[BurstCompile]
public partial struct ApplyPoseJob : IJobEntity
{
    public void Execute(
        in PartAnimatedPose animatedPose,
        ref LocalTransform transform,
        ref ImageIndex imageIndex)
    {
        // Apply position and rotation
        transform.Position = animatedPose.localPosition;
        transform.Rotation = quaternion.Euler(0, 0, math.radians(animatedPose.rotation));
        transform.Scale = 1f; // We handle scale separately for 2D
        
        // For non-uniform 2D scale, you might need a separate component
        // or handle it in the shader
        
        // Apply image index
        imageIndex.index = animatedPose.imageIndex;
        imageIndex.onUpdate = true;
    }
}