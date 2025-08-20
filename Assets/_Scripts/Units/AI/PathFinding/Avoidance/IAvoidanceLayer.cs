using UnityEngine;

public struct AvoidanceContext
{
    public Vector3 positionWorld;     // agent world position (XZ relevant)
    public Vector3 desiredDirWorld;   // normalized desired direction (XZ)
    public float   probeDistance;     // how far we care to look
    public float   agentRadius;       // clearance we'd like
    public int     groupId;           // for filtering (e.g., separation group)
}

public interface IAvoidanceLayer
{
    /// <summary>
    /// Return a world-space planar nudge to apply to desiredDirWorld.
    /// Magnitude 0..~1. Keep it modest; manager will clamp.
    /// </summary>
    Vector3 GetNudge(in AvoidanceContext ctx);
}
