using UnityEngine;

[System.Serializable]
public struct TerrainPaletteLayer
{
    public string displayName;
    public Texture2D layerTexture;
    public float tileSizeMetres;
    [Tooltip("Authoring colour; also the strategic-view colour later.")]
    public Color tint;
    public byte navCostByte;
    public float footstepSoundId;
}

[CreateAssetMenu(menuName = "Stitch Punk/Terrain/Palette", fileName = "TerrainPalette")]
public class TerrainPalette : ScriptableObject
{
    public TerrainPaletteLayer dirt = CreateDefaultLayer("Dirt", 12f);
    public TerrainPaletteLayer grass = CreateDefaultLayer("Grass", 10f);
    public TerrainPaletteLayer forest = CreateDefaultLayer("Forest", 14f);
    public TerrainPaletteLayer stone = CreateDefaultLayer("Stone", 12f);

    [Header("Edge fringe")]
    public Texture2D edgeFringeStrip;
    public float fringeSizeMetres = 3f;
    public float fringeStretch = 1f;

    [Header("Cliffs")]
    public Texture2D cliffTexture;
    public float cliffTileSizeMetres = 6f;

    static TerrainPaletteLayer CreateDefaultLayer(string layerDisplayName, float defaultTileSizeMetres)
    {
        TerrainPaletteLayer layer = new TerrainPaletteLayer();
        layer.displayName = layerDisplayName;
        layer.tileSizeMetres = defaultTileSizeMetres;
        layer.tint = Color.white;
        layer.navCostByte = 1;
        return layer;
    }

    // Index order matches the editor's TerrainSplatLayer enum.
    public TerrainPaletteLayer GetLayer(int layerIndex)
    {
        switch (layerIndex)
        {
            case 1: return grass;
            case 2: return forest;
            case 3: return stone;
            default: return dirt;
        }
    }

    public void ApplyToMaterial(Material groundMaterial)
    {
        if (groundMaterial == null)
            return;

        SetTextureIfAssigned(groundMaterial, "_BaseTex", dirt.layerTexture);
        SetTextureIfAssigned(groundMaterial, "_LayerRTex", grass.layerTexture);
        SetTextureIfAssigned(groundMaterial, "_LayerGTex", forest.layerTexture);
        SetTextureIfAssigned(groundMaterial, "_LayerBTex", stone.layerTexture);
        SetTextureIfAssigned(groundMaterial, "_FringeTex", edgeFringeStrip);
        SetTextureIfAssigned(groundMaterial, "_CliffTex", cliffTexture);

        if (groundMaterial.HasProperty("_TileSizes"))
        {
            groundMaterial.SetVector("_TileSizes", new Vector4(
                dirt.tileSizeMetres, grass.tileSizeMetres, forest.tileSizeMetres, stone.tileSizeMetres));
        }
        if (groundMaterial.HasProperty("_FringeSize"))
            groundMaterial.SetFloat("_FringeSize", fringeSizeMetres);
        if (groundMaterial.HasProperty("_FringeStretch"))
            groundMaterial.SetFloat("_FringeStretch", fringeStretch);
        if (groundMaterial.HasProperty("_CliffTileSize"))
            groundMaterial.SetFloat("_CliffTileSize", cliffTileSizeMetres);
    }

    static void SetTextureIfAssigned(Material groundMaterial, string propertyName, Texture2D texture)
    {
        if (texture != null && groundMaterial.HasProperty(propertyName))
            groundMaterial.SetTexture(propertyName, texture);
    }
}
