using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Packs three hand-painted grayscale images into the single RGB painterly
/// mask, so the mask can be authored in Affinity (or anything) as three plain
/// grayscale paintings — no channel-panel editing required.
///
/// Paint and export these files (any of them may be omitted — missing
/// channels fall back to flat mid gray):
///   Assets/Textures/Painterly/Mask_R.png
///   Assets/Textures/Painterly/Mask_G.png
///   Assets/Textures/Painterly/Mask_B.png
/// then run Stitch Punk ▸ Pack Painterly Mask From Grayscale Files.
/// The result overwrites T_PainterlyMask.png in place, so the texture GUID,
/// import settings, and every material reference are preserved.
/// </summary>
public static class PainterlyMaskPacker
{
    private const string SourceDirectory = "Assets/Textures/Painterly";
    private const string OutputPath = SourceDirectory + "/T_PainterlyMask.png";

    [MenuItem("Stitch Punk/Pack Painterly Mask From Grayscale Files")]
    public static void PackMaskFromGrayscaleFiles()
    {
        Texture2D redSource = LoadSourceImage(SourceDirectory + "/Mask_R.png");
        Texture2D greenSource = LoadSourceImage(SourceDirectory + "/Mask_G.png");
        Texture2D blueSource = LoadSourceImage(SourceDirectory + "/Mask_B.png");

        Texture2D sizeReference = redSource;
        if (sizeReference == null) { sizeReference = greenSource; }
        if (sizeReference == null) { sizeReference = blueSource; }
        if (sizeReference == null)
        {
            Debug.LogError("PainterlyMaskPacker: no Mask_R/Mask_G/Mask_B png found in " + SourceDirectory);
            return;
        }

        int width = sizeReference.width;
        int height = sizeReference.height;
        if (!SizeMatches(redSource, width, height) || !SizeMatches(greenSource, width, height) || !SizeMatches(blueSource, width, height))
        {
            Debug.LogError("PainterlyMaskPacker: source images have mismatched sizes — make all three " + width + "x" + height + ".");
            CleanupSources(redSource, greenSource, blueSource);
            return;
        }

        Color32[] redPixels = ReadPixelsOrNull(redSource);
        Color32[] greenPixels = ReadPixelsOrNull(greenSource);
        Color32[] bluePixels = ReadPixelsOrNull(blueSource);

        Color32[] packedPixels = new Color32[width * height];
        for (int pixelIndex = 0; pixelIndex < packedPixels.Length; pixelIndex++)
        {
            byte redValue = redPixels != null ? redPixels[pixelIndex].r : (byte)128;
            byte greenValue = greenPixels != null ? greenPixels[pixelIndex].r : (byte)128;
            byte blueValue = bluePixels != null ? bluePixels[pixelIndex].r : (byte)128;
            packedPixels[pixelIndex] = new Color32(redValue, greenValue, blueValue, 255);
        }

        Texture2D packedTexture = new Texture2D(width, height, TextureFormat.RGB24, false, true);
        packedTexture.SetPixels32(packedPixels);
        packedTexture.Apply();
        byte[] pngBytes = packedTexture.EncodeToPNG();
        Object.DestroyImmediate(packedTexture);
        CleanupSources(redSource, greenSource, blueSource);

        File.WriteAllBytes(OutputPath, pngBytes);
        AssetDatabase.ImportAsset(OutputPath);

        TextureImporter maskImporter = (TextureImporter)AssetImporter.GetAtPath(OutputPath);
        maskImporter.sRGBTexture = false;
        maskImporter.wrapMode = TextureWrapMode.Repeat;
        maskImporter.mipmapEnabled = true;
        maskImporter.SaveAndReimport();

        string usedChannels =
            (redPixels != null ? "R" : "-") +
            (greenPixels != null ? "G" : "-") +
            (bluePixels != null ? "B" : "-");
        Debug.Log("Painterly mask packed (" + usedChannels + ") at " + OutputPath + " — missing channels are flat mid gray.");
    }

    /// <summary>
    /// Loads a PNG straight from disk (bypasses the importer, so source images
    /// don't need readable/uncompressed import settings — or any import at all).
    /// </summary>
    private static Texture2D LoadSourceImage(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }
        byte[] fileBytes = File.ReadAllBytes(path);
        Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
        if (!texture.LoadImage(fileBytes))
        {
            Debug.LogWarning("PainterlyMaskPacker: could not decode " + path + " — treating as missing.");
            Object.DestroyImmediate(texture);
            return null;
        }
        return texture;
    }

    private static bool SizeMatches(Texture2D texture, int width, int height)
    {
        return texture == null || (texture.width == width && texture.height == height);
    }

    private static Color32[] ReadPixelsOrNull(Texture2D texture)
    {
        return texture != null ? texture.GetPixels32() : null;
    }

    private static void CleanupSources(Texture2D redSource, Texture2D greenSource, Texture2D blueSource)
    {
        if (redSource != null) { Object.DestroyImmediate(redSource); }
        if (greenSource != null) { Object.DestroyImmediate(greenSource); }
        if (blueSource != null) { Object.DestroyImmediate(blueSource); }
    }
}
