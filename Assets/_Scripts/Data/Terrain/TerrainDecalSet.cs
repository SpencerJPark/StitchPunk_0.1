using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public struct TerrainDecalEntry
{
    public string label;
    public int atlasTile;
    public bool multiplyBlend;
    public Vector2 sizeRangeMetres;
    public float onDirt;
    public float onGrass;
    public float onForest;
    public float onStone;
    public float maxSlopeDegrees;
    public Vector2 opacityRange;
    public float densityPer100SquareMetres;
    public Color tint;
}

[CreateAssetMenu(menuName = "Stitch Punk/Terrain/Decal Set", fileName = "TerrainDecalSet")]
public class TerrainDecalSet : ScriptableObject
{
    public Texture2D atlas;
    public Vector2Int atlasGrid = new Vector2Int(4, 2);
    public Material multiplyMaterial;
    public Material normalMaterial;
    public List<TerrainDecalEntry> entries = new List<TerrainDecalEntry>();

    public int AtlasTileCount => Mathf.Max(1, atlasGrid.x * atlasGrid.y);

    public static TerrainDecalEntry CreateEntry(string label, int atlasTile, bool multiplyBlend, float minSize, float maxSize, float onDirt, float onGrass, float onForest, float onStone, float densityPer100SquareMetres)
    {
        TerrainDecalEntry entry = new TerrainDecalEntry();
        entry.label = label;
        entry.atlasTile = atlasTile;
        entry.multiplyBlend = multiplyBlend;
        entry.sizeRangeMetres = new Vector2(minSize, maxSize);
        entry.onDirt = onDirt;
        entry.onGrass = onGrass;
        entry.onForest = onForest;
        entry.onStone = onStone;
        entry.maxSlopeDegrees = 40f;
        entry.opacityRange = new Vector2(0.7f, 1f);
        entry.densityPer100SquareMetres = densityPer100SquareMetres;
        entry.tint = Color.white;
        return entry;
    }

    // Mirrors the rows that were hard-coded in PaintedGround DecalScatter; density sums to about 5 per 100 m^2.
    public void FillWithDefaultRules()
    {
        entries.Clear();
        entries.Add(CreateEntry("Crack", 0, true, 1.6f, 3.2f, 0.6f, 0.05f, 0.0f, 1.4f, 0.6f));
        entries.Add(CreateEntry("Crack 2", 1, true, 1.6f, 3.2f, 0.6f, 0.05f, 0.0f, 1.4f, 0.6f));
        entries.Add(CreateEntry("Stain", 2, true, 1.5f, 3.5f, 1.0f, 0.35f, 0.6f, 0.5f, 0.8f));
        entries.Add(CreateEntry("Puddle", 4, false, 1.2f, 2.4f, 0.7f, 0.10f, 0.2f, 0.2f, 0.5f));
        entries.Add(CreateEntry("Leaves", 5, false, 1.8f, 3.2f, 0.2f, 0.30f, 1.6f, 0.1f, 0.9f));
        entries.Add(CreateEntry("Pebbles", 6, false, 1.2f, 2.2f, 0.8f, 0.15f, 0.1f, 1.0f, 0.9f));
        entries.Add(CreateEntry("Moss", 7, false, 1.4f, 2.8f, 0.2f, 0.50f, 1.0f, 0.5f, 0.7f));
    }
}
