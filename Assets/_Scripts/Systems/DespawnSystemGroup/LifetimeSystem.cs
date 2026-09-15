using Unity.Burst;
using Unity.Entities;

[BurstCompile]
[UpdateInGroup(typeof(DespawnSystemGroup), OrderFirst = true)]
public partial struct LifetimeSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        LifetimeCountdownJob lifetimeCountdownJob = new LifetimeCountdownJob
        {
            deltaTime = SystemAPI.Time.DeltaTime,
        };
        state.Dependency = lifetimeCountdownJob.ScheduleParallel(state.Dependency);
    }

    // WithPresent is required: this job turns Despawn ON, and an EnabledRefRW<Despawn> alone filters to enabled-only.
    [BurstCompile]
    [WithPresent(typeof(Despawn))]
    public partial struct LifetimeCountdownJob : IJobEntity
    {
        public float deltaTime;

        private void Execute(ref Lifetime lifetime, EnabledRefRW<Lifetime> lifetimeEnabled, EnabledRefRW<Despawn> despawnEnabled)
        {
            lifetime.secondsRemaining -= deltaTime;
            if (lifetime.secondsRemaining > 0f)
            {
                return;
            }

            despawnEnabled.ValueRW = true;
            lifetimeEnabled.ValueRW = false;
        }
    }
}
