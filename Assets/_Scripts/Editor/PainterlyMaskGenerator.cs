using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Generates the placeholder painterly RGB mask texture: three independent
/// grayscale brush-stroke layers packed into the R, G and B channels
/// (the single-texture setup described in _Vault/Tasks/Materials/Transcript.md).
/// Strokes wrap at the edges so the mask tiles. Replace with a hand-painted
/// Affinity texture later — the shader nodes don't care where it came from.
/// </summary>
public static class PainterlyMaskGenerator
{
    private const int TextureSize = 1024;
    private const int StrokesPerChannel = 650;
    // Low-frequency flatten: kills the per-tile brightness "signature" that
    // makes repeat tiling obvious, while keeping per-stroke value variation.
    private const int FlattenBlurRadius = 48;
    private const float FlattenGain = 1.15f;
    private const string OutputDirectory = "Assets/Textures/Painterly";
    private const string OutputPath = OutputDirectory + "/T_PainterlyMask.png";

    [MenuItem("Stitch Punk/Generate Painterly Mask Texture")]
    public static void GenerateMaskTexture()
    {
        float[] redChannel = PaintStrokeChannel(101);
        float[] greenChannel = PaintStrokeChannel(202);
        float[] blueChannel = PaintStrokeChannel(303);

        Color32[] pixels = new Color32[TextureSize * TextureSize];
        for (int pixelIndex = 0; pixelIndex < pixels.Length; pixelIndex++)
        {
            byte redByte = (byte)Mathf.RoundToInt(Mathf.Clamp01(redChannel[pixelIndex]) * 255f);
            byte greenByte = (byte)Mathf.RoundToInt(Mathf.Clamp01(greenChannel[pixelIndex]) * 255f);
            byte blueByte = (byte)Mathf.RoundToInt(Mathf.Clamp01(blueChannel[pixelIndex]) * 255f);
            pixels[pixelIndex] = new Color32(redByte, greenByte, blueByte, 255);
        }

        Texture2D maskTexture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGB24, false, true);
        maskTexture.SetPixels32(pixels);
        maskTexture.Apply();
        byte[] pngBytes = maskTexture.EncodeToPNG();
        Object.DestroyImmediate(maskTexture);

        if (!Directory.Exists(OutputDirectory))
        {
            Directory.CreateDirectory(OutputDirectory);
        }
        File.WriteAllBytes(OutputPath, pngBytes);
        AssetDatabase.ImportAsset(OutputPath);

        TextureImporter maskImporter = (TextureImporter)AssetImporter.GetAtPath(OutputPath);
        // The mask is grayscale stroke DATA, not color — keep it linear so the
        // ramp nodes see the painted values unchanged.
        maskImporter.sRGBTexture = false;
        maskImporter.wrapMode = TextureWrapMode.Repeat;
        maskImporter.mipmapEnabled = true;
        maskImporter.SaveAndReimport();

        Debug.Log("Painterly mask generated at " + OutputPath);
    }

    private static float[] PaintStrokeChannel(int randomSeed)
    {
        System.Random random = new System.Random(randomSeed);
        float[] channelValues = new float[TextureSize * TextureSize];
        for (int pixelIndex = 0; pixelIndex < channelValues.Length; pixelIndex++)
        {
            channelValues[pixelIndex] = 0.5f;
        }

        for (int strokeIndex = 0; strokeIndex < StrokesPerChannel; strokeIndex++)
        {
            float positionX = (float)random.NextDouble() * TextureSize;
            float positionY = (float)random.NextDouble() * TextureSize;
            float direction = (float)random.NextDouble() * Mathf.PI * 2f;
            float strokeLength = Mathf.Lerp(60f, 260f, (float)random.NextDouble());
            float strokeRadius = Mathf.Lerp(6f, 22f, (float)random.NextDouble());
            // The value variation between strokes is what the color ramp feeds
            // on — spread it across almost the whole 0..1 range.
            float strokeValue = Mathf.Lerp(0.08f, 0.95f, (float)random.NextDouble());
            float curvaturePerPixel = ((float)random.NextDouble() - 0.5f) * 0.02f;

            float stepSize = Mathf.Max(strokeRadius * 0.5f, 1f);
            int stepCount = Mathf.CeilToInt(strokeLength / stepSize);
            for (int stepIndex = 0; stepIndex < stepCount; stepIndex++)
            {
                StampSoftDisc(channelValues, positionX, positionY, strokeRadius, strokeValue);
                direction += curvaturePerPixel * stepSize;
                positionX += Mathf.Cos(direction) * stepSize;
                positionY += Mathf.Sin(direction) * stepSize;
            }
        }

        FlattenLowFrequency(channelValues);
        return channelValues;
    }

    /// <summary>
    /// Removes the low-frequency brightness signature: subtracts a wrapped box
    /// blur of the channel and re-centers around mid gray, so no region of the
    /// tile is recognizably brighter/darker than another when the mask repeats.
    /// </summary>
    private static void FlattenLowFrequency(float[] channelValues)
    {
        float[] blurred = new float[channelValues.Length];
        float[] scratch = new float[channelValues.Length];
        int windowSize = FlattenBlurRadius * 2 + 1;

        for (int pixelY = 0; pixelY < TextureSize; pixelY++)
        {
            int rowStart = pixelY * TextureSize;
            float runningSum = 0f;
            for (int offset = -FlattenBlurRadius; offset <= FlattenBlurRadius; offset++)
            {
                runningSum += channelValues[rowStart + ((offset + TextureSize) % TextureSize)];
            }
            for (int pixelX = 0; pixelX < TextureSize; pixelX++)
            {
                scratch[rowStart + pixelX] = runningSum / windowSize;
                int leavingX = (pixelX - FlattenBlurRadius + TextureSize) % TextureSize;
                int enteringX = (pixelX + FlattenBlurRadius + 1) % TextureSize;
                runningSum += channelValues[rowStart + enteringX] - channelValues[rowStart + leavingX];
            }
        }

        for (int pixelX = 0; pixelX < TextureSize; pixelX++)
        {
            float runningSum = 0f;
            for (int offset = -FlattenBlurRadius; offset <= FlattenBlurRadius; offset++)
            {
                runningSum += scratch[((offset + TextureSize) % TextureSize) * TextureSize + pixelX];
            }
            for (int pixelY = 0; pixelY < TextureSize; pixelY++)
            {
                blurred[pixelY * TextureSize + pixelX] = runningSum / windowSize;
                int leavingY = (pixelY - FlattenBlurRadius + TextureSize) % TextureSize;
                int enteringY = (pixelY + FlattenBlurRadius + 1) % TextureSize;
                runningSum += scratch[enteringY * TextureSize + pixelX] - scratch[leavingY * TextureSize + pixelX];
            }
        }

        for (int pixelIndex = 0; pixelIndex < channelValues.Length; pixelIndex++)
        {
            channelValues[pixelIndex] = Mathf.Clamp01(0.5f + (channelValues[pixelIndex] - blurred[pixelIndex]) * FlattenGain);
        }
    }

    private static void StampSoftDisc(float[] channelValues, float centerX, float centerY, float radius, float strokeValue)
    {
        int minPixelX = Mathf.FloorToInt(centerX - radius);
        int maxPixelX = Mathf.CeilToInt(centerX + radius);
        int minPixelY = Mathf.FloorToInt(centerY - radius);
        int maxPixelY = Mathf.CeilToInt(centerY + radius);

        for (int pixelY = minPixelY; pixelY <= maxPixelY; pixelY++)
        {
            for (int pixelX = minPixelX; pixelX <= maxPixelX; pixelX++)
            {
                float offsetX = pixelX - centerX;
                float offsetY = pixelY - centerY;
                float distance = Mathf.Sqrt(offsetX * offsetX + offsetY * offsetY);
                float falloff = Mathf.Clamp01(1f - distance / radius);
                float smoothFalloff = falloff * falloff * (3f - 2f * falloff);
                if (smoothFalloff <= 0f)
                {
                    continue;
                }

                // Wrap so strokes crossing an edge continue on the other side
                // and the mask stays tileable.
                int wrappedX = ((pixelX % TextureSize) + TextureSize) % TextureSize;
                int wrappedY = ((pixelY % TextureSize) + TextureSize) % TextureSize;
                int wrappedIndex = wrappedY * TextureSize + wrappedX;

                float blendStrength = smoothFalloff * 0.85f;
                channelValues[wrappedIndex] = Mathf.Lerp(channelValues[wrappedIndex], strokeValue, blendStrength);
            }
        }
    }
}
