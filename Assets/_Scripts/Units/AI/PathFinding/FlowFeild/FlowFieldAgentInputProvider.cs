// Assets/_Scripts/Units/AI/FlowField/FlowFieldAgentInputProvider.cs
using UnityEngine;

/// <summary>
/// Simple agent input provider that follows a flow field vector,
/// then blends in your layered local avoidance manager.
/// </summary>
public class FlowFieldAgentInputProvider : InputProviderBase, IUpdateObserver
{
    [Header("Goal")]
    public Transform goalTransform;     // optional: if set, system builds to this position
    public Vector3   goalWorld;         // used if no transform
    public float     rebuildInterval = 0.5f;

    [Header("Sampling")]
    public float desiredSpeedScale = 1.0f;
    public float inputDeadzone = 0.05f;
    public MoveSpace outputSpace = MoveSpace.WorldXZ;
    public Camera cameraReference;

    [Header("Avoidance (layered)")]
    public bool useLocalAvoidance = true;

    // runtime
    Vector2 _move;
    float _nextRebuild;
    Transform _self;

    public override Vector2 MoveInput => _move;

    void Awake()
    {
        _self = transform;
        if (!cameraReference && Camera.main) cameraReference = Camera.main;
    }

    void OnEnable()  => UpdateManager.RegisterObserver(this);
    void OnDisable() => UpdateManager.UnregisterObserver(this);

    public void ObservedUpdate()
    {
        var sys = FlowFieldSystem.Instance;
        if (!sys) { _move = Vector2.zero; return; }

        // Rebuild field occasionally or when goal changes (cheap)
        Vector3 target = goalTransform ? goalTransform.position : goalWorld;
        if (Time.time >= _nextRebuild)
        {
            sys.BuildToGoal(target);
            _nextRebuild = Time.time + rebuildInterval;
        }

        // Sample base direction
        Vector3 dirWorld = sys.SampleDirection(_self.position);
        if (dirWorld == Vector3.zero) { _move = Vector2.zero; return; }

        // Blend in layered local avoidance (optional)
        if (useLocalAvoidance && LocalAvoidanceManager.Instance)
        {
            var ctx = new AvoidanceContext {
                positionWorld   = _self.position,
                desiredDirWorld = dirWorld,
                probeDistance   = sys ? sys.GetComponent<FlowFieldSystem>().cellSize * 2.5f : 2f,
                agentRadius     = 0.4f,
                groupId         = 0
            };
            Vector3 nudge = LocalAvoidanceManager.Instance.ComputeNudge(ctx, smooth: true);
            Vector3 combined = dirWorld + nudge;
            if (combined.sqrMagnitude > 1e-6f) dirWorld = combined.normalized;
        }

        // Convert to motor input
        Vector2 input2D = (outputSpace == MoveSpace.WorldXZ || !cameraReference)
            ? new Vector2(dirWorld.x, dirWorld.z)
            : WorldToCameraInput(dirWorld);

        if (input2D.magnitude < inputDeadzone) input2D = Vector2.zero;

        // scale (you can remap to speed elsewhere)
        _move = input2D * desiredSpeedScale;
    }

    Vector2 WorldToCameraInput(Vector3 worldDir)
    {
        var f = cameraReference.transform.forward; f.y = 0f; f.Normalize();
        var r = cameraReference.transform.right;  r.y = 0f; r.Normalize();
        return new Vector2(Vector3.Dot(worldDir, r), Vector3.Dot(worldDir, f)).normalized;
    }
}
