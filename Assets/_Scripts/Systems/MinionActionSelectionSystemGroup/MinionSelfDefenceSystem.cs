using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

public struct CounterAlert
{
    public Entity attackerEntity;
    public float3 triggerPosition;
}

[BurstCompile]
[UpdateInGroup(typeof(MinionActionSelectionSystemGroup))]
public partial struct MinionSelfDefenceSystem : ISystem
{
    private NativeList<CounterAlert> alertList;
    private ComponentLookup<PlayerOrder> playerOrderLookup;
    private ComponentLookup<Alive> aliveLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        alertList = new NativeList<CounterAlert>(16, Allocator.Persistent);
        playerOrderLookup = state.GetComponentLookup<PlayerOrder>(true);
        aliveLookup = state.GetComponentLookup<Alive>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        alertList.Clear();
        playerOrderLookup.Update(ref state);
        aliveLookup.Update(ref state);

        state.Dependency = new MinionCounterJob
        {
            alertList = alertList,
            playerOrderLookup = playerOrderLookup,
            aliveLookup = aliveLookup,
        }.Schedule(state.Dependency);

        state.Dependency = new NearbyAlertJob
        {
            alertEntries = alertList,
            playerOrderLookup = playerOrderLookup,
            aliveLookup = aliveLookup,
        }.ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        alertList.Dispose();
    }
}

[BurstCompile]
[WithAll(typeof(Minion), typeof(PlayerControlled))]
[WithDisabled(typeof(Dead))]
[WithDisabled(typeof(MeleeContinuousAction))]
public partial struct MinionCounterJob : IJobEntity
{
    public NativeList<CounterAlert> alertList;
    [ReadOnly] public ComponentLookup<PlayerOrder> playerOrderLookup;
    [ReadOnly] public ComponentLookup<Alive> aliveLookup;

    public void Execute(
        Entity entity,
        in LocalTransform transform,
        ref DynamicBuffer<ThreatEntry> threatBuffer,
        ref CombatTarget combatTarget,
        EnabledRefRW<PlayerControlled> playerControlledEnabled,
        EnabledRefRW<MeleeContinuousAction> meleeContinuousEnabled)
    {
        if (threatBuffer.Length == 0)
            return;

        if (playerOrderLookup.HasComponent(entity) &&
            playerOrderLookup[entity].commandType == CommandType.Attack)
            return;

        Entity bestAttacker = Entity.Null;
        float highestScore = -1f;
        for (int i = 0; i < threatBuffer.Length; i++)
        {
            ThreatEntry threat = threatBuffer[i];
            if (threat.attackerEntity == Entity.Null)
                continue;
            if (!aliveLookup.IsComponentEnabled(threat.attackerEntity))
                continue;
            if (threat.threatScore > highestScore)
            {
                highestScore = threat.threatScore;
                bestAttacker = threat.attackerEntity;
            }
        }

        threatBuffer.Clear();

        if (bestAttacker == Entity.Null)
            return;

        combatTarget.targetEntity = bestAttacker;
        playerControlledEnabled.ValueRW = false;
        meleeContinuousEnabled.ValueRW = true;

        alertList.Add(new CounterAlert
        {
            attackerEntity  = bestAttacker,
            triggerPosition = transform.Position,
        });
    }
}

[BurstCompile]
[WithAll(typeof(Minion), typeof(PlayerControlled))]
[WithDisabled(typeof(Dead))]
[WithDisabled(typeof(MeleeContinuousAction))]
public partial struct NearbyAlertJob : IJobEntity
{
    private const float ALERT_RADIUS_SQ = 64f;

    [ReadOnly] public NativeList<CounterAlert> alertEntries;
    [ReadOnly] public ComponentLookup<PlayerOrder> playerOrderLookup;
    [ReadOnly] public ComponentLookup<Alive> aliveLookup;

    public void Execute(
        Entity entity,
        in LocalTransform transform,
        ref CombatTarget combatTarget,
        EnabledRefRW<PlayerControlled> playerControlledEnabled,
        EnabledRefRW<MeleeContinuousAction> meleeContinuousEnabled)
    {
        if (playerOrderLookup.HasComponent(entity) &&
            playerOrderLookup[entity].commandType == CommandType.Attack)
            return;

        for (int i = 0; i < alertEntries.Length; i++)
        {
            CounterAlert alert = alertEntries[i];
            if (math.distancesq(transform.Position, alert.triggerPosition) > ALERT_RADIUS_SQ)
                continue;
            if (!aliveLookup.IsComponentEnabled(alert.attackerEntity))
                continue;

            combatTarget.targetEntity = alert.attackerEntity;
            playerControlledEnabled.ValueRW = false;
            meleeContinuousEnabled.ValueRW = true;
            return;
        }
    }
}
