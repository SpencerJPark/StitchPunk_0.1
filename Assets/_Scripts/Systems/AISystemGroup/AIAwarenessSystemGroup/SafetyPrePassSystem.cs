using Unity.Burst;
using Unity.Entities;

/// <summary>
/// Pre-pass: when a unit has active threats in its ThreatEntry buffer, set
/// contextMultiplier = 2.0 on their Safety motivation. Runs after MotivationDecaySystem
/// (which resets all multipliers to 1.0) and before AIScoringSystemGroup reads them.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(AIAwarenessSystemGroup))]
[UpdateAfter(typeof(MotivationDecaySystem))]
public partial struct SafetyPrePassSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        state.Dependency = new SafetyPrePassJob().ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(ActiveBrain))]
public partial struct SafetyPrePassJob : IJobEntity
{
    private const float THREAT_MULTIPLIER = 2.0f;

    public void Execute(ref DynamicBuffer<Motivation> motivations, in DynamicBuffer<ThreatEntry> threats)
    {
        if (threats.Length == 0)
            return;

        // Update ALL Safety entries — both Interaction and any Action modes sharing this type
        // are boosted simultaneously when threats are present.
        for (int i = 0; i < motivations.Length; i++)
        {
            Motivation m = motivations[i];
            if (m.motivationType != MotivationType.Safety)
                continue;

            m.contextMultiplier = THREAT_MULTIPLIER;
            motivations[i] = m;
        }
    }
}
