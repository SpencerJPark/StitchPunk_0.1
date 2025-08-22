using UnityEngine;
using UnityEngine.AI;

[System.Serializable]
public class PathfindingComponent : MonoBehaviour
{
    private Vector3[] corners = System.Array.Empty<Vector3>();
    private int currentCornerIndex;
    private Transform owner;

    private Vector3 accumulatedAvoidance = Vector3.zero;

    public Vector2 CurrentMoveInput { get; private set; }

    private void Awake()
    {
        owner = transform;   // make sure we use Unity's transform
        Register();
    }

    private void OnDestroy()
    {
        Unregister();
    }

    #region Registration
    private void Register()
    {
        if (LocalAvoidanceManager.Instance != null)
            LocalAvoidanceManager.Instance.Register(this);
    }

    private void Unregister()
    {
        if (LocalAvoidanceManager.Instance != null)
            LocalAvoidanceManager.Instance.Unregister(this);
    }
    #endregion

    #region Public API
    public void SetDestination(Vector3 targetPosition)
    {
        if (PathService.Instance == null)
        {
            Debug.LogWarning("No PathService in scene, cannot request path.");
            return;
        }

        var start = owner.position;
        int areaMask = NavMesh.AllAreas;

        Debug.Log($"[Pathfinding] Requesting path from {start} to {targetPosition}");
        PathService.Instance.RequestPath(start, targetPosition, areaMask, OnPathReady);
    }

    private void OnPathReady(PathResult result)
    {
        if (result.IsValid)
        {
            Debug.Log($"[Pathfinding] Got path with {result.Corners.Length} corners");
            corners = result.Corners;
            currentCornerIndex = 0;
        }
        else
        {
            Debug.LogWarning("[Pathfinding] Path invalid!");
            corners = System.Array.Empty<Vector3>();
            currentCornerIndex = 0;
        }
    }

    private void Update()
    {
        TickUpdate();
    }

    public void TickUpdate()
    {
        if (corners == null || corners.Length == 0 || currentCornerIndex >= corners.Length)
        {
            CurrentMoveInput = Vector2.zero;
            return;
        }

        Vector3 worldTarget = corners[currentCornerIndex];
        Vector3 planarDir = (worldTarget - owner.position);
        planarDir.y = 0f;

        float dist = planarDir.magnitude;
        if (dist < 0.25f)
        {
            Debug.Log($"[Pathfinding] Reached corner {currentCornerIndex}, dist {dist}");
            currentCornerIndex++;
            CurrentMoveInput = Vector2.zero;
            return;
        }

        planarDir.Normalize();
        Vector3 finalWorldDir = planarDir + accumulatedAvoidance;
        finalWorldDir.y = 0f;

        if (finalWorldDir.sqrMagnitude > 1f)
            finalWorldDir.Normalize();

        CurrentMoveInput = new Vector2(finalWorldDir.x, finalWorldDir.z);

        //Debug.Log($"[Pathfinding] CurrentMoveInput = {CurrentMoveInput}");

        accumulatedAvoidance = Vector3.zero;
    }

    #endregion

    #region Called by LocalAvoidanceManager
    public void AddAvoidanceNudge(Vector3 nudge) => accumulatedAvoidance += nudge;
    public void ClearAvoidanceNudge() => accumulatedAvoidance = Vector3.zero;
    #endregion
}
