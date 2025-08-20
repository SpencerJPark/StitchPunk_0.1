using UnityEngine;
using UnityEngine.AI;

public class WallAvoidanceLayer : MonoBehaviour, IAvoidanceLayer
{
    [Header("Feelers")]
    public float probeDistance = 1.2f;
    public float sideAngleDeg  = 35f;
    public float sideWeight    = 0.7f;

    [Header("Response")]
    public float strength = 1.2f;          // blend toward away-from-normal
    public LayerMask solidLayers = ~0;
    public int navMeshAreaMask = NavMesh.AllAreas;

    void OnEnable()  => LocalAvoidanceManager.Instance?.Register(this);
    void OnDisable() => LocalAvoidanceManager.Instance?.Unregister(this);

    public Vector3 GetNudge(in AvoidanceContext ctx)
    {
        if (ctx.desiredDirWorld.sqrMagnitude < 1e-6f) return Vector3.zero;

        Vector3 dir = ctx.desiredDirWorld;
        Vector3 left  = Quaternion.Euler(0f, -sideAngleDeg, 0f) * dir;
        Vector3 right = Quaternion.Euler(0f,  sideAngleDeg, 0f) * dir;

        Vector3 nudge =  SampleFeeler(ctx.positionWorld, dir,   ctx.probeDistance, 1.0f)
                       + SampleFeeler(ctx.positionWorld, left,  ctx.probeDistance, sideWeight)
                       + SampleFeeler(ctx.positionWorld, right, ctx.probeDistance, sideWeight);

        if (nudge == Vector3.zero) return Vector3.zero;

        // steer away from obstacles (already a sum of normals), scale
        Vector3 away = nudge.normalized * Mathf.Min(nudge.magnitude, 1f) * strength;
        away.y = 0f;
        return away;
    }

    Vector3 SampleFeeler(Vector3 origin, Vector3 dir, float dist, float weight)
    {
        Vector3 outVec = Vector3.zero;

        if (Physics.Raycast(origin + Vector3.up * 0.1f, dir, out var hit, dist, solidLayers, QueryTriggerInteraction.Ignore))
        {
            float t = 1f - Mathf.Clamp01(hit.distance / dist);
            Vector3 away = Vector3.ProjectOnPlane(hit.normal, Vector3.up).normalized;
            outVec += away * (t * weight);
        }

        if (NavMesh.Raycast(origin, origin + dir * dist, out var navHit, navMeshAreaMask))
        {
            Vector3 away = Vector3.ProjectOnPlane(navHit.normal, Vector3.up).normalized;
            float t = 1f - Mathf.Clamp01(navHit.distance / dist);
            outVec += away * (t * weight);
        }

        return outVec;
    }
}
