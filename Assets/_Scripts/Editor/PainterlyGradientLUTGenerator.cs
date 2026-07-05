using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Bakes a PainterlyGradientLUTSO's list of gradients into the 64x64 palette atlas
/// T_PainterlyGradientLUT.png — the "uber colour map" the PainterlyGradientMap node samples
/// by mesh UV. Each gradient becomes a horizontal band (equal share of the 64 rows, so a zone
/// spans several rows for UV-mapping tolerance), sampled left-to-right across the 64 columns.
///
/// Gradient index 0 is the TOP band (highest UV.y) so the inspector list reads top-to-bottom
/// like the texture. A row-index reference sheet (which UV.y band = which gradient) is written
/// next to the PNG so UV-mapping a part to a colour is lookup, not guesswork.
///
/// Select a PainterlyGradientLUTSO (or have exactly one in the project) and run
/// Stitch Punk ▸ Generate Painterly Gradient LUT. The texture is overwritten in place, so its
/// GUID, import settings, and every material reference are preserved.
/// </summary>
public static class PainterlyGradientLUTGenerator
{
    private const int TextureSize = 64;
    private const string OutputDirectory = "Assets/Textures/Painterly";
    private const string OutputPath = OutputDirectory + "/T_PainterlyGradientLUT.png";
    private const string ReferenceSheetPath = OutputDirectory + "/T_PainterlyGradientLUT_rows.txt";

    [MenuItem("Stitch Punk/Generate Painterly Gradient LUT")]
    public static void GenerateGradientLUT()
    {
        PainterlyGradientLUTSO source = ResolveSource();
        if (source == null)
        {
            Debug.LogError("PainterlyGradientLUTGenerator: select a PainterlyGradientLUTSO asset " +
                           "(or keep exactly one in the project) before generating.");
            return;
        }

        int gradientCount = source.gradients != null ? source.gradients.Count : 0;
        if (gradientCount == 0)
        {
            Debug.LogError("PainterlyGradientLUTGenerator: the selected " + source.name +
                           " has no gradients — add at least one colour zone.");
            return;
        }
        if (gradientCount > TextureSize)
        {
            Debug.LogError("PainterlyGradientLUTGenerator: " + gradientCount + " gradients exceeds the " +
                           TextureSize + "-row atlas. Reduce to " + TextureSize + " or fewer.");
            return;
        }

        Color[] pixels = new Color[TextureSize * TextureSize];
        for (int pixelY = 0; pixelY < TextureSize; pixelY++)
        {
            // pixelY 0 is the BOTTOM row; gradient index 0 must land at the TOP, so invert.
            int bandFromTop = ((TextureSize - 1 - pixelY) * gradientCount) / TextureSize;
            int gradientIndex = Mathf.Clamp(bandFromTop, 0, gradientCount - 1);
            Gradient gradient = source.gradients[gradientIndex];

            for (int pixelX = 0; pixelX < TextureSize; pixelX++)
            {
                float gradientPosition = TextureSize > 1 ? pixelX / (float)(TextureSize - 1) : 0f;
                Color sampledColor = gradient != null ? gradient.Evaluate(gradientPosition) : Color.magenta;
                pixels[pixelY * TextureSize + pixelX] = sampledColor;
            }
        }

        Texture2D lutTexture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false, false);
        lutTexture.SetPixels(pixels);
        lutTexture.Apply();
        byte[] pngBytes = lutTexture.EncodeToPNG();
        Object.DestroyImmediate(lutTexture);

        if (!Directory.Exists(OutputDirectory))
        {
            Directory.CreateDirectory(OutputDirectory);
        }
        File.WriteAllBytes(OutputPath, pngBytes);
        AssetDatabase.ImportAsset(OutputPath);

        TextureImporter lutImporter = (TextureImporter)AssetImporter.GetAtPath(OutputPath);
        // Real colours (unlike the linear stroke mask) → sRGB. Point + Clamp + no mips keep the
        // colour zones crisp: no bleeding between adjacent gradient rows, no palette wrapping.
        lutImporter.sRGBTexture = true;
        lutImporter.wrapMode = TextureWrapMode.Clamp;
        lutImporter.filterMode = FilterMode.Point;
        lutImporter.mipmapEnabled = false;
        lutImporter.textureCompression = TextureImporterCompression.Uncompressed;
        lutImporter.SaveAndReimport();

        WriteReferenceSheet(source, gradientCount);

        Debug.Log("Painterly gradient LUT baked (" + gradientCount + " zones) at " + OutputPath +
                  " — row map written to " + ReferenceSheetPath + ".");
    }

    private static PainterlyGradientLUTSO ResolveSource()
    {
        if (Selection.activeObject is PainterlyGradientLUTSO selected)
        {
            return selected;
        }

        string[] found = AssetDatabase.FindAssets("t:PainterlyGradientLUTSO");
        if (found.Length == 1)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(found[0]);
            return AssetDatabase.LoadAssetAtPath<PainterlyGradientLUTSO>(assetPath);
        }
        return null;
    }

    private static void WriteReferenceSheet(PainterlyGradientLUTSO source, int gradientCount)
    {
        StringBuilder sheet = new StringBuilder();
        sheet.AppendLine("Painterly Gradient LUT — UV.y band → colour zone");
        sheet.AppendLine("Source: " + source.name);
        sheet.AppendLine("Map a mesh part's UV.y into the band of the colour it should be.");
        sheet.AppendLine();
        for (int gradientIndex = 0; gradientIndex < gradientCount; gradientIndex++)
        {
            float bandTop = (gradientCount - gradientIndex) / (float)gradientCount;
            float bandBottom = (gradientCount - 1 - gradientIndex) / (float)gradientCount;
            float bandCenter = (bandTop + bandBottom) * 0.5f;
            sheet.AppendLine("zone " + gradientIndex +
                             ": UV.y [" + bandBottom.ToString("F3") + " .. " + bandTop.ToString("F3") + "]" +
                             "  centre " + bandCenter.ToString("F3"));
        }
        File.WriteAllText(ReferenceSheetPath, sheet.ToString());
        AssetDatabase.ImportAsset(ReferenceSheetPath);
    }
}
