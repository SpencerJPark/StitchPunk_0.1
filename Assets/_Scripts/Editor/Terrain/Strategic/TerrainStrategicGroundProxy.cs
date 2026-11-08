#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// The far-view layer is drawn with the real ground material (same shader, same layer textures, same lighting), so zoomed out looks exactly
// like zoomed in. Only the splat map differs: the tiles' signed-distance splats stitched into one texture per super-chunk.
public static class TerrainStrategicGroundProxy
{
    private const string GroundShaderName = "Painted/Ground";
    private const string SplatMapProperty = "_SplatMap";
    private const string SplatBoundsProperty = "_SplatBounds";
    private const string StrategicProxyProperty = "_StrategicProxy";

    public static Dictionary<Vector2Int, TerrainTile> CollectTilesOfOpenScene()
    {
        Dictionary<Vector2Int, TerrainTile> tiles = new Dictionary<Vector2Int, TerrainTile>();
        foreach (TerrainTile tile in Object.FindObjectsByType<TerrainTile>(FindObjectsInactive.Include))
        {
            tiles[tile.tileCoordinate] = tile;
        }
        return tiles;
    }

    // The tile nearest the chunk centre that has a real ground material; null when no tile renders with Painted/Ground.
    public static Material FindTemplateGroundMaterial(IReadOnlyDictionary<Vector2Int, TerrainTile> tiles, Vector2 chunkCentreMetres)
    {
        Material best = null;
        float bestDistance = float.MaxValue;
        foreach (KeyValuePair<Vector2Int, TerrainTile> entry in tiles)
        {
            Material material = GroundMaterialOf(entry.Value);
            if (material == null)
            {
                continue;
            }
            float distance = (TerrainTile.CenterOfTile(entry.Key) - chunkCentreMetres).sqrMagnitude;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = material;
            }
        }
        return best;
    }

    private static Material GroundMaterialOf(TerrainTile tile)
    {
        foreach (MeshRenderer meshRenderer in tile.GetComponentsInChildren<MeshRenderer>(true))
        {
            Material material = meshRenderer.sharedMaterial;
            if (material != null && material.shader != null && material.shader.name == GroundShaderName)
            {
                return material;
            }
        }
        return null;
    }

    // Row 0 is the minimum z, like the tile splats, so the ground shader samples it with the same world-to-uv mapping.
    public static Texture2D BuildSplatComposite(IReadOnlyDictionary<Vector2Int, TerrainTile> tiles, Vector2 chunkMinMetres, float chunkSizeMetres, int pixelsPerSide)
    {
        RenderTexture target = RenderTexture.GetTemporary(pixelsPerSide, pixelsPerSide, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        RenderTexture previousActive = RenderTexture.active;
        RenderTexture.active = target;
        GL.PushMatrix();
        GL.LoadPixelMatrix(0f, pixelsPerSide, 0f, pixelsPerSide);
        GL.Clear(true, true, Color.black);

        float pixelsPerMetre = pixelsPerSide / chunkSizeMetres;
        foreach (KeyValuePair<Vector2Int, TerrainTile> entry in tiles)
        {
            Texture2D splat = entry.Value.splatSignedDistanceField;
            if (splat == null)
            {
                continue;
            }
            Vector4 tileRect = TerrainTile.SplatWorldRectOf(entry.Key);
            Rect pixelRect = new Rect(
                (tileRect.x - chunkMinMetres.x) * pixelsPerMetre, (tileRect.y - chunkMinMetres.y) * pixelsPerMetre,
                tileRect.z * pixelsPerMetre, tileRect.w * pixelsPerMetre);
            if (pixelRect.xMax <= 0f || pixelRect.yMax <= 0f || pixelRect.xMin >= pixelsPerSide || pixelRect.yMin >= pixelsPerSide)
            {
                continue;
            }
            Graphics.DrawTexture(pixelRect, splat);
        }
        GL.PopMatrix();

        Texture2D composite = new Texture2D(pixelsPerSide, pixelsPerSide, TextureFormat.RGBA32, true, true);
        composite.ReadPixels(new Rect(0, 0, pixelsPerSide, pixelsPerSide), 0, 0);
        composite.wrapMode = TextureWrapMode.Clamp;
        composite.filterMode = FilterMode.Bilinear;
        composite.Apply(true);

        RenderTexture.active = previousActive;
        RenderTexture.ReleaseTemporary(target);
        return composite;
    }

    public static void ConfigureStandInMaterial(Material standIn, Texture2D splatComposite, Vector2 chunkMinMetres, float chunkSizeMetres)
    {
        standIn.SetTexture(SplatMapProperty, splatComposite);
        standIn.SetVector(SplatBoundsProperty, new Vector4(chunkMinMetres.x, chunkMinMetres.y, chunkSizeMetres, chunkSizeMetres));
        standIn.SetFloat(StrategicProxyProperty, 1f);
    }
}
#endif
