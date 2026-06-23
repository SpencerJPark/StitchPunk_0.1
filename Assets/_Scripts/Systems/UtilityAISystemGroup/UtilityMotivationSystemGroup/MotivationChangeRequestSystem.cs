using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

// Processes all queued MotivationChangeRequest entries each frame and clears the buffer.
// Any system — action execution, world events, damage — queues requests here instead of
// writing to the Motivation buffer directly. The buffer accumulates across the frame and
// is flushed once per selection cycle before awareness systems run.
[BurstCompile]
[UpdateInGroup(typeof(AIMotivationSystemGroup), OrderFirst = true)]
public partial struct MotivationChangeRequestSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        state.Dependency = new MotivationChangeJob().ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(UtilityBrain))]
[WithDisabled(typeof(Dead))]
public partial struct MotivationChangeJob : IJobEntity
{
    public void Execute(
        ref DynamicBuffer<Motivation>             motivations,
        ref DynamicBuffer<MotivationChangeRequest> requests)
    {
        for (int r = 0; r < requests.Length; r++)
        {
            MotivationChangeRequest req = requests[r];
            for (int m = 0; m < motivations.Length; m++)
            {
                Motivation motivation = motivations[m];
                if (motivation.needType != req.needType)
                    continue;

                motivation.value = req.changeType == MotivationChangeType.Set
                    ? math.clamp(req.value, 0f, 100f)
                    : math.clamp(motivation.value + req.value, 0f, 100f);

                motivations[m] = motivation;
                break;
            }
        }
        requests.Clear();
    }
}
