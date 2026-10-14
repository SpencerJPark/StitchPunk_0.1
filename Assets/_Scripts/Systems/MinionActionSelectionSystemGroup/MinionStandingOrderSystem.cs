using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// Turns "Follow me" / "Hold" standing orders into UtilityActions options each frame: engage hostiles
// near the anchor, otherwise walk to the (formation) spot. Player orders clear the standing order in
// MinionActionSelectionSystem, so this never fights a fresh command.
[BurstCompile]
[UpdateInGroup(typeof(MinionActionSelectionSystemGroup))]
[UpdateAfter(typeof(MinionActionSelectionSystem))]
public partial struct MinionStandingOrderSystem : ISystem
{
    private ComponentLookup<Dead>           _deadLookup;
    private ComponentLookup<LocalTransform> _transformLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Player>();
        state.RequireForUpdate<BrainLibrary>();
        state.RequireForUpdate<FactionRegistry>();
        _deadLookup      = state.GetComponentLookup<Dead>(true);
        _transformLookup = state.GetComponentLookup<LocalTransform>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        _deadLookup.Update(ref state);
        _transformLookup.Update(ref state);

        Entity playerEntity = SystemAPI.GetSingletonEntity<Player>();
        if (!_transformLookup.TryGetComponent(playerEntity, out LocalTransform playerTransform))
            return;

        state.Dependency = new MinionStandingOrderJob
        {
            deadLookup      = _deadLookup,
            transformLookup = _transformLookup,
            aiConfig        = SystemAPI.GetSingleton<BrainLibrary>().blob,
            factionEntities = SystemAPI.GetSingleton<FactionRegistry>().entities,
            playerPosition  = playerTransform.Position,
            elapsedTime     = SystemAPI.Time.ElapsedTime,
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(PlayerUnitBrain))]
[WithDisabled(typeof(UtilityBrain))]
[WithDisabled(typeof(Dead))]
public partial struct MinionStandingOrderJob : IJobEntity
{
    private const float ArrivedDistance          = 1.0f;
    private const double ReissueIntervalSeconds  = 2.0;
    private const float KeepFightingRadiusFactor = 1.6f;

    [ReadOnly] public ComponentLookup<Dead>                         deadLookup;
    [ReadOnly] public ComponentLookup<LocalTransform>               transformLookup;
    [ReadOnly] public BlobAssetReference<BrainLibraryBlob>          aiConfig;
    [ReadOnly] public NativeParallelMultiHashMap<byte, Entity>      factionEntities;
    public float3 playerPosition;
    public double elapsedTime;

    public void Execute(
        ref MinionStandingOrder            standingOrder,
        in LocalTransform                  transform,
        in StateMachine                    stateMachine,
        in UnitData                        unitData,
        in DynamicBuffer<AttackFaction>    attackFactions,
        in DynamicBuffer<AvailableAttack>  availableAttacks,
        ref DynamicBuffer<UtilityActions>  actions)
    {
        if (standingOrder.type == StandingOrderType.None) return;

        bool   isFollowing = standingOrder.type == StandingOrderType.FollowCommander;
        float3 anchor      = isFollowing ? playerPosition : standingOrder.anchorPosition;

        // Keep fighting a live target that is still near the anchor; otherwise it would be re-picked every frame.
        if (stateMachine.action.IsCombatAction()
            && IsAlive(stateMachine.targetEntity)
            && transformLookup.TryGetComponent(stateMachine.targetEntity, out LocalTransform currentTargetTransform)
            && math.distance(currentTargetTransform.Position, anchor) <= standingOrder.engageRadius * KeepFightingRadiusFactor)
            return;

        Entity nearestHostile = FindNearestAliveHostile(anchor, standingOrder.engageRadius, attackFactions);
        if (nearestHostile != Entity.Null
            && AIUtils.ResolveOrderedAttack(
                ref aiConfig.Value, unitData.unitType, availableAttacks.AsNativeArray(),
                out ActionType attackActionType, out int attackDefIndex))
        {
            actions.Add(new UtilityActions
            {
                actionType      = attackActionType,
                targetEntity    = nearestHostile,
                actionDefIndex  = attackDefIndex,
                isPlayerOrdered = true,
            });
            standingOrder.lastIssuedTime = elapsedTime;
            return;
        }

        float3 desired = anchor + (isFollowing
            ? FormationSlots.RoundedSlotOffset(standingOrder.formationSlotIndex, FormationSlots.DefaultSpacing)
            : float3.zero);

        if (PlanarDistance(transform.Position, desired) <= ArrivedDistance) return;

        // Re-adding every frame would restart the behaviour, so only re-issue on a moved goal or a stale order.
        bool goalMoved  = PlanarDistance(standingOrder.lastIssuedDestination, desired) > ArrivedDistance;
        bool orderStale = elapsedTime - standingOrder.lastIssuedTime > ReissueIntervalSeconds;
        if (!goalMoved && !orderStale) return;

        int wanderDefIndex = BrainBlobUtils.GetActionDefIndex(ref aiConfig.Value, unitData.unitType, ActionType.Wander);
        if (wanderDefIndex < 0) return;

        actions.Add(new UtilityActions
        {
            actionType        = ActionType.Wander,
            targetEntity      = Entity.Null,
            targetPosition    = desired,
            hasTargetPosition = true,
            actionDefIndex    = wanderDefIndex,
            isPlayerOrdered   = true,
        });
        standingOrder.lastIssuedDestination = desired;
        standingOrder.lastIssuedTime        = elapsedTime;
    }

    private bool IsAlive(Entity entity) =>
        entity != Entity.Null && deadLookup.HasComponent(entity) && !deadLookup.IsComponentEnabled(entity);

    private static float PlanarDistance(float3 from, float3 to) =>
        math.distance(new float2(from.x, from.z), new float2(to.x, to.z));

    private Entity FindNearestAliveHostile(float3 anchor, float radius, in DynamicBuffer<AttackFaction> attackFactions)
    {
        Entity nearest         = Entity.Null;
        float  nearestDistance = radius;

        for (int factionIndex = 0; factionIndex < attackFactions.Length; factionIndex++)
        {
            byte factionKey = (byte)attackFactions[factionIndex].faction;
            if (!factionEntities.TryGetFirstValue(factionKey, out Entity candidate, out NativeParallelMultiHashMapIterator<byte> iterator))
                continue;

            do
            {
                if (!IsAlive(candidate)) continue;
                if (!transformLookup.TryGetComponent(candidate, out LocalTransform candidateTransform)) continue;

                float distance = math.distance(candidateTransform.Position, anchor);
                if (distance > nearestDistance) continue;

                nearestDistance = distance;
                nearest         = candidate;
            }
            while (factionEntities.TryGetNextValue(out candidate, ref iterator));
        }
        return nearest;
    }
}
