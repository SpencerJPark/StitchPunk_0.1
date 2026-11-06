using UnityEngine;

public struct TerrainTileBudgetResult
{
    public bool isOverBudget;
    public int decalCount;
    public int propCount;
    public int maskResolution;
    public int meshVertexCount;
    public string summary;
}

public static class TerrainBudgetCheck
{
    // TT-D28: decals plus props per tile.
    private const int MaxDecalsPlusPropsPerTile = 600;
    // TT-D28: mask resolution a hero tile may reach; MaskResolutionFor already picks 256 for normal tiles.
    private const int MaxMaskResolution = 512;
    // TT-D28: 101x101 ground grid plus 10 percent slack.
    private const int MaxMeshVertices = 10201 + 1020;

    public static TerrainTileBudgetResult Evaluate(TerrainTile tile)
    {
        TerrainTileBudgetResult result = new TerrainTileBudgetResult();
        CountMarkers(tile, out result.decalCount, out result.propCount);

        MeshFilter groundMeshFilter = tile.GetComponentInChildren<MeshFilter>(true);
        bool hasMesh = groundMeshFilter != null && groundMeshFilter.sharedMesh != null;
        result.meshVertexCount = hasMesh ? groundMeshFilter.sharedMesh.vertexCount : 0;
        result.maskResolution = TerrainTileAssets.MaskResolutionFor(tile);

        int decalsPlusProps = result.decalCount + result.propCount;
        bool overMarkers = decalsPlusProps > MaxDecalsPlusPropsPerTile;
        bool overMask = result.maskResolution > MaxMaskResolution;
        bool overMesh = result.meshVertexCount > MaxMeshVertices;
        result.isOverBudget = overMarkers || overMask || overMesh;

        string markerPart = decalsPlusProps + " / " + MaxDecalsPlusPropsPerTile + " decals+props" + (overMarkers ? " OVER" : "");
        string maskPart = "mask " + result.maskResolution + (overMask ? " OVER" : "");
        string meshPart = result.meshVertexCount + " verts" + (overMesh ? " OVER" : "");
        result.summary = markerPart + ", " + maskPart + ", " + meshPart;
        return result;
    }

    public static bool TryEvaluateByCoordinate(Vector2Int coordinate, out TerrainTileBudgetResult result)
    {
        TerrainTile[] openTiles = Object.FindObjectsByType<TerrainTile>(FindObjectsInactive.Include);
        foreach (TerrainTile openTile in openTiles)
        {
            if (openTile.tileCoordinate != coordinate)
                continue;
            result = Evaluate(openTile);
            return true;
        }
        result = default;
        return false;
    }

    // Each marker component is one placed instance, so the count is the component count.
    private static void CountMarkers(TerrainTile tile, out int decalCount, out int propCount)
    {
        decalCount = tile.GetComponentsInChildren<TerrainDecalMarker>(true).Length;
        propCount = tile.GetComponentsInChildren<TerrainScatterMarker>(true).Length;
    }
}
