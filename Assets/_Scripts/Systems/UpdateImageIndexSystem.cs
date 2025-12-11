using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;

[BurstCompile]
public partial struct UpdateImageIndexSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // Schedule as a parallel job
        new UpdateImageIndexJob().ScheduleParallel();
    }
}

[BurstCompile]
public partial struct UpdateImageIndexJob : IJobEntity
{
    public void Execute(in ImageIndex imageIndex, ref ImageIndexOverride imageIndexOverride)
    {
        if (imageIndex.onUpdate)
        {
            imageIndexOverride.value = imageIndex.index;
        }
    }
}
