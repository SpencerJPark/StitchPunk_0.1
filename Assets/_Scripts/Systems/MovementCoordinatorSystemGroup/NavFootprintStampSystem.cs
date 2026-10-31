using DotsMovementToolkit;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

// NavWindowSystem rewrites the whole cost map from tile blobs, so footprints are re-stamped after every window rebuild.
[BurstCompile]
[UpdateInGroup(typeof(MovementCoordinatorSystemGroup))]
[UpdateAfter(typeof(NavWindowSystem))]
public partial struct NavFootprintStampSystem : ISystem
{
    private EntityQuery footprintQuery;
    private uint lastStampedWindowVersion;
    private bool hasStamped;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<NavGridSettings>();
        footprintQuery = SystemAPI.QueryBuilder().WithAll<NavFootprint, LocalTransform>().Build();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        NavGridSettings gridSettings = SystemAPI.GetSingleton<NavGridSettings>();
        if (!gridSettings.externalCostRegions)
            return;
        if (!SystemAPI.HasSingleton<NavGridConfig>() || !SystemAPI.HasSingleton<NavGridCostMap>())
            return;

        Entity gridEntity = SystemAPI.GetSingletonEntity<NavGridConfig>();
        NavGridConfig gridConfig = SystemAPI.GetComponent<NavGridConfig>(gridEntity);
        NavGridCostMap gridCostMap = SystemAPI.GetComponent<NavGridCostMap>(gridEntity);
        if (!gridCostMap.costs.IsCreated)
            return;
        if (hasStamped && gridCostMap.windowVersion == lastStampedWindowVersion)
            return;

        lastStampedWindowVersion = gridCostMap.windowVersion;
        hasStamped = true;
        if (footprintQuery.IsEmpty)
            return;

        NativeArray<NavFootprint> footprints = footprintQuery.ToComponentDataArray<NavFootprint>(state.WorldUpdateAllocator);
        NativeArray<LocalTransform> transforms = footprintQuery.ToComponentDataArray<LocalTransform>(state.WorldUpdateAllocator);

        state.Dependency = new StampNavFootprintsJob
        {
            costs = gridCostMap.costs,
            footprints = footprints,
            transforms = transforms,
            gridWidth = gridConfig.width,
            gridHeight = gridConfig.height,
            cellSize = gridConfig.cellSize,
            gridOriginXZ = gridConfig.gridOrigin.xz
        }.Schedule(state.Dependency);
        // Downstream path systems read the costs this frame.
        state.CompleteDependency();

        gridCostMap.costMapVersion += 1;
        SystemAPI.SetComponent(gridEntity, gridCostMap);
    }
}

[BurstCompile]
public struct StampNavFootprintsJob : IJob
{
    public NativeArray<byte> costs;
    [ReadOnly] public NativeArray<NavFootprint> footprints;
    [ReadOnly] public NativeArray<LocalTransform> transforms;
    public int gridWidth;
    public int gridHeight;
    public float cellSize;
    public float2 gridOriginXZ;

    public void Execute()
    {
        for (int footprintIndex = 0; footprintIndex < footprints.Length; footprintIndex++)
        {
            NavFootprint footprint = footprints[footprintIndex];
            LocalTransform footprintTransform = transforms[footprintIndex];
            float2 footprintCentre = footprintTransform.Position.xz;
            float3 forward = math.mul(footprintTransform.Rotation, new float3(0f, 0f, 1f));
            float yawRadians = math.atan2(forward.x, forward.z) + footprint.rotationRadians;

            float2 axisAlignedHalfExtents = NavFootprintMath.AxisAlignedHalfExtents(footprint.halfExtents, yawRadians);
            int2 minCell = (int2)math.floor((footprintCentre - axisAlignedHalfExtents - gridOriginXZ) / cellSize);
            int2 maxCell = (int2)math.floor((footprintCentre + axisAlignedHalfExtents - gridOriginXZ) / cellSize);
            minCell = math.max(minCell, new int2(0, 0));
            maxCell = math.min(maxCell, new int2(gridWidth - 1, gridHeight - 1));

            for (int cellZ = minCell.y; cellZ <= maxCell.y; cellZ++)
            {
                for (int cellX = minCell.x; cellX <= maxCell.x; cellX++)
                {
                    float2 cellCentre = gridOriginXZ + (new float2(cellX, cellZ) + 0.5f) * cellSize;
                    if (NavFootprintMath.IsCellCentreInsideFootprint(cellCentre, footprintCentre, footprint.halfExtents, yawRadians))
                        costs[cellZ * gridWidth + cellX] = footprint.cost;
                }
            }
        }
    }
}

public static class NavFootprintMath
{
    // Yaw is rotation about +Y measured from +Z toward +X, matching atan2(forward.x, forward.z).
    public static bool IsCellCentreInsideFootprint(float2 cellCentre, float2 footprintCentre, float2 halfExtents, float yawRadians)
    {
        float2 offset = cellCentre - footprintCentre;
        float sine = math.sin(yawRadians);
        float cosine = math.cos(yawRadians);
        // Footprint local X axis in world XZ is (cos, -sin); local Z axis is (sin, cos).
        float localX = offset.x * cosine - offset.y * sine;
        float localZ = offset.x * sine + offset.y * cosine;
        return math.abs(localX) <= halfExtents.x && math.abs(localZ) <= halfExtents.y;
    }

    public static float2 AxisAlignedHalfExtents(float2 halfExtents, float yawRadians)
    {
        float absSine = math.abs(math.sin(yawRadians));
        float absCosine = math.abs(math.cos(yawRadians));
        return new float2(
            halfExtents.x * absCosine + halfExtents.y * absSine,
            halfExtents.x * absSine + halfExtents.y * absCosine);
    }
}
