using Unity.Burst;
using Unity.Entities;

[BurstCompile]
[UpdateInGroup(typeof(AIAwarenessSystemGroup))]
[UpdateAfter(typeof(SelfDefenceAwarenessSystem))]
public partial struct FleeAwarenessSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<Health>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        state.Dependency = new FleeAwarenessJob().ScheduleParallel(state.Dependency);
    }
}

// Offers Flee as a normal decision-point option: only runs when the unit is asking for a
// new action (ActionRequest) and is in active combat (has at least one ThreatEntry).
// No interrupt, no bracket tracking — for melee-single units each punch re-enables
// ActionRequest, so flee competes against fighting on every swing.
[BurstCompile]
[WithAll(typeof(AIBrain), typeof(ActionRequest))]
[WithDisabled(typeof(Dead))]
[WithPresent(typeof(FleeAction))]
public partial struct FleeAwarenessJob : IJobEntity
{
    public void Execute(
        in  Health                      health,
        in  Personality                 personality,
        ref DynamicBuffer<Motivation>   motivations,
        ref DynamicBuffer<ActionOption> options,
        in  DynamicBuffer<ThreatEntry>  threats)
    {
        if (threats.Length == 0)
            return;

        float healthRatio = (float)health.healthAmount / health.healthAmountMax;
        
        if (healthRatio >= .3f)
            return;

        // ~0 at full health, rising as the unit is wounded and damped by bravery.
        float fleeUtility = (1f - healthRatio) * (1f - personality.bravery);

        // SelfPreservation drives the scoring curve; mirrors EnemyAwareness setting BloodLust.
        AIUtils.SetMotivationValue(ref motivations, MotivationType.SelfPreservation, 100f);

        options.Add(new ActionOption
        {
            actionType     = ActionType.Flee,
            motivationType = MotivationType.SelfPreservation,
            priority       = 2,
            utilityScore   = fleeUtility,
        });
    }
}
