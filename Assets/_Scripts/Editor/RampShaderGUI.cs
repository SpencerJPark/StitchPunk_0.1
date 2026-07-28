using UnityEngine;
using UnityEditor;

public class RampShaderGUI : ShaderGUI
{
    private Gradient colorGradient;

    public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
    {
        Material targetMat = materialEditor.target as Material;
        if (targetMat == null) return;

        // Loop through every single property exposed by your Shader Graph Blackboard
        for (int i = 0; i < properties.Length; i++)
        {
            MaterialProperty prop = properties[i];

            // Skip drawing the hidden background texture property completely
            if (prop.name == "_RampTex") continue;

            // Draw the current property normally
            materialEditor.ShaderProperty(prop, prop.displayName);

            // Check if this property is exactly your target property
            if (prop.name == "_MainTex")
            {
                // Inject and draw the visual color ramp right here!
                DrawGradientField(targetMat);
            }
        }
    }

    private void DrawGradientField(Material targetMat)
    {
        if (colorGradient == null)
        {
            colorGradient = new Gradient();
            string savedJson = targetMat.GetTag("GradientData", false, "");
            if (!string.IsNullOrEmpty(savedJson))
            {
                JsonUtility.FromJsonOverwrite(savedJson, colorGradient);
            }
        }

        EditorGUI.BeginChangeCheck();
        colorGradient = EditorGUILayout.GradientField("Material Color Ramp", colorGradient);

        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(targetMat, "Changed Material Gradient");

            string jsonString = JsonUtility.ToJson(colorGradient);
            targetMat.SetOverrideTag("GradientData", jsonString);

            Texture2D bakedTex = BakeGradient(colorGradient);
            targetMat.SetTexture("_RampTex", bakedTex);
            
            EditorUtility.SetDirty(targetMat);
        }
    }

    private Texture2D BakeGradient(Gradient grad)
    {
        const int width = 256;
        Texture2D tex = new Texture2D(width, 1, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        Color[] colors = new Color[width];
        for (int i = 0; i < width; i++)
        {
            float t = (float)i / (width - 1);
            colors[i] = grad.Evaluate(t);
        }

        tex.SetPixels(colors);
        tex.Apply();
        return tex;
    }
}
