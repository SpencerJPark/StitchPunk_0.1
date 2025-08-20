// CrowdService.cs
using System.Collections.Generic;
using UnityEngine;

public sealed class CrowdService : MonoBehaviour
{
    public static CrowdService Instance { get; private set; }

    [Header("Grid")]
    [SerializeField] float cellSize = 1.0f;  // ~ separationRadius works well

    struct AgentRef {
        public Transform tf;
        public int groupId;
        public int version; // for lazy removal
    }

    Dictionary<int, AgentRef> agents = new();
    Dictionary<Vector2Int, List<int>> grid = new(); // cell -> agent ids
    Dictionary<int, Vector2Int> lastCell = new();    // agent id -> last cell
    int nextId = 1;

    void Awake() {
        if (Instance && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public int Register(Transform tf, int groupId) {
        int id = nextId++;
        agents[id] = new AgentRef { tf = tf, groupId = groupId, version = 1 };
        var c = Cell(tf.position);
        lastCell[id] = c;
        if (!grid.TryGetValue(c, out var list)) grid[c] = list = new List<int>(8);
        list.Add(id);
        return id;
    }

    public void Unregister(int id) {
        if (!agents.TryGetValue(id, out _)) return;
        var c = lastCell[id];
        if (grid.TryGetValue(c, out var list)) { list.Remove(id); if (list.Count == 0) grid.Remove(c); }
        agents.Remove(id);
        lastCell.Remove(id);
    }

    public void UpdateAgent(int id) {
        if (!agents.TryGetValue(id, out var a) || a.tf == null) { Unregister(id); return; }
        var c = Cell(a.tf.position);
        if (lastCell.TryGetValue(id, out var prev) && prev == c) return;
        // move between buckets
        if (grid.TryGetValue(prev, out var lst)) { lst.Remove(id); if (lst.Count == 0) grid.Remove(prev); }
        if (!grid.TryGetValue(c, out var list)) grid[c] = list = new List<int>(8);
        list.Add(id);
        lastCell[id] = c;
    }

    // Query neighbors within radius in same group; returns count and fills 'outIds'
    static readonly Vector2Int[] kRing = {
        new(0,0), new(1,0), new(-1,0), new(0,1), new(0,-1),
        new(1,1), new(1,-1), new(-1,1), new(-1,-1)
    };
    public int Query(Vector3 pos, float radius, int groupId, int max, List<Transform> outTfs)
    {
        outTfs.Clear();
        float r2 = radius * radius;
        var c0 = Cell(pos);

        foreach (var off in kRing)
        {
            var c = new Vector2Int(c0.x + off.x, c0.y + off.y);
            if (!grid.TryGetValue(c, out var list)) continue;
            for (int i = 0; i < list.Count; i++)
            {
                int id = list[i];
                if (!agents.TryGetValue(id, out var a) || a.tf == null) continue;
                if (a.groupId != groupId) continue;
                Vector3 p = a.tf.position;
                float dx = p.x - pos.x, dz = p.z - pos.z;
                float d2 = dx * dx + dz * dz;
                if (d2 <= r2) { outTfs.Add(a.tf); if (outTfs.Count >= max) return outTfs.Count; }
            }
        }
        return outTfs.Count;
    }

    Vector2Int Cell(Vector3 p) {
        float s = Mathf.Max(0.01f, cellSize);
        return new Vector2Int(Mathf.FloorToInt(p.x / s), Mathf.FloorToInt(p.z / s));
    }
}
