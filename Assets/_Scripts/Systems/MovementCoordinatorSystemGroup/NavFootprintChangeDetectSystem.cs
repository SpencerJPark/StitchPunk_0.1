using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;
using DotsMovementToolkit;

// Asks NavWindowSystem for a rebuild whenever a nav footprint is added, removed or moved.
[BurstCompile]
[UpdateInGroup(typeof(MovementCoordinatorSystemGroup))]
[UpdateAfter(typeof(NavGridSystem))]
[UpdateBefore(typeof(NavWindowSystem))]
public partial struct NavFootprintChangeDetectSystem : ISystem
{
    private EntityQuery movedFootprintQuery;
    private int lastFootprintOrderVersion;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<TerrainNavWindowSettings>();
        movedFootprintQuery = SystemAPI.QueryBuilder().WithAll<NavFootprint, LocalTransform>().Build();
        movedFootprintQuery.SetChangedVersionFilter(ComponentType.ReadOnly<LocalTransform>());
        lastFootprintOrderVersion = state.EntityManager.GetComponentOrderVersion<NavFootprint>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        int footprintOrderVersion = state.EntityManager.GetComponentOrderVersion<NavFootprint>();
        bool footprintSetChanged = footprintOrderVersion != lastFootprintOrderVersion;
        lastFootprintOrderVersion = footprintOrderVersion;

        // Evaluated unconditionally so the change filter's last-seen version advances every frame.
        bool footprintMoved = !movedFootprintQuery.IsEmpty;

        if (!footprintSetChanged && !footprintMoved)
            return;

        TerrainNavWindowSettings windowSettings = SystemAPI.GetSingleton<TerrainNavWindowSettings>();
        windowSettings.rebuildRequested = true;
        SystemAPI.SetSingleton(windowSettings);
    }
}
