using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// Shrinks and fades the frozen roll afterimages left by PlayerRollMoveSystem. Ghosts have no
// LocalTransform, so the transform system never overwrites the LocalToWorld written here.
// Lifetime -> Despawn destroys them; this only animates.
[BurstCompile]
[UpdateInGroup(typeof(PresentationSystemGroup))]
public partial struct AfterimageFadeSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<AfterimageGhost>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        AfterimageShrinkJob afterimageShrinkJob = new AfterimageShrinkJob
        {
            deltaTime = SystemAPI.Time.DeltaTime,
        };
        state.Dependency = afterimageShrinkJob.ScheduleParallel(state.Dependency);
        state.Dependency = new AfterimageTintFadeJob().ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
public partial struct AfterimageShrinkJob : IJobEntity
{
    public float deltaTime;

    private void Execute(ref AfterimageGhost afterimageGhost, ref LocalToWorld ghostPose)
    {
        afterimageGhost.age += deltaTime;
        float lifeFraction = math.saturate(afterimageGhost.age / afterimageGhost.lifetime);
        ghostPose.Value = math.mul(afterimageGhost.startPose, float4x4.Scale(math.lerp(1f, END_SCALE_FRACTION, lifeFraction)));
    }

    private const float END_SCALE_FRACTION = 0.85f;
}

// Only ghosts copied from a tinted source carry BodyPartTint; its shader is the one that reads _BaseColor.
[BurstCompile]
public partial struct AfterimageTintFadeJob : IJobEntity
{
    private void Execute(in AfterimageGhost afterimageGhost, ref BodyPartTint bodyPartTint)
    {
        if (!afterimageGhost.hasTint) return;
        float lifeFraction = math.saturate(afterimageGhost.age / afterimageGhost.lifetime);
        bodyPartTint.Value = new float4(afterimageGhost.startTint.xyz, afterimageGhost.startTint.w * (1f - lifeFraction));
    }
}
