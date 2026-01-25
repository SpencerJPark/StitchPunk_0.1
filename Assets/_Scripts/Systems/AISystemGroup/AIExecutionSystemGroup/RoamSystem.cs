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
    private ComponentLookup<UnitMover> unitMoverLookup;
    private ComponentLookup<LocalTransform> transformLookup;
    private ComponentLookup<TargetPositionPathQueued> pathQueuedLookup;
    private EntityQuery waypointQuery;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        unitMoverLookup = SystemAPI.GetComponentLookup<UnitMover>(false);
        transformLookup = SystemAPI.GetComponentLookup<LocalTransform>(true);
        pathQueuedLookup = SystemAPI.GetComponentLookup<TargetPositionPathQueued>(false);

        waypointQuery = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<RoamWaypoint, LocalTransform>()
            .Build(ref state);

        state.RequireForUpdate(waypointQuery);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        unitMoverLookup.Update(ref state);
        transformLookup.Update(ref state);
        pathQueuedLookup.Update(ref state);

        NativeArray<Entity> waypointEntities = waypointQuery.ToEntityArray(Allocator.TempJob);
        NativeArray<LocalTransform> waypointTransforms = waypointQuery.ToComponentDataArray<LocalTransform>(Allocator.TempJob);

        uint seed = (uint)(SystemAPI.Time.ElapsedTime * 1000) + 1;
        float deltaTime = SystemAPI.Time.DeltaTime;

        state.Dependency = new RoamJob
        {
            baseSeed = seed,
            deltaTime = deltaTime,
            waypointEntities = waypointEntities,
            waypointTransforms = waypointTransforms,
            unitMoverLookup = unitMoverLookup,
            transformLookup = transformLookup,
            pathQueuedLookup = pathQueuedLookup
        }.ScheduleParallel(state.Dependency);

        waypointEntities.Dispose(state.Dependency);
        waypointTransforms.Dispose(state.Dependency);
    }
}

[BurstCompile]
public partial struct RoamJob : IJobEntity
{
    public uint baseSeed;
    public float deltaTime;

    [ReadOnly] public NativeArray<Entity> waypointEntities;
    [ReadOnly] public NativeArray<LocalTransform> waypointTransforms;
    [ReadOnly] public ComponentLookup<LocalTransform> transformLookup;

    [NativeDisableParallelForRestriction] public ComponentLookup<UnitMover> unitMoverLookup;
    [NativeDisableParallelForRestriction] public ComponentLookup<TargetPositionPathQueued> pathQueuedLookup;

    public void Execute(
        ref RoamState roamState,
        in SelectedAction selectedAction,
        in BrainLink brainLink,
        in CanRoam canRoam,
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
        Unity.Mathematics.Random random = new Unity.Mathematics.Random(baseSeed + (uint)index + 1);

        // Waiting at waypoint
        if (roamState.waitTimer > 0f)
        {
            roamState.waitTimer -= deltaTime;
            return;
        }

        // Check if we need a new waypoint
        bool needNewWaypoint = roamState.currentWaypoint == Entity.Null;

        if (!needNewWaypoint)
        {
            // Check distance to current waypoint
            int waypointIndex = FindWaypointIndex(roamState.currentWaypoint);
            if (waypointIndex >= 0)
            {
                float3 waypointPos = waypointTransforms[waypointIndex].Position;
                float distSq = math.distancesq(currentPos, waypointPos);

                if (distSq < roamState.arrivalThreshold * roamState.arrivalThreshold)
                {
                    // Arrived - wait then pick new waypoint
                    roamState.waitTimer = random.NextFloat(roamState.minWaitTime, roamState.maxWaitTime);
                    needNewWaypoint = true;
                }
            }
            else
            {
                needNewWaypoint = true;
            }
        }

        if (needNewWaypoint)
        {
            // Pick a new waypoint (weighted by distance - prefer farther ones for more purposeful movement)
            int newWaypointIndex = PickNextWaypoint(currentPos, roamState.currentWaypoint, ref random);

            if (newWaypointIndex >= 0)
            {
                roamState.currentWaypoint = waypointEntities[newWaypointIndex];
                float3 targetPos = waypointTransforms[newWaypointIndex].Position;

                SetMovementTarget(body, targetPos);
            }
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

        // Weight by distance - farther waypoints get higher weight for more purposeful travel
        float totalWeight = 0f;
        NativeArray<float> weights = new NativeArray<float>(waypointEntities.Length, Allocator.Temp);

        for (int i = 0; i < waypointEntities.Length; i++)
        {
            if (waypointEntities[i] == excludeWaypoint)
            {
                weights[i] = 0f;
                continue;
            }

            float dist = math.distance(currentPos, waypointTransforms[i].Position);
            float weight = math.max(dist, 1f); // Favor farther waypoints
            weights[i] = weight;
            totalWeight += weight;
        }

        if (totalWeight <= 0f)
        {
            weights.Dispose();
            return random.NextInt(0, waypointEntities.Length);
        }

        float roll = random.NextFloat(0f, totalWeight);
        float cumulative = 0f;

        for (int i = 0; i < weights.Length; i++)
        {
            cumulative += weights[i];
            if (roll <= cumulative)
            {
                weights.Dispose();
                return i;
            }
        }

        weights.Dispose();
        return waypointEntities.Length - 1;
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
}