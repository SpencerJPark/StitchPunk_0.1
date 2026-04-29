using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

[BurstCompile]
[UpdateInGroup(typeof(AnimationAssignmentSystemGroup), OrderFirst = true)]
public partial struct AnimationRequestSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        state.Dependency = new AnimationRequestJob().ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state) { }
}

[BurstCompile]
[WithAll(typeof(AnimationRequest))]
public partial struct AnimationRequestJob : IJobEntity
{
    public void Execute(
        ref DynamicBuffer<SetAnimation>   requests,
        ref DynamicBuffer<AnimationLayer> layers,
        EnabledRefRW<AnimationRequest>    animationRequestEnabled)
    {
        for (int i = 0; i < requests.Length; i++)
        {
            AnimationUtils.SetLayer(
                ref layers,
                requests[i].layer,
                requests[i].animation,
                requests[i].speed,
                requests[i].looping);
        }
        requests.Clear();
        animationRequestEnabled.ValueRW = false;
    }
}
