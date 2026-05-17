using Unity.Burst;
using Unity.Entities;

/// <summary>
/// Prunes the ActionOption buffer so only the highest-priority tier survives.
///
/// Priority is an int set by each awareness system when it creates options:
///   InteractionAwarenessSystem  → blob.priority  (1 by default, configurable per SO)
///   EnemyAwarenessSystem        → 2
///   HealthAwarenessSystem       → 2 or 3
///   SelfDefenceAwarenessSystem  → 3
///
/// Two-pass algorithm:
///   1. Find the maximum priority value present in the buffer.
///   2. Remove every option whose priority is below that maximum.
///
/// If all options share the same tier (e.g. all priority 1), none are removed.
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
[WithAll(typeof(AIBrain), typeof(ActionRequest), typeof(Alive))]
public partial struct ActionPriorityJob : IJobEntity
{
    public void Execute(ref DynamicBuffer<ActionOption> options)
    {
        int maxPriority = 0;
        for (int i = 0; i < options.Length; i++)
        {
            if (options[i].priority > maxPriority)
                maxPriority = options[i].priority;
        }

        for (int i = options.Length - 1; i >= 0; i--)
        {
            if (options[i].priority < maxPriority)
                options.RemoveAt(i);
        }
    }
}
