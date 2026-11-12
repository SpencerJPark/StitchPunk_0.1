using UnityEngine;

[CreateAssetMenu(menuName = "Stitch Punk/Terrain/Forest Recipe")]
public class TerrainForestRecipe : TerrainAreaRecipe
{
    public TerrainScatterSet scatterSet;
    public TerrainCardAtlas cardAtlas;
    [Tooltip("Trees deeper than this many metres inside the area become cards.")]
    public float interiorDemotionMetres = 12f;
    public float clearingNoiseScaleMetres = 60f;
    [Tooltip("0 = no clearings.")]
    public float clearingThreshold01 = 0f;
}
