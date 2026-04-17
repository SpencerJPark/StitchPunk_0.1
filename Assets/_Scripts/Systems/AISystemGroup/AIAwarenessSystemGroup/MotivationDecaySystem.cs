using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

[BurstCompile]
[UpdateInGroup(typeof(AIAwarenessSystemGroup))]
partial struct MotivationDecaySystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;

        state.Dependency = new MotivationDecayJob
        {
            deltaTime = deltaTime
        }.ScheduleParallel(state.Dependency);
    }
}

/// <summary>
/// Each frame: resets contextMultiplier to 1.0 (cleared before pre-pass systems write),
/// then applies decayRate to value. Pre-pass systems (SelfPreservationPrePassSystem,
/// SafetyPrePassSystem) run after this and override contextMultiplier where needed.
/// </summary>
[BurstCompile]
[WithAll(typeof(ActiveBrain))]
public partial struct MotivationDecayJob : IJobEntity
{
    public float deltaTime;

    public void Execute(ref DynamicBuffer<Behaviour> motivations)
    {
        for (int i = 0; i < motivations.Length; i++)
        {
            Behaviour m = motivations[i];
            m.contextMultiplier = 1f;
            m.value = math.clamp(m.value - m.decayRate * deltaTime, -100f, 100f);
            motivations[i] = m;
        }
    }
}
