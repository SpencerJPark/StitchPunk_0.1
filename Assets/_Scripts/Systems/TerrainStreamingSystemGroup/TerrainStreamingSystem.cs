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

        int2 focusTile = TerrainNavWindowMath.TileCoordinateOf(focusPosition, TileSizeMetres);
        TerrainStreamingSettings settings = SystemAPI.GetSingleton<TerrainStreamingSettings>();
        Entity settingsEntity = SystemAPI.GetSingletonEntity<TerrainStreamingSettings>();
        DynamicBuffer<TerrainTileEntry> entries = SystemAPI.GetBuffer<TerrainTileEntry>(settingsEntity);
        WorldUnmanaged world = state.WorldUnmanaged;

        for (int index = 0; index < entries.Length; index++)
        {
            TerrainTileEntry entry = entries[index];
            int distance = ChebyshevDistance(entry.coordinate, focusTile);

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
                int distance = ChebyshevDistance(entries[index].coordinate, focusTile);
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

    private static int ChebyshevDistance(int2 tileA, int2 tileB)
    {
        int2 delta = math.abs(tileA - tileB);
        return math.max(delta.x, delta.y);
    }
}
