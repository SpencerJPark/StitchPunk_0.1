using System.Collections.Generic;
using UnityEngine;

public interface ITickable
{
    void Tick();
}


[DefaultExecutionOrder(-300)]
public sealed class TickManager : PersistentSingleton<TickManager>
{
    [SerializeField] private int targetFps = 24;

    public static float StepSeconds => Instance ? Instance._stepSeconds : (1f / 24f);

    private float _stepSeconds;
    private float _accum;

    private static readonly List<ITickable> _tickables        = new();
    private static readonly List<ITickable> _pendingTickables = new();
    private static int _iterIndex;

    void OnEnable()
    {
        if (targetFps < 1) targetFps = 1;
        _stepSeconds = 1f / targetFps;
    }

    void OnValidate()
    {
        if (targetFps < 1) targetFps = 1;
        _stepSeconds = 1f / targetFps;
    }

    void Update()
    {
        _accum += Time.deltaTime;
        if (_accum < _stepSeconds) return;

        // consume exactly one tick (keep any extra for next frame)
        _accum -= _stepSeconds;

        // run tick
        for (_iterIndex = _tickables.Count - 1; _iterIndex >= 0; _iterIndex--)
            _tickables[_iterIndex]?.Tick();

        // add any late registrations after the loop
        if (_pendingTickables.Count > 0)
        {
            _tickables.AddRange(_pendingTickables);
            _pendingTickables.Clear();
        }
    }

    // --- Static API mirrors UpdateManager’s style ---
    public static void Register(ITickable t)
    {
        if (t == null) return;
        _pendingTickables.Add(t);
    }

    public static void Unregister(ITickable t)
    {
        if (t == null) return;
        _tickables.Remove(t);
        _iterIndex--; // keep index stable (same trick as your UpdateManager)
    }
}