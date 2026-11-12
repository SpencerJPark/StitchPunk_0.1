#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

public static class TerrainWorldRecipePreview
{
    static readonly Color32 WaterColor = new Color32(70, 98, 120, 255);
    static readonly Color32 LowlandColor = new Color32(104, 128, 84, 255);
    static readonly Color32 UplandColor = new Color32(140, 142, 92, 255);
    static readonly Color32 RockColor = new Color32(128, 118, 108, 255);
    static readonly Color32 PeakColor = new Color32(222, 220, 214, 255);
    static readonly Color32 RoadColor = new Color32(214, 188, 130, 255);
    static readonly Color32 RiverColor = new Color32(96, 150, 196, 255);
    static readonly Color32 LocationColor = new Color32(200, 72, 60, 255);
    static readonly Color32 CampColor = new Color32(250, 250, 250, 255);

    const float WaterBelowMetres = -4f;
    const float LowlandBelowMetres = 4f;
    const float UplandBelowMetres = 12f;
    const float RockBelowMetres = 80f;
    const float ShadeStrengthPerMetreOfSlope = 0.04f;
    const float ShadeMinimum = 0.6f;
    const float ShadeMaximum = 1.4f;
    const int LocationDotRadiusPixels = 3;
    const int CampMarkerRadiusPixels = 4;

    public static TerrainGenerationProfile CreatePreviewProfile()
    {
        TerrainGenerationProfile profile = ScriptableObject.CreateInstance<TerrainGenerationProfile>();
        profile.hideFlags = HideFlags.HideAndDontSave;
        profile.heightLayers = new List<TerrainHeightLayer>
        {
            new TerrainHeightLayer { frequencyPerMetre = 0.006f, amplitudeMetres = 7f, octaves = 3, lacunarity = 2f, gain = 0.45f },
            new TerrainHeightLayer { frequencyPerMetre = 0.02f, amplitudeMetres = 1.2f, octaves = 2, lacunarity = 2f, gain = 0.5f }
        };
        profile.heightRemap = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.45f, 0.1f), new Keyframe(1f, 1f));
        profile.mountainProfile = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        return profile;
    }

    public static void Render(TerrainWorldRecipe recipe, TerrainGenerationProfile profile, int resolution, Color32[] outPixels)
    {
        float worldSpan = recipe.worldHalfExtentMetres * 2f;
        float metresPerPixel = worldSpan / resolution;
        float[] heights = new float[resolution * resolution];
        for (int pixelY = 0; pixelY < resolution; pixelY++)
        {
            for (int pixelX = 0; pixelX < resolution; pixelX++)
            {
                float worldX = PixelToWorld(pixelX, resolution, recipe.worldHalfExtentMetres);
                float worldZ = PixelToWorld(pixelY, resolution, recipe.worldHalfExtentMetres);
                heights[pixelY * resolution + pixelX] = TerrainProceduralGenerator.AnalyticHeightAt(profile, worldX, worldZ);
            }
        }

        for (int pixelY = 0; pixelY < resolution; pixelY++)
        {
            for (int pixelX = 0; pixelX < resolution; pixelX++)
            {
                float height = heights[pixelY * resolution + pixelX];
                float heightEast = heights[pixelY * resolution + Mathf.Min(pixelX + 1, resolution - 1)];
                float heightWest = heights[pixelY * resolution + Mathf.Max(pixelX - 1, 0)];
                float heightNorth = heights[Mathf.Min(pixelY + 1, resolution - 1) * resolution + pixelX];
                float heightSouth = heights[Mathf.Max(pixelY - 1, 0) * resolution + pixelX];
                // light from the north-west: a slope rising toward the west or the north is lit
                float lightedSlope = ((heightWest - heightEast) + (heightNorth - heightSouth)) / (2f * metresPerPixel);
                float shade = Mathf.Clamp(1f + lightedSlope * ShadeStrengthPerMetreOfSlope * metresPerPixel, ShadeMinimum, ShadeMaximum);
                outPixels[pixelY * resolution + pixelX] = Shaded(ColorForHeight(height), shade);
            }
        }

        foreach (List<Vector3> road in recipe.roads) DrawPolyline(road, RoadColor, recipe.worldHalfExtentMetres, resolution, outPixels);
        DrawPolyline(recipe.river, RiverColor, recipe.worldHalfExtentMetres, resolution, outPixels);
        foreach (Vector2 location in recipe.locations)
            DrawDot(WorldToPixel(location.x, recipe.worldHalfExtentMetres, resolution), WorldToPixel(location.y, recipe.worldHalfExtentMetres, resolution), LocationDotRadiusPixels, LocationColor, resolution, outPixels);
        DrawDot(WorldToPixel(0f, recipe.worldHalfExtentMetres, resolution), WorldToPixel(0f, recipe.worldHalfExtentMetres, resolution), CampMarkerRadiusPixels, CampColor, resolution, outPixels);
    }

    public static Texture2D RenderToTexture(int seed, int tilesPerSide, int resolution, Texture2D reuse)
    {
        TerrainGenerationProfile profile = CreatePreviewProfile();
        try
        {
            TerrainWorldRecipe recipe = TerrainWorldRecipeBuilder.Build(seed, tilesPerSide, profile);
            Color32[] pixels = new Color32[resolution * resolution];
            Render(recipe, profile, resolution, pixels);

            Texture2D texture = reuse;
            if (texture == null || texture.width != resolution || texture.height != resolution)
            {
                if (texture != null) Object.DestroyImmediate(texture);
                texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false);
                texture.hideFlags = HideFlags.HideAndDontSave;
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.filterMode = FilterMode.Bilinear;
            }
            texture.SetPixels32(pixels);
            texture.Apply(false);
            return texture;
        }
        finally
        {
            Object.DestroyImmediate(profile);
        }
    }

    static float PixelToWorld(int pixel, int resolution, float halfExtentMetres)
    {
        return ((pixel + 0.5f) / resolution * 2f - 1f) * halfExtentMetres;
    }

    static int WorldToPixel(float world, float halfExtentMetres, int resolution)
    {
        return Mathf.FloorToInt((world / halfExtentMetres * 0.5f + 0.5f) * resolution);
    }

    static Color32 ColorForHeight(float heightMetres)
    {
        if (heightMetres < WaterBelowMetres) return WaterColor;
        if (heightMetres < LowlandBelowMetres) return LowlandColor;
        if (heightMetres < UplandBelowMetres) return UplandColor;
        if (heightMetres < RockBelowMetres) return RockColor;
        return PeakColor;
    }

    static Color32 Shaded(Color32 color, float shade)
    {
        return new Color32(
            (byte)Mathf.Clamp(color.r * shade, 0f, 255f),
            (byte)Mathf.Clamp(color.g * shade, 0f, 255f),
            (byte)Mathf.Clamp(color.b * shade, 0f, 255f),
            255);
    }

    static void DrawPolyline(List<Vector3> points, Color32 color, float halfExtentMetres, int resolution, Color32[] pixels)
    {
        if (points == null) return;
        for (int pointIndex = 1; pointIndex < points.Count; pointIndex++)
        {
            DrawLine(
                WorldToPixel(points[pointIndex - 1].x, halfExtentMetres, resolution), WorldToPixel(points[pointIndex - 1].z, halfExtentMetres, resolution),
                WorldToPixel(points[pointIndex].x, halfExtentMetres, resolution), WorldToPixel(points[pointIndex].z, halfExtentMetres, resolution),
                color, resolution, pixels);
        }
    }

    static void DrawLine(int fromX, int fromY, int toX, int toY, Color32 color, int resolution, Color32[] pixels)
    {
        int deltaX = Mathf.Abs(toX - fromX);
        int deltaY = Mathf.Abs(toY - fromY);
        int stepX = fromX < toX ? 1 : -1;
        int stepY = fromY < toY ? 1 : -1;
        int error = deltaX - deltaY;
        int currentX = fromX;
        int currentY = fromY;
        int guard = deltaX + deltaY + 2;
        while (guard-- > 0)
        {
            SetPixel(currentX, currentY, color, resolution, pixels);
            SetPixel(currentX + 1, currentY, color, resolution, pixels);
            SetPixel(currentX, currentY + 1, color, resolution, pixels);
            SetPixel(currentX + 1, currentY + 1, color, resolution, pixels);
            if (currentX == toX && currentY == toY) break;
            int doubledError = error * 2;
            if (doubledError > -deltaY) { error -= deltaY; currentX += stepX; }
            if (doubledError < deltaX) { error += deltaX; currentY += stepY; }
        }
    }

    static void DrawDot(int centreX, int centreY, int radius, Color32 color, int resolution, Color32[] pixels)
    {
        for (int offsetY = -radius; offsetY <= radius; offsetY++)
            for (int offsetX = -radius; offsetX <= radius; offsetX++)
                if (offsetX * offsetX + offsetY * offsetY <= radius * radius)
                    SetPixel(centreX + offsetX, centreY + offsetY, color, resolution, pixels);
    }

    static void SetPixel(int pixelX, int pixelY, Color32 color, int resolution, Color32[] pixels)
    {
        if (pixelX < 0 || pixelY < 0 || pixelX >= resolution || pixelY >= resolution) return;
        pixels[pixelY * resolution + pixelX] = color;
    }
}
#endif
