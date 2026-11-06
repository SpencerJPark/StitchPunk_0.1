using UnityEngine;

[CreateAssetMenu(menuName = "Stitch Punk/Terrain/Strategic Profile")]
public class TerrainStrategicProfile : ScriptableObject
{
    [Header("Layout")]
    [Min(1)] [Tooltip("Tiles per side of one strategic super-chunk.")]
    public int superChunkTiles = 10;
    [Min(0.5f)] [Tooltip("Metres per cell of the coarse height grid.")]
    public float coarseCellMetres = 4f;
    [Min(0.5f)] [Tooltip("Metres per texel of the baked colour texture.")]
    public float colourTexelMetres = 2f;

    [Header("Distance fade under the detail ring")]
    [Min(0f)] public float fadeStartMetres = 160f;
    [Min(0f)] public float fadeEndMetres = 260f;

    [Header("Baked lighting")]
    [Range(0f, 1f)] public float hillShadeStrength = 0.6f;
    public Vector3 bakedSunEuler = new Vector3(35f, -40f, 0f);
    [Range(0f, 1f)] public float bakedShadowStrength = 0.45f;

    [Header("Canopy and fields")]
    [Range(0f, 1f)] public float canopySpeckleContrast = 0.35f;
    [Min(0.5f)] public float canopySpeckleScaleMetres = 3f;
    [Range(0f, 1f)] public float fieldPatchTintStrength = 0.5f;

    [Header("Linear features")]
    [Min(0.5f)] public float roadWidthMetres = 3f;
    [Min(0.5f)] public float riverWidthMetres = 5f;

    [Header("Buildings")]
    [Min(0.5f)] public float buildingDotSizeMetres = 4f;
    public Color buildingDotColour = new Color(0.55f, 0.42f, 0.34f, 1f);
    public Color buildingDotRoofColour = new Color(0.36f, 0.24f, 0.2f, 1f);

    [Header("Water")]
    public Color waterShallowColour = new Color(0.25f, 0.55f, 0.6f, 1f);
    public Color waterDeepColour = new Color(0.08f, 0.22f, 0.36f, 1f);

    private void OnValidate()
    {
        if (fadeEndMetres < fadeStartMetres + 1f)
        {
            fadeEndMetres = fadeStartMetres + 1f;
        }
    }
}
