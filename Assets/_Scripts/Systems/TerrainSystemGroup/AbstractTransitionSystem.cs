using DotsMovementToolkit;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

// Units whose tile is not Loaded become abstract (a route or an anchor, no simulation); they materialise when the tile, its collider and the player are close.
[BurstCompile]
[UpdateInGroup(typeof(TerrainSystemGroup))]
public partial struct AbstractTransitionSystem : ISystem
{
    private const float TileSizeMetres = 100f;

    private uint frameCounter;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<TerrainTileEntry>();
        state.RequireForUpdate<TerrainStreamingSettings>();
        state.RequireForUpdate<WorldClock>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        frameCounter++;

        bool foundFocus = false;
        float3 focusPosition = float3.zero;
        foreach (RefRO<LocalTransform> focusTransform in SystemAPI.Query<RefRO<LocalTransform>>().WithAll<TerrainNavFocus>())
        {
            focusPosition = focusTransform.ValueRO.Position;
            foundFocus = true;
            break;
        }
        if (!foundFocus)
            return;

        Entity settingsEntity = SystemAPI.GetSingletonEntity<TerrainStreamingSettings>();
        NativeArray<TerrainTileEntry> tileEntries = SystemAPI.GetBuffer<TerrainTileEntry>(settingsEntity)
            .ToNativeArray(state.WorldUpdateAllocator);

        bool hasRoadGraph = SystemAPI.TryGetSingleton<RoadGraphSingleton>(out RoadGraphSingleton roadGraphSingleton);
        bool hasPhysics = SystemAPI.TryGetSingleton<PhysicsWorldSingleton>(out PhysicsWorldSingleton physicsWorldSingleton)
            && SystemAPI.HasSingleton<NavGridSettings>();
        CollisionFilter groundFilter = default;
        if (hasPhysics)
        {
            groundFilter = new CollisionFilter
            {
                BelongsTo = ~0u,
                CollidesWith = SystemAPI.GetSingleton<NavGridSettings>().groundLayerMask,
                GroupIndex = 0
            };
        }

        AbstractTransitionJob transitionJob = new AbstractTransitionJob
        {
            tileEntries = tileEntries,
            focusTile = TerrainNavWindowMath.TileCoordinateOf(focusPosition, TileSizeMetres),
            materializeRadiusTiles = SystemAPI.GetSingleton<TerrainStreamingSettings>().loadRadiusTiles,
            nowWorldSeconds = SystemAPI.GetSingleton<WorldClock>().totalWorldSeconds,
            frameCounter = frameCounter,
            hasRoadGraph = hasRoadGraph,
            roadGraph = hasRoadGraph ? roadGraphSingleton.graph : default,
            hasPhysics = hasPhysics,
            collisionWorld = hasPhysics ? physicsWorldSingleton.CollisionWorld : default,
            groundFilter = groundFilter,
            aStarFollowerLookup = SystemAPI.GetComponentLookup<AStarFollower>(false),
            localTransformLookup = SystemAPI.GetComponentLookup<LocalTransform>(true)
        };
        state.Dependency = transitionJob.Schedule(state.Dependency);
    }
}

[BurstCompile]
[WithPresent(typeof(AbstractSimulation))]
[WithPresent(typeof(Movement))]
[WithPresent(typeof(Gravity))]
[WithPresent(typeof(PathRequest))]
[WithPresent(typeof(DStarLiteFollower))]
[WithPresent(typeof(FlowFieldFollower))]
[WithNone(typeof(PinnedActive), typeof(TerrainNavFocus), typeof(Dead), typeof(CutsceneActor), typeof(Parent))]
public partial struct AbstractTransitionJob : IJobEntity
{
    private const float TileSizeMetres = 100f;
    private const uint FramesBetweenUnitChecks = 10;
    private const float GroundProbeHeightMetres = 50f;
    private const float GroundProbeDepthMetres = 300f;
    private const float FallbackSpeedMetresPerSecond = 1f;
    private const float ArrivalStoppingDistanceMetres = 0.5f;

    [ReadOnly] public NativeArray<TerrainTileEntry> tileEntries;
    public int2 focusTile;
    public int materializeRadiusTiles;
    public double nowWorldSeconds;
    public uint frameCounter;

    public bool hasRoadGraph;
    public BlobAssetReference<RoadGraphBlob> roadGraph;

    public bool hasPhysics;
    [ReadOnly] public CollisionWorld collisionWorld;
    public CollisionFilter groundFilter;

    public ComponentLookup<AStarFollower> aStarFollowerLookup;
    // Reads a target's position while this job writes LocalTransform; the job is single-threaded and only ever reads other entities through it.
    [ReadOnly, NativeDisableContainerSafetyRestriction] public ComponentLookup<LocalTransform> localTransformLookup;

    public void Execute(
        Entity entity,
        ref LocalTransform transform,
        in StateMachine stateMachine,
        ref Movement movement,
        ref Gravity gravity,
        ref PathRequest pathRequest,
        ref AbstractSavedState savedState,
        ref AbstractRoute route,
        ref AbstractRestAnchor restAnchor,
        DynamicBuffer<AbstractRouteWaypoint> waypoints,
        EnabledRefRW<AbstractSimulation> abstractSimulationEnabled,
        EnabledRefRW<Movement> movementEnabled,
        EnabledRefRW<Gravity> gravityEnabled,
        EnabledRefRW<PathRequest> pathRequestEnabled,
        EnabledRefRW<DStarLiteFollower> dStarLiteFollowerEnabled,
        EnabledRefRW<FlowFieldFollower> flowFieldFollowerEnabled)
    {
        if ((uint)entity.Index % FramesBetweenUnitChecks != frameCounter % FramesBetweenUnitChecks)
            return;

        if (!abstractSimulationEnabled.ValueRO)
        {
            TryGoAbstract(
                entity, ref transform, in stateMachine, ref movement, ref savedState, ref route, ref restAnchor,
                waypoints, abstractSimulationEnabled, movementEnabled, gravityEnabled, pathRequestEnabled,
                dStarLiteFollowerEnabled, flowFieldFollowerEnabled);
        }
        else
        {
            TryGoActive(
                ref transform, ref movement, ref gravity, ref pathRequest, in savedState, ref route, in restAnchor,
                waypoints, abstractSimulationEnabled, movementEnabled, gravityEnabled, pathRequestEnabled,
                dStarLiteFollowerEnabled, flowFieldFollowerEnabled);
        }
    }

    private void TryGoAbstract(
        Entity entity,
        ref LocalTransform transform,
        in StateMachine stateMachine,
        ref Movement movement,
        ref AbstractSavedState savedState,
        ref AbstractRoute route,
        ref AbstractRestAnchor restAnchor,
        DynamicBuffer<AbstractRouteWaypoint> waypoints,
        EnabledRefRW<AbstractSimulation> abstractSimulationEnabled,
        EnabledRefRW<Movement> movementEnabled,
        EnabledRefRW<Gravity> gravityEnabled,
        EnabledRefRW<PathRequest> pathRequestEnabled,
        EnabledRefRW<DStarLiteFollower> dStarLiteFollowerEnabled,
        EnabledRefRW<FlowFieldFollower> flowFieldFollowerEnabled)
    {
        int tileIndex = FindTileIndex(TerrainNavWindowMath.TileCoordinateOf(transform.Position, TileSizeMetres));
        // A unit outside every known tile (no streaming data for it) is left alone rather than frozen.
        if (tileIndex < 0 || tileEntries[tileIndex].state == TerrainTileState.Loaded)
            return;

        savedState = new AbstractSavedState
        {
            hadMovementEnabled = movementEnabled.ValueRO,
            hadGravityEnabled = gravityEnabled.ValueRO
        };

        restAnchor = new AbstractRestAnchor { position = transform.Position };
        waypoints.Clear();
        route = new AbstractRoute
        {
            departureWorldSeconds = nowWorldSeconds,
            speedMetresPerSecond = movement.moveSpeed > 0f ? movement.moveSpeed : FallbackSpeedMetresPerSecond,
            totalLengthMetres = 0f,
            endBehaviour = AbstractRouteEnd.Stop
        };

        if (TryResolveDestination(in stateMachine, out float3 destination))
            BuildRoute(transform.Position, destination, waypoints, ref route);

        movement.isMoving = false;
        movementEnabled.ValueRW = false;
        gravityEnabled.ValueRW = false;
        pathRequestEnabled.ValueRW = false;
        dStarLiteFollowerEnabled.ValueRW = false;
        flowFieldFollowerEnabled.ValueRW = false;
        if (aStarFollowerLookup.HasComponent(entity))
            aStarFollowerLookup.SetComponentEnabled(entity, false);
        abstractSimulationEnabled.ValueRW = true;
    }

    private void TryGoActive(
        ref LocalTransform transform,
        ref Movement movement,
        ref Gravity gravity,
        ref PathRequest pathRequest,
        in AbstractSavedState savedState,
        ref AbstractRoute route,
        in AbstractRestAnchor restAnchor,
        DynamicBuffer<AbstractRouteWaypoint> waypoints,
        EnabledRefRW<AbstractSimulation> abstractSimulationEnabled,
        EnabledRefRW<Movement> movementEnabled,
        EnabledRefRW<Gravity> gravityEnabled,
        EnabledRefRW<PathRequest> pathRequestEnabled,
        EnabledRefRW<DStarLiteFollower> dStarLiteFollowerEnabled,
        EnabledRefRW<FlowFieldFollower> flowFieldFollowerEnabled)
    {
        if (!hasPhysics)
            return;

        bool hasRoute = waypoints.Length >= 2 && route.totalLengthMetres > 0f;
        float3 abstractPosition = restAnchor.position;
        bool routeFinished = false;
        if (hasRoute)
            AbstractTravel.PointAlongRoute(in waypoints, in route, nowWorldSeconds, out abstractPosition, out float3 _, out routeFinished);

        int2 tileCoordinate = TerrainNavWindowMath.TileCoordinateOf(abstractPosition, TileSizeMetres);
        int tileIndex = FindTileIndex(tileCoordinate);
        if (tileIndex < 0 || tileEntries[tileIndex].state != TerrainTileState.Loaded)
            return;
        int2 tileOffset = math.abs(tileCoordinate - focusTile);
        if (math.max(tileOffset.x, tileOffset.y) > materializeRadiusTiles)
            return;

        // The tile can report Loaded a frame before its collider is in the physics world; no hit means try again next cycle.
        RaycastInput groundProbe = new RaycastInput
        {
            Start = abstractPosition + new float3(0f, GroundProbeHeightMetres, 0f),
            End = abstractPosition - new float3(0f, GroundProbeDepthMetres, 0f),
            Filter = groundFilter
        };
        if (!collisionWorld.CastRay(groundProbe, out Unity.Physics.RaycastHit groundHit))
            return;
        float3 groundPoint = math.lerp(groundProbe.Start, groundProbe.End, groundHit.Fraction);

        transform.Position = groundPoint + new float3(0f, gravity.groundOffset, 0f);
        gravity.verticalVelocity = 0f;
        gravity.isGrounded = true;
        gravity.lastGroundedPosition = transform.Position;
        gravity.hasLastGroundedPosition = true;
        movement.isMoving = false;
        movement.targetPosition = transform.Position;

        // Every bit is written explicitly: enableable bits are not preserved across structural changes or pool reclaim.
        movementEnabled.ValueRW = savedState.hadMovementEnabled;
        gravityEnabled.ValueRW = savedState.hadGravityEnabled;
        dStarLiteFollowerEnabled.ValueRW = false;
        flowFieldFollowerEnabled.ValueRW = false;

        if (hasRoute && !routeFinished)
        {
            pathRequest = new PathRequest
            {
                targetPosition = waypoints[waypoints.Length - 1].position,
                requestedMode = PathfindingMode.AStar,
                stoppingDistance = ArrivalStoppingDistanceMetres
            };
            pathRequestEnabled.ValueRW = true;
        }
        else
        {
            pathRequestEnabled.ValueRW = false;
        }

        waypoints.Clear();
        route.totalLengthMetres = 0f;
        abstractSimulationEnabled.ValueRW = false;
    }

    private int FindTileIndex(int2 tileCoordinate)
    {
        for (int index = 0; index < tileEntries.Length; index++)
        {
            if (tileEntries[index].coordinate.Equals(tileCoordinate))
                return index;
        }
        return -1;
    }

    private bool TryResolveDestination(in StateMachine stateMachine, out float3 destination)
    {
        destination = float3.zero;
        if (!ActionHasAbstractDestination(stateMachine.action))
            return false;

        if (stateMachine.hasTargetPosition)
        {
            destination = stateMachine.targetPosition;
            return true;
        }
        if (stateMachine.targetEntity != Entity.Null && localTransformLookup.HasComponent(stateMachine.targetEntity))
        {
            destination = localTransformLookup[stateMachine.targetEntity].Position;
            return true;
        }
        return false;
    }

    // Wander, Idle and every local or combat action rest in place while abstract (spike S8); only goal-driven walks keep a route.
    private static bool ActionHasAbstractDestination(ActionType action)
    {
        return action == ActionType.Patrol
            || action == ActionType.SeekEntertainment
            || action == ActionType.Repair
            || action == ActionType.Build
            || action == ActionType.EatFromSupplies;
    }

    private void BuildRoute(float3 start, float3 destination, DynamicBuffer<AbstractRouteWaypoint> waypoints, ref AbstractRoute route)
    {
        NativeList<float3> routePoints = new NativeList<float3>(8, Allocator.Temp);
        bool foundRoadRoute = hasRoadGraph
            && RoadGraphQuery.TryFindRoute(ref roadGraph.Value, start, destination, ref routePoints)
            && routePoints.Length >= 2;
        if (!foundRoadRoute)
        {
            routePoints.Clear();
            routePoints.Add(start);
            routePoints.Add(destination);
        }

        for (int index = 0; index < routePoints.Length; index++)
            waypoints.Add(new AbstractRouteWaypoint { position = routePoints[index] });
        route.totalLengthMetres = AbstractTravel.BuildCumulativeLengths(ref waypoints);
        routePoints.Dispose();
    }
}
