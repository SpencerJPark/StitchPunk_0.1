using System.ComponentModel;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

[BurstCompile]
[UpdateInGroup(typeof(AIScoringSystemGroup))]
public partial struct HealthScoringSystem : ISystem
{
    private ComponentLookup<Health> healthLookup;

    public void OnCreate(ref SystemState state)
    {
        healthLookup = state.GetComponentLookup<Health>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        healthLookup.Update(ref state);

        new NeedsHealthJob
        {
            healthLookup = healthLookup
        }.ScheduleParallel();
    }
}

[BurstCompile]
public partial struct NeedsHealthJob : IJobEntity
{
    [Unity.Collections.ReadOnly] public ComponentLookup<Health> healthLookup;

    public void Execute(ref Needs needs, in BrainLink brainLink)
    {
        Entity body = brainLink.body;

        // Low health reduces safety
        if (healthLookup.TryGetComponent(body, out Health health))
        {
            float healthPercent = health.healthAmount / health.healthAmountMax;
            
            if (healthPercent < 0.5f)
            {
                float healthPenalty = (0.5f - healthPercent) * 0.5f;
                needs.safety = math.saturate(needs.safety - healthPenalty);
            }
        }

        // Bladder accident when safety is low and bladder is critical
        if (needs.safety < 0.3f && needs.bladder > 0.9f)
        {
            needs.bladder = 0.1f; // Accident happened
            needs.comfort = math.saturate(needs.comfort - 0.3f); // Uncomfortable now
        }
    }
}