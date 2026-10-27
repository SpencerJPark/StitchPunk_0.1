using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

// Drives the harvest -> carry one load -> deposit loop for player minions by emitting Wander options,
// exactly like MinionStandingOrderSystem. Single-threaded on purpose: nodes, corpses and the player
// ledger are shared across minions, so parallel writes would race.
[BurstCompile]
[UpdateInGroup(typeof(MinionActionSelectionSystemGroup))]
[UpdateAfter(typeof(MinionStandingOrderSystem))]
public partial struct MinionGatherSystem : ISystem
{
    private ComponentLookup<CampLootLocked>          _campLootLockedLookup;
    private ComponentLookup<Dead>                    _deadLookup;
    private ComponentLookup<Despawn>                 _despawnLookup;
    private ComponentLookup<Faction>                 _factionLookup;
    private ComponentLookup<LocalTransform>          _transformLookup;
    private ComponentLookup<ResourceNode>            _resourceNodeLookup;
    private ComponentLookup<Workstation>             _workstationLookup;
    private BufferLookup<ResourceChangeRequest>      _requestLookup;
    private ComponentLookup<ResourceLedgerLink>      _resourceLedgerLinkLookup;
    private EntityQuery                              _resourceNodeQuery;
    private EntityQuery                              _corpseQuery;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<BrainLibrary>();
        _campLootLockedLookup = state.GetComponentLookup<CampLootLocked>(true);
        _deadLookup         = state.GetComponentLookup<Dead>(true);
        _despawnLookup      = state.GetComponentLookup<Despawn>(false);
        _factionLookup      = state.GetComponentLookup<Faction>(true);
        _transformLookup    = state.GetComponentLookup<LocalTransform>(true);
        _resourceNodeLookup = state.GetComponentLookup<ResourceNode>(false);
        _workstationLookup  = state.GetComponentLookup<Workstation>(true);
        _requestLookup      = state.GetBufferLookup<ResourceChangeRequest>(false);
        _resourceLedgerLinkLookup = state.GetComponentLookup<ResourceLedgerLink>(true);
        _resourceNodeQuery  = SystemAPI.QueryBuilder().WithAll<ResourceNode, LocalTransform>().Build();
        _corpseQuery        = SystemAPI.QueryBuilder().WithAll<Dead, UnitData, Faction, LocalTransform>().Build();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        _campLootLockedLookup.Update(ref state);
        _deadLookup.Update(ref state);
        _despawnLookup.Update(ref state);
        _factionLookup.Update(ref state);
        _transformLookup.Update(ref state);
        _resourceNodeLookup.Update(ref state);
        _workstationLookup.Update(ref state);
        _requestLookup.Update(ref state);
        _resourceLedgerLinkLookup.Update(ref state);

        NativeArray<Entity>         nodeEntities  = _resourceNodeQuery.ToEntityArray(Allocator.TempJob);
        NativeArray<LocalTransform> nodeTransforms = _resourceNodeQuery.ToComponentDataArray<LocalTransform>(Allocator.TempJob);
        NativeArray<Entity>         corpseEntities  = _corpseQuery.ToEntityArray(Allocator.TempJob);
        NativeArray<LocalTransform> corpseTransforms = _corpseQuery.ToComponentDataArray<LocalTransform>(Allocator.TempJob);

        EntityCommandBuffer commandBuffer = SystemAPI
            .GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
            .CreateCommandBuffer(state.WorldUnmanaged);

        JobHandle gatherHandle = new MinionGatherJob
        {
            campLootLockedLookup = _campLootLockedLookup,
            deadLookup          = _deadLookup,
            despawnLookup       = _despawnLookup,
            factionLookup       = _factionLookup,
            transformLookup     = _transformLookup,
            resourceNodeLookup  = _resourceNodeLookup,
            workstationLookup   = _workstationLookup,
            requestLookup       = _requestLookup,
            resourceLedgerLinkLookup = _resourceLedgerLinkLookup,
            nodeEntities        = nodeEntities,
            nodeTransforms      = nodeTransforms,
            corpseEntities      = corpseEntities,
            corpseTransforms    = corpseTransforms,
            commandBuffer       = commandBuffer,
            aiConfig            = SystemAPI.GetSingleton<BrainLibrary>().blob,
            elapsedTime         = SystemAPI.Time.ElapsedTime,
            deltaTime           = SystemAPI.Time.DeltaTime,
        }.Schedule(state.Dependency);

        gatherHandle = nodeEntities.Dispose(gatherHandle);
        gatherHandle = nodeTransforms.Dispose(gatherHandle);
        gatherHandle = corpseEntities.Dispose(gatherHandle);
        gatherHandle = corpseTransforms.Dispose(gatherHandle);
        state.Dependency = gatherHandle;
    }
}

[BurstCompile]
[WithAll(typeof(PlayerUnitBrain))]
[WithDisabled(typeof(UtilityBrain))]
[WithPresent(typeof(Dead))]
public partial struct MinionGatherJob : IJobEntity
{
    private const float NodeReachDistance        = 1.6f;
    private const float StationReachDistance     = 1.8f;
    private const float NextNodeSearchRadius     = 15f;
    private const float CorpseHarvestSeconds     = 1.5f;
    private const double ReissueIntervalSeconds  = 2.0;
    private const float GoalMovedDistance        = 1.0f;

    public ComponentLookup<Despawn>                         despawnLookup;
    public ComponentLookup<ResourceNode>                    resourceNodeLookup;
    public BufferLookup<ResourceChangeRequest>              requestLookup;
    [ReadOnly] public ComponentLookup<ResourceLedgerLink>   resourceLedgerLinkLookup;
    [ReadOnly] public ComponentLookup<CampLootLocked>       campLootLockedLookup;
    [ReadOnly] public ComponentLookup<Dead>                 deadLookup;
    [ReadOnly] public ComponentLookup<Faction>              factionLookup;
    [ReadOnly] public ComponentLookup<LocalTransform>       transformLookup;
    [ReadOnly] public ComponentLookup<Workstation>          workstationLookup;
    [ReadOnly] public NativeArray<Entity>                   nodeEntities;
    [ReadOnly] public NativeArray<LocalTransform>           nodeTransforms;
    [ReadOnly] public NativeArray<Entity>                   corpseEntities;
    [ReadOnly] public NativeArray<LocalTransform>           corpseTransforms;
    [ReadOnly] public BlobAssetReference<BrainLibraryBlob>  aiConfig;
    public EntityCommandBuffer commandBuffer;
    public double elapsedTime;
    public float  deltaTime;

    public void Execute(
        ref MinionGatherOrder             gatherOrder,
        EnabledRefRO<Dead>                deadEnabled,
        in LocalTransform                 transform,
        in UnitData                       unitData,
        ref DynamicBuffer<UtilityActions> actions)
    {
        if (gatherOrder.phase == GatherPhase.None) return;

        // Dying ends the job, so a revived minion doesn't resume an old trip with a stale load.
        if (deadEnabled.ValueRO)
        {
            gatherOrder.phase = GatherPhase.None;
            gatherOrder.carriedAmount = 0;
            return;
        }

        if (!workstationLookup.TryGetComponent(gatherOrder.station, out Workstation workstation)
            || !transformLookup.TryGetComponent(gatherOrder.station, out LocalTransform stationTransform))
        {
            gatherOrder.phase = GatherPhase.None;
            return;
        }
        float3 dropPoint = stationTransform.Position + math.rotate(stationTransform.Rotation, workstation.summonSpawnLocalOffset);
        bool   isCorpseJob = gatherOrder.resourceType == ResourceType.Corpse;

        if (gatherOrder.phase == GatherPhase.ToStation)
        {
            if (PlanarDistance(transform.Position, dropPoint) <= StationReachDistance)
            {
                if (gatherOrder.carriedAmount > 0)
                {
                    Entity ledgerHolder = ResourceLedger.HolderOf(workstation.ownerPlayer, in resourceLedgerLinkLookup);
                    if (!requestLookup.TryGetBuffer(ledgerHolder, out DynamicBuffer<ResourceChangeRequest> playerRequests))
                        return; // keep carrying until the ledger exists
                    ResourceLedger.QueueAdd(ref playerRequests, gatherOrder.resourceType, gatherOrder.carriedAmount);
                    gatherOrder.carriedAmount = 0;
                }
                // A null node here means the last one emptied with nothing nearby: the work is finished.
                gatherOrder.phase = gatherOrder.node == Entity.Null ? GatherPhase.None : GatherPhase.ToNode;
                return;
            }
            IssueMove(ref gatherOrder, ref actions, unitData, dropPoint);
            return;
        }

        float3 nodePosition;
        bool   nodeIsValid = TryGetNodePosition(gatherOrder.node, isCorpseJob, out nodePosition);

        if (gatherOrder.phase == GatherPhase.Harvesting && !nodeIsValid)
        {
            gatherOrder.phase = GatherPhase.ToNode;
            return;
        }

        if (gatherOrder.phase == GatherPhase.ToNode && !nodeIsValid)
        {
            float3 searchCenter = transformLookup.TryGetComponent(gatherOrder.node, out LocalTransform lostNodeTransform)
                ? lostNodeTransform.Position
                : gatherOrder.lastIssuedDestination;
            Entity replacement = FindReplacementNode(gatherOrder.resourceType, searchCenter, gatherOrder.node);
            if (replacement != Entity.Null)
            {
                gatherOrder.node = replacement;
                TryGetNodePosition(replacement, isCorpseJob, out nodePosition);
            }
            else if (gatherOrder.carriedAmount > 0)
            {
                gatherOrder.phase = GatherPhase.ToStation;
                return;
            }
            else
            {
                gatherOrder.phase = GatherPhase.None;
                if (PlanarDistance(transform.Position, dropPoint) > StationReachDistance)
                    IssueMove(ref gatherOrder, ref actions, unitData, dropPoint);
                return;
            }
        }

        if (gatherOrder.phase == GatherPhase.ToNode)
        {
            if (PlanarDistance(transform.Position, nodePosition) <= NodeReachDistance)
            {
                gatherOrder.phase        = GatherPhase.Harvesting;
                gatherOrder.harvestTimer = 0f;
                return;
            }
            IssueMove(ref gatherOrder, ref actions, unitData, nodePosition);
            return;
        }

        // Harvesting
        gatherOrder.harvestTimer += deltaTime;
        float harvestSeconds = CorpseHarvestSeconds;
        ResourceNode resourceNode = default;
        if (!isCorpseJob)
        {
            resourceNodeLookup.TryGetComponent(gatherOrder.node, out resourceNode);
            harvestSeconds = resourceNode.harvestSeconds;
        }
        if (gatherOrder.harvestTimer < harvestSeconds) return;

        Entity harvestedNode = gatherOrder.node;
        bool   nodeEmptied   = true;
        int    taken;
        if (isCorpseJob)
        {
            taken = 1;
            if (despawnLookup.HasComponent(harvestedNode))
                despawnLookup.SetComponentEnabled(harvestedNode, true);
            else
                commandBuffer.DestroyEntity(harvestedNode);
        }
        else
        {
            taken = math.min(math.max(resourceNode.loadPerTrip, 1), resourceNode.remaining);
            resourceNode.remaining -= taken;
            resourceNodeLookup[harvestedNode] = resourceNode;
            nodeEmptied = resourceNode.remaining <= 0;
            if (nodeEmptied) commandBuffer.DestroyEntity(harvestedNode);
        }

        // Pick the follow-up now: after the node is gone its position is no longer known.
        if (nodeEmptied)
            gatherOrder.node = FindReplacementNode(gatherOrder.resourceType, nodePosition, harvestedNode);

        gatherOrder.carriedAmount = taken;
        gatherOrder.harvestTimer  = 0f;
        gatherOrder.phase         = GatherPhase.ToStation;
    }

    private bool TryGetNodePosition(Entity node, bool isCorpseJob, out float3 position)
    {
        position = default;
        if (node == Entity.Null || !transformLookup.TryGetComponent(node, out LocalTransform nodeTransform)) return false;

        if (isCorpseJob)
        {
            if (!IsHaulableCorpse(node)) return false;
        }
        else if (!resourceNodeLookup.TryGetComponent(node, out ResourceNode resourceNode) || resourceNode.remaining <= 0
                 || campLootLockedLookup.HasComponent(node))
        {
            return false;
        }

        position = nodeTransform.Position;
        return true;
    }

    private bool IsHaulableCorpse(Entity corpse)
    {
        if (!deadLookup.HasComponent(corpse) || !deadLookup.IsComponentEnabled(corpse)) return false;
        if (despawnLookup.HasComponent(corpse) && despawnLookup.IsComponentEnabled(corpse)) return false;
        if (factionLookup.TryGetComponent(corpse, out Faction faction) && faction.factionType == FactionType.Player) return false;
        return true;
    }

    private Entity FindReplacementNode(ResourceType resourceType, float3 center, Entity excluded)
    {
        Entity nearest         = Entity.Null;
        float  nearestDistance = NextNodeSearchRadius;

        if (resourceType == ResourceType.Corpse)
        {
            for (int corpseIndex = 0; corpseIndex < corpseEntities.Length; corpseIndex++)
            {
                Entity candidate = corpseEntities[corpseIndex];
                if (candidate == excluded || !IsHaulableCorpse(candidate)) continue;
                float distance = PlanarDistance(corpseTransforms[corpseIndex].Position, center);
                if (distance > nearestDistance) continue;
                nearestDistance = distance;
                nearest         = candidate;
            }
            return nearest;
        }

        for (int nodeIndex = 0; nodeIndex < nodeEntities.Length; nodeIndex++)
        {
            Entity candidate = nodeEntities[nodeIndex];
            if (candidate == excluded || campLootLockedLookup.HasComponent(candidate)) continue;
            if (!resourceNodeLookup.TryGetComponent(candidate, out ResourceNode candidateNode)) continue;
            if (candidateNode.resourceType != resourceType || candidateNode.remaining <= 0) continue;
            float distance = PlanarDistance(nodeTransforms[nodeIndex].Position, center);
            if (distance > nearestDistance) continue;
            nearestDistance = distance;
            nearest         = candidate;
        }
        return nearest;
    }

    // Re-adding every frame would restart the behaviour, so only re-issue on a moved goal or a stale order.
    private void IssueMove(
        ref MinionGatherOrder gatherOrder, ref DynamicBuffer<UtilityActions> actions, in UnitData unitData, float3 destination)
    {
        bool goalMoved  = PlanarDistance(gatherOrder.lastIssuedDestination, destination) > GoalMovedDistance;
        bool orderStale = elapsedTime - gatherOrder.lastIssuedTime > ReissueIntervalSeconds;
        if (!goalMoved && !orderStale) return;

        int wanderDefIndex = BrainBlobUtils.GetActionDefIndex(ref aiConfig.Value, unitData.unitType, ActionType.Wander);
        if (wanderDefIndex < 0) return;

        actions.Add(new UtilityActions
        {
            actionType        = ActionType.Wander,
            targetEntity      = Entity.Null,
            targetPosition    = destination,
            hasTargetPosition = true,
            actionDefIndex    = wanderDefIndex,
            isPlayerOrdered   = true,
        });
        gatherOrder.lastIssuedDestination = destination;
        gatherOrder.lastIssuedTime        = elapsedTime;
    }

    private static float PlanarDistance(float3 from, float3 to) =>
        math.distance(new float2(from.x, from.z), new float2(to.x, to.z));
}
