using UnityEngine;

// One 100 x 100 m ground tile on the world grid. Tile (x, z) is centred on (x * 100, z * 100),
// so tile (0, 0) is the camp-centred square from -50 to 50. Streaming loads and unloads whole tiles.
public class TerrainTile : MonoBehaviour
{
    public const float TileSizeMetres = 100f;

    [Tooltip("x = world X cell, y = world Z cell")]
    public Vector2Int tileCoordinate;

    [Tooltip("Hard RGB mask that is painted: R grass, G forest, B stone, black dirt")]
    public Texture2D splatMask;

    [Tooltip("Signed-distance encode of the mask; this is what the ground material samples")]
    public Texture2D splatSignedDistanceField;

    public static Vector2 CenterOfTile(Vector2Int coordinate)
    {
        return new Vector2(coordinate.x * TileSizeMetres, coordinate.y * TileSizeMetres);
    }

    public static Vector2Int CoordinateContaining(Vector3 worldPosition)
    {
        return new Vector2Int(
            Mathf.RoundToInt(worldPosition.x / TileSizeMetres),
            Mathf.RoundToInt(worldPosition.z / TerrainTile.TileSizeMetres));
    }

    // (minX, minZ, sizeX, sizeZ) as the ground material's _SplatBounds expects
    public static Vector4 SplatWorldRectOf(Vector2Int coordinate)
    {
        Vector2 center = CenterOfTile(coordinate);
        float halfSize = TileSizeMetres * 0.5f;
        return new Vector4(center.x - halfSize, center.y - halfSize, TileSizeMetres, TileSizeMetres);
    }
}
