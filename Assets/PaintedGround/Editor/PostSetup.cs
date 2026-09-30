// Post-process setup tool.
// Menu: Tools > Painted Ground > Add Painted Post Effect to Renderer
//
// Adds a URP FullScreenPassRendererFeature (using the Painted/Post shader) to the project's
// active Universal Renderer asset, so the painted look gets its full-screen paper grain /
// vignette pass. Also makes sure the URP asset has the depth texture enabled, since the
// Painted/Post shader samples it.

#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PaintedGround
{
    public static class PostSetup
    {
        const string FeatureName = "Painted Post";

        [MenuItem("Tools/Painted Ground/Add Painted Post Effect to Renderer")]
        public static void AddPostEffect()
        {
            var pipelineAsset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (pipelineAsset == null)
            {
                EditorUtility.DisplayDialog("Painted Ground", "Project is not using URP", "OK");
                return;
            }

            string folder = FindFolder();
            if (folder == null) { Debug.LogError("[Painted Ground] folder not found"); return; }
            var shader = Shader.Find("Painted/Post");
            if (shader == null) { Debug.LogError("[Painted Ground] Painted/Post shader not found"); return; }

            var pipelineSO = new SerializedObject(pipelineAsset);
            var rendererDataListProp = pipelineSO.FindProperty("m_RendererDataList");
            var defaultIndexProp = pipelineSO.FindProperty("m_DefaultRendererIndex");
            if (rendererDataListProp == null || defaultIndexProp == null || rendererDataListProp.arraySize == 0)
            {
                Debug.LogError("[Painted Ground] Could not find the URP asset's renderer data list.");
                return;
            }
            int defaultIndex = Mathf.Clamp(defaultIndexProp.intValue, 0, rendererDataListProp.arraySize - 1);
            var rendererData = rendererDataListProp.GetArrayElementAtIndex(defaultIndex).objectReferenceValue as UniversalRendererData;
            if (rendererData == null)
            {
                Debug.LogError("[Painted Ground] Default renderer data is not a UniversalRendererData.");
                return;
            }

            // grain texture: sRGB off, tiling
            string grainPath = folder + "/Textures/Noise/T_PaperGrain.png";
            if (AssetImporter.GetAtPath(grainPath) is TextureImporter ti &&
                (ti.sRGBTexture || ti.wrapMode != TextureWrapMode.Repeat))
            {
                ti.sRGBTexture = false;
                ti.wrapMode = TextureWrapMode.Repeat;
                ti.SaveAndReimport();
            }
            var grain = AssetDatabase.LoadAssetAtPath<Texture2D>(grainPath);

            var mat = LoadOrCreateMaterial(folder, "M_PaintedPost", shader);
            mat.SetTexture("_GrainTex", grain);
            EditorUtility.SetDirty(mat);

            // does the renderer already have our feature?
            FullScreenPassRendererFeature existing = null;
            foreach (var f in rendererData.rendererFeatures)
                if (f is FullScreenPassRendererFeature fsf && fsf.name == FeatureName) { existing = fsf; break; }

            if (existing != null)
            {
                existing.passMaterial = mat;
                EditorUtility.SetDirty(existing);
                EditorUtility.SetDirty(rendererData);
                AssetDatabase.SaveAssets();
                Debug.Log("[Painted Ground] Updated existing 'Painted Post' renderer feature's material.");
                SetDepthTexture(pipelineSO);
                return;
            }

            var feature = ScriptableObject.CreateInstance<FullScreenPassRendererFeature>();
            feature.name = FeatureName;
            feature.passMaterial = mat;
            feature.injectionPoint = FullScreenPassRendererFeature.InjectionPoint.AfterRenderingPostProcessing;
            feature.requirements = ScriptableRenderPassInput.None;
            feature.fetchColorBuffer = true;

            AssetDatabase.AddObjectToAsset(feature, rendererData);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out string guid, out long localId);

            var rendererDataSO = new SerializedObject(rendererData);
            var featuresProp = rendererDataSO.FindProperty("m_RendererFeatures");
            var featureMapProp = rendererDataSO.FindProperty("m_RendererFeatureMap");

            featuresProp.arraySize++;
            featuresProp.GetArrayElementAtIndex(featuresProp.arraySize - 1).objectReferenceValue = feature;

            featureMapProp.arraySize++;
            featureMapProp.GetArrayElementAtIndex(featureMapProp.arraySize - 1).longValue = localId;

            rendererDataSO.ApplyModifiedProperties();
            EditorUtility.SetDirty(rendererData);
            AssetDatabase.SaveAssets();

            SetDepthTexture(pipelineSO);

            Debug.Log("[Painted Ground] Added 'Painted Post' full-screen renderer feature to '" + rendererData.name + "'.");
        }

        static void SetDepthTexture(SerializedObject pipelineSO)
        {
            var depthProp = pipelineSO.FindProperty("m_RequireDepthTexture");
            if (depthProp != null && !depthProp.boolValue)
            {
                depthProp.boolValue = true;
                pipelineSO.ApplyModifiedProperties();
                EditorUtility.SetDirty(pipelineSO.targetObject);
                AssetDatabase.SaveAssets();
            }
        }

        // ---- helpers ----
        static Material LoadOrCreateMaterial(string folder, string name, Shader shader)
        {
            string path = folder + "/Materials/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
            m.shader = shader;
            EditorUtility.SetDirty(m);
            return m;
        }

        static string FindFolder()
        {
            foreach (var guid in AssetDatabase.FindAssets("PaintedGround t:Shader"))
            {
                var p = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileName(p) == "PaintedGround.shader")
                {
                    // the shader lives in <root>/Shaders; return <root>
                    string root = Path.GetDirectoryName(Path.GetDirectoryName(p)).Replace('\\', '/');
                    foreach (var sub in new[] { "Materials", "Generated" })
                        if (!AssetDatabase.IsValidFolder(root + "/" + sub)) AssetDatabase.CreateFolder(root, sub);
                    return root;
                }
            }
            return null;
        }
    }
}
#endif
