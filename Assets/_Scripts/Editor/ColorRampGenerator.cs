using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Bakes ColorRampSO gradients into 1D colour-ramp PNGs under Assets/Textures/ColorRamps/.
/// Those textures are the "curve" the painterly shader remaps a texture's luminance through:
/// Luminance Ramp UV inverts the greyscale (light → U 0, dark → U 1) and samples the ramp,
/// so the ramp alone decides the palette and the Hue Shift slider tunes it per material.
///
/// A ramp always bakes to the SAME path (T_Ramp_&lt;asset name&gt;.png), overwriting in place, so the
/// texture's GUID and import settings survive a re-bake and no material ever loses its ramp.
/// Rename the SO and you get a new texture — the old one is left behind for you to delete.
///
/// Entry points: the Bake button on a ColorRampSO's inspector, or
/// Stitch Punk ▸ Bake All Color Ramps for every ramp in the project.
/// </summary>
public static class ColorRampGenerator
{
    public const string OutputDirectory = "Assets/Textures/ColorRamps";

    // The ramp is one gradient repeated down every row, so the shader's V coordinate is irrelevant
    // (it uses a fixed 0.5). Height exists purely so the asset reads as a colour strip in the
    // Project browser instead of an unclickable 1px sliver.
    private const int RampHeight = 8;

    [MenuItem("Stitch Punk/Bake All Color Ramps")]
    public static void BakeAllColorRamps()
    {
        string[] foundGuids = AssetDatabase.FindAssets("t:ColorRampSO");
        if (foundGuids.Length == 0)
        {
            Debug.LogError("ColorRampGenerator: no ColorRampSO assets found. Create one via " +
                           "Assets ▸ Create ▸ Colors ▸ Color Ramp first.");
            return;
        }

        StringBuilder bakedList = new StringBuilder();
        int bakedCount = 0;
        for (int rampIndex = 0; rampIndex < foundGuids.Length; rampIndex++)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(foundGuids[rampIndex]);
            ColorRampSO ramp = AssetDatabase.LoadAssetAtPath<ColorRampSO>(assetPath);
            if (ramp == null)
            {
                continue;
            }

            string outputPath = BakeColorRamp(ramp);
            if (outputPath != null)
            {
                bakedList.AppendLine("  " + outputPath);
                bakedCount++;
            }
        }

        Debug.Log("ColorRampGenerator: baked " + bakedCount + " colour ramp(s).\n" + bakedList);
    }

    /// <summary>
    /// Bakes one ramp and returns the asset path it was written to, or null if the ramp was unusable.
    /// </summary>
    public static string BakeColorRamp(ColorRampSO ramp)
    {
        if (ramp == null)
        {
            Debug.LogError("ColorRampGenerator: cannot bake a null ColorRampSO.");
            return null;
        }
        if (ramp.gradient == null)
        {
            Debug.LogError("ColorRampGenerator: " + ramp.name + " has no gradient.");
            return null;
        }

        int rampWidth = Mathf.Clamp(ramp.width, 8, 1024);
        Color[] pixels = new Color[rampWidth * RampHeight];

        for (int pixelX = 0; pixelX < rampWidth; pixelX++)
        {
            // Endpoints land exactly on the gradient's first and last key, so the ramp's extremes
            // survive the bake — U 0 is genuinely the leftmost colour, U 1 the rightmost.
            float gradientPosition = pixelX / (float)(rampWidth - 1);
            Color sampledColor = ramp.gradient.Evaluate(gradientPosition);

            for (int pixelY = 0; pixelY < RampHeight; pixelY++)
            {
                pixels[pixelY * rampWidth + pixelX] = sampledColor;
            }
        }

        Texture2D rampTexture = new Texture2D(rampWidth, RampHeight, TextureFormat.RGBA32, false, false);
        rampTexture.SetPixels(pixels);
        rampTexture.Apply();
        byte[] pngBytes = rampTexture.EncodeToPNG();
        Object.DestroyImmediate(rampTexture);

        if (!Directory.Exists(OutputDirectory))
        {
            Directory.CreateDirectory(OutputDirectory);
        }

        string outputPath = GetOutputPath(ramp);
        File.WriteAllBytes(outputPath, pngBytes);
        AssetDatabase.ImportAsset(outputPath);

        TextureImporter rampImporter = (TextureImporter)AssetImporter.GetAtPath(outputPath);
        rampImporter.textureType = TextureImporterType.Default;
        rampImporter.sRGBTexture = ramp.sRGB;
        // Clamp is what makes the inverted-luminance lookup safe at both ends: a fully white or
        // fully black pixel lands on U 0 / U 1 and must hold the end colour, never wrap around to
        // the opposite end of the ramp. Bilinear keeps a Blend-mode gradient smooth; a Fixed-mode
        // gradient still bands hard because the baked pixels themselves are hard-edged.
        rampImporter.wrapMode = TextureWrapMode.Clamp;
        rampImporter.filterMode = FilterMode.Bilinear;
        rampImporter.mipmapEnabled = false;
        rampImporter.textureCompression = TextureImporterCompression.Uncompressed;
        rampImporter.npotScale = TextureImporterNPOTScale.None;
        rampImporter.SaveAndReimport();

        return outputPath;
    }

    public static string GetOutputPath(ColorRampSO ramp)
    {
        return OutputDirectory + "/T_Ramp_" + SanitizeFileName(ramp.name) + ".png";
    }

    public static Texture2D FindBakedTexture(ColorRampSO ramp)
    {
        return AssetDatabase.LoadAssetAtPath<Texture2D>(GetOutputPath(ramp));
    }

    private static string SanitizeFileName(string assetName)
    {
        HashSet<char> invalidCharacters = new HashSet<char>(Path.GetInvalidFileNameChars());
        StringBuilder sanitized = new StringBuilder(assetName.Length);
        for (int characterIndex = 0; characterIndex < assetName.Length; characterIndex++)
        {
            char currentCharacter = assetName[characterIndex];
            if (invalidCharacters.Contains(currentCharacter) || currentCharacter == ' ')
            {
                sanitized.Append('_');
                continue;
            }
            sanitized.Append(currentCharacter);
        }
        return sanitized.Length > 0 ? sanitized.ToString() : "Unnamed";
    }
}

/// <summary>
/// ColorRampSO inspector: the gradient field plus a live preview of what will be baked, the current
/// on-disk state, and the Bake button. The preview is generated from the gradient every repaint, so
/// it shows unsaved edits; the "Baked" row shows what the material actually gets until you re-bake.
/// </summary>
[CustomEditor(typeof(ColorRampSO))]
public class ColorRampSOEditor : Editor
{
    private Texture2D previewTexture;

    private void OnDisable()
    {
        if (previewTexture != null)
        {
            DestroyImmediate(previewTexture);
            previewTexture = null;
        }
    }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        ColorRampSO ramp = (ColorRampSO)target;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Preview (light → dark, left → right)", EditorStyles.boldLabel);
        DrawRampPreview(ramp);

        EditorGUILayout.Space();
        string outputPath = ColorRampGenerator.GetOutputPath(ramp);
        Texture2D bakedTexture = ColorRampGenerator.FindBakedTexture(ramp);
        if (bakedTexture == null)
        {
            EditorGUILayout.HelpBox("Not baked yet. Bake, then drag " + Path.GetFileName(outputPath) +
                                    " onto a material's Ramp Tex slot.", MessageType.Info);
        }
        else
        {
            EditorGUILayout.ObjectField("Baked", bakedTexture, typeof(Texture2D), false);
        }

        EditorGUILayout.Space();
        if (GUILayout.Button("Bake Ramp Texture", GUILayout.Height(28f)))
        {
            string bakedPath = ColorRampGenerator.BakeColorRamp(ramp);
            if (bakedPath != null)
            {
                Debug.Log("ColorRampGenerator: baked " + ramp.name + " → " + bakedPath, target);
                EditorGUIUtility.PingObject(ColorRampGenerator.FindBakedTexture(ramp));
            }
        }
    }

    private void DrawRampPreview(ColorRampSO ramp)
    {
        if (ramp.gradient == null)
        {
            return;
        }

        const int previewWidth = 256;
        if (previewTexture == null)
        {
            previewTexture = new Texture2D(previewWidth, 1, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        Color[] previewPixels = new Color[previewWidth];
        for (int pixelX = 0; pixelX < previewWidth; pixelX++)
        {
            previewPixels[pixelX] = ramp.gradient.Evaluate(pixelX / (float)(previewWidth - 1));
        }
        previewTexture.SetPixels(previewPixels);
        previewTexture.Apply();

        Rect previewRect = GUILayoutUtility.GetRect(0f, 32f, GUILayout.ExpandWidth(true));
        EditorGUI.DrawPreviewTexture(previewRect, previewTexture);
    }
}
