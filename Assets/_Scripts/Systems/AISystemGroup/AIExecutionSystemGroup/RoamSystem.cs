using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
[UpdateInGroup(typeof(AIExecutionSystemGroup))]
[UpdateAfter(typeof(AIExecutionSystem))]
public partial struct RoamSystem : ISystem
{
    private ComponentLookup<LocalTransform> transformLookup;
    private ComponentLookup<UnitMover> unitMoverLookup;
    private ComponentLookup<TargetPositionPathQueued> pathQueuedLookup;
    private ComponentLookup<RoamWaypoint> waypointLookup;
    private EntityQuery waypointQuery;

    public void OnCreate(ref SystemState state)
    {
        transformLookup = state.GetComponentLookup<LocalTransform>(true);
        unitMoverLookup = state.GetComponentLookup<UnitMover>(false);
        pathQueuedLookup = state.GetComponentLookup<TargetPositionPathQueued>(false);
        waypointLookup = state.GetComponentLookup<RoamWaypoint>(true);

        EntityQueryBuilder builder = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<RoamWaypoint, LocalTransform>();
        waypointQuery = builder.Build(ref state);
        builder.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (waypointQuery.IsEmpty)
            return;

        transformLookup.Update(ref state);
        unitMoverLookup.Update(ref state);
        pathQueuedLookup.Update(ref state);
        waypointLookup.Update(ref state);

        NativeArray<Entity> waypointEntities = waypointQuery.ToEntityArray(Allocator.TempJob);
        NativeArray<LocalTransform> waypointTransforms = waypointQuery.ToComponentDataArray<LocalTransform>(Allocator.TempJob);

        uint seed = (uint)(SystemAPI.Time.ElapsedTime * 10000) + 1;

        state.Dependency = new RoamJob
        {
            seed = seed,
            waypointEntities = waypointEntities,
            waypointTransforms = waypointTransforms,
            transformLookup = transformLookup,
            unitMoverLookup = unitMoverLookup,
            pathQueuedLookup = pathQueuedLookup,
            waypointLookup = waypointLookup
        }.ScheduleParallel(state.Dependency);

        state.Dependency = waypointEntities.Dispose(state.Dependency);
        state.Dependency = waypointTransforms.Dispose(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(CanRoam))]
public partial struct RoamJob : IJobEntity
{
    public uint seed;

    [ReadOnly] public NativeArray<Entity> waypointEntities;
    [ReadOnly] public NativeArray<LocalTransform> waypointTransforms;
    [ReadOnly] public ComponentLookup<LocalTransform> transformLookup;
    [ReadOnly] public ComponentLookup<RoamWaypoint> waypointLookup;

    [NativeDisableParallelForRestriction] public ComponentLookup<UnitMover> unitMoverLookup;
    [NativeDisableParallelForRestriction] public ComponentLookup<TargetPositionPathQueued> pathQueuedLookup;

    public void Execute(
        ref RoamState roamState,
        ref ActionLock actionLock,
        in SelectedAction selectedAction,
        in BrainLink brainLink,
        [EntityIndexInQuery] int index)
    {
        if (selectedAction.current != ActionType.Roam)
            return;

        if (waypointEntities.Length == 0)
            return;

        Entity body = brainLink.body;

        if (!transformLookup.TryGetComponent(body, out LocalTransform bodyTransform))
            return;

        if (!unitMoverLookup.TryGetComponent(body, out UnitMover mover))
            return;

        float3 currentPos = bodyTransform.Position;
        Unity.Mathematics.Random random = new Unity.Mathematics.Random(seed + (uint)index + 1);

        bool needNewWaypoint = roamState.currentWaypoint == Entity.Null;

        if (!needNewWaypoint)
        {
            int waypointIndex = FindWaypointIndex(roamState.currentWaypoint);

            if (waypointIndex >= 0 && waypointLookup.TryGetComponent(roamState.currentWaypoint, out RoamWaypoint waypoint))
            {
                float3 waypointPos = waypointTransforms[waypointIndex].Position;
                float distSq = math.distancesq(currentPos, waypointPos);
                float thresholdSq = waypoint.arrivalThreshold * waypoint.arrivalThreshold;

                if (distSq < thresholdSq)
                {
                    // Arrived - current becomes previous, then pick new
                    roamState.previousWaypoint = roamState.currentWaypoint;
                    roamState.currentWaypoint = Entity.Null;
                    actionLock.isComplete = true;
                    return;
                }
            }
            else
            {
                needNewWaypoint = true;
            }
        }

        if (needNewWaypoint)
        {
            // previousWaypoint is already set from last arrival
            int newWaypointIndex = PickNextWaypoint(currentPos, roamState.previousWaypoint, ref random);

            if (newWaypointIndex >= 0)
            {
                roamState.currentWaypoint = waypointEntities[newWaypointIndex];

                float3 targetPos = waypointTransforms[newWaypointIndex].Position;
                SetMovementTarget(body, targetPos);
            }
            else
            {
                actionLock.isComplete = true;
                return;
            }
        }
    }

    private void SetMovementTarget(Entity body, float3 targetPosition)
    {
        if (pathQueuedLookup.HasComponent(body))
        {
            pathQueuedLookup[body] = new TargetPositionPathQueued { targetPosition = targetPosition };
            pathQueuedLookup.SetComponentEnabled(body, true);
        }
        else if (unitMoverLookup.TryGetComponent(body, out UnitMover mover))
        {
            mover.targetPosition = targetPosition;
            unitMoverLookup[body] = mover;
        }
    }

    private int FindWaypointIndex(Entity waypoint)
    {
        for (int i = 0; i < waypointEntities.Length; i++)
        {
            if (waypointEntities[i] == waypoint)
                return i;
        }
        return -1;
    }

    private int PickNextWaypoint(float3 currentPos, Entity excludeWaypoint, ref Unity.Mathematics.Random random)
    {
        if (waypointEntities.Length == 0)
            return -1;

        if (waypointEntities.Length == 1)
            return 0;

        // If only 2 waypoints, we have to go back
        if (waypointEntities.Length == 2)
        {
            for (int i = 0; i < waypointEntities.Length; i++)
            {
                if (waypointEntities[i] != excludeWaypoint)
                    return i;
            }
        }

        float totalWeight = 0f;

        for (int i = 0; i < waypointEntities.Length; i++)
        {
            if (waypointEntities[i] == excludeWaypoint)
                continue;

            float dist = math.distance(currentPos, waypointTransforms[i].Position);
            totalWeight += math.max(dist, 1f);
        }

        if (totalWeight <= 0f)
            return random.NextInt(0, waypointEntities.Length);

        float roll = random.NextFloat(0f, totalWeight);
        float cumulative = 0f;

        for (int i = 0; i < waypointEntities.Length; i++)
        {
            if (waypointEntities[i] == excludeWaypoint)
                continue;

            float dist = math.distance(currentPos, waypointTransforms[i].Position);
            cumulative += math.max(dist, 1f);

            if (roll <= cumulative)
                return i;
        }

        return waypointEntities.Length - 1;
    }
}