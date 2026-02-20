using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Static utility class providing pathfinding helper methods for both
/// Flow Field and D* Lite algorithms. Use this for common operations
/// across both systems.
/// </summary>
public static class PathfindingUtils
{
    // ============================================
    // GRID UTILITIES (shared by both algorithms)
    // ============================================
    
    /// <summary>
    /// Convert world position to grid coordinates.
    /// </summary>
    public static int2 WorldToGrid(float3 worldPos, float gridNodeSize)
    {
        return new int2(
            (int)math.floor(worldPos.x / gridNodeSize),
            (int)math.floor(worldPos.z / gridNodeSize)
        );
    }
    
    /// <summary>
    /// Convert grid coordinates to world position (cell center).
    /// </summary>
    public static float3 GridToWorld(int2 gridPos, float gridNodeSize)
    {
        return new float3(
            gridPos.x * gridNodeSize + gridNodeSize * 0.5f,
            0f,
            gridPos.y * gridNodeSize + gridNodeSize * 0.5f
        );
    }
    
    /// <summary>
    /// Convert grid coordinates to world position (cell corner).
    /// </summary>
    public static float3 GridToWorldCorner(int2 gridPos, float gridNodeSize)
    {
        return new float3(
            gridPos.x * gridNodeSize,
            0f,
            gridPos.y * gridNodeSize
        );
    }
    
    /// <summary>
    /// Calculate flat array index from 2D grid position.
    /// </summary>
    public static int CalculateIndex(int2 pos, int width) => pos.x + pos.y * width;
    
    /// <summary>
    /// Calculate flat array index from x,y coordinates.
    /// </summary>
    public static int CalculateIndex(int x, int y, int width) => x + y * width;
    
    /// <summary>
    /// Convert flat array index back to 2D grid position.
    /// </summary>
    public static int2 IndexToGrid(int index, int width)
    {
        return new int2(index % width, index / width);
    }
    
    /// <summary>
    /// Check if a grid position is within bounds.
    /// </summary>
    public static bool IsValidPosition(int2 pos, int width, int height)
    {
        return pos.x >= 0 && pos.y >= 0 && pos.x < width && pos.y < height;
    }
    
    /// <summary>
    /// Check if a world position maps to a valid grid cell.
    /// </summary>
    public static bool IsValidWorldPosition(float3 worldPos, int width, int height, float gridNodeSize)
    {
        int2 gridPos = WorldToGrid(worldPos, gridNodeSize);
        return IsValidPosition(gridPos, width, height);
    }
    
    /// <summary>
    /// Check if a grid cell is walkable (not a wall).
    /// </summary>
    public static bool IsWalkable(int2 pos, int width, NativeArray<byte> costMap)
    {
        int index = CalculateIndex(pos, width);
        return costMap[index] != GridSystem.WALL_COST;
    }
    
    /// <summary>
    /// Check if a world position is walkable.
    /// </summary>
    public static bool IsWalkable(float3 worldPos, int width, int height, 
        NativeArray<byte> costMap, float gridNodeSize)
    {
        int2 gridPos = WorldToGrid(worldPos, gridNodeSize);
        if (!IsValidPosition(gridPos, width, height))
            return false;
        return IsWalkable(gridPos, width, costMap);
    }
    
    /// <summary>
    /// Get the terrain cost at a grid position.
    /// </summary>
    public static byte GetCost(int2 pos, int width, NativeArray<byte> costMap)
    {
        return costMap[CalculateIndex(pos, width)];
    }
    
    // ============================================
    // DISTANCE & HEURISTICS
    // ============================================
    
    /// <summary>
    /// Euclidean distance between two grid positions.
    /// </summary>
    public static float EuclideanDistance(int2 a, int2 b)
    {
        float dx = a.x - b.x;
        float dy = a.y - b.y;
        return math.sqrt(dx * dx + dy * dy);
    }
    
    /// <summary>
    /// Manhattan distance between two grid positions.
    /// </summary>
    public static int ManhattanDistance(int2 a, int2 b)
    {
        return math.abs(a.x - b.x) + math.abs(a.y - b.y);
    }
    
    /// <summary>
    /// Chebyshev (chessboard) distance between two grid positions.
    /// </summary>
    public static int ChebyshevDistance(int2 a, int2 b)
    {
        return math.max(math.abs(a.x - b.x), math.abs(a.y - b.y));
    }
    
    /// <summary>
    /// Octile distance - optimal heuristic for 8-directional movement.
    /// </summary>
    public static float OctileDistance(int2 a, int2 b)
    {
        int dx = math.abs(a.x - b.x);
        int dy = math.abs(a.y - b.y);
        return math.max(dx, dy) + 0.414f * math.min(dx, dy);
    }
    
    /// <summary>
    /// Calculate movement cost considering diagonal movement.
    /// </summary>
    public static float CalculateMoveCost(int dx, int dy, byte terrainCost)
    {
        float baseCost = (dx != 0 && dy != 0) ? 1.414f : 1f;
        return baseCost * terrainCost;
    }
    
    // ============================================
    // FLOW FIELD UTILITIES
    // ============================================
    
    /// <summary>
    /// Get the flow direction at a world position for a specific flow field.
    /// </summary>
    public static float2 GetFlowDirection(float3 worldPos, int gridIndex, int width, int height,
        int cellCount, float gridNodeSize, NativeArray<float2> vectors, NativeArray<bool> isValid)
    {
        if (!isValid[gridIndex])
            return float2.zero;
            
        int2 gridPos = WorldToGrid(worldPos, gridNodeSize);
        if (!IsValidPosition(gridPos, width, height))
            return float2.zero;
            
        int localIndex = CalculateIndex(gridPos, width);
        int globalIndex = gridIndex * cellCount + localIndex;
        
        return vectors[globalIndex];
    }
    
    /// <summary>
    /// Get the flow direction as a world-space vector (XZ plane).
    /// </summary>
    public static float3 GetFlowDirectionWorld(float3 worldPos, int gridIndex, int width, int height,
        int cellCount, float gridNodeSize, NativeArray<float2> vectors, NativeArray<bool> isValid)
    {
        float2 flow = GetFlowDirection(worldPos, gridIndex, width, height, 
            cellCount, gridNodeSize, vectors, isValid);
        return new float3(flow.x, 0f, flow.y);
    }
    
    /// <summary>
    /// Sample flow field with bilinear interpolation for smoother movement.
    /// </summary>
    public static float2 GetFlowDirectionSmooth(float3 worldPos, int gridIndex, int width, int height,
        int cellCount, float gridNodeSize, NativeArray<float2> vectors, NativeArray<bool> isValid,
        NativeArray<byte> costMap)
    {
        if (!isValid[gridIndex])
            return float2.zero;
        
        // Get fractional position within cell
        float fx = worldPos.x / gridNodeSize;
        float fz = worldPos.z / gridNodeSize;
        
        int x0 = (int)math.floor(fx);
        int y0 = (int)math.floor(fz);
        
        float tx = fx - x0;
        float tz = fz - y0;
        
        // Sample 4 corners
        float2 v00 = SampleFlowSafe(x0, y0, gridIndex, width, height, cellCount, vectors, costMap);
        float2 v10 = SampleFlowSafe(x0 + 1, y0, gridIndex, width, height, cellCount, vectors, costMap);
        float2 v01 = SampleFlowSafe(x0, y0 + 1, gridIndex, width, height, cellCount, vectors, costMap);
        float2 v11 = SampleFlowSafe(x0 + 1, y0 + 1, gridIndex, width, height, cellCount, vectors, costMap);
        
        // Bilinear interpolation
        float2 v0 = math.lerp(v00, v10, tx);
        float2 v1 = math.lerp(v01, v11, tx);
        
        return math.normalizesafe(math.lerp(v0, v1, tz));
    }
    
    private static float2 SampleFlowSafe(int x, int y, int gridIndex, int width, int height,
        int cellCount, NativeArray<float2> vectors, NativeArray<byte> costMap)
    {
        if (x < 0 || x >= width || y < 0 || y >= height)
            return float2.zero;
            
        int localIndex = x + y * width;
        if (costMap[localIndex] == GridSystem.WALL_COST)
            return float2.zero;
            
        return vectors[gridIndex * cellCount + localIndex];
    }
    
    // ============================================
    // D* LITE UTILITIES
    // ============================================
    
    /// <summary>
    /// Calculate D* Lite priority key.
    /// </summary>
    public static float2 CalculateDStarKey(int2 pos, int2 start, float g, float rhs, float km)
    {
        float h = OctileDistance(pos, start);
        float minGRhs = math.min(g, rhs);
        return new float2(minGRhs + h + km, minGRhs);
    }
    
    /// <summary>
    /// Compare two D* Lite keys (returns true if a < b).
    /// </summary>
    public static bool KeyLessThan(float2 a, float2 b)
    {
        const float epsilon = 0.001f;
        return a.x < b.x - epsilon || (math.abs(a.x - b.x) < epsilon && a.y < b.y - epsilon);
    }
    
    // ============================================
    // PATHFINDING MODE SELECTION
    // ============================================
    
    /// <summary>
    /// Determine the optimal pathfinding mode based on context.
    /// </summary>
    public static PathfindingMode DetermineOptimalMode(
        bool isInHorde,
        int entitiesWithSameTarget,
        int hordeFormationThreshold,
        bool hasExistingFlowField)
    {
        // If in a horde, always use flow field
        if (isInHorde)
        {
            return PathfindingMode.FlowField;
        }
        
        // If many entities share the same target, consider flow field
        if (entitiesWithSameTarget >= hordeFormationThreshold && hasExistingFlowField)
        {
            return PathfindingMode.FlowField;
        }
        
        // Default to individual pathfinding
        return PathfindingMode.DStarLite;
    }
    
    /// <summary>
    /// Check if an entity should switch pathfinding modes.
    /// </summary>
    public static bool ShouldSwitchMode(
        PathfindingMode currentMode,
        PathfindingMode preferredMode,
        bool isInHorde,
        int entitiesWithSameTarget,
        int hordeFormationThreshold,
        bool hasValidFlowField,
        bool hasValidDStarPath)
    {
        PathfindingMode optimalMode = DetermineOptimalMode(
            isInHorde, entitiesWithSameTarget, hordeFormationThreshold, hasValidFlowField);
            
        if (optimalMode == currentMode)
            return false;
            
        // Don't switch if we don't have valid data for the new mode
        if (optimalMode == PathfindingMode.FlowField && !hasValidFlowField)
            return false;
        if (optimalMode == PathfindingMode.DStarLite && !hasValidDStarPath)
            return false;
            
        return true;
    }
    
    // ============================================
    // NEIGHBOR ITERATION
    // ============================================
    
    /// <summary>
    /// Get all valid neighbor positions (8-directional).
    /// </summary>
    public static void GetNeighbors(int2 pos, int width, int height, 
        NativeArray<byte> costMap, ref NativeList<int2> neighbors)
    {
        neighbors.Clear();
        
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                
                int2 neighborPos = pos + new int2(dx, dy);
                if (!IsValidPosition(neighborPos, width, height))
                    continue;
                    
                if (costMap[CalculateIndex(neighborPos, width)] == GridSystem.WALL_COST)
                    continue;
                    
                neighbors.Add(neighborPos);
            }
        }
    }
    
    /// <summary>
    /// Get cardinal neighbors only (4-directional).
    /// </summary>
    public static void GetCardinalNeighbors(int2 pos, int width, int height,
        NativeArray<byte> costMap, ref NativeList<int2> neighbors)
    {
        neighbors.Clear();
        
        int2[] offsets = { new int2(-1, 0), new int2(1, 0), new int2(0, -1), new int2(0, 1) };
        
        for (int i = 0; i < 4; i++)
        {
            int2 neighborPos = pos + offsets[i];
            if (!IsValidPosition(neighborPos, width, height))
                continue;
                
            if (costMap[CalculateIndex(neighborPos, width)] == GridSystem.WALL_COST)
                continue;
                
            neighbors.Add(neighborPos);
        }
    }
    
    // ============================================
    // LINE OF SIGHT
    // ============================================
    
    /// <summary>
    /// Check if there's a clear line of sight between two grid positions (no walls).
    /// Uses Bresenham's line algorithm.
    /// </summary>
    public static bool HasLineOfSight(int2 from, int2 to, int width, int height, NativeArray<byte> costMap)
    {
        int dx = math.abs(to.x - from.x);
        int dy = math.abs(to.y - from.y);
        int sx = from.x < to.x ? 1 : -1;
        int sy = from.y < to.y ? 1 : -1;
        int err = dx - dy;
        
        int2 current = from;
        
        while (current.x != to.x || current.y != to.y)
        {
            if (!IsValidPosition(current, width, height))
                return false;
                
            if (costMap[CalculateIndex(current, width)] == GridSystem.WALL_COST)
                return false;
            
            int e2 = 2 * err;
            if (e2 > -dy)
            {
                err -= dy;
                current.x += sx;
            }
            if (e2 < dx)
            {
                err += dx;
                current.y += sy;
            }
        }
        
        return true;
    }
    
    /// <summary>
    /// Check line of sight using world positions.
    /// </summary>
    public static bool HasLineOfSight(float3 fromWorld, float3 toWorld, 
        int width, int height, float gridNodeSize, NativeArray<byte> costMap)
    {
        int2 from = WorldToGrid(fromWorld, gridNodeSize);
        int2 to = WorldToGrid(toWorld, gridNodeSize);
        return HasLineOfSight(from, to, width, height, costMap);
    }
}