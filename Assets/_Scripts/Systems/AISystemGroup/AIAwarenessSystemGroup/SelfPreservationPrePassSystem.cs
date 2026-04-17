using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Pre-pass: when a unit's health drops below 30%, set contextMultiplier = 2.5
/// on their SelfPreservation motivation. Runs after MotivationDecaySystem
/// (which resets all multipliers to 1.0) and before AIScoringSystemGroup reads them.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(AIAwarenessSystemGroup))]
[UpdateAfter(typeof(MotivationDecaySystem))]
public partial struct SelfPreservationPrePassSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        state.Dependency = new SelfPreservationPrePassJob().ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(ActiveBrain))]
public partial struct SelfPreservationPrePassJob : IJobEntity
{
    private const float HEALTH_THRESHOLD = 0.3f;
    private const float LOW_HEALTH_MULTIPLIER = 2.5f;

    public void Execute(ref DynamicBuffer<Behaviour> motivations, in Health health)
    {
        if (health.healthAmountMax <= 0)
            return;

        float healthRatio = (float)health.healthAmount / health.healthAmountMax;
        if (healthRatio >= HEALTH_THRESHOLD)
            return;

        // Update ALL SelfPreservation entries — both Interaction and Action (Flee) modes
        // are boosted simultaneously when health is critical.
        for (int i = 0; i < motivations.Length; i++)
        {
            Behaviour m = motivations[i];
            if (m.behaviourType != BehaviourType.SelfPreservation)
                continue;

            m.contextMultiplier = LOW_HEALTH_MULTIPLIER;
            motivations[i] = m;
        }
    }
}
