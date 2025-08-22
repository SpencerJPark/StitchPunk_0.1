using UnityEngine;

[CreateAssetMenu(fileName = "PathFindingData", menuName = "Units/AI/PathFindingData")]
public class PathFindingData : ScriptableObject
{
     [Header("Pathfinding")]
    [Tooltip("Seconds between path refreshes while travelling.")]
    public float repathIntervalSeconds = 0.5f;

    [Tooltip("If closer than this to a path corner, advance to the next one.")]
    public float cornerProximityThreshold = 0.25f;

    [Tooltip("Unused when using hard stop; kept for compatibility.")]
    public float softStoppingDistance = 0.5f;

    [Tooltip("Area mask for NavMesh queries.")]
    public int navMeshAreaMask = UnityEngine.AI.NavMesh.AllAreas;

    [Tooltip("Apply simple probe-based static steering on the last direction step.")]
    public bool enableSimpleSteering = true;

    #region Inspector: Arrival / Goal

    [Header("Arrival (hard stop)")]
    [Tooltip("Stop immediately when entering this planar radius around the goal (per-agent slot).")]
    public bool hardStopOnEnter = true;

    [Tooltip("Planar acceptance radius (meters) for hard stop.")]
    public float goalAcceptanceRadius = 0.6f;

    [Tooltip("Cooldown after arriving, to suppress repaths/jitter.")]
    public float arrivalCooldownSeconds = 0.35f;

    [Header("Arrival (soft mode, optional)")]
    [Tooltip("Used only if hardStopOnEnter is false.")]
    public float softArriveRadius = 0.45f;

    [Tooltip("Begin slowing at this planar distance from the goal (soft mode only).")]
    public float softSlowRadius = 1.2f;

    [Tooltip("Must remain inside softArriveRadius this long to count as arrived (soft mode only).")]
    public float softArriveHoldSeconds = 0.15f;

    #endregion

    #region Inspector: Sampling & Steering

    [Header("Sampling & Steering")]
    [Tooltip("Snap start & destination to the NavMesh (within sampleMaxDistance).")]
    public bool samplePositionsToNavMesh = true;

    [Tooltip("Maximum distance for NavMesh.SamplePosition.")]
    public float sampleMaxDistance = 1.5f;

    [Tooltip("Layers considered for simple probe-based static steering.")]
    public LayerMask steeringObstacleMask = ~0;

    [Tooltip("Probe length (meters) for simple steering queries.")]
    public float steeringProbeLength = 0.6f;

    #endregion

    #region Inspector: Motion Smoothing

    [Header("Smoothing")]
    [Tooltip("Exponential smoothing time (seconds). 0 disables smoothing.")]
    public float moveSmoothingSeconds = 0.15f;

    [Tooltip("Ignore tiny inputs to prevent micro-jitter.")]
    public float inputDeadzone = 0.05f;

    [Tooltip("Meters ahead along the current segment to aim at for stability.")]
    public float segmentLookaheadDistance = 0.6f;

    #endregion

    #region Inspector: Separation

    [Header("Separation (built-in)")]
    [Tooltip("Enable push-away from nearby agents in the same group.")]
    public bool enableSeparation = true;

    [Tooltip("Units with the same group id separate from each other.")]
    public int separationGroupId = 0;

    [Tooltip("Desired minimum spacing (meters).")]
    public float separationRadius = 1.25f;

    [Tooltip("0..2 push intensity.")]
    public float separationStrength = 1.0f;

    [Tooltip("Cap for separation nudge magnitude (so path still dominates).")]
    public float separationMaxNudge = 0.7f;

    [Tooltip("Exponential smoothing for the separation nudge (seconds).")]
    public float separationSmoothingSeconds = 0.08f;

    [Tooltip("Max neighbors considered per frame (perf guard).")]
    public int separationMaxNeighbors = 16;

    #endregion

    #region Inspector: Shared Goal Slotting

    [Header("Shared Goal Slotting")]
    [Tooltip("Assign slots around a shared destination so agents do not fight over the same point.")]
    public bool enableSharedGoalSlotting = true;

    [Tooltip("Number of slots on the first ring around the goal.")]
    public int firstRingSlotCount = 6;

    [Tooltip("Radius of the first slot ring (meters).")]
    public float firstRingRadius = 0.7f;

    [Tooltip("Additional radius per outer ring (meters).")]
    public float ringSpacing = 0.6f;

    [Tooltip("Quantization (meters) for grouping 'same' goals.")]
    public float goalQuantizationMeters = 0.5f;

    #endregion

    #region Inspector: Wall Avoidance & Clearance

    [Header("Wall Avoidance & Clearance")]
    [Tooltip("Meters ahead to probe for walls.")]
    public float wallProbeDistance = 0.9f;

    [Tooltip("Extra feelers to left/right (deg) for early detection.")]
    public float wallFeelerAngle = 30f;

    [Tooltip("How strongly we steer away from detected wall normals.")]
    public float wallAvoidStrength = 1.0f;

    [Tooltip("Minimum free radius we require around the motion corridor.")]
    public float minClearance = 0.45f; // agent radius + padding

    [Tooltip("Layers considered 'solid' for clearance/feeler checks.")]
    public LayerMask solidLayers = ~0;

    [Tooltip("How hard we slow when clearance is poor (0..1).")]
    public float clearanceSlowFactor = 0.5f;

    [Tooltip("Max tries to rotate away from a blocked direction (deg step).")]
    public int clearanceAngleTries = 3;

    [Tooltip("Angle step in degrees for each try.")]
    public float clearanceAngleStep = 15f;

    #endregion    
}
