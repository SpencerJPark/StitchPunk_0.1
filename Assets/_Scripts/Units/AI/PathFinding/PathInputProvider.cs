// Assets/_Scripts/Units/AI/PathFinding/PathInputProvider.cs
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Space to express output input for the motor.
/// </summary>
public enum MoveSpace
{
    WorldXZ,
    CameraRelative
}

/// <summary>
/// Generates movement input for a unit by following NavMesh paths, with:
/// - hard planar goal stop
/// - look-ahead + corner beveling
/// - motion smoothing + deadzone
/// - built-in neighbor separation
/// - shared-goal slotting (no pileups)
/// - wall-aware steering (feelers + normals)
/// - clearance enforcement (require corridor width; rotate/slow/stop if tight)
/// </summary>
public class PathInputProvider : InputProviderBase, IUpdateObserver
{
    #region Inspector: Pathfinding

    [Header("Pathfinding")]
    [Tooltip("Seconds between path refreshes while travelling.")]
    public float repathIntervalSeconds = 0.5f;

    [Tooltip("If closer than this to a path corner, advance to the next one.")]
    public float cornerProximityThreshold = 0.25f;

    [Tooltip("Unused when using hard stop; kept for compatibility.")]
    public float softStoppingDistance = 0.5f;

    [Tooltip("Area mask for NavMesh queries.")]
    public int navMeshAreaMask = NavMesh.AllAreas;

    [Tooltip("Apply simple probe-based static steering on the last direction step.")]
    public bool enableSimpleSteering = true;

    #endregion

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

    #region Inspector: Output

    [Header("Output Space")]
    public MoveSpace outputSpace = MoveSpace.WorldXZ;
    public Camera cameraReference;

    // Back-compat alias for old scripts (optional):
    [System.Obsolete("Use outputSpace instead.")]
    public MoveSpace space { get => outputSpace; set => outputSpace = value; }

    #endregion

    #region Runtime (Debug)

    [Header("Runtime (debug)")]
    public Vector3 requestedDestinationWorld;        // original requested destination (center)
    public bool hasDestination;
    public bool hasArrived;
    public NavMeshPathStatus lastNavStatus = NavMeshPathStatus.PathInvalid;

    #endregion

    #region IInputProvider backing fields

    private Vector2 _currentMoveInput;
    private Vector2 _smoothedMoveInput;
    private Vector2 _smoothedSeparation;
    private bool _actionPressed;
    private bool _interactPressed;
    private Vector2 _steerInput; // unused here

    public override Vector2 MoveInput        => _currentMoveInput;
    public override bool    ActionFired      => Consume(ref _actionPressed);
    public override bool    InteractFired    => Consume(ref _interactPressed);
    public override Vector2 SteerInput       => _steerInput;
    public override bool    ExitVehicleFired => false;

    #endregion

    #region Private State

    // Path following
    private Vector3[] _pathCorners = System.Array.Empty<Vector3>();
    private int _activeCornerIndex = 0;
    private float _nextRepathAllowedTime;

    // Arrival debounce (soft mode)
    private float _softArriveEnteredTime = -1f;

    // Cooldown after arriving
    private float _arrivalCooldownUntil = -1f;

    // Cached references
    private Transform _transform;

    // Simple registry for separation (all live providers)
    private static readonly List<PathInputProvider> _allAgents = new(capacity: 256);

    // Shared goal slotting
    private struct QuantizedGoalKey
    {
        public int quantizedX;
        public int quantizedZ;

        public override int GetHashCode() => (quantizedX * 73856093) ^ (quantizedZ * 19349663);
        public override bool Equals(object other) =>
            other is QuantizedGoalKey key && key.quantizedX == quantizedX && key.quantizedZ == quantizedZ;
    }

    private class GoalGroup
    {
        public readonly List<PathInputProvider> agents = new(capacity: 8);

        public int AssignSlot(PathInputProvider agent)
        {
            for (int i = 0; i < agents.Count; i++)
            {
                if (agents[i] == null) { agents[i] = agent; return i; }
            }
            agents.Add(agent);
            return agents.Count - 1;
        }

        public void Release(PathInputProvider agent)
        {
            for (int i = 0; i < agents.Count; i++)
            {
                if (agents[i] == agent) { agents[i] = null; return; }
            }
        }
    }

    private static readonly Dictionary<QuantizedGoalKey, GoalGroup> _goalGroups = new(capacity: 64);
    private QuantizedGoalKey _myQuantizedGoalKey;
    private int _myGoalSlotIndex = -1;

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        _transform = transform;
        if (!cameraReference && Camera.main) cameraReference = Camera.main;
    }

    private void OnEnable()
    {
        UpdateManager.RegisterObserver(this);
        _allAgents.Add(this);
    }

    private void OnDisable()
    {
        UpdateManager.UnregisterObserver(this);
        _allAgents.Remove(this);
        ReleaseMyGoalSlot();
    }

    #endregion

    #region Public API

    /// <summary>Clear destination and reset state. Keeps position unchanged.</summary>
    public void ClearDestination()
    {
        hasDestination = false;
        hasArrived = true;

        _softArriveEnteredTime = -1f;
        _arrivalCooldownUntil = -1f;

        _pathCorners = System.Array.Empty<Vector3>();
        _activeCornerIndex = 0;

        _currentMoveInput = Vector2.zero;
        _smoothedMoveInput = Vector2.zero;
        _smoothedSeparation = Vector2.zero;

        lastNavStatus = NavMeshPathStatus.PathInvalid;

        ReleaseMyGoalSlot();
    }

    /// <summary>
    /// Set a new world destination. If shared-goal slotting is enabled,
    /// the agent is assigned a personal slot around this destination.
    /// </summary>
    public void SetDestination(Vector3 destinationWorld)
    {
        if (samplePositionsToNavMesh &&
            NavMesh.SamplePosition(destinationWorld, out var navHit, sampleMaxDistance, navMeshAreaMask))
        {
            destinationWorld = navHit.position;
        }

        requestedDestinationWorld = destinationWorld;
        hasDestination = true;
        hasArrived = false;

        _softArriveEnteredTime = -1f;
        _arrivalCooldownUntil = -1f;

        if (enableSharedGoalSlotting) AcquireMyGoalSlot(requestedDestinationWorld);
        else                          ReleaseMyGoalSlot();

        RequestNewPathNow();
    }

    public void FireAction()   => _actionPressed   = true;
    public void FireInteract() => _interactPressed = true;

    #endregion

    #region Update Loop

    public void ObservedUpdate()
    {
        if (Time.time < _arrivalCooldownUntil) { ZeroOutputs(); return; }

        if (hasDestination && !hasArrived && Time.time >= _nextRepathAllowedTime)
            RequestNewPathNow();

        _currentMoveInput = ComputeMoveInput();
    }

    #endregion

    #region Path Requests / Callbacks

    private void RequestNewPathNow()
    {
        if (!hasDestination) return;
        if (Time.time < _arrivalCooldownUntil) return;

        _nextRepathAllowedTime = Time.time + repathIntervalSeconds;

        Vector3 startWorld = _transform.position;
        if (samplePositionsToNavMesh &&
            NavMesh.SamplePosition(startWorld, out var navHit, sampleMaxDistance, navMeshAreaMask))
        {
            startWorld = navHit.position;
        }

        Vector3 myPersonalGoal = GetMyPersonalGoalWorld();
        PathService.Instance.RequestPath(startWorld, myPersonalGoal, navMeshAreaMask, OnPathReady);
    }

    private void OnPathReady(PathResult pathResult)
    {
        lastNavStatus = pathResult.Status;

        if (!hasDestination || !pathResult.IsValid)
        {
            _pathCorners = System.Array.Empty<Vector3>();
            hasArrived = true;
#if UNITY_EDITOR
            Debug.LogWarning($"{name}: Path invalid ({lastNavStatus}). Start or destination may be off the NavMesh.");
#endif
            return;
        }

        _pathCorners = CreateBeveledCorners(pathResult.Corners, bevelStrength: 0.15f);
        _activeCornerIndex = Mathf.Min(1, _pathCorners.Length - 1); // often skip the first near-start corner
    }

    #endregion

    #region Input Computation

    private Vector2 ComputeMoveInput()
{
    if (hasArrived) return SmoothMove(Vector2.zero);
    if (!hasDestination || _pathCorners.Length == 0) return SmoothMove(Vector2.zero);

    Vector3 currentWorldPosition = _transform.position;

    // 1) Corner following
    AdvanceActiveCornerIfClose(currentWorldPosition);

    // 2) Personal goal (slot)
    Vector3 personalGoalWorld = GetMyPersonalGoalWorld();

    // 3) Hard stop if inside acceptance radius
    if (TryHardStopAtGoal(currentWorldPosition, personalGoalWorld))
        return Vector2.zero;

    // 4) Lookahead target along current segment
    Vector3 targetWorld = ComputeLookaheadTarget(currentWorldPosition, personalGoalWorld);

    // 5) Base direction toward target (planar)
    Vector3 directionWorld = ComputeNormalizedPlanarDirection(currentWorldPosition, targetWorld);
    if (directionWorld == Vector3.zero) return SmoothMove(Vector2.zero);

    // 6) Optional cheap static steering (your existing probe)
    if (enableSimpleSteering)
        directionWorld = SimpleSteering.AvoidStatic(directionWorld, currentWorldPosition, steeringProbeLength, steeringObstacleMask);

    // 7) Layered local-avoidance blend (walls, vehicles, separation, etc.)
    //    This replaces the in-class wall/separation blending.
    Vector3 avoidanceNudgeWorld = ComputeAvoidanceNudge(currentWorldPosition, directionWorld);
    Vector3 combinedWorldDir = (directionWorld + avoidanceNudgeWorld);
    if (combinedWorldDir.sqrMagnitude > 1e-6f) combinedWorldDir.Normalize();
    else combinedWorldDir = directionWorld;

    // 8) Clearance enforcement (rotate/slow/stop if corridor too tight)
    var clearanceResult = EnforceClearance(currentWorldPosition, combinedWorldDir);
    combinedWorldDir = clearanceResult.adjustedDir;
    float clearanceSpeedScale = clearanceResult.speedScale;

    // 9) Soft slowdown near goal (only if NOT using hard stop)
    float softSpeedScale = 1f;
    if (!hardStopOnEnter && softSlowRadius > softArriveRadius)
    {
        float distanceToGoal = ComputePlanarDistance(currentWorldPosition, personalGoalWorld);
        float t = Mathf.InverseLerp(softArriveRadius, softSlowRadius, distanceToGoal);
        softSpeedScale = SmoothStep01(t);
    }

    // 10) Convert to motor input space
    Vector2 move2D = ConvertDirectionToMove2D(combinedWorldDir) * softSpeedScale * clearanceSpeedScale;

    // 11) Deadzone + clamp
    if (move2D.magnitude > 1f) move2D.Normalize();
    if (move2D.magnitude < inputDeadzone) move2D = Vector2.zero;

    return SmoothMove(move2D);
}

/// <summary>
/// Ask the LocalAvoidanceManager for a combined nudge from all active layers
/// (wall feelers, dynamic obstacles/vehicles, separation, etc.).
/// Returns world-space planar vector to add to desired direction.
/// </summary>
private Vector3 ComputeAvoidanceNudge(Vector3 positionWorld, Vector3 desiredDirWorld)
{
    if (!LocalAvoidanceManager.Instance || desiredDirWorld.sqrMagnitude < 1e-6f)
        return Vector3.zero;

    // Choose a reasonable probe distance & agent radius for your agents.
    var ctx = new AvoidanceContext
    {
        positionWorld   = positionWorld,
        desiredDirWorld = desiredDirWorld,
        probeDistance   = Mathf.Max(segmentLookaheadDistance, steeringProbeLength), // or wallProbeDistance
        agentRadius     = Mathf.Max(goalAcceptanceRadius * 0.5f, 0.35f),
        groupId         = separationGroupId
    };

    Vector3 nudge = LocalAvoidanceManager.Instance.ComputeNudge(ctx, smooth: true);
    nudge.y = 0f;
    return nudge;
}


    private void AdvanceActiveCornerIfClose(Vector3 currentWorldPosition)
    {
        while (_activeCornerIndex < _pathCorners.Length &&
               Vector3.Distance(currentWorldPosition, _pathCorners[_activeCornerIndex]) <= cornerProximityThreshold)
        {
            _activeCornerIndex++;
        }
    }

    private bool TryHardStopAtGoal(Vector3 currentWorldPosition, Vector3 personalGoalWorld)
    {
        if (!hardStopOnEnter) return false;

        float planarDistance = ComputePlanarDistance(currentWorldPosition, personalGoalWorld);
        if (planarDistance > goalAcceptanceRadius) return false;

        // Stop immediately and clear path
        hasArrived = true;
        _pathCorners = System.Array.Empty<Vector3>();
        _activeCornerIndex = 0;

        // Suppress repaths briefly
        _arrivalCooldownUntil = Time.time + arrivalCooldownSeconds;

        // Zero all outputs instantly (no smoothing tail)
        ZeroOutputs();

        // Release shared slot so others may reuse if needed
        ReleaseMyGoalSlot();

        return true;
    }

    private Vector3 ComputeLookaheadTarget(Vector3 currentWorldPosition, Vector3 personalGoalWorld)
    {
        if (_activeCornerIndex >= _pathCorners.Length)
            return personalGoalWorld;

        Vector3 immediateTarget = _pathCorners[_activeCornerIndex];

        if (_activeCornerIndex > 0)
        {
            Vector3 segmentStart = _pathCorners[_activeCornerIndex - 1];
            Vector3 segmentEnd   = _pathCorners[_activeCornerIndex];

            Vector3 segment = segmentEnd - segmentStart; segment.y = 0f;
            if (segment.sqrMagnitude > 1e-6f)
            {
                Vector3 segmentDir = segment.normalized;
                float   segmentLen = segment.magnitude;

                float projected = Vector3.Dot(currentWorldPosition - segmentStart, segmentDir);
                float clamped   = Mathf.Clamp(projected, 0f, segmentLen);

                Vector3 onSegment = segmentStart + segmentDir * clamped;
                Vector3 lookahead = onSegment + segmentDir * segmentLookaheadDistance;

                if (Vector3.Dot(lookahead - segmentStart, segment) > segment.sqrMagnitude)
                    lookahead = segmentEnd;

                immediateTarget = lookahead;
            }
        }

        return immediateTarget;
    }

    private Vector3 ComputeNormalizedPlanarDirection(Vector3 fromWorld, Vector3 toWorld)
    {
        Vector3 direction = toWorld - fromWorld;
        direction.y = 0f;
        float magnitudeSquared = direction.sqrMagnitude;
        if (magnitudeSquared < 1e-6f) return Vector3.zero;
        return direction / Mathf.Sqrt(magnitudeSquared);
    }

    private float ComputePlanarDistance(Vector3 aWorld, Vector3 bWorld)
    {
        Vector3 delta = bWorld - aWorld; delta.y = 0f;
        return delta.magnitude;
    }

    #endregion

    #region Wall Avoidance & Clearance (new)

    /// <summary>Bias moveDir away from nearby walls using feelers and NavMesh boundary raycasts.</summary>
    private Vector3 AdjustForWalls(Vector3 position, Vector3 moveDirWorld)
    {
        if (moveDirWorld.sqrMagnitude < 1e-6f) return moveDirWorld;

        Vector3 accumulated = moveDirWorld;

        // Feeler directions: forward, +/- angle
        Vector3 forwardFeeler = moveDirWorld;
        Vector3 leftFeeler    = Quaternion.Euler(0f, -wallFeelerAngle, 0f) * forwardFeeler;
        Vector3 rightFeeler   = Quaternion.Euler(0f,  wallFeelerAngle, 0f) * forwardFeeler;

        // Sample each feeler using Physics and NavMesh.Raycast
        accumulated += SampleFeeler(position, forwardFeeler);
        accumulated += SampleFeeler(position, leftFeeler)  * 0.7f;   // side feelers slightly weaker
        accumulated += SampleFeeler(position, rightFeeler) * 0.7f;

        // Normalize + keep some of the original heading
        Vector3 blended = Vector3.Slerp(moveDirWorld, accumulated.normalized, Mathf.Clamp01(wallAvoidStrength));
        blended.y = 0f;
        return blended.normalized;
    }

    /// <summary>Return a push-away vector from walls hit along the feeler.</summary>
    private Vector3 SampleFeeler(Vector3 origin, Vector3 dirWorld)
    {
        Vector3 result = Vector3.zero;

        // Physics raycast into solids
        if (Physics.Raycast(origin + Vector3.up * 0.1f, dirWorld, out var hit, wallProbeDistance, solidLayers, QueryTriggerInteraction.Ignore))
        {
            float proximity01 = 1f - Mathf.Clamp01(hit.distance / wallProbeDistance);
            Vector3 away = Vector3.ProjectOnPlane(hit.normal, Vector3.up).normalized;
            result += away * proximity01;
        }

        // NavMesh boundary cast (detect off-mesh or wall edges)
        if (NavMesh.Raycast(origin, origin + dirWorld * wallProbeDistance, out var navHit, navMeshAreaMask))
        {
            Vector3 away = Vector3.ProjectOnPlane(navHit.normal, Vector3.up).normalized;
            float proximity01 = 1f - Mathf.Clamp01(navHit.distance / wallProbeDistance);
            result += away * proximity01;
        }

        return result;
    }

    /// <summary>
    /// Ensure the motion corridor has enough free space. If not, try to rotate away a bit.
    /// Returns an adjusted direction and a speed scale (slow down when tight).
    /// </summary>
    private (Vector3 adjustedDir, float speedScale) EnforceClearance(Vector3 position, Vector3 desiredDirWorld)
    {
        if (desiredDirWorld.sqrMagnitude < 1e-6f) return (Vector3.zero, 0f);

        if (HasClearCorridor(position, desiredDirWorld)) return (desiredDirWorld, 1f);

        // Try small rotations left/right to find a nearby clear direction
        for (int i = 1; i <= clearanceAngleTries; i++)
        {
            float ang = clearanceAngleStep * i;

            Vector3 left = Quaternion.Euler(0f, -ang, 0f) * desiredDirWorld;
            if (HasClearCorridor(position, left)) return (left, clearanceSlowFactor);

            Vector3 right = Quaternion.Euler(0f,  ang, 0f) * desiredDirWorld;
            if (HasClearCorridor(position, right)) return (right, clearanceSlowFactor);
        }

        // No clear corridor nearby: slow/stop
        return (Vector3.zero, 0f);
    }

    /// <summary>
    /// Corridor check using spherecast steps: does a cylinder ahead keep at least minClearance?
    /// Also rejects leaving the NavMesh.
    /// </summary>
    private bool HasClearCorridor(Vector3 position, Vector3 dirWorld)
    {
        float stepDistance = Mathf.Max(0.2f, minClearance * 0.8f);
        int   stepCount    = Mathf.CeilToInt(wallProbeDistance / stepDistance);

        float sphereRadius = Mathf.Max(0.05f, minClearance);
        Vector3 start = position + Vector3.up * 0.2f; // small lift to avoid ground
        Vector3 dir   = dirWorld.normalized;

        for (int i = 1; i <= stepCount; i++)
        {
            Vector3 to = start + dir * (i * stepDistance);
            Vector3 delta = to - start;
            float   dist  = delta.magnitude;

            if (Physics.SphereCast(start, sphereRadius, dir, out _, dist, solidLayers, QueryTriggerInteraction.Ignore))
                return false;

            if (NavMesh.Raycast(start, to, out _, navMeshAreaMask))
                return false;
        }
        return true;
    }

    #endregion

    #region Separation

    private Vector2 ComputeSeparationNudgePlanar(Vector3 myWorldPosition)
    {
        if (_allAgents.Count <= 1) return Vector2.zero;

        float radius = Mathf.Max(0.01f, separationRadius);
        float radiusSquared = radius * radius;

        Vector2 accumulatedPush = Vector2.zero;
        int neighborCount = 0;

        for (int i = 0; i < _allAgents.Count; i++)
        {
            PathInputProvider other = _allAgents[i];
            if (other == null || other == this) continue;
            if (!other.enableSeparation) continue;
            if (other.separationGroupId != separationGroupId) continue;

            Vector3 otherPos = other._transform.position;

            float deltaX = myWorldPosition.x - otherPos.x;
            float deltaZ = myWorldPosition.z - otherPos.z;
            float planarDistanceSquared = (deltaX * deltaX) + (deltaZ * deltaZ);

            if (planarDistanceSquared > radiusSquared || planarDistanceSquared < 1e-6f) continue;

            float planarDistance = Mathf.Sqrt(planarDistanceSquared);
            float closeness01 = 1f - (planarDistance / radius);      // 0 at edge, 1 at same pos

            Vector2 awayDirection = new(deltaX, deltaZ);
            awayDirection /= Mathf.Max(planarDistance, 1e-4f);

            accumulatedPush += awayDirection * (closeness01 * separationStrength);

            neighborCount++;
            if (neighborCount >= separationMaxNeighbors) break;
        }

        float pushMagnitude = accumulatedPush.magnitude;
        if (pushMagnitude > separationMaxNudge)
            accumulatedPush *= (separationMaxNudge / Mathf.Max(pushMagnitude, 1e-6f));

        if (accumulatedPush.magnitude < 0.02f)
            accumulatedPush = Vector2.zero;

        return accumulatedPush;
    }

    private Vector2 SmoothSeparation(Vector2 rawSeparation)
    {
        float smoothing = Mathf.Max(0f, separationSmoothingSeconds);
        float lerpAlpha = (smoothing <= 0f)
            ? 1f
            : 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(1e-4f, smoothing));

        _smoothedSeparation = Vector2.Lerp(_smoothedSeparation, rawSeparation, lerpAlpha);
        return _smoothedSeparation;
    }

    #endregion

    #region Shared Goal Slotting

    private void AcquireMyGoalSlot(Vector3 goalWorld)
    {
        _myQuantizedGoalKey = QuantizeGoal(goalWorld, goalQuantizationMeters);

        if (!_goalGroups.TryGetValue(_myQuantizedGoalKey, out GoalGroup group))
        {
            group = new GoalGroup();
            _goalGroups[_myQuantizedGoalKey] = group;
        }

        _myGoalSlotIndex = group.AssignSlot(this);
    }

    private void ReleaseMyGoalSlot()
    {
        if (_myGoalSlotIndex < 0) return;

        if (_goalGroups.TryGetValue(_myQuantizedGoalKey, out GoalGroup group))
            group.Release(this);

        _myGoalSlotIndex = -1;
    }

    private Vector3 GetMyPersonalGoalWorld()
    {
        if (!enableSharedGoalSlotting || _myGoalSlotIndex < 0)
            return requestedDestinationWorld;

        int slotsPerRing = Mathf.Max(1, firstRingSlotCount);

        int ringIndex  = _myGoalSlotIndex / slotsPerRing;
        int slotOnRing = _myGoalSlotIndex % slotsPerRing;

        float ringRadius = Mathf.Max(0f, firstRingRadius + ringIndex * ringSpacing);
        float angleRadians = (slotOnRing / (float)slotsPerRing) * Mathf.PI * 2f;

        Vector3 offset = new(Mathf.Cos(angleRadians) * ringRadius, 0f, Mathf.Sin(angleRadians) * ringRadius);
        return requestedDestinationWorld + offset;
    }

    private static QuantizedGoalKey QuantizeGoal(Vector3 world, float quantizationMeters)
    {
        float cell = Mathf.Max(0.01f, quantizationMeters);
        return new QuantizedGoalKey
        {
            quantizedX = Mathf.FloorToInt(world.x / cell),
            quantizedZ = Mathf.FloorToInt(world.z / cell)
        };
    }

    #endregion

    #region Helpers

    private void ZeroOutputs()
    {
        _currentMoveInput = Vector2.zero;
        _smoothedMoveInput = Vector2.zero;
        _smoothedSeparation = Vector2.zero;
    }

    private static float SmoothStep01(float t) => t * t * (3f - 2f * t);

    private Vector2 ConvertDirectionToMove2D(Vector3 worldDirection)
    {
        if (outputSpace == MoveSpace.WorldXZ || cameraReference == null)
            return new Vector2(worldDirection.x, worldDirection.z);

        Vector3 forward = cameraReference.transform.forward; forward.y = 0f; forward.Normalize();
        Vector3 right   = cameraReference.transform.right;  right.y   = 0f; right.Normalize();

        return new Vector2(Vector3.Dot(worldDirection, right), Vector3.Dot(worldDirection, forward)).normalized;
    }

    /// <summary>Bevel sharp corners slightly to reduce 90-degree snaps.</summary>
    private static Vector3[] CreateBeveledCorners(IReadOnlyList<Vector3> rawCorners, float bevelStrength)
    {
        if (rawCorners == null || rawCorners.Count == 0) return System.Array.Empty<Vector3>();

        Vector3[] beveled = new Vector3[rawCorners.Count];
        for (int i = 0; i < rawCorners.Count; i++) beveled[i] = rawCorners[i];

        for (int i = 1; i < beveled.Length - 1; i++)
        {
            Vector3 prev = beveled[i - 1]; prev.y = 0;
            Vector3 curr = beveled[i];     curr.y = 0;
            Vector3 next = beveled[i + 1]; next.y = 0;

            Vector3 toPrev = curr - prev;
            Vector3 toNext = next - curr;

            if (toPrev.sqrMagnitude <= 0.01f || toNext.sqrMagnitude <= 0.01f)
                continue;

            float t = Mathf.Clamp01(bevelStrength);
            beveled[i] = curr - toPrev.normalized * t - toNext.normalized * t;
        }

        return beveled;
    }

    private Vector2 SmoothMove(Vector2 rawMove)
    {
        if (moveSmoothingSeconds <= 0f)
        {
            _smoothedMoveInput = rawMove;
            return rawMove;
        }

        float lerpAlpha = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(1e-4f, moveSmoothingSeconds));
        _smoothedMoveInput = Vector2.Lerp(_smoothedMoveInput, rawMove, lerpAlpha);
        return _smoothedMoveInput;
    }

    private static bool Consume(ref bool flag)
    {
        if (!flag) return false;
        flag = false;
        return true;
    }

    #endregion

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        // Draw current path
        if (_pathCorners != null && _pathCorners.Length > 0)
        {
            Gizmos.color = Color.cyan;
            for (int i = 0; i < _pathCorners.Length - 1; i++)
                Gizmos.DrawLine(_pathCorners[i], _pathCorners[i + 1]);

            foreach (Vector3 corner in _pathCorners)
                Gizmos.DrawWireSphere(corner, 0.06f);
        }

        // Draw my personal slot radius (what I stop on)
        if (hasDestination)
        {
            Vector3 myGoal = GetMyPersonalGoalWorld();
            Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.9f);
            Gizmos.DrawWireSphere(myGoal, goalAcceptanceRadius);

            // Draw center ring for reference
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.85f);
            const int segments = 32;
            Vector3 prev = requestedDestinationWorld + new Vector3(goalAcceptanceRadius, 0f, 0f);
            for (int i = 1; i <= segments; i++)
            {
                float angle = (i / (float)segments) * Mathf.PI * 2f;
                Vector3 next = requestedDestinationWorld + new Vector3(Mathf.Cos(angle) * goalAcceptanceRadius, 0f, Mathf.Sin(angle) * goalAcceptanceRadius);
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
        }
    }
#endif
}
