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

    [Header("Terraces")]
    public float terraceLevelHeightMetres = 3f;
    public int maxTerraceLevels = 3;
    [Tooltip("Rounded outline corner radius in metres")] public float terraceCornerRadiusMetres = 2.5f;
    [Tooltip("Tight rock bevel at a cliff top, metres (TX-D11, owner round 2)")] public float terraceTopRoundMetres = 0.25f;
    [Tooltip("Tight rock bevel at a cliff foot, metres (TX-D11, owner round 2)")] public float terraceFootRoundMetres = 0.45f;
    // Bevels go into the ground mesh and are inked by the ground shader's rim/foot tiles instead of the wall fillets (CE-D2).
    public bool terraceUseTileCliffEdges = false;
    // Profile field so 1.0 / 1.5 / 2.0 can be compared in play (HL-Q1).
    public float hillStepHeightMetres = 0.7f;

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
    // Sculpt strokes call this every frame; the AssetDatabase search is cached until the asset is destroyed.
    static TerrainWorldProfile cachedProjectProfile;

    public static TerrainWorldProfile FindInProject()
    {
        if (cachedProjectProfile != null)
        {
            return cachedProjectProfile;
        }
        string[] guids = AssetDatabase.FindAssets("t:TerrainWorldProfile");
        if (guids.Length == 0)
        {
            return null;
        }
        string path = AssetDatabase.GUIDToAssetPath(guids[0]);
        cachedProjectProfile = AssetDatabase.LoadAssetAtPath<TerrainWorldProfile>(path);
        return cachedProjectProfile;
    }

    public static TerrainWorldProfile FindOrCreateInProject()
    {
        TerrainWorldProfile existingProfile = FindInProject();
        if (existingProfile != null)
        {
            return existingProfile;
        }
        const string folderPath = "Assets/ScriptableObjects/Terrain";
        if (!AssetDatabase.IsValidFolder(folderPath))
        {
            if (!AssetDatabase.IsValidFolder("Assets/ScriptableObjects"))
            {
                AssetDatabase.CreateFolder("Assets", "ScriptableObjects");
            }
            AssetDatabase.CreateFolder("Assets/ScriptableObjects", "Terrain");
        }
        string assetPath = folderPath + "/TerrainWorldProfile.asset";
        TerrainWorldProfile createdProfile = CreateInstance<TerrainWorldProfile>();
        AssetDatabase.CreateAsset(createdProfile, assetPath);
        cachedProjectProfile = createdProfile;
        AssetDatabase.SaveAssets();
        Debug.LogWarning("TerrainWorldProfile: none in the project, created " + assetPath + " with defaults (terraces need it).");
        return createdProfile;
    }
#endif
}
