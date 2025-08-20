using System.Collections.Generic;
using UnityEngine;

public sealed class LocalAvoidanceManager : MonoBehaviour
{
    public static LocalAvoidanceManager Instance { get; private set; }

    [Header("Blending")]
    [Tooltip("Cap for total nudge magnitude (so path intent still dominates).")]
    public float maxTotalNudge = 0.9f;

    [Tooltip("Exponential smoothing time for combined nudge (seconds).")]
    public float smoothingSeconds = 0.08f;

    readonly List<IAvoidanceLayer> _layers = new();
    Vector3 _smoothedNudge = Vector3.zero;

    void Awake()
    {
        if (Instance && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        // Auto discover on scene
        GetComponentsInChildren<MonoBehaviour>(true, _monoBuffer);
        foreach (var mb in _monoBuffer) if (mb is IAvoidanceLayer layer) _layers.Add(layer);
        _monoBuffer.Clear();
    }

    static readonly List<MonoBehaviour> _monoBuffer = new();

    public void Register(IAvoidanceLayer layer)
    {
        if (!_layers.Contains(layer)) _layers.Add(layer);
    }

    public void Unregister(IAvoidanceLayer layer)
    {
        _layers.Remove(layer);
    }

    public Vector3 ComputeNudge(in AvoidanceContext ctx, bool smooth = true)
    {
        Vector3 acc = Vector3.zero;
        for (int i = 0; i < _layers.Count; i++)
        {
            var n = _layers[i].GetNudge(ctx);
            if (n.y != 0f) n.y = 0f;
            acc += n;
        }

        // Clamp
        float mag = acc.magnitude;
        if (mag > maxTotalNudge) acc *= maxTotalNudge / mag;

        if (!smooth || smoothingSeconds <= 0f) { _smoothedNudge = acc; return acc; }

        float a = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(1e-4f, smoothingSeconds));
        _smoothedNudge = Vector3.Lerp(_smoothedNudge, acc, a);
        return _smoothedNudge;
    }
}
