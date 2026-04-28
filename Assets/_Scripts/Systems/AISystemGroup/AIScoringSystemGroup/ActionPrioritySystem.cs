using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Applies flat tier bonuses to ActionOption scores after MotivationScoringSystem.
///
/// Tiers:
///   Combat (+1.0)      — BloodLust or SelfDefence options. Ensures attack options (scored 0-1
///                        by motivation curves) reliably beat civilian interactions (also 0-1).
///   Survival (+2.0)    — SelfPreservation or Safety options when health < 60%.
///                        Overrides combat at +1 so the unit flees or grabs survival items
///                        rather than continuing to fight when badly hurt.
///
/// Weapon-grab interactions baked with MotivationType.BloodLust naturally receive the combat
/// tier bonus, letting them compete with bare-hand attack options in the top-3 selection.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(AIScoringSystemGroup))]
[UpdateAfter(typeof(MotivationScoringSystem))]
public partial struct ActionPrioritySystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        state.Dependency = new ActionPriorityJob().ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(ActiveBrain), typeof(ActionRequest))]
public partial struct ActionPriorityJob : IJobEntity
{
    private const float COMBAT_BONUS   = 1.0f;
    private const float SURVIVAL_BONUS = 2.0f;
    private const float FLEE_HEALTH_THRESHOLD = 0.6f;

    public void Execute(in Health health, ref DynamicBuffer<ActionOption> options)
    {
        float healthRatio = health.healthAmountMax > 0
            ? (float)health.healthAmount / health.healthAmountMax
            : 1f;

        bool isHurt = healthRatio < FLEE_HEALTH_THRESHOLD;

        for (int i = 0; i < options.Length; i++)
        {
            ActionOption option = options[i];

            if (option.motivationType == MotivationType.BloodLust ||
                option.motivationType == MotivationType.SelfDefence)
            {
                option.utilityScore += COMBAT_BONUS;
            }

            if (isHurt &&
                (option.motivationType == MotivationType.SelfPreservation ||
                 option.motivationType == MotivationType.Safety))
            {
                option.utilityScore += SURVIVAL_BONUS;
            }

            options[i] = option;
        }

        // Prune after all bonuses are applied — an option at score 0 after bonuses
        // has nothing to offer selection and would pollute the random top-3 pick.
        for (int i = options.Length - 1; i >= 0; i--)
        {
            if (options[i].utilityScore <= 0f)
                options.RemoveAt(i);
        }
    }
}
