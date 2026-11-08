#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Adds the day/night + weather driver to the open scene (it needs the scene's sun) and creates the rain material.
public static class TerrainAtmosphereSetup
{
    private const string RainMaterialPath = "Assets/Terrain/Profiles/M_Rain.mat";
    private const string RainShaderName = "Universal Render Pipeline/Particles/Unlit";
    private const string ObjectName = "Atmosphere";

    [MenuItem("Stitch Punk/Terrain/Set Up Atmosphere (Day Night Weather)")]
    public static void EnsureInOpenScene()
    {
        Light sun = FindSun();
        if (sun == null)
        {
            Debug.LogError("[Atmosphere] No directional light in the open scene.");
            return;
        }

        WorldAtmosphereDriver driver = Object.FindFirstObjectByType<WorldAtmosphereDriver>();
        if (driver == null)
        {
            GameObject atmosphereObject = new GameObject(ObjectName);
            driver = atmosphereObject.AddComponent<WorldAtmosphereDriver>();
        }
        TerrainLightingBridge bridge = driver.GetComponent<TerrainLightingBridge>();
        if (bridge == null)
        {
            bridge = Object.FindFirstObjectByType<TerrainLightingBridge>();
            if (bridge == null)
                bridge = driver.gameObject.AddComponent<TerrainLightingBridge>();
        }

        SerializedObject driverObject = new SerializedObject(driver);
        driverObject.FindProperty("sun").objectReferenceValue = sun;
        driverObject.FindProperty("lightingBridge").objectReferenceValue = bridge;
        driverObject.FindProperty("rainMaterial").objectReferenceValue = LoadOrCreateRainMaterial();
        driverObject.ApplyModifiedProperties();
        EditorSceneManager.MarkSceneDirty(driver.gameObject.scene);
        Debug.Log("[Atmosphere] Driver wired to '" + sun.name + "'. Save the scene to keep it.");
    }

    private static Light FindSun()
    {
        if (RenderSettings.sun != null)
            return RenderSettings.sun;
        foreach (Light light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
        {
            if (light.type == LightType.Directional)
                return light;
        }
        return null;
    }

    private static Material LoadOrCreateRainMaterial()
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(RainMaterialPath);
        if (material != null)
            return material;
        Shader shader = Shader.Find(RainShaderName);
        material = new Material(shader) { name = "M_Rain" };
        material.SetFloat("_Surface", 1f);
        material.SetFloat("_Blend", 0f);
        material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_ZWrite", 0f);
        material.SetOverrideTag("RenderType", "Transparent");
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.SetColor("_BaseColor", new Color(0.78f, 0.85f, 0.95f, 0.45f));
        AssetDatabase.CreateAsset(material, RainMaterialPath);
        AssetDatabase.SaveAssets();
        return material;
    }
}
#endif
