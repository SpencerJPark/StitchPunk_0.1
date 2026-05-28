using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

// Prunes stale ThreatEntry records before the awareness consumers (SelfDefence, Flee) read them.
// Dead/despawned attackers are removed immediately so units never fight or flee a corpse; everything
// else decays via staleTimer, which ThreatUpdateSystem refreshes on each hit — so an actively
// attacking enemy never expires, while a disengaged one is forgotten once its timer runs out.
[BurstCompile]
[UpdateInGroup(typeof(AIAwarenessSystemGroup), OrderFirst = true)]
public partial struct ThreatDecaySystem : ISystem
{
    private ComponentLookup<Dead> deadLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        deadLookup = state.GetComponentLookup<Dead>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        deadLookup.Update(ref state);
        state.Dependency = new ThreatDecayJob
        {
            deadLookup = deadLookup,
            deltaTime  = SystemAPI.Time.DeltaTime,
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
[WithDisabled(typeof(Dead))]
public partial struct ThreatDecayJob : IJobEntity
{
    [ReadOnly]
    public ComponentLookup<Dead> deadLookup;
    public float                 deltaTime;

    public void Execute(ref DynamicBuffer<ThreatEntry> threats)
    {
        for (int i = threats.Length - 1; i >= 0; i--)
        {
            ThreatEntry threat = threats[i];

            // Immediate removal: null ref, despawned attacker (no Dead component), or dead attacker.
            if (threat.attackerEntity == Entity.Null
                || !deadLookup.HasComponent(threat.attackerEntity)
                || deadLookup.IsComponentEnabled(threat.attackerEntity))
            {
                threats.RemoveAt(i);
                continue;
            }

            // Timed decay: un-refreshed entries (attacker disengaged / far away) expire.
            threat.staleTimer -= deltaTime;
            if (threat.staleTimer <= 0f)
            {
                threats.RemoveAt(i);
                continue;
            }

            threats[i] = threat;
        }
    }
}
