// PathInputProvider.cs
using UnityEngine;
using UnityEngine.AI;

public class PathInputProvider : InputProviderBase
{
    [Header("Config")]
    public float repathInterval = 0.5f;
    public float cornerReachRadius = 0.2f;
    public float stoppingDistance = 0.5f;
    public int areaMask = NavMesh.AllAreas;
    public bool useSimpleSteering = true;   // lightweight local avoidance

    [Header("Runtime (debug)")]
    public Vector3 destination;
    public bool hasDestination;
    public bool atGoal;

    Vector3[] _corners = System.Array.Empty<Vector3>();
    int _cornerIndex = 0;
    float _nextRepathTime;

    Transform _self;
    Camera _cam;

    void Awake() {
        _self = transform;
        _cam = Camera.main;
    }

    public void ClearDestination() {
        hasDestination = false;
        atGoal = true;
        _corners = System.Array.Empty<Vector3>();
        _cornerIndex = 0;
    }

    public void SetDestination(Vector3 worldPos) {
        destination = worldPos;
        hasDestination = true;
        atGoal = false;
        RequestPathNow();
    }

    void RequestPathNow() {
        if (!hasDestination) return;
        _nextRepathTime = Time.time + repathInterval;
        PathService.Instance.RequestPath(_self.position, destination, areaMask, OnPathReady);
    }

    void OnPathReady(PathResult result) {
        if (!hasDestination) return;
        if (!result.IsValid) { _corners = System.Array.Empty<Vector3>(); atGoal = true; return; }
        _corners = result.Corners;
        _cornerIndex = Mathf.Min(1, _corners.Length - 1); // usually skip the first corner (it’s near start)
    }

    public override Vector3 GetDesiredMove()
    {
        if (!hasDestination || _corners.Length == 0) return Vector3.zero;

        // advance corners
        var pos = _self.position;
        while (_cornerIndex < _corners.Length && Vector3.Distance(pos, _corners[_cornerIndex]) <= cornerReachRadius)
            _cornerIndex++;

        // reached goal?
        if (_cornerIndex >= _corners.Length || Vector3.Distance(pos, destination) <= stoppingDistance) {
            atGoal = true;
            return Vector3.zero;
        }

        // raw direction to current corner
        Vector3 dir = (_corners[_cornerIndex] - pos);
        dir.y = 0f;

        if (useSimpleSteering) dir = SimpleSteering.AvoidStatic(dir.normalized, pos, 0.6f, 1 << LayerMask.NameToLayer("Default"));

        // convert to camera-relative 2.5D if your controller expects it
        if (_cam != null) {
            var camF = _cam.transform.forward; camF.y = 0f; camF.Normalize();
            var camR = _cam.transform.right;  camR.y = 0f;  camR.Normalize();
            Vector2 planar = new Vector2(Vector3.Dot(dir, camR), Vector3.Dot(dir, camF)).normalized;
            return new Vector3(planar.x, 0f, planar.y);
        }
        return dir.normalized;
    }

    void Update() {
        if (hasDestination && Time.time >= _nextRepathTime && !atGoal) {
            RequestPathNow(); // staggered re-path
        }
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected() {
        if (_corners == null || _corners.Length == 0) return;
        Gizmos.color = Color.cyan;
        for (int i = 0; i < _corners.Length - 1; i++) Gizmos.DrawLine(_corners[i], _corners[i + 1]);
        foreach (var c in _corners) { Gizmos.DrawWireSphere(c, 0.06f); }
    }
#endif
}
