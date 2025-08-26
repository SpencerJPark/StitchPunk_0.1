using System.Collections.Generic;
using UnityEngine;

public interface ITickable { void Tick(); }

[DefaultExecutionOrder(-300)]
public sealed class TickManager : PersistentSingleton<TickManager>
{
    [Header("Cadence")]
    [SerializeField, Min(1)] private int targetFps = 12;

    [Header("Workload Splitting")]
    [Tooltip("Split each 12Hz window into this many slices; each slice triggers one group once per window.")]
    [SerializeField, Min(1)] private int groupCount = 5;

    [Tooltip("Rebalance existing tickables when groupCount changes in the Inspector.")]
    [SerializeField] private bool autoRebucketOnValidate = true;

    public static float StepSeconds => Instance ? Instance._stepSeconds : (1f / 12f);

    private float _stepSeconds;        // duration of one 12Hz window (≈0.0833s)
    private float _windowElapsed;      // time elapsed inside current window
    private int   _windowId;           // increments when we advance to next 12Hz window
    private int   _lastExecutedSlice = -1; // last slice index we executed in the current window

    // Buckets: each group runs once per window
    private static List<List<ITickable>> _buckets = new();
    private static List<List<ITickable>> _pendingBuckets = new();
    private static Dictionary<ITickable, int> _bucketOf = new();

    private static int _iterIndex; // maintain iterator stability on remove

    void OnEnable()
    {
        if (targetFps < 1) targetFps = 1;
        if (groupCount < 1) groupCount = 1;

        _stepSeconds = 1f / targetFps;
        EnsureBucketStructure(groupCount);

        // Reset window state
        _windowElapsed = 0f;
        _windowId = 0;
        _lastExecutedSlice = -1;
    }

    void OnValidate()
    {
        if (targetFps < 1) targetFps = 1;
        if (groupCount < 1) groupCount = 1;

        _stepSeconds = 1f / targetFps;

        if (autoRebucketOnValidate && _buckets != null && _buckets.Count != groupCount)
            RebucketAll(groupCount);
        else
            EnsureBucketStructure(groupCount);
    }

    void Update()
    {
        // Advance time inside the current 12Hz window.
        _windowElapsed += Time.deltaTime;

        // If we overran the window, roll into the next and reset per-window state.
        if (_windowElapsed >= _stepSeconds)
        {
            // Keep extra time (prevents drift)
            _windowElapsed -= _stepSeconds;
            _windowId++;
            _lastExecutedSlice = -1;
        }

        // Determine which slice of this window we’re currently in: [0 .. groupCount-1]
        // Example: with groupCount=5, sliceLength ≈ 0.0833/5 ≈ 0.0167s
        float sliceLength = _stepSeconds / groupCount;
        int currentSlice = Mathf.Clamp((int)(_windowElapsed / sliceLength), 0, groupCount - 1);

        // Run the slice only once per window; if Update fires multiple times within the same slice, skip.
        if (currentSlice != _lastExecutedSlice)
        {
            RunBucket(currentSlice);
            _lastExecutedSlice = currentSlice;

            // Apply any late registrations to all buckets
            for (int i = 0; i < groupCount; i++)
            {
                if (_pendingBuckets[i].Count == 0) continue;
                _buckets[i].AddRange(_pendingBuckets[i]);
                _pendingBuckets[i].Clear();
            }
        }
    }

    // --- Public Static API ---

    /// <summary>Register a tickable into the least-loaded bucket.</summary>
    public static void Register(ITickable t)
    {
        if (t == null || Instance == null) return;
        if (_bucketOf.ContainsKey(t)) return; // already registered

        int idx = FindLeastLoadedBucket();
        _pendingBuckets[idx].Add(t);
        _bucketOf[t] = idx;
    }

    /// <summary>Unregister from whichever bucket it lives in.</summary>
    public static void Unregister(ITickable t)
    {
        if (t == null) return;
        if (!_bucketOf.TryGetValue(t, out int idx)) return;

        var bucket = _buckets[idx];
        int pos = bucket.IndexOf(t);
        if (pos >= 0)
        {
            bucket.RemoveAt(pos);
            // Maintain iterator stability only if we're currently iterating this bucket
            if (_currentlyIteratingBucket == idx && pos <= _iterIndex) _iterIndex--;
        }
        else
        {
            _pendingBuckets[idx].Remove(t);
        }

        _bucketOf.Remove(t);
    }

    // --- Helpers ---

    private static int _currentlyIteratingBucket = -1;

    private void RunBucket(int bucketIndex)
    {
        _currentlyIteratingBucket = bucketIndex;

        var bucket = _buckets[bucketIndex];
        // Back-to-front so removal during Tick is safe
        for (_iterIndex = bucket.Count - 1; _iterIndex >= 0; _iterIndex--)
        {
            var t = bucket[_iterIndex];
            t?.Tick();
        }

        _currentlyIteratingBucket = -1;
    }

    private static int FindLeastLoadedBucket()
    {
        int best = 0;
        int bestLoad = int.MaxValue;
        for (int i = 0; i < _buckets.Count; i++)
        {
            int load = _buckets[i].Count + _pendingBuckets[i].Count;
            if (load < bestLoad) { bestLoad = load; best = i; }
        }
        return best;
    }

    private void EnsureBucketStructure(int desiredCount)
    {
        if (_buckets.Count == desiredCount && _pendingBuckets.Count == desiredCount) return;

        if (_buckets.Count == 0)
        {
            for (int i = 0; i < desiredCount; i++)
            {
                _buckets.Add(new List<ITickable>(64));
                _pendingBuckets.Add(new List<ITickable>(16));
            }
        }
        else if (_buckets.Count != desiredCount)
        {
            RebucketAll(desiredCount);
        }
    }

    private void RebucketAll(int newCount)
    {
        var all = new List<ITickable>();
        foreach (var b in _buckets) all.AddRange(b);
        foreach (var p in _pendingBuckets) all.AddRange(p);

        _buckets.Clear();
        _pendingBuckets.Clear();
        _bucketOf.Clear();

        for (int i = 0; i < newCount; i++)
        {
            _buckets.Add(new List<ITickable>(Mathf.Max(64, all.Count / Mathf.Max(1, newCount))));
            _pendingBuckets.Add(new List<ITickable>(16));
        }

        for (int i = 0; i < all.Count; i++)
        {
            int idx = i % newCount; // deterministic even spread
            _buckets[idx].Add(all[i]);
            _bucketOf[all[i]] = idx;
        }

        groupCount = newCount;
        _iterIndex = -1;
        _currentlyIteratingBucket = -1;

        // Reset window to avoid weird partial states across a big reconfigure
        _windowElapsed = 0f;
        _windowId++;
        _lastExecutedSlice = -1;
    }
}
