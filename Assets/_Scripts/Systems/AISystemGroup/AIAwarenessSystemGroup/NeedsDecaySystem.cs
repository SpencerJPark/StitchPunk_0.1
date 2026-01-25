using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

[BurstCompile]
[UpdateInGroup(typeof(AISystemGroup))]
[UpdateBefore(typeof(AIAwarenessSystemGroup))]
public partial struct NeedsDecaySystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;

        new NeedsDecayJob
        {
            deltaTime = deltaTime
        }.ScheduleParallel();
    }
}

[BurstCompile]
public partial struct NeedsDecayJob : IJobEntity
{
    public float deltaTime;

    public void Execute(ref Needs needs)
    {
        // Needs increase over time (0 = satisfied, 1 = desperate)
        float hungerRate = 0.01f;
        float energyRate = 0.008f;
        float entertainmentRate = 0.015f;
        float socialRate = 0.005f;

        needs.hunger = math.saturate(needs.hunger + hungerRate * deltaTime);
        needs.energy = math.saturate(needs.energy - energyRate * deltaTime);
        needs.entertainment = math.saturate(needs.entertainment - entertainmentRate * deltaTime);
        needs.social = math.saturate(needs.social - socialRate * deltaTime);
    }
}