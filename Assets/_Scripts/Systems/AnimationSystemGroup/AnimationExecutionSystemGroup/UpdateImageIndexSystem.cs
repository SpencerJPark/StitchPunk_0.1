using Unity.Burst;
using Unity.Entities;

[BurstCompile]
[UpdateInGroup(typeof(AnimationExecutionSystemGroup), OrderLast = true)]
public partial struct UpdateImageIndexSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // Schedule as a parallel job
        new UpdateImageIndexJob().ScheduleParallel();
    }
}

// CameraVisible gate: skips the per-frame MaterialProperty (_ImageIndex) write for off-screen parts.
// Design changes applied off screen still land — DesignApplyUtil writes restPose.baseImageIndex, so
// sampling re-derives the slice the frame the rig becomes visible again.
[BurstCompile]
[WithAll(typeof(CameraVisible))]
public partial struct UpdateImageIndexJob : IJobEntity
{
    public void Execute(in ImageIndex imageIndex, ref ImageIndexOverride imageIndexOverride)
    {
        if (imageIndex.onUpdate)
        {
            imageIndexOverride.Value = imageIndex.index;
        }
    }
}
