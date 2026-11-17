using System;
using System.Collections.Generic;
using UnityEngine;

public enum TerrainFeatureShapeKind
{
    Curve,
    Straight,
    Box,
    Circle,
    Polygon
}

[Flags]
public enum TerrainFeatureShapeSupport
{
    OpenPath = 1,
    ClosedLoop = 2,
    ClosedArea = 4
}

public enum TerrainFeatureCategory
{
    Areas,
    LinesAndFences,
    GroundCover
}

[Serializable]
public struct TerrainPlaceable
{
    public GameObject prefab;
    [Min(0f)] public float weight;
    public TerrainScatterTier tier;
    // Interim card data until TerrainCardSource exists; a Dressing placeable may have no prefab.
    public int cardAtlasEntry;
    public Vector2 cardSizeMetres;
    public bool blocksNavigation;
}

[CreateAssetMenu(menuName = "Stitch Punk/Terrain/Feature Recipe")]
public class TerrainFeatureRecipe : TerrainAreaRecipe
{
    public string displayName;
    public Texture2D thumbnail;
    public TerrainFeatureCategory category;
    public TerrainFeatureShapeSupport supportedShapes = TerrainFeatureShapeSupport.ClosedArea;
    public TerrainFeatureShapeKind defaultShape = TerrainFeatureShapeKind.Curve;
    [SerializeReference] public TerrainPlacer placer;
    public List<TerrainPlaceable> placeables = new List<TerrainPlaceable>();
}
