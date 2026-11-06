using UnityEngine;

[CreateAssetMenu(menuName = "Stitch Punk/Terrain/Road Ribbon Style", fileName = "TerrainRoadRibbonStyle")]
public class TerrainRoadRibbonStyle : ScriptableObject
{
    [Tooltip("Material the draped road ribbon mesh renders with.")]
    public Material material;

    [Tooltip("The ribbon is this much narrower than the painted road on each side.")]
    [Min(0f)]
    public float ribbonInsetMetres = 0.4f;

    [Tooltip("Width of the soft fade at each ribbon edge.")]
    [Min(0f)]
    public float fadeBandMetres = 0.5f;

    [Tooltip("Distance between the two rut centrelines.")]
    [Min(0.01f)]
    public float rutSpacingMetres = 1.4f;

    [Tooltip("Width of each rut.")]
    [Min(0.01f)]
    public float rutWidthMetres = 0.5f;

    [Tooltip("How dark the ruts are, 0 = none, 1 = fully dark.")]
    [Range(0f, 1f)]
    public float rutDarkness = 0.5f;

    [Tooltip("Width of the worn band along each ribbon edge.")]
    [Min(0.01f)]
    public float edgeWearWidthMetres = 0.6f;

    [Tooltip("Noise frequency of the edge wear; higher = finer breakup.")]
    [Min(0.01f)]
    public float edgeWearNoiseScale = 0.35f;

    [Tooltip("World metres covered by one texture repeat along the road.")]
    [Min(0.01f)]
    public float textureMetresPerTile = 4f;

    [Tooltip("Height the ribbon sits above the terrain to avoid z-fighting.")]
    [Min(0f)]
    public float verticalLiftMetres = 0.02f;

    [Tooltip("Distance between cross-section rings along the road.")]
    [Min(0.01f)]
    public float ringSpacingMetres = 1f;

    [Tooltip("Fade the ruts out where the road crosses a ford.")]
    public bool fadeRutsOnFord = true;
}
