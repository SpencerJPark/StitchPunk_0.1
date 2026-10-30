#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

public enum TerrainSplatLayer
{
    Dirt,
    Grass,
    Forest,
    Stone
}

// Pixel work for a tile's splat mask. The mask is the source of truth; the signed-distance
// field is derived from it and is what the ground material actually samples.
public static class TerrainSplatCodec
{
    public const int MaskResolution = 512;

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
        importer.sRGBTexture = false;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = isSignedDistanceField ? FilterMode.Bilinear : FilterMode.Point;
        importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
    }

    // Per channel: threshold at 0.5, take the exact distance to the border on both sides, and store
    // 0.5 + signedMetres / (2 * range). 0.5 is the painted border, 1.0 is range metres or more inside.
    public static Color32[] EncodeSignedDistanceField(Color32[] mask, int resolution, float tileSizeMetres)
    {
        int texelCount = resolution * resolution;
        float texelSizeMetres = tileSizeMetres / resolution;
        Color32[] encoded = new Color32[texelCount];
        bool[] isInside = new bool[texelCount];
        bool[] isOutside = new bool[texelCount];

        for (int channel = 0; channel < 3; channel++)
        {
            for (int texelIndex = 0; texelIndex < texelCount; texelIndex++)
            {
                Color32 maskTexel = mask[texelIndex];
                byte channelValue = channel == 0 ? maskTexel.r : channel == 1 ? maskTexel.g : maskTexel.b;
                bool insideChannel = channelValue > 127;
                isInside[texelIndex] = insideChannel;
                isOutside[texelIndex] = !insideChannel;
            }

            float[] distanceFromInsideToBorder = DistanceToNearestFalse(isInside, resolution);
            float[] distanceFromOutsideToBorder = DistanceToNearestFalse(isOutside, resolution);

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
    static float[] DistanceToNearestFalse(bool[] isTrue, int resolution)
    {
        const float Unreachable = 1e10f;
        float[] baseCost = new float[resolution];
        float[] squaredDistance = new float[resolution];
        int[] parabolaVertices = new int[resolution];
        float[] parabolaBoundaries = new float[resolution + 1];

        float[] rowPass = new float[resolution * resolution];
        for (int row = 0; row < resolution; row++)
        {
            int rowStart = row * resolution;
            for (int column = 0; column < resolution; column++)
                baseCost[column] = isTrue[rowStart + column] ? Unreachable : 0f;
            SquaredDistanceTransform1D(baseCost, resolution, squaredDistance, parabolaVertices, parabolaBoundaries);
            for (int column = 0; column < resolution; column++)
                rowPass[rowStart + column] = squaredDistance[column];
        }

        float[] distance = new float[resolution * resolution];
        for (int column = 0; column < resolution; column++)
        {
            for (int row = 0; row < resolution; row++)
                baseCost[row] = rowPass[row * resolution + column];
            SquaredDistanceTransform1D(baseCost, resolution, squaredDistance, parabolaVertices, parabolaBoundaries);
            for (int row = 0; row < resolution; row++)
                distance[row * resolution + column] = Mathf.Sqrt(squaredDistance[row]);
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
