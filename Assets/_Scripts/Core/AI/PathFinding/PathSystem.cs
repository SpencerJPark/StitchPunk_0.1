// // Assets/_Scripts/ScriptableSystems/PathSystem.cs
// using System;
// using System.Collections.Generic;
// using UnityEngine;
// using UnityEngine.AI;
// using UnityEngine.SceneManagement;
//
//
// namespace PathFinding
// {
//     [CreateAssetMenu(fileName = "Path System", menuName = "Scriptable Systems/Path System")]
//     public sealed class PathSystem
//     {
//         [Header("Perf")]
//         [SerializeField] private int maxRequestsPerFrame = 16;
//         [SerializeField] private float cacheCellSize = 1.0f;
//         [SerializeField] private float cacheTTL = 1.0f;
//
//         [Header("Graph (auto-rebuilt per scene)")]
//         [SerializeField] private NavMeshGraph navGraph = new();
//
//         public NavMeshGraph Graph => navGraph;
//
//         struct Request
//         {
//             public Vector3 start, end;
//             public int areaMask;
//             public Action<PathResult> onComplete;
//         }
//
//         struct CacheKey : IEquatable<CacheKey>
//         {
//             public Vector3 endQ; public int areaMask;
//             public bool Equals(CacheKey other) => endQ == other.endQ && areaMask == other.areaMask;
//             public override int GetHashCode() => endQ.GetHashCode() ^ areaMask.GetHashCode();
//         }
//
//         class CacheVal { public float time; public Vector3[] corners; public NavMeshPathStatus status; }
//
//         private readonly Queue<Request> queue = new();
//         private readonly Dictionary<CacheKey, CacheVal> cache = new();
//
//         // ---- ScriptableSystem lifecycle ----
//
//         public void Initialize()
//         {
//             // subscribe to scene load and build immediately for the active scene
//             SceneManager.sceneLoaded += OnSceneLoaded;
//             RebuildGraphForActiveScene();
//             LogGraph("Initialize");
//         }
//
//         public void Shutdown()
//         {
//             SceneManager.sceneLoaded -= OnSceneLoaded;
//         }
//
//         // Tick runs every scheduler tick (configure in your GameInitializer)
//         public void Tick()
//         {
//             PruneCache();
//
//             int budget = maxRequestsPerFrame;
//             while (budget-- > 0 && queue.Count > 0)
//             {
//                 var req = queue.Dequeue();
//
//                 var key = new CacheKey
//                 {
//                     endQ = Quantize(req.end, cacheCellSize),
//                     areaMask = req.areaMask
//                 };
//
//                 if (cache.TryGetValue(key, out var cached))
//                 {
//                     req.onComplete?.Invoke(new PathResult(cached.status, cached.corners));
//                     continue;
//                 }
//
//                 var path = new NavMeshPath();
//                 NavMesh.CalculatePath(req.start, req.end, req.areaMask, path);
//                 var result = new PathResult(path.status, path.corners);
//
//                 cache[key] = new CacheVal
//                 {
//                     time = Time.unscaledTime,
//                     status = path.status,
//                     corners = result.Corners
//                 };
//
//                 req.onComplete?.Invoke(result);
//             }
//         }
//
//         // ---- Public API ----
//         public void RequestPath(Vector3 start, Vector3 end, int areaMask, Action<PathResult> onComplete)
//             => queue.Enqueue(new Request { start = start, end = end, areaMask = areaMask, onComplete = onComplete });
//
//         public void ForceRebuildGraph()
//         {
//             RebuildGraphForActiveScene();
//             LogGraph("ForceRebuildGraph");
//         }
//
//
//         public bool TryGetFirstWaypoint(Vector3 start, Vector3 end, int areaMask, out Vector3 firstWaypoint)
//         {
//             firstWaypoint = default;
//
//             var path = new NavMeshPath();
//             if (!NavMesh.CalculatePath(start, end, areaMask, path)) return false;
//             if (path.status != NavMeshPathStatus.PathComplete || path.corners == null || path.corners.Length == 0)
//                 return false;
//
//             // Usually corner[0] ~ start. If there’s a second corner, head there.
//             if (path.corners.Length >= 2)
//                 firstWaypoint = path.corners[1];
//             else
//                 firstWaypoint = path.corners[0];
//
//             return true;
//         }
//
//
//         // ---- Internals ----
//         private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
//         {
//             RebuildGraphForActiveScene();
//             LogGraph($"OnSceneLoaded: {scene.name}");
//             // Optional: clear path cache on scene change
//             cache.Clear();
//         }
//
//         private void RebuildGraphForActiveScene()
//         {
//             var t0 = Time.realtimeSinceStartup;
//             navGraph.BuildFromNavMesh();
//             var ms = (Time.realtimeSinceStartup - t0) * 1000f;
//             Debug.Log($"[PathSystem] NavMeshGraph built (nodes={navGraph.Nodes.Count}, edges={navGraph.Edges.Count}) in {ms:0.00} ms (version {navGraph.Version}).");
//         }
//
//         private void LogGraph(string where)
//         {
//             Debug.Log($"[PathSystem] {where} → Graph version {navGraph.Version}, nodes={navGraph.Nodes.Count}, edges={navGraph.Edges.Count}");
//         }
//
//         private void PruneCache()
//         {
//             if (cache.Count == 0) return;
//             float now = Time.unscaledTime;
//             var remove = ListPool<CacheKey>.Get(); // small GC saver (see pool below)
//             foreach (var kv in cache)
//             {
//                 if (now - kv.Value.time > cacheTTL) remove.Add(kv.Key);
//             }
//             foreach (var k in remove) cache.Remove(k);
//             ListPool<CacheKey>.Release(remove);
//         }
//
//         private static Vector3 Quantize(Vector3 v, float cell)
//         {
//             if (cell <= 0f) return v;
//             float qx = Mathf.Round(v.x / cell) * cell;
//             float qy = Mathf.Round(v.y / cell) * cell;
//             float qz = Mathf.Round(v.z / cell) * cell;
//             return new Vector3(qx, qy, qz);
//         }
//     }
//
//     public readonly struct PathResult
//     {
//         public readonly NavMeshPathStatus Status;
//         public readonly Vector3[] Corners;
//         public bool IsValid => Status == NavMeshPathStatus.PathComplete && Corners != null && Corners.Length > 0;
//         public PathResult(NavMeshPathStatus status, Vector3[] corners)
//         { Status = status; Corners = corners ?? Array.Empty<Vector3>(); }
//     }
//
//     /// <summary>Tiny list pool to avoid small GC spikes in cache pruning.</summary>
//     static class ListPool<T>
//     {
//         static readonly Stack<List<T>> Pool = new();
//         public static List<T> Get() => Pool.Count > 0 ? Pool.Pop() : new List<T>(8);
//         public static void Release(List<T> list) { list.Clear(); Pool.Push(list); }
//     }
// }