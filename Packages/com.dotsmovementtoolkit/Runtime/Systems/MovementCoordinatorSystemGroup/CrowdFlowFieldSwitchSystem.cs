using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace DotsMovementToolkit
{
// When NavGridSettings.crowdFlowFieldThreshold or more agents head for the same spot, their A*
// requests become one shared flow field; once the group drops below the threshold they go back to A*.
// Horde members are left alone: HordeSystem already routes them through flow fields.
[BurstCompile]
[UpdateInGroup(typeof(MovementCoordinatorSystemGroup))]
[UpdateBefore(typeof(PathRequestSystem))]
public partial struct CrowdFlowFieldSwitchSystem : ISystem
{
    private EntityQuery pendingRequestQuery;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<NavGridSettings>();
        pendingRequestQuery = SystemAPI.QueryBuilder()
            .WithAll<PathRequest, PathfindingAgent>()
            .WithPresent<FlowFieldFollower>()
            .Build();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        NavGridSettings gridSettings = SystemAPI.GetSingleton<NavGridSettings>();
        if (gridSettings.crowdFlowFieldThreshold <= 0 || pendingRequestQuery.IsEmpty)
            return;

        // A lookup rather than [WithNone]: agents without HordeMembership at all must still count.
        ComponentLookup<HordeMembership> hordeMembershipLookup = SystemAPI.GetComponentLookup<HordeMembership>(true);
        NativeList<float3> crowdTargets = new NativeList<float3>(64, state.WorldUpdateAllocator);
        state.Dependency = new GatherCrowdTargetsJob
        {
            crowdTargets = crowdTargets,
            hordeMembershipLookup = hordeMembershipLookup,
        }.Schedule(state.Dependency);
        state.Dependency = new CrowdFlowFieldSwitchJob
        {
            crowdTargets = crowdTargets.AsDeferredJobArray(),
            hordeMembershipLookup = hordeMembershipLookup,
            threshold = gridSettings.crowdFlowFieldThreshold,
            targetRadiusSquared = gridSettings.crowdTargetRadius * gridSettings.crowdTargetRadius,
        }.ScheduleParallel(state.Dependency);
    }

    public static bool IsHordeMember(in ComponentLookup<HordeMembership> hordeMembershipLookup, Entity entity)
        => hordeMembershipLookup.HasComponent(entity) && hordeMembershipLookup.IsComponentEnabled(entity);
}

// Every individual agent that is moving, or about to: a pending request counts at its new target.
[BurstCompile]
[WithPresent(typeof(PathRequest))]
public partial struct GatherCrowdTargetsJob : IJobEntity
{
    public NativeList<float3> crowdTargets;
    [ReadOnly] public ComponentLookup<HordeMembership> hordeMembershipLookup;

    public void Execute(Entity entity, in PathfindingAgent agent, in PathRequest pathRequest, EnabledRefRO<PathRequest> pathRequestEnabled)
    {
        if (CrowdFlowFieldSwitchSystem.IsHordeMember(hordeMembershipLookup, entity))
            return;
        bool hasPendingRoute = pathRequestEnabled.ValueRO
            && (pathRequest.requestedMode == PathfindingMode.AStar || pathRequest.requestedMode == PathfindingMode.FlowField);
        if (hasPendingRoute)
            crowdTargets.Add(pathRequest.targetPosition);
        else if (agent.isActive && (agent.currentMode == PathfindingMode.AStar || agent.currentMode == PathfindingMode.FlowField))
            crowdTargets.Add(agent.targetPosition);
    }
}

// `ref PathRequest` alone already limits this to enabled (pending) requests.
[BurstCompile]
[WithPresent(typeof(FlowFieldFollower))]
public partial struct CrowdFlowFieldSwitchJob : IJobEntity
{
    [ReadOnly] public NativeArray<float3> crowdTargets;
    [ReadOnly] public ComponentLookup<HordeMembership> hordeMembershipLookup;
    public int threshold;
    public float targetRadiusSquared;

    public void Execute(Entity entity, ref PathRequest pathRequest, ref PathfindingAgent agent)
    {
        if (CrowdFlowFieldSwitchSystem.IsHordeMember(hordeMembershipLookup, entity))
            return;
        bool isSwitchCandidate = pathRequest.requestedMode == PathfindingMode.AStar
            || (pathRequest.requestedMode == PathfindingMode.FlowField && agent.crowdFlowFieldActive);
        if (!isSwitchCandidate)
            return;

        int agentsSharingTarget = 0;
        for (int targetIndex = 0; targetIndex < crowdTargets.Length; targetIndex++)
        {
            if (math.distancesq(crowdTargets[targetIndex].xz, pathRequest.targetPosition.xz) <= targetRadiusSquared)
                agentsSharingTarget++;
        }

        agent.crowdFlowFieldActive = agentsSharingTarget >= threshold;
        pathRequest.requestedMode = agent.crowdFlowFieldActive ? PathfindingMode.FlowField : PathfindingMode.AStar;
    }
}
}
