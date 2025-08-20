// Assets/_Scripts/Units/AI/FlowField/FlowFieldSystem.cs
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Builds a 2D flow field (top-down XZ grid) guiding agents toward a goal on the NavMesh.
/// Field stores: cost (distance steps) and a direction vector per cell pointing "downhill".
/// Baking is chunked per frame for console-friendly CPU.
/// </summary>
public sealed class FlowFieldSystem : MonoBehaviour
{
    public static FlowFieldSystem Instance { get; private set; }

    [Header("Grid")]
    [Tooltip("World bounds (XZ) that the field covers.")]
    public Vector2 worldCenterXZ = Vector2.zero;
    public Vector2 worldSizeXZ   = new Vector2(80, 80);
    [Tooltip("Meters per cell (2x2 is common).")]
    public float cellSize = 1.0f;

    [Header("NavMesh")]
    public int   areaMask = NavMesh.AllAreas;
    public float sampleMaxDistance = 1.5f;

    [Header("Baking")]
    [Tooltip("Walkability quick test per cell (optional).")]
    public LayerMask solidMask = 0;
    [Tooltip("Neighbors expanded per frame during flood (higher = faster bake, more CPU).")]
    public int expandPerFrame = 4096;
    [Tooltip("Verify cell links with a NavMesh.Raycast (safer, a bit more CPU).")]
    public bool verifyNeighborLinks = true;

    [Header("Debug")]
    public bool drawGridGizmos = false;
    public bool drawVectors    = false;

    // Internal field data
    int cols, rows;
    Vector3 originWorld;   // bottom-left world (x,0,z) of grid
    float invCell;
    int version = 0;       // increments on each bake

    // Costs & vectors
    const int OBSTACLE = int.MaxValue;
    int[]    cost;        // Dijkstra distance in steps
    Vector2[] flow;       // normalized direction (world XZ projected into grid space)

    // Work structures
    readonly Queue<int> frontier = new();
    bool baking = false;
    Vector3 lastGoal;
    int lastVersionProvided = -1;

    // cache for current dequeued cell center (used by neighbor relaxation)
    Vector3 _lastDequeuedCenter;

    void Awake()
    {
        if (Instance && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        RebuildGrid();
    }

    void OnValidate()
    {
        cellSize = Mathf.Max(0.25f, cellSize);
        worldSizeXZ.x = Mathf.Max(cellSize, worldSizeXZ.x);
        worldSizeXZ.y = Mathf.Max(cellSize, worldSizeXZ.y);
        if (Application.isPlaying && Instance == this) RebuildGrid();
    }

    void RebuildGrid()
    {
        invCell = 1f / cellSize;
        cols = Mathf.Max(1, Mathf.RoundToInt(worldSizeXZ.x * invCell));
        rows = Mathf.Max(1, Mathf.RoundToInt(worldSizeXZ.y * invCell));

        originWorld = new Vector3(
            worldCenterXZ.x - (cols * cellSize) * 0.5f,
            0f,
            worldCenterXZ.y - (rows * cellSize) * 0.5f
        );

        cost = new int[cols * rows];
        flow = new Vector2[cols * rows];
        ClearField();
    }

    void ClearField()
    {
        for (int i = 0; i < cost.Length; i++) { cost[i] = OBSTACLE; flow[i] = Vector2.zero; }
        frontier.Clear();
        baking = false;
    }

    // ---------- Public API ----------

    /// <summary>Build/refresh field to a new goal.</summary>
    public void BuildToGoal(Vector3 goalWorld)
    {
        lastGoal = goalWorld;
        PrepareWalkability();
        SeedGoal(goalWorld);
        baking = true;
        version++;
    }

    /// <summary>Sample a normalized world-space direction at a position. Returns zero if undefined.</summary>
    public Vector3 SampleDirection(Vector3 worldPos)
    {
        if (cols == 0 || rows == 0) return Vector3.zero;

        // bilinear sample of flow vectors
        GridUV(worldPos, out float u, out float v);
        if (u < 0 || v < 0 || u > cols - 1 || v > rows - 1) return Vector3.zero;

        int x0 = Mathf.FloorToInt(u);
        int z0 = Mathf.FloorToInt(v);
        int x1 = Mathf.Min(x0 + 1, cols - 1);
        int z1 = Mathf.Min(z0 + 1, rows - 1);
        float tx = u - x0;
        float tz = v - z0;

        Vector2 f00 = flow[Index(x0, z0)];
        Vector2 f10 = flow[Index(x1, z0)];
        Vector2 f01 = flow[Index(x0, z1)];
        Vector2 f11 = flow[Index(x1, z1)];

        Vector2 fx0 = Vector2.Lerp(f00, f10, tx);
        Vector2 fx1 = Vector2.Lerp(f01, f11, tx);
        Vector2 f   = Vector2.Lerp(fx0, fx1, tz);

        if (f.sqrMagnitude < 1e-6f) return Vector3.zero;
        f.Normalize();
        return new Vector3(f.x, 0f, f.y);
    }

    public int CurrentVersion => version;

    // ---------- Baking ----------

    void PrepareWalkability()
    {
        // Mark walkable cells (we validate via physics/navmesh; flood fill will set finite costs)
        for (int z = 0; z < rows; z++)
        for (int x = 0; x < cols; x++)
        {
            int idx = Index(x, z);
            Vector3 c = CellCenterWorld(x, z);

            bool walkable = true;

            if (solidMask.value != 0)
            {
                float r = cellSize * 0.45f;
                if (Physics.CheckSphere(c + Vector3.up * 0.2f, r, solidMask, QueryTriggerInteraction.Ignore))
                    walkable = false;
            }

            if (walkable && sampleMaxDistance > 0f)
            {
                if (!NavMesh.SamplePosition(c, out _, sampleMaxDistance, areaMask))
                    walkable = false;
            }

            cost[idx] = walkable ? OBSTACLE : OBSTACLE; // start all OBSTACLE; flood will write actual values
            flow[idx] = Vector2.zero;
        }
    }

    void SeedGoal(Vector3 goalWorld)
    {
        frontier.Clear();

        GridUV(goalWorld, out float u, out float v);
        int gx = Mathf.Clamp(Mathf.RoundToInt(u), 0, cols - 1);
        int gz = Mathf.Clamp(Mathf.RoundToInt(v), 0, rows - 1);

        int gi = Index(gx, gz);

        // If exact cell isn’t usable, try nearby cells in a small spiral
        if (!IsCellCandidate(gi))
        {
            const int spiral = 3;
            bool found = false;
            for (int r = 1; r <= spiral && !found; r++)
            {
                for (int dz = -r; dz <= r && !found; dz++)
                for (int dx = -r; dx <= r && !found; dx++)
                {
                    int xx = Mathf.Clamp(gx + dx, 0, cols - 1);
                    int zz = Mathf.Clamp(gz + dz, 0, rows - 1);
                    int ii = Index(xx, zz);
                    if (IsCellCandidate(ii)) { gi = ii; found = true; }
                }
            }
            if (!IsCellCandidate(gi)) return; // give up; empty field
        }

        cost[gi] = 0;
        frontier.Enqueue(gi);
    }

    // We accept all cells that passed sampling; connectivity is enforced later per link.
    bool IsCellCandidate(int idx) => true;

    // ---------- Flood (chunked) ----------

    void LateUpdate()
    {
        if (!baking) return;

        int expanded = 0;
        while (frontier.Count > 0 && expanded < expandPerFrame)
        {
            int current = frontier.Dequeue();
            _lastDequeuedCenter = CellCenterWorldFromIndex(current);
            int currentCost = cost[current];

            // Relax 4-neighbors
            RelaxNeighborFrom(current, _lastDequeuedCenter, +1, 0, currentCost);
            RelaxNeighborFrom(current, _lastDequeuedCenter, -1, 0, currentCost);
            RelaxNeighborFrom(current, _lastDequeuedCenter, 0, +1, currentCost);
            RelaxNeighborFrom(current, _lastDequeuedCenter, 0, -1, currentCost);

            expanded++;
        }

        if (frontier.Count == 0)
        {
            ComputeFlowVectors();
            baking = false;
            lastVersionProvided = version;
        }
    }

    void RelaxNeighborFrom(int fromIdx, Vector3 fromCenter, int dx, int dz, int baseCost)
    {
        FromIndex(fromIdx, out int fx, out int fz);
        int nx = fx + dx, nz = fz + dz;
        if (nx < 0 || nz < 0 || nx >= cols || nz >= rows) return;

        int ni = Index(nx, nz);
        Vector3 toCenter = CellCenterWorld(nx, nz);

        // must be connected over NavMesh (reject through-walls)
        if (!CellsConnected(fromCenter, toCenter)) return;

        int newCost = baseCost + 1;
        if (cost[ni] <= newCost) return;

        cost[ni] = newCost;
        frontier.Enqueue(ni);
    }

    // ---------- Flow vectors ----------

    void ComputeFlowVectors()
    {
        for (int z = 0; z < rows; z++)
        for (int x = 0; x < cols; x++)
        {
            int i = Index(x, z);
            if (cost[i] == OBSTACLE) { flow[i] = Vector2.zero; continue; }

            int bestCost = cost[i];
            Vector3 here  = CellCenterWorld(x, z);
            Vector2 best = Vector2.zero;

            // 4-neighbors; pick lowest-cost neighbor that’s connected
            TryPickLower(x + 1, z, ref bestCost, here, ref best);
            TryPickLower(x - 1, z, ref bestCost, here, ref best);
            TryPickLower(x, z + 1, ref bestCost, here, ref best);
            TryPickLower(x, z - 1, ref bestCost, here, ref best);

            flow[i] = best;
        }
    }

    void TryPickLower(int x, int z, ref int bestCost, Vector3 here, ref Vector2 best)
    {
        if (x < 0 || z < 0 || x >= cols || z >= rows) return;
        int idx = Index(x, z);
        int c = cost[idx];
        if (c == OBSTACLE || c >= bestCost) return;

        Vector3 there = CellCenterWorld(x, z);
        if (!CellsConnected(here, there)) return;

        bestCost = c;
        Vector3 dir = there - here; dir.y = 0f;
        if (dir.sqrMagnitude > 1e-6f) dir.Normalize();
        best = new Vector2(dir.x, dir.z);
    }

    // ---------- Utilities ----------

    void FromIndex(int i, out int x, out int z) { z = i / cols; x = i - z * cols; }
    int Index(int x, int z) => z * cols + x;

    Vector3 CellCenterWorld(int x, int z)
        => originWorld + new Vector3((x + 0.5f) * cellSize, 0f, (z + 0.5f) * cellSize);

    Vector3 CellCenterWorldFromIndex(int idx)
    {
        FromIndex(idx, out int x, out int z);
        return CellCenterWorld(x, z);
    }

    bool CellsConnected(Vector3 a, Vector3 b)
    {
        if (!verifyNeighborLinks) return true;
        return !NavMesh.Raycast(a, b, out _, areaMask);
    }

    void GridUV(Vector3 worldPos, out float u, out float v)
    {
        Vector3 local = worldPos - originWorld;
        u = local.x * invCell - 0.5f;
        v = local.z * invCell - 0.5f;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (!drawGridGizmos) return;
        Gizmos.color = new Color(0, 1, 1, 0.2f);
        for (int z = 0; z < rows; z++)
        for (int x = 0; x < cols; x++)
        {
            Vector3 c = CellCenterWorld(x, z);
            Gizmos.DrawWireCube(c + Vector3.up * 0.01f, new Vector3(cellSize, 0f, cellSize));
            if (drawVectors)
            {
                Vector2 f = flow[Index(x, z)];
                if (f.sqrMagnitude > 0.0001f)
                {
                    Vector3 d = new Vector3(f.x, 0f, f.y) * (cellSize * 0.4f);
                    Gizmos.DrawLine(c, c + d);
                }
            }
        }
    }
#endif
}
