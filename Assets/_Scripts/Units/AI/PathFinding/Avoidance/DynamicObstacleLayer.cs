using UnityEngine;

public class DynamicObstacleLayer : MonoBehaviour, IAvoidanceLayer
{
    [Header("Prediction")]
    public float timeHorizon = 0.8f;  // seconds look-ahead
    public float minTime     = 0.1f;  // ignore super short

    [Header("Weights")]
    public float sideBias      = 1.0f;  // how strongly to prefer a side-step
    public float approachGain  = 1.0f;  // stronger when on direct approach
    public float maxNudge      = 0.8f;  // per-obstacle cap

    [Header("Filters")]
    public int   maxChecks = 16;
    public float maxAffectDistance = 4.0f; // meters

    void OnEnable()  => LocalAvoidanceManager.Instance?.Register(this);
    void OnDisable() => LocalAvoidanceManager.Instance?.Unregister(this);

    public Vector3 GetNudge(in AvoidanceContext ctx)
    {
        var items = DynamicObstacleRegistry.Items;
        if (items.Count == 0) return Vector3.zero;

        Vector3 acc = Vector3.zero;
        int checks = 0;

        Vector3 pA = ctx.positionWorld;
        Vector3 vA = ctx.desiredDirWorld * Mathf.Max(ctx.probeDistance / Mathf.Max(timeHorizon, 1e-3f), 0f);

        for (int i = 0; i < items.Count && checks < maxChecks; i++)
        {
            var it = items[i];
            if (!it.transform) continue;

            Vector3 pB = it.transform.position;
            Vector3 vB = it.velocityWorld;

            // quick distance gate
            Vector3 delta = pB - pA; delta.y = 0f;
            float dist = delta.magnitude;
            if (dist > maxAffectDistance) continue;

            float combinedRadius = ctx.agentRadius + it.radius;

            // Relative motion
            Vector3 vRel = vA - vB; vRel.y = 0f;
            float vRelSqr = vRel.sqrMagnitude;
            if (vRelSqr < 1e-6f) continue;

            float tCPA = -Vector3.Dot(delta, vRel) / vRelSqr; // time of closest approach
            if (tCPA < minTime || tCPA > timeHorizon) continue;

            Vector3 closestDelta = delta + vRel * tCPA; closestDelta.y = 0f;
            float dCPA = closestDelta.magnitude;

            if (dCPA >= combinedRadius) continue; // no conflict

            // Side-step direction: perpendicular to relative velocity toward free side
            Vector3 side = Vector3.Cross(Vector3.up, vRel).normalized;
            float sideSign = Mathf.Sign(Vector3.Dot(side, delta)); // choose the freer side
            Vector3 sidestep = side * sideSign;

            // Scale by penetration ratio & approach alignment
            float pen = Mathf.Clamp01((combinedRadius - dCPA) / Mathf.Max(combinedRadius, 1e-3f));
            float approach = Mathf.Clamp01(Vector3.Dot(ctx.desiredDirWorld, -vRel.normalized)); // head-on => 1

            Vector3 nudge = sidestep * (pen * approach * approachGain) + (-closestDelta.normalized * pen * 0.2f);
            float m = nudge.magnitude;
            if (m > maxNudge) nudge *= maxNudge / m;

            acc += nudge * sideBias;
            checks++;
        }

        acc.y = 0f;
        return acc;
    }
}
