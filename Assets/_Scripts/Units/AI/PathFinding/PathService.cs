// PathService.cs
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public sealed class PathService : MonoBehaviour
{
    public static PathService Instance { get; private set; }

    [Header("Perf")]
    [SerializeField] int maxRequestsPerFrame = 16;
    [SerializeField] float cacheCellSize = 1.0f;    // quantize end points
    [SerializeField] float cacheTTL = 1.0f;         // seconds

    struct Request
    {
        public Vector3 start, end;
        public int areaMask;
        public Action<PathResult> onComplete;
    }
    
    // Simple registry for separation (all live providers)
    private static readonly List<PathfindingComponent> AllAgents = new(capacity: 256);


    struct CacheKey : IEquatable<CacheKey>
    {
        public Vector3 endQ; public int areaMask;
        public bool Equals(CacheKey other) => endQ == other.endQ && areaMask == other.areaMask;
        public override int GetHashCode() => endQ.GetHashCode() ^ areaMask.GetHashCode();
    }

    class CacheVal { public float time; public Vector3[] corners; public NavMeshPathStatus status; }

    Queue<Request> _queue = new();
    Dictionary<CacheKey, CacheVal> _cache = new();

    void Awake() {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Update() {
        // prune cache
        if (_cache.Count > 0) {
            float now = Time.time;
            var toRemove = new List<CacheKey>();
            foreach (var kv in _cache)
                if (now - kv.Value.time > cacheTTL) toRemove.Add(kv.Key);
            foreach (var k in toRemove) _cache.Remove(k);
        }

        int budget = maxRequestsPerFrame;
        while (budget-- > 0 && _queue.Count > 0) {
            var req = _queue.Dequeue();

            // cache lookup by END only; start is handled by path following steering
            var key = new CacheKey { endQ = Quantize(req.end, cacheCellSize), areaMask = req.areaMask };
            if (_cache.TryGetValue(key, out var cached)) {
                req.onComplete?.Invoke(new PathResult(cached.status, cached.corners));
                continue;
            }

            var path = new NavMeshPath();
            bool ok = NavMesh.CalculatePath(req.start, req.end, req.areaMask, path);
            var result = new PathResult(path.status, path.corners);

            _cache[key] = new CacheVal { time = Time.time, status = path.status, corners = result.Corners };
            req.onComplete?.Invoke(result);
        }
    }

    public void RequestPath(Vector3 start, Vector3 end, int areaMask, Action<PathResult> onComplete) {
        _queue.Enqueue(new Request { start = start, end = end, areaMask = areaMask, onComplete = onComplete });
    }

    static Vector3 Quantize(Vector3 v, float cell) {
        float qx = Mathf.Round(v.x / cell) * cell;
        float qy = Mathf.Round(v.y / cell) * cell;
        float qz = Mathf.Round(v.z / cell) * cell;
        return new Vector3(qx, qy, qz);
    }
}

public readonly struct PathResult {
    public readonly NavMeshPathStatus Status;
    public readonly Vector3[] Corners;
    public bool IsValid => Status == NavMeshPathStatus.PathComplete && Corners != null && Corners.Length > 0;
    public PathResult(NavMeshPathStatus status, Vector3[] corners){ Status = status; Corners = corners ?? Array.Empty<Vector3>(); }
}
