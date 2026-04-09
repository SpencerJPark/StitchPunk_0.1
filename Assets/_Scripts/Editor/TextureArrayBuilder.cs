// Editor/TexturePackerBuilder.cs
using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(TexturePackerConfig))]
public class TexturePackerBuilder : Editor
{
    public override void OnInspectorGUI()
    {
        var config = (TexturePackerConfig)target;
        
        EditorGUILayout.PropertyField(serializedObject.FindProperty("isTextureArray"));
        EditorGUILayout.Space();
        
        EditorGUILayout.PropertyField(serializedObject.FindProperty("baseSize"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("format"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("linear"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("wrapMode"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("filterMode"));
        EditorGUILayout.Space();
        
        if (config.isTextureArray)
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("slices"), true);
        }
        else
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("mips"), true);
        }
        
        serializedObject.ApplyModifiedProperties();
        
        GUILayout.Space(10);
        
        string buttonText = config.isTextureArray ? "Build Texture2DArray" : "Build Texture2D";
        if (GUILayout.Button(buttonText, GUILayout.Height(30)))
        {
            if (config.isTextureArray)
                BuildArray(config);
            else
                BuildSingle(config);
        }
    }

    void BuildSingle(TexturePackerConfig config)
    {
        if (config.mips == null || config.mips.Length == 0)
        {
            Debug.LogError("No mips defined!");
            return;
        }

        var tex = new Texture2D(
            config.baseSize,
            config.baseSize,
            config.format,
            config.mips.Length,
            config.linear
        );

        for (int m = 0; m < config.mips.Length; m++)
        {
            Texture2D src = config.mips[m];
            
            if (src == null)
            {
                Debug.LogError($"Missing mip {m}");
                return;
            }

            if (!src.isReadable)
            {
                Debug.LogError($"'{src.name}' needs Read/Write enabled");
                return;
            }

            tex.SetPixels(src.GetPixels(), m);
        }

        tex.wrapMode = config.wrapMode;
        tex.filterMode = config.filterMode;
        tex.Apply(false);

        string configPath = AssetDatabase.GetAssetPath(config);
        string outputPath = configPath.Replace(".asset", "_Tex.asset");
        
        AssetDatabase.CreateAsset(tex, outputPath);
        AssetDatabase.SaveAssets();
        
        Debug.Log($"Created: {outputPath}");
    }

    void BuildArray(TexturePackerConfig config)
    {
        if (config.slices == null || config.slices.Length == 0)
        {
            Debug.LogError("No slices defined!");
            return;
        }

        int mipCount = config.slices[0].mips?.Length ?? 0;
        
        foreach (var slice in config.slices)
        {
            if (slice.mips == null || slice.mips.Length != mipCount)
            {
                Debug.LogError($"Slice '{slice.name}' has inconsistent mip count");
                return;
            }
        }

        var texArray = new Texture2DArray(
            config.baseSize,
            config.baseSize,
            config.slices.Length,
            config.format,
            mipCount,
            config.linear
        );

        for (int s = 0; s < config.slices.Length; s++)
        {
            for (int m = 0; m < mipCount; m++)
            {
                Texture2D src = config.slices[s].mips[m];
                
                if (src == null)
                {
                    Debug.LogError($"Missing mip {m} for slice '{config.slices[s].name}'");
                    return;
                }

                if (!src.isReadable)
                {
                    Debug.LogError($"'{src.name}' needs Read/Write enabled");
                    return;
                }

                texArray.SetPixels(src.GetPixels(), s, m);
            }
        }

        texArray.wrapMode = config.wrapMode;
        texArray.filterMode = config.filterMode;
        texArray.Apply(false);

        string configPath = AssetDatabase.GetAssetPath(config);
        string outputPath = configPath.Replace(".asset", "_Array.asset");
        
        AssetDatabase.CreateAsset(texArray, outputPath);
        AssetDatabase.SaveAssets();
        
        Debug.Log($"Created: {outputPath}");
    }
}