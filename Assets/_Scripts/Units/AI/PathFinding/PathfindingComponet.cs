using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Handles navigation/path-following for a single unit.
/// Path requests are handled by PathService to allow caching & throttling.
/// LocalAvoidanceManager injects separation/wall nudges.
/// </summary>
[System.Serializable]
public class PathfindingComponent
{
    private Vector3[] corners = System.Array.Empty<Vector3>();
    private int currentCornerIndex;
    private Transform owner;

    private Vector3 accumulatedAvoidance = Vector3.zero;

    public Vector2 CurrentMoveInput { get; private set; } = Vector2.zero;

    public PathfindingComponent(Transform ownerTransform)
    {
        owner = ownerTransform;
        Register();
    }

    ~PathfindingComponent()
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

        PathService.Instance.RequestPath(start, targetPosition, areaMask, OnPathReady);
    }

    private void OnPathReady(PathResult result)
    {
        if (result.IsValid)
        {
            corners = result.Corners;
            currentCornerIndex = 0;
        }
        else
        {
            corners = System.Array.Empty<Vector3>();
            currentCornerIndex = 0;
        }
    }

    public void TickUpdate()
    {
        if (corners == null || corners.Length == 0 || currentCornerIndex >= corners.Length)
        {
            CurrentMoveInput = Vector2.zero;
            return;
        }

        // get current target corner
        Vector3 worldTarget = corners[currentCornerIndex];
        Vector3 planarDir = (worldTarget - owner.position);
        planarDir.y = 0f;

        // advance corner if close
        if (planarDir.magnitude < 0.25f)
        {
            currentCornerIndex++;
            CurrentMoveInput = Vector2.zero;
            return;
        }

        planarDir.Normalize();

        // apply avoidance nudges
        Vector3 finalWorldDir = planarDir + accumulatedAvoidance;
        finalWorldDir.y = 0f;

        if (finalWorldDir.sqrMagnitude > 1f)
            finalWorldDir.Normalize();

        CurrentMoveInput = new Vector2(finalWorldDir.x, finalWorldDir.z);

        // clear nudges for next frame
        accumulatedAvoidance = Vector3.zero;
    }
    #endregion

    #region Called by LocalAvoidanceManager
    public void AddAvoidanceNudge(Vector3 nudge)
    {
        accumulatedAvoidance += nudge;
    }

    public void ClearAvoidanceNudge()
    {
        accumulatedAvoidance = Vector3.zero;
    }

    public Transform transform => owner;
    #endregion
}
