using System.Collections.Generic;
using UnityEngine;

public enum TerrainRegionMode
{
    Hand,
    Procedural,
    Mixed
}

// A world-space box that decides how the tiles inside it are made: painted by hand, generated, or both.
public class TerrainRegion : MonoBehaviour
{
    [Tooltip("World coordinates, not relative to this transform")]
    public Vector3 boundsMin = new Vector3(-150f, -50f, -150f);

    [Tooltip("World coordinates, not relative to this transform")]
    public Vector3 boundsMax = new Vector3(150f, 500f, 150f);

    public TerrainRegionMode mode = TerrainRegionMode.Mixed;

    public TerrainGenerationProfile profile;

    [Tooltip("Replaces the tile palette for tiles inside this region when set")]
    public TerrainPalette paletteOverride;

    [Tooltip("Highest priority wins where regions overlap")]
    public int priority;

    public bool ContainsTile(Vector2Int tileCoordinate)
    {
        Vector2 tileCenter = TerrainTile.CenterOfTile(tileCoordinate);
        return tileCenter.x >= boundsMin.x && tileCenter.x <= boundsMax.x
            && tileCenter.y >= boundsMin.z && tileCenter.y <= boundsMax.z;
    }

    public static TerrainRegion FindRegionFor(Vector2Int tileCoordinate)
    {
        TerrainRegion[] regions = Object.FindObjectsByType<TerrainRegion>(FindObjectsInactive.Exclude);
        TerrainRegion best = null;
        for (int index = 0; index < regions.Length; index++)
        {
            TerrainRegion candidate = regions[index];
            if (!candidate.ContainsTile(tileCoordinate))
            {
                continue;
            }
            if (best == null || candidate.priority > best.priority)
            {
                best = candidate;
            }
        }
        return best;
    }

    public IEnumerable<Vector2Int> TilesInside()
    {
        int firstTileX = Mathf.CeilToInt(boundsMin.x / TerrainTile.TileSizeMetres);
        int lastTileX = Mathf.FloorToInt(boundsMax.x / TerrainTile.TileSizeMetres);
        int firstTileZ = Mathf.CeilToInt(boundsMin.z / TerrainTile.TileSizeMetres);
        int lastTileZ = Mathf.FloorToInt(boundsMax.z / TerrainTile.TileSizeMetres);
        for (int tileX = firstTileX; tileX <= lastTileX; tileX++)
        {
            for (int tileZ = firstTileZ; tileZ <= lastTileZ; tileZ++)
            {
                yield return new Vector2Int(tileX, tileZ);
            }
        }
    }

    void OnDrawGizmos()
    {
        DrawBox(0.08f, 0.5f);
    }

    void OnDrawGizmosSelected()
    {
        DrawBox(0.2f, 1f);
    }

    void DrawBox(float fillAlpha, float wireAlpha)
    {
        Color baseColor = ColorForMode(mode);
        Vector3 center = (boundsMin + boundsMax) * 0.5f;
        Vector3 size = boundsMax - boundsMin;
        Gizmos.color = new Color(baseColor.r, baseColor.g, baseColor.b, fillAlpha);
        Gizmos.DrawCube(center, size);
        Gizmos.color = new Color(baseColor.r, baseColor.g, baseColor.b, wireAlpha);
        Gizmos.DrawWireCube(center, size);
    }

    static Color ColorForMode(TerrainRegionMode regionMode)
    {
        switch (regionMode)
        {
            case TerrainRegionMode.Hand:
                return new Color(1f, 0.6f, 0.1f);
            case TerrainRegionMode.Procedural:
                return new Color(0.2f, 0.9f, 0.3f);
            default:
                return new Color(0.1f, 0.9f, 0.95f);
        }
    }
}
