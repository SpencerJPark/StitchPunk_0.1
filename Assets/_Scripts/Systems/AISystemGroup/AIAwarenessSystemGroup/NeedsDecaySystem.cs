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
        // Hunger increases slowly (0 = full, 1 = starving)
        float hungerRate = 0.001f;
        
        // Energy decreases slowly (1 = rested, 0 = exhausted)
        float energyRate = 0.0008f;
        
        // Entertainment decreases (1 = entertained, 0 = bored)
        float entertainmentRate = 0.002f;
        
        // Social decreases slowly (1 = social, 0 = lonely)
        float socialRate = 0.001f;
        
        // Comfort decreases while standing/walking (1 = comfortable, 0 = need to sit)
        float comfortRate = 0.0015f;
        
        // Bladder increases over time (0 = empty, 1 = urgent)
        float bladderRate = 0.0012f;
        
        // Safety slowly recovers when not threatened (1 = safe, 0 = terrified)
        float safetyRecoveryRate = 0.005f;

        needs.hunger = math.saturate(needs.hunger + hungerRate * deltaTime);
        needs.energy = math.saturate(needs.energy - energyRate * deltaTime);
        needs.entertainment = math.saturate(needs.entertainment - entertainmentRate * deltaTime);
        needs.social = math.saturate(needs.social - socialRate * deltaTime);
        needs.comfort = math.saturate(needs.comfort - comfortRate * deltaTime);
        needs.bladder = math.saturate(needs.bladder + bladderRate * deltaTime);
        
        // Safety slowly recovers toward 1
        needs.safety = math.saturate(needs.safety + safetyRecoveryRate * deltaTime);
    }
}