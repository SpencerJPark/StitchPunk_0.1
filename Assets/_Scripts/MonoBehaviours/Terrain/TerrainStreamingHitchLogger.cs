#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class TerrainStreamingHitchLogger : MonoBehaviour
{
    private const string LogPrefix = "[TerrainHitch]";
    private const float OverSixtyFpsMs = 16.7f;
    private const float OverThirtyFpsMs = 33f;

    private class ActiveLoad
    {
        public int2 coordinate;
        public int startFrame;
        public float startRealtime;
        public int frames;
        public float worstFrameMs;
        public int framesOverSixtyFps;
        public int framesOverThirtyFps;
    }

    private struct PendingUnload
    {
        public int2 coordinate;
        public float firstFrameMs;
    }

    private static TerrainStreamingHitchLogger installedInstance;
    private static int totalLoads;
    private static int totalUnloads;
    private static float worstLoadFrameMs;
    private static float worstUnloadFrameMs;
    private static readonly List<float> worstFramePerLoad = new List<float>();

    private readonly Dictionary<int2, TerrainTileState> lastStateByTile = new Dictionary<int2, TerrainTileState>();
    private readonly List<ActiveLoad> activeLoads = new List<ActiveLoad>();
    private readonly List<PendingUnload> pendingUnloads = new List<PendingUnload>();
    private readonly List<int2> observedCoordinates = new List<int2>();
    private readonly List<TerrainTileState> observedStates = new List<TerrainTileState>();

    private EntityQuery tileQuery;
    private World queryWorld;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        if (installedInstance != null)
            return;

        GameObject host = new GameObject("TerrainStreamingHitchLogger");
        DontDestroyOnLoad(host);
        installedInstance = host.AddComponent<TerrainStreamingHitchLogger>();
    }

    private const string ToggleName = "Terrain hitch log";

    private static void LogLine(string message)
    {
        if (DebugMenuVisibility.IsToggleOn(ToggleName))
            Debug.Log(message);
    }

    private void Awake()
    {
        DebugMenuVisibility.RegisterToggle(ToggleName);
        totalLoads = 0;
        totalUnloads = 0;
        worstLoadFrameMs = 0f;
        worstUnloadFrameMs = 0f;
        worstFramePerLoad.Clear();
    }

    private void OnDestroy()
    {
        if (installedInstance == this)
            installedInstance = null;
        LogSummary();
    }

    [ContextMenu("Log Hitch Summary")]
    private void LogSummaryFromContextMenu()
    {
        LogSummary();
    }

    public static void LogSummary()
    {
        float percentile95 = 0f;
        if (worstFramePerLoad.Count > 0)
        {
            float[] sorted = worstFramePerLoad.ToArray();
            Array.Sort(sorted);
            int index = Mathf.Clamp(Mathf.CeilToInt(sorted.Length * 0.95f) - 1, 0, sorted.Length - 1);
            percentile95 = sorted[index];
        }

        LogLine($"{LogPrefix} summary: {totalLoads} loads, worst load frame {worstLoadFrameMs:F1} ms, "
            + $"{totalUnloads} unloads, worst unload frame {worstUnloadFrameMs:F1} ms, P95 load frame {percentile95:F1} ms");
    }

    internal static void AccumulateFrame(float frameMs, ref float worstFrameMs, ref int framesOverSixtyFps, ref int framesOverThirtyFps)
    {
        if (frameMs > worstFrameMs)
            worstFrameMs = frameMs;
        if (frameMs > OverSixtyFpsMs)
            framesOverSixtyFps++;
        if (frameMs > OverThirtyFpsMs)
            framesOverThirtyFps++;
    }

    private void Update()
    {
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
            return;

        if (queryWorld != world)
        {
            queryWorld = world;
            tileQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<TerrainTileEntry>());
            lastStateByTile.Clear();
            activeLoads.Clear();
            pendingUnloads.Clear();
        }

        if (tileQuery.IsEmptyIgnoreFilter)
            return;

        observedCoordinates.Clear();
        observedStates.Clear();
        DynamicBuffer<TerrainTileEntry> tiles = world.EntityManager.GetBuffer<TerrainTileEntry>(tileQuery.GetSingletonEntity(), true);
        for (int index = 0; index < tiles.Length; index++)
        {
            observedCoordinates.Add(tiles[index].coordinate);
            observedStates.Add(tiles[index].state);
        }

        float frameMs = Time.unscaledDeltaTime * 1000f;

        ResolvePendingUnloads(frameMs);

        List<ActiveLoad> finishedLoads = null;
        for (int index = 0; index < observedCoordinates.Count; index++)
        {
            int2 coordinate = observedCoordinates[index];
            TerrainTileState state = observedStates[index];

            if (!lastStateByTile.TryGetValue(coordinate, out TerrainTileState previousState))
            {
                lastStateByTile[coordinate] = state;
                if (state == TerrainTileState.Loading)
                    activeLoads.Add(CreateActiveLoad(coordinate));
                continue;
            }

            if (previousState == state)
                continue;

            lastStateByTile[coordinate] = state;

            if (state == TerrainTileState.Unloaded)
            {
                activeLoads.RemoveAll(load => load.coordinate.Equals(coordinate));
                pendingUnloads.Add(new PendingUnload { coordinate = coordinate, firstFrameMs = frameMs });
                continue;
            }

            if (previousState == TerrainTileState.Unloaded)
                activeLoads.Add(CreateActiveLoad(coordinate));

            if (state == TerrainTileState.Loaded)
            {
                ActiveLoad finished = activeLoads.Find(load => load.coordinate.Equals(coordinate));
                if (finished != null)
                {
                    if (finishedLoads == null)
                        finishedLoads = new List<ActiveLoad>();
                    finishedLoads.Add(finished);
                }
            }
        }

        // Every in-flight load, including ones finishing this frame, pays for this frame.
        for (int index = 0; index < activeLoads.Count; index++)
        {
            ActiveLoad load = activeLoads[index];
            load.frames++;
            AccumulateFrame(frameMs, ref load.worstFrameMs, ref load.framesOverSixtyFps, ref load.framesOverThirtyFps);
        }

        if (finishedLoads != null)
        {
            for (int index = 0; index < finishedLoads.Count; index++)
                FinishLoad(finishedLoads[index]);
        }
    }

    private ActiveLoad CreateActiveLoad(int2 coordinate)
    {
        return new ActiveLoad
        {
            coordinate = coordinate,
            startFrame = Time.frameCount,
            startRealtime = Time.realtimeSinceStartup,
        };
    }

    private void FinishLoad(ActiveLoad load)
    {
        activeLoads.Remove(load);
        float wallMs = (Time.realtimeSinceStartup - load.startRealtime) * 1000f;
        totalLoads++;
        worstLoadFrameMs = Mathf.Max(worstLoadFrameMs, load.worstFrameMs);
        worstFramePerLoad.Add(load.worstFrameMs);
        LogLine($"{LogPrefix} tile ({load.coordinate.x}, {load.coordinate.y}) loaded: {load.frames} frames, "
            + $"{wallMs:F0} ms wall (unscaled realtime), worst frame {load.worstFrameMs:F1} ms, "
            + $"over 16.7 ms: {load.framesOverSixtyFps}, over 33 ms: {load.framesOverThirtyFps}");
    }

    private void ResolvePendingUnloads(float frameMs)
    {
        for (int index = 0; index < pendingUnloads.Count; index++)
        {
            PendingUnload pending = pendingUnloads[index];
            float worstOfTwo = Mathf.Max(pending.firstFrameMs, frameMs);
            totalUnloads++;
            worstUnloadFrameMs = Mathf.Max(worstUnloadFrameMs, worstOfTwo);
            LogLine($"{LogPrefix} tile ({pending.coordinate.x}, {pending.coordinate.y}) unloaded: frame {worstOfTwo:F1} ms");
        }

        pendingUnloads.Clear();
    }
}
#endif
