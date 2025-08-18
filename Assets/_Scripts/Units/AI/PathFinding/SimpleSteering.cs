// SimpleSteering.cs
using UnityEngine;

public static class SimpleSteering
{
    // Nudge the desired direction if a short ray hits something.
    public static Vector3 AvoidStatic(Vector3 desired, Vector3 origin, float probeDistance, int layerMask)
    {
        if (desired.sqrMagnitude < 1e-4f) return desired;
        if (!Physics.Raycast(origin + Vector3.up * 0.1f, desired, out var hit, probeDistance, layerMask))
            return desired;

        // pick a lateral escape (right then left)
        Vector3 right = Vector3.Cross(Vector3.up, desired).normalized;
        if (!Physics.Raycast(origin + Vector3.up * 0.1f, (desired + right * 0.75f).normalized, probeDistance * 0.75f, layerMask))
            return (desired + right * 0.75f).normalized;

        Vector3 left = -right;
        if (!Physics.Raycast(origin + Vector3.up * 0.1f, (desired + left * 0.75f).normalized, probeDistance * 0.75f, layerMask))
            return (desired + left * 0.75f).normalized;

        // fallback: slow down toward the obstacle
        return desired * 0.25f;
    }
}
