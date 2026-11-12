using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Scenes;
using Unity.Transforms;

[BurstCompile]
[UpdateInGroup(typeof(TerrainStreamingSystemGroup))]
public partial struct TerrainStreamingSystem : ISystem
{
    private const float TileSizeMetres = 100f;

    // The first pass loads the whole ring synchronously: units simulate from frame one and would fall through ground that is still streaming in.
    private bool initialLoadDone;

    // A caravan at 4x zoomed-out time scale outruns one async load per frame around its own tile, so tiles ahead of the focus load too.
    private const float LOAD_AHEAD_SECONDS = 4f;
    private const float MAX_LOAD_AHEAD_METRES = 150f;
    private const float TELEPORT_METRES = 50f;

    private float3 previousFocusPosition;
    private bool hasPreviousFocusPosition;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<TerrainStreamingSettings>();
        state.RequireForUpdate<TerrainTileEntry>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
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

        float3 focusVelocity = float3.zero;
        float deltaTime = SystemAPI.Time.DeltaTime;
        if (hasPreviousFocusPosition && deltaTime > 0f)
        {
            float3 stepDelta = focusPosition - previousFocusPosition;
            stepDelta.y = 0f;
            if (math.length(stepDelta) <= TELEPORT_METRES)
                focusVelocity = stepDelta / deltaTime;
        }
        previousFocusPosition = focusPosition;
        hasPreviousFocusPosition = true;

        float3 leadOffset = focusVelocity * LOAD_AHEAD_SECONDS;
        float leadDistance = math.length(leadOffset);
        if (leadDistance > MAX_LOAD_AHEAD_METRES)
            leadOffset *= MAX_LOAD_AHEAD_METRES / leadDistance;
        float3 aheadPosition = focusPosition + leadOffset;

        int2 focusTile = TerrainNavWindowMath.TileCoordinateOf(focusPosition, TileSizeMetres);
        int2 aheadTile = TerrainNavWindowMath.TileCoordinateOf(aheadPosition, TileSizeMetres);
        TerrainStreamingSettings settings = SystemAPI.GetSingleton<TerrainStreamingSettings>();
        Entity settingsEntity = SystemAPI.GetSingletonEntity<TerrainStreamingSettings>();
        DynamicBuffer<TerrainTileEntry> entries = SystemAPI.GetBuffer<TerrainTileEntry>(settingsEntity);
        WorldUnmanaged world = state.WorldUnmanaged;

        for (int index = 0; index < entries.Length; index++)
        {
            TerrainTileEntry entry = entries[index];
            int distance = DistanceToFocusOrAhead(entry.coordinate, focusTile, aheadTile);

            if (entry.state == TerrainTileState.Loading)
            {
                if (SceneSystem.IsSceneLoaded(world, entry.sceneEntity))
                    entry.state = TerrainTileState.Loaded;
                else if (distance > settings.unloadRadiusTiles)
                {
                    SceneSystem.UnloadScene(world, entry.sceneEntity);
                    entry.state = TerrainTileState.Unloaded;
                }
            }
            else if (entry.state == TerrainTileState.Loaded && distance > settings.unloadRadiusTiles)
            {
                SceneSystem.UnloadScene(world, entry.sceneEntity);
                entry.state = TerrainTileState.Unloaded;
            }

            // Scene load/unload make structural changes, which invalidate the buffer handle taken before them.
            entries = SystemAPI.GetBuffer<TerrainTileEntry>(settingsEntity);
            entries[index] = entry;
        }

        bool blockingInitialLoad = !initialLoadDone;
        initialLoadDone = true;
        int loadBudget = blockingInitialLoad ? int.MaxValue : settings.maxLoadsPerFrame;
        SceneSystem.LoadParameters loadParameters = default;
        if (blockingInitialLoad)
            loadParameters.Flags = SceneLoadFlags.BlockOnImport | SceneLoadFlags.BlockOnStreamIn;

        // Nearest-first by repeated minimum scan; the entry list is small.
        for (int loadsStarted = 0; loadsStarted < loadBudget; loadsStarted++)
        {
            int nearestIndex = -1;
            int nearestDistance = int.MaxValue;
            for (int index = 0; index < entries.Length; index++)
            {
                if (entries[index].state != TerrainTileState.Unloaded)
                    continue;
                int distance = DistanceToFocusOrAhead(entries[index].coordinate, focusTile, aheadTile);
                if (distance <= settings.loadRadiusTiles && distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearestIndex = index;
                }
            }
            if (nearestIndex < 0)
                break;

            TerrainTileEntry entry = entries[nearestIndex];
            entry.sceneEntity = SceneSystem.LoadSceneAsync(world, entry.sceneGuid, loadParameters);
            entry.state = TerrainTileState.Loading;
            entries = SystemAPI.GetBuffer<TerrainTileEntry>(settingsEntity);
            entries[nearestIndex] = entry;
        }
    }

    private static int DistanceToFocusOrAhead(int2 tile, int2 focusTile, int2 aheadTile)
    {
        return math.min(ChebyshevDistance(tile, focusTile), ChebyshevDistance(tile, aheadTile));
    }

    private static int ChebyshevDistance(int2 tileA, int2 tileB)
    {
        int2 delta = math.abs(tileA - tileB);
        return math.max(delta.x, delta.y);
    }
}
