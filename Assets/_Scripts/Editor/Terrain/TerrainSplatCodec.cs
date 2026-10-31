#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

// Pixel work for a tile's splat mask. The mask is the source of truth; the signed-distance
// field is derived from it and is what the ground material actually samples.
public static class TerrainSplatCodec
{
    public const int FallbackMaskResolution = 256;

    // Must stay below the ground material's fringe width; matches PaintedGround's SplatBaker.SdfRange
    public const float SignedDistanceRangeMetres = 4f;

    public static Color32 ColourOfLayer(TerrainSplatLayer layer)
    {
        switch (layer)
        {
            case TerrainSplatLayer.Grass: return new Color32(255, 0, 0, 255);
            case TerrainSplatLayer.Forest: return new Color32(0, 255, 0, 255);
            case TerrainSplatLayer.Stone: return new Color32(0, 0, 255, 255);
            default: return new Color32(0, 0, 0, 255);
        }
    }

    public static Color32[] CreateFilledMask(int resolution, TerrainSplatLayer layer)
    {
        Color32 fillColour = ColourOfLayer(layer);
        Color32[] pixels = new Color32[resolution * resolution];
        for (int pixelIndex = 0; pixelIndex < pixels.Length; pixelIndex++)
            pixels[pixelIndex] = fillColour;
        return pixels;
    }

    // Byte-exact PNG decode that ignores import settings (see Editor.md, reading source texture pixels)
    public static Color32[] ReadPng(string assetPath, out int resolution)
    {
        Texture2D decoded = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
        decoded.LoadImage(File.ReadAllBytes(assetPath));
        resolution = decoded.width;
        Color32[] pixels = decoded.GetPixels32();
        Object.DestroyImmediate(decoded);
        return pixels;
    }

    // Overwrites in place so the texture GUID survives; import settings are only stamped on first creation
    public static void WritePng(string assetPath, Color32[] pixels, int resolution, bool isSignedDistanceField)
    {
        bool existedBefore = File.Exists(assetPath);
        Texture2D encoded = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false, true);
        encoded.SetPixels32(pixels);
        encoded.Apply();
        File.WriteAllBytes(assetPath, encoded.EncodeToPNG());
        Object.DestroyImmediate(encoded);
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

        if (existedBefore) return;
        TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null) return;
        if (isSignedDistanceField)
        {
            StampSignedDistanceFieldImportSettings(assetPath, resolution);
            return;
        }

        importer.sRGBTexture = false;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Point;
        importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
    }

    // BC7 measured about 0.3 m off the true distance; DXT1/DXT5 were 1.3 m off, so never use those for the SDF.
    public static void StampSignedDistanceFieldImportSettings(string assetPath, int resolution)
    {
        TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null) return;
        importer.sRGBTexture = false;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Bilinear;
        importer.mipmapEnabled = false;
        importer.isReadable = false;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.textureCompression = TextureImporterCompression.Compressed;
        importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings
        {
            name = "DefaultTexturePlatform",
            overridden = true,
            maxTextureSize = Mathf.Max(resolution, 32),
            format = TextureImporterFormat.BC7,
            textureCompression = TextureImporterCompression.Compressed,
            compressionQuality = 100
        });
        importer.SaveAndReimport();
    }

    // Per channel: threshold at 0.5, take the exact distance to the border on both sides, and store
    // 0.5 + signedMetres / (2 * range). 0.5 is the painted border, 1.0 is range metres or more inside.
    public static Color32[] EncodeSignedDistanceField(Color32[] mask, int resolution, float tileSizeMetres)
    {
        float texelSizeMetres = tileSizeMetres / resolution;
        return EncodeSignedDistanceFieldWindow(mask, resolution, new RectInt(0, 0, resolution, resolution), texelSizeMetres);
    }

    // Re-encodes only the texels an edit can change; sdfInOut must hold the SDF of the mask before the edit.
    // Texels further than the range from the edit saturate, so a window padded by the range twice is exact.
    public static void EncodeSignedDistanceFieldRegion(Color32[] mask, int resolution, float tileSizeMetres, RectInt dirtyTexels, Color32[] sdfInOut)
    {
        if (dirtyTexels.width <= 0 || dirtyTexels.height <= 0) return;

        float texelSizeMetres = tileSizeMetres / resolution;
        int padTexels = Mathf.CeilToInt(SignedDistanceRangeMetres / texelSizeMetres) + 2;
        RectInt outputRect = PaddedAndClamped(dirtyTexels, padTexels, resolution);
        if (outputRect.width <= 0 || outputRect.height <= 0) return;
        RectInt windowRect = PaddedAndClamped(outputRect, padTexels, resolution);

        Color32[] windowEncoded = EncodeSignedDistanceFieldWindow(mask, resolution, windowRect, texelSizeMetres);
        for (int row = outputRect.yMin; row < outputRect.yMax; row++)
        {
            for (int column = outputRect.xMin; column < outputRect.xMax; column++)
            {
                int windowIndex = (row - windowRect.yMin) * windowRect.width + (column - windowRect.xMin);
                sdfInOut[row * resolution + column] = windowEncoded[windowIndex];
            }
        }
    }

    static RectInt PaddedAndClamped(RectInt rect, int padTexels, int resolution)
    {
        int minX = Mathf.Max(rect.xMin - padTexels, 0);
        int minY = Mathf.Max(rect.yMin - padTexels, 0);
        int maxX = Mathf.Min(rect.xMax + padTexels, resolution);
        int maxY = Mathf.Min(rect.yMax + padTexels, resolution);
        return new RectInt(minX, minY, maxX - minX, maxY - minY);
    }

    static Color32[] EncodeSignedDistanceFieldWindow(Color32[] mask, int resolution, RectInt window, float texelSizeMetres)
    {
        int width = window.width;
        int height = window.height;
        int texelCount = width * height;
        Color32[] encoded = new Color32[texelCount];
        bool[] isInside = new bool[texelCount];
        bool[] isOutside = new bool[texelCount];

        for (int channel = 0; channel < 3; channel++)
        {
            for (int row = 0; row < height; row++)
            {
                for (int column = 0; column < width; column++)
                {
                    Color32 maskTexel = mask[(window.yMin + row) * resolution + window.xMin + column];
                    byte channelValue = channel == 0 ? maskTexel.r : channel == 1 ? maskTexel.g : maskTexel.b;
                    bool insideChannel = channelValue > 127;
                    int texelIndex = row * width + column;
                    isInside[texelIndex] = insideChannel;
                    isOutside[texelIndex] = !insideChannel;
                }
            }

            float[] distanceFromInsideToBorder = DistanceToNearestFalse(isInside, width, height);
            float[] distanceFromOutsideToBorder = DistanceToNearestFalse(isOutside, width, height);

            for (int texelIndex = 0; texelIndex < texelCount; texelIndex++)
            {
                float signedTexels = isInside[texelIndex]
                    ? distanceFromInsideToBorder[texelIndex]
                    : -distanceFromOutsideToBorder[texelIndex];
                float stored = Mathf.Clamp01(0.5f + signedTexels * texelSizeMetres / (2f * SignedDistanceRangeMetres));
                byte storedByte = (byte)Mathf.RoundToInt(stored * 255f);

                Color32 encodedTexel = encoded[texelIndex];
                if (channel == 0) encodedTexel.r = storedByte;
                else if (channel == 1) encodedTexel.g = storedByte;
                else encodedTexel.b = storedByte;
                encodedTexel.a = 255;
                encoded[texelIndex] = encodedTexel;
            }
        }

        return encoded;
    }

    // Exact Euclidean distance (in texels) from every true texel to the nearest false texel
    static float[] DistanceToNearestFalse(bool[] isTrue, int width, int height)
    {
        const float Unreachable = 1e10f;
        int longestSide = Mathf.Max(width, height);
        float[] baseCost = new float[longestSide];
        float[] squaredDistance = new float[longestSide];
        int[] parabolaVertices = new int[longestSide];
        float[] parabolaBoundaries = new float[longestSide + 1];

        float[] rowPass = new float[width * height];
        for (int row = 0; row < height; row++)
        {
            int rowStart = row * width;
            for (int column = 0; column < width; column++)
                baseCost[column] = isTrue[rowStart + column] ? Unreachable : 0f;
            SquaredDistanceTransform1D(baseCost, width, squaredDistance, parabolaVertices, parabolaBoundaries);
            for (int column = 0; column < width; column++)
                rowPass[rowStart + column] = squaredDistance[column];
        }

        float[] distance = new float[width * height];
        for (int column = 0; column < width; column++)
        {
            for (int row = 0; row < height; row++)
                baseCost[row] = rowPass[row * width + column];
            SquaredDistanceTransform1D(baseCost, height, squaredDistance, parabolaVertices, parabolaBoundaries);
            for (int row = 0; row < height; row++)
                distance[row * width + column] = Mathf.Sqrt(squaredDistance[row]);
        }

        return distance;
    }

    // Felzenszwalb and Huttenlocher lower envelope of parabolas
    static void SquaredDistanceTransform1D(float[] baseCost, int sampleCount, float[] squaredDistance, int[] parabolaVertices, float[] parabolaBoundaries)
    {
        const float Unreachable = 1e10f;
        int envelopeIndex = 0;
        parabolaVertices[0] = 0;
        parabolaBoundaries[0] = -Unreachable;
        parabolaBoundaries[1] = Unreachable;

        for (int sample = 1; sample < sampleCount; sample++)
        {
            float intersection;
            while (true)
            {
                int lastVertex = parabolaVertices[envelopeIndex];
                intersection = ((baseCost[sample] + (float)sample * sample) - (baseCost[lastVertex] + (float)lastVertex * lastVertex)) / (2f * sample - 2f * lastVertex);
                if (intersection <= parabolaBoundaries[envelopeIndex]) envelopeIndex--;
                else break;
            }
            envelopeIndex++;
            parabolaVertices[envelopeIndex] = sample;
            parabolaBoundaries[envelopeIndex] = intersection;
            parabolaBoundaries[envelopeIndex + 1] = Unreachable;
        }

        envelopeIndex = 0;
        for (int sample = 0; sample < sampleCount; sample++)
        {
            while (parabolaBoundaries[envelopeIndex + 1] < sample) envelopeIndex++;
            int vertex = parabolaVertices[envelopeIndex];
            float offset = sample - vertex;
            squaredDistance[sample] = offset * offset + baseCost[vertex];
        }
    }
}
#endif
