using System;
using UnityEngine;

[Serializable]
public abstract class TerrainPlacer
{
}

[Serializable]
public sealed class TerrainFillPlacer : TerrainPlacer
{
    public float densityPer100SquareMetres = 2f;
    public float minSpacingMetres = 2f;
    public Vector2 scaleRange = new Vector2(0.85f, 1.15f);
    public float maxSlopeDegrees = 35f;
    public TerrainCardAtlas cardAtlas;
    [Tooltip("Interactable instances deeper than this many metres inside the area become cards.")]
    public float interiorDemotionMetres = 12f;
    public float clearingNoiseScaleMetres = 60f;
    [Tooltip("0 = no clearings.")]
    public float clearingThreshold01;
}

public enum TerrainAlongPathMode
{
    Points,
    Segments
}

[Serializable]
public sealed class TerrainAlongPathPlacer : TerrainPlacer
{
    // In Segments mode the recipe's placeables are the posts.
    public TerrainAlongPathMode mode;
    public float spacingMetres = 3f;
    public float jitterMetres;
    public float sideOffsetMetres;
    public bool alignToPath = true;
    public GameObject segmentPrefab;
}

[Serializable]
public sealed class TerrainRowsPlacer : TerrainPlacer
{
    public float rowSpacingMetres = 0.6f;
    public float alongRowSpacingMetres = 0.5f;
    public float jitterMetres = 0.1f;
    // NaN = longest outline edge.
    public float rowDirectionDegrees = float.NaN;
}
