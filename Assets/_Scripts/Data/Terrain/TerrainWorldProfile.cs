using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[CreateAssetMenu(menuName = "Stitch Punk/Terrain/World Profile", fileName = "TerrainWorldProfile")]
public class TerrainWorldProfile : ScriptableObject
{
    public float tileSizeMetres = 100f;

    [Tooltip("Height samples along one tile edge; tileSizeMetres / (this - 1) is the sample spacing.")]
    public int heightSamplesPerSide = 101;

    [Tooltip("Splat mask resolution per tile. A hero tile may override to 512.")]
    public int defaultMaskResolution = 256;

    public float navCellSizeMetres = 1f;

    public int worldTilesX = 30;
    public int worldTilesZ = 30;

    [Tooltip("Lowest tile coordinate. Tiles run -15..+14 on both axes; tile (0,0) is the camp.")]
    public Vector2Int worldOriginTile = new Vector2Int(-15, -15);

    public int loadRadiusTiles = 1;

    [Tooltip("Keep this larger than loadRadiusTiles so tiles do not thrash at the boundary.")]
    public int unloadRadiusTiles = 2;

    public int maxTileLoadsPerFrame = 1;

    public string groundLayerName = "Ground";

    public Material tileMaterialTemplate;

    public Vector2Int WorldMaxTile => worldOriginTile + new Vector2Int(worldTilesX - 1, worldTilesZ - 1);

    public bool ContainsTile(Vector2Int coordinate)
    {
        Vector2Int maxTile = WorldMaxTile;
        return coordinate.x >= worldOriginTile.x && coordinate.x <= maxTile.x
            && coordinate.y >= worldOriginTile.y && coordinate.y <= maxTile.y;
    }

#if UNITY_EDITOR
    public static TerrainWorldProfile FindInProject()
    {
        string[] guids = AssetDatabase.FindAssets("t:TerrainWorldProfile");
        if (guids.Length == 0)
        {
            return null;
        }
        string path = AssetDatabase.GUIDToAssetPath(guids[0]);
        return AssetDatabase.LoadAssetAtPath<TerrainWorldProfile>(path);
    }
#endif
}
