using UnityEngine;
using UnityEngine.AI;

public enum MoveSpace { WorldXZ, CameraRelative }

public class PathInputProvider : InputProviderBase, IUpdateObserver
{
    [Header("Path Config")]
    [Tooltip("Seconds between path refreshes while travelling.")]
    public float repathInterval = 0.5f;
    public float cornerReachRadius = 0.25f;
    public float stoppingDistance = 0.5f;
    public int   areaMask = NavMesh.AllAreas;
    public bool  useSimpleSteering = true;

    [Header("Arrival Tuning")]
    public float arriveRadius   = 0.45f;   // acceptance radius (meters)
    public float slowRadius     = 1.2f;    // start slowing here
    public float arriveHold     = 0.15f;   // must remain inside arriveRadius this long
    public float arriveCooldown = 0.35f;   // after arrival, ignore repaths for this long
    float _arriveEnterTime = -1f;
    float _arrivedUntil    = -1f;

    [Header("Goal Stop")]
    public bool  stopOnEnter = true; // stop immediately on entering goal radius
    public float goalRadius  = 0.6f; // hard acceptance radius

    [Tooltip("If true, snap start & destination positions to the NavMesh.")]
    public bool  sampleToNavMesh   = true;
    public float sampleMaxDistance = 1.5f;

    [Header("Steering Probe")]
    public LayerMask steeringObstacles = ~0; // everything by default
    public float steeringProbe = 0.6f;

    [Header("Smoothing")]
    [Tooltip("Exp smoothing time (seconds). 0 = no smoothing.")]
    public float moveSmoothing = 0.15f;
    [Tooltip("Ignore tiny inputs to prevent micro jitter.")]
    public float inputDeadzone = 0.05f;
    [Tooltip("Meters ahead along current segment to aim at.")]
    public float lookahead = 0.6f;

    [Header("Output Space")]
    public MoveSpace space = MoveSpace.WorldXZ;   // default for CCMotor
    public Camera cameraRef;                      // only used if CameraRelative

    [Header("Runtime (debug)")]
    public Vector3 destination;
    public bool hasDestination;
    public bool atGoal;
    public NavMeshPathStatus lastStatus = NavMeshPathStatus.PathInvalid;

    // IInputProvider backing fields
    private Vector2 _moveInput;
    private Vector2 _smoothedMove;
    private bool _actionFired;
    private bool _interactFired;
    private Vector2 _steerInput;  // unused here

    // IInputProvider
    public override Vector2 MoveInput        => _moveInput;
    public override bool    ActionFired      => Consume(ref _actionFired);
    public override bool    InteractFired    => Consume(ref _interactFired);
    public override Vector2 SteerInput       => _steerInput;
    public override bool    ExitVehicleFired => false;

    Vector3[] _corners = System.Array.Empty<Vector3>();
    int _cornerIndex = 0;
    float _nextRepathTime;

    Transform _self;

    void Awake() {
        _self = transform;
        if (!cameraRef && Camera.main) cameraRef = Camera.main;
    }

    void OnEnable()  => UpdateManager.RegisterObserver(this);
    void OnDisable() => UpdateManager.UnregisterObserver(this);

    // --- Public API ---
    public void ClearDestination() {
        hasDestination  = false;
        atGoal          = true;
        _arriveEnterTime = -1f;
        _arrivedUntil    = -1f;
        _corners = System.Array.Empty<Vector3>();
        _cornerIndex = 0;
        _moveInput = Vector2.zero;
        _smoothedMove = Vector2.zero;
        lastStatus = NavMeshPathStatus.PathInvalid;
    }

    public void SetDestination(Vector3 worldPos) {
        if (sampleToNavMesh && NavMesh.SamplePosition(worldPos, out var hit, sampleMaxDistance, areaMask))
            worldPos = hit.position;

        destination = worldPos;
        hasDestination = true;
        atGoal = false;
        _arriveEnterTime = -1f;
        _arrivedUntil    = -1f;
        RequestPathNow();
    }

    public void FireAction()   => _actionFired = true;
    public void FireInteract() => _interactFired = true;

    // --- Update loop via UpdateManager ---
    public void ObservedUpdate()
    {
        if (Time.time < _arrivedUntil) {
            _moveInput = Vector2.zero;
            _smoothedMove = Vector2.zero;
            return;
        }

        if (hasDestination && Time.time >= _nextRepathTime && !atGoal)
            RequestPathNow();

        _moveInput = ComputeMoveInput();
    }

    // --- Internals ---
    void RequestPathNow() {
        if (!hasDestination) return;
        if (Time.time < _arrivedUntil) return;

        _nextRepathTime = Time.time + repathInterval;

        var start = _self.position;
        if (sampleToNavMesh && NavMesh.SamplePosition(start, out var sh, sampleMaxDistance, areaMask))
            start = sh.position;

        PathService.Instance.RequestPath(start, destination, areaMask, OnPathReady);
    }

    void OnPathReady(PathResult result) {
        lastStatus = result.Status;

        if (!hasDestination || !result.IsValid) {
            _corners = System.Array.Empty<Vector3>();
            atGoal = true;
#if UNITY_EDITOR
            Debug.LogWarning($"{name}: Path invalid ({lastStatus}). Start or destination may be off the NavMesh.");
#endif
            return;
        }

        // Copy & lightly bevel sharp corners to reduce 90° snaps
        var c = (Vector3[])result.Corners.Clone();
        for (int i = 1; i < c.Length - 1; i++) {
            Vector3 prev = c[i - 1]; prev.y = 0;
            Vector3 curr = c[i];     curr.y = 0;
            Vector3 next = c[i + 1]; next.y = 0;
            Vector3 d1 = curr - prev; Vector3 d2 = next - curr;
            if (d1.sqrMagnitude > 0.01f && d2.sqrMagnitude > 0.01f) {
                float t = 0.15f; // bevel strength
                c[i] = curr - d1.normalized * t - d2.normalized * t;
            }
        }

        _corners = c;
        _cornerIndex = Mathf.Min(1, _corners.Length - 1); // skip corner 0 (often near start)
    }

    Vector2 ComputeMoveInput()
    {
        // If we already arrived, don't move until a new destination is set.
        if (atGoal) return Smooth(Vector2.zero);

        if (!hasDestination || _corners.Length == 0) return Smooth(Vector2.zero);

        Vector3 pos = _self.position;

        // Advance corner when close
        while (_cornerIndex < _corners.Length &&
               Vector3.Distance(pos, _corners[_cornerIndex]) <= cornerReachRadius)
            _cornerIndex++;

        // ---- PLANAR (XZ) distance to goal ----
        Vector3 delta3 = destination - pos; delta3.y = 0f;
        float distToGoalXZ = delta3.magnitude;

        // ---- HARD STOP ON ENTER (planar) ----
        if (stopOnEnter && distToGoalXZ <= goalRadius)
        {
            atGoal = true;
            _corners = System.Array.Empty<Vector3>();
            _cornerIndex = 0;
            _arriveEnterTime = -1f;
            _arrivedUntil = Time.time + arriveCooldown;

            // kill motion immediately (no smoothing tail)
            _smoothedMove = Vector2.zero;
            _moveInput    = Vector2.zero;
            return Vector2.zero;
        }

        // Optional: debounce if hard stop is disabled
        if (!stopOnEnter && distToGoalXZ <= arriveRadius)
        {
            if (_arriveEnterTime < 0f) _arriveEnterTime = Time.time;
            if (Time.time - _arriveEnterTime >= arriveHold)
            {
                atGoal = true;
                _arrivedUntil = Time.time + arriveCooldown;
                _smoothedMove = Vector2.zero;
                _moveInput    = Vector2.zero;
                return Vector2.zero;
            }
        }
        else if (!stopOnEnter)
        {
            _arriveEnterTime = -1f;
        }

        // Target with LOOKAHEAD along current segment for stability
        Vector3 target = (_cornerIndex < _corners.Length) ? _corners[_cornerIndex] : destination;

        if (_cornerIndex > 0) {
            Vector3 a = _corners[_cornerIndex - 1];
            Vector3 b = _corners[_cornerIndex];
            Vector3 ab = b - a; ab.y = 0f;
            if (ab.sqrMagnitude > 1e-6f) {
                Vector3 abN = ab.normalized;
                float   abL = ab.magnitude;
                float t = Mathf.Clamp01(Vector3.Dot(pos - a, abN) / abL);
                Vector3 p = a + abN * (t * abL);
                Vector3 ahead = p + abN * lookahead;
                if (Vector3.Dot(ahead - a, ab) > ab.sqrMagnitude) ahead = b;
                target = ahead;
            }
        }

        Vector3 dir = target - pos; dir.y = 0f;
        if (dir.sqrMagnitude < 1e-6f) return Smooth(Vector2.zero);
        dir.Normalize();

        if (useSimpleSteering)
            dir = SimpleSteering.AvoidStatic(dir, pos, steeringProbe, steeringObstacles);

        // Slowdown near goal only when NOT using hard stop, using planar distance
        float mag = 1f;
        if (!stopOnEnter && slowRadius > arriveRadius) {
            float t = Mathf.InverseLerp(arriveRadius, slowRadius, distToGoalXZ);
            mag = t * t * (3f - 2f * t);
        }

        // Output in the space your motor expects
        Vector2 out2D;
        if (space == MoveSpace.WorldXZ || cameraRef == null) {
            out2D = new Vector2(dir.x, dir.z) * mag; // CCMotor wants world XZ
        } else {
            var f = cameraRef.transform.forward; f.y = 0; f.Normalize();
            var r = cameraRef.transform.right;  r.y = 0; r.Normalize();
            out2D = new Vector2(Vector3.Dot(dir, r), Vector3.Dot(dir, f)).normalized * mag;
        }

        if (out2D.magnitude < inputDeadzone) out2D = Vector2.zero;

        return Smooth(out2D);
    }

    // Exponential smoothing (frame-rate independent)
    Vector2 Smooth(Vector2 raw)
    {
        if (moveSmoothing <= 0f) { _smoothedMove = raw; return raw; }
        float a = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(1e-4f, moveSmoothing));
        _smoothedMove = Vector2.Lerp(_smoothedMove, raw, a);
        return _smoothedMove;
    }

    static bool Consume(ref bool flag) {
        if (!flag) return false; flag = false; return true;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected() {
        // Path
        if (_corners != null && _corners.Length > 0) {
            Gizmos.color = Color.cyan;
            for (int i = 0; i < _corners.Length - 1; i++) Gizmos.DrawLine(_corners[i], _corners[i + 1]);
            foreach (var c in _corners) Gizmos.DrawWireSphere(c, 0.06f);
        }
        // Goal radius ring (planar)
        if (hasDestination) {
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.85f);
            const int segs = 32;
            Vector3 prev = destination + new Vector3(goalRadius, 0f, 0f);
            for (int i = 1; i <= segs; i++) {
                float ang = (i / (float)segs) * Mathf.PI * 2f;
                Vector3 next = destination + new Vector3(Mathf.Cos(ang)*goalRadius, 0f, Mathf.Sin(ang)*goalRadius);
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
        }
    }
#endif
}
