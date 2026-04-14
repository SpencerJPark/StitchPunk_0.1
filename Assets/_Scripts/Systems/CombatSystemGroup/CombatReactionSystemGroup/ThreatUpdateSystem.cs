using Unity.Burst;
using Unity.Entities;

/// <summary>
/// Reads each entity's Hurt buffer (before DamageApplicationSystem clears it)
/// and updates the ThreatEntry buffer with cumulative damage per attacker.
///
/// The ThreatEntry buffer is later read by ChaseTargetingSystem to bias target
/// selection toward whoever has been dealing the most damage to this unit —
/// implementing the "attack back" instinct in a data-oriented way.
///
/// Only processes entities that have both Hurt and ThreatEntry buffers.
/// Hurt is on all units; ThreatEntry is added by FactionAuthoring (only
/// entities that participate in faction-based combat need threat tracking).
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(CombatReactionSystemGroup))]
[UpdateBefore(typeof(DamageApplicationSystem))]
public partial struct ThreatUpdateSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        state.Dependency = new ThreatUpdateJob().ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
[WithDisabled(typeof(Dead))]
public partial struct ThreatUpdateJob : IJobEntity
{
    public void Execute(in DynamicBuffer<Hurt> hurtBuffer, ref DynamicBuffer<ThreatEntry> threatBuffer)
    {
        if (hurtBuffer.Length == 0)
            return;

        for (int hurtIndex = 0; hurtIndex < hurtBuffer.Length; hurtIndex++)
        {
            Hurt hurt = hurtBuffer[hurtIndex];

            if (hurt.attackerEntity == Entity.Null)
                continue;

            bool found = false;
            for (int threatIndex = 0; threatIndex < threatBuffer.Length; threatIndex++)
            {
                if (threatBuffer[threatIndex].attackerEntity == hurt.attackerEntity)
                {
                    ThreatEntry entry = threatBuffer[threatIndex];
                    entry.threatScore += hurt.damageAmount;
                    threatBuffer[threatIndex] = entry;
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                threatBuffer.Add(new ThreatEntry
                {
                    attackerEntity = hurt.attackerEntity,
                    threatScore = hurt.damageAmount
                });
            }
        }
    }
}
