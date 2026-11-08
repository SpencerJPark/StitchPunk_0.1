#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Adds the tilt-shift full-screen pass to the active renderer (switched off; StrategicRigDriver turns it on while zoomed out)
// and finds the outline passes the driver must switch off while zoomed out.
public static class StrategicTiltShiftSetup
{
    private const string FeatureName = "Strategic Tilt Shift";
    private const string ShaderName = "Painted/StrategicTiltShift";
    private const string MaterialPath = "Assets/Terrain/Profiles/M_StrategicTiltShift.mat";
    private static readonly string[] OutlineFeatureTypeNames = { "RobertsCrossRenderFeature", "SilhouetteOutlineFeature" };

    public static void EnsureAndWire(StrategicRigDriver driver)
    {
        UniversalRendererData rendererData = FindActiveRendererData();
        if (rendererData == null)
        {
            Debug.LogError("[StrategicRig] No Universal renderer data found for the tilt-shift pass.");
            return;
        }

        FullScreenPassRendererFeature tiltShift = FindOrAddTiltShiftFeature(rendererData);
        List<ScriptableRendererFeature> outlineFeatures = new List<ScriptableRendererFeature>();
        foreach (ScriptableRendererFeature feature in rendererData.rendererFeatures)
        {
            if (feature != null && System.Array.IndexOf(OutlineFeatureTypeNames, feature.GetType().Name) >= 0)
                outlineFeatures.Add(feature);
        }

        SerializedObject driverObject = new SerializedObject(driver);
        driverObject.FindProperty("tiltShiftFeature").objectReferenceValue = tiltShift;
        SerializedProperty outlineList = driverObject.FindProperty("outlineFeaturesOffWhileZoomed");
        outlineList.arraySize = outlineFeatures.Count;
        for (int index = 0; index < outlineFeatures.Count; index++)
            outlineList.GetArrayElementAtIndex(index).objectReferenceValue = outlineFeatures[index];
        driverObject.ApplyModifiedProperties();
        Debug.Log("[StrategicRig] Tilt-shift pass wired; " + outlineFeatures.Count + " outline pass(es) switch off while zoomed out.");
    }

    private static UniversalRendererData FindActiveRendererData()
    {
        UniversalRenderPipelineAsset pipelineAsset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (pipelineAsset == null)
            return null;
        SerializedObject pipelineObject = new SerializedObject(pipelineAsset);
        SerializedProperty rendererList = pipelineObject.FindProperty("m_RendererDataList");
        SerializedProperty defaultIndex = pipelineObject.FindProperty("m_DefaultRendererIndex");
        if (rendererList == null || defaultIndex == null || rendererList.arraySize == 0)
            return null;
        int index = Mathf.Clamp(defaultIndex.intValue, 0, rendererList.arraySize - 1);
        return rendererList.GetArrayElementAtIndex(index).objectReferenceValue as UniversalRendererData;
    }

    private static FullScreenPassRendererFeature FindOrAddTiltShiftFeature(UniversalRendererData rendererData)
    {
        Material material = LoadOrCreateMaterial();
        foreach (ScriptableRendererFeature feature in rendererData.rendererFeatures)
        {
            FullScreenPassRendererFeature existing = feature as FullScreenPassRendererFeature;
            if (existing != null && existing.name == FeatureName)
            {
                existing.passMaterial = material;
                existing.SetActive(false);
                EditorUtility.SetDirty(existing);
                return existing;
            }
        }

        FullScreenPassRendererFeature created = ScriptableObject.CreateInstance<FullScreenPassRendererFeature>();
        created.name = FeatureName;
        created.passMaterial = material;
        created.injectionPoint = FullScreenPassRendererFeature.InjectionPoint.AfterRenderingPostProcessing;
        created.requirements = ScriptableRenderPassInput.None;
        created.fetchColorBuffer = true;
        created.SetActive(false);

        AssetDatabase.AddObjectToAsset(created, rendererData);
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(created, out string guid, out long localId);
        SerializedObject rendererObject = new SerializedObject(rendererData);
        SerializedProperty features = rendererObject.FindProperty("m_RendererFeatures");
        SerializedProperty featureMap = rendererObject.FindProperty("m_RendererFeatureMap");
        features.arraySize++;
        features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = created;
        featureMap.arraySize++;
        featureMap.GetArrayElementAtIndex(featureMap.arraySize - 1).longValue = localId;
        rendererObject.ApplyModifiedProperties();
        EditorUtility.SetDirty(rendererData);
        AssetDatabase.SaveAssets();
        return created;
    }

    private static Material LoadOrCreateMaterial()
    {
        Shader shader = Shader.Find(ShaderName);
        Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, MaterialPath);
        }
        material.shader = shader;
        EditorUtility.SetDirty(material);
        return material;
    }
}
#endif
