using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// Look spike only: builds a throwaway terraced-terrain scene and captures review shots.
public static class TerraceSpikeScene
{
    const string SpikeFolder = "Assets/Terrain/Spikes";
    const string ScenePath = SpikeFolder + "/TerraceSpike.unity";
    const string CaptureFolder = "Assets/_Vault/Tasks/Captures/Terraces_2026-11-21";
    const float LevelHeightMetres = 3f;
    const float CameraPitchDegrees = 50f;

    [MenuItem("Stitch Punk/Terrain/Spikes/Build Terrace Spike")]
    public static void BuildTerraceSpikeScene()
    {
        EnsureFolder("Assets/Terrain", "Spikes");
        UnityEngine.SceneManagement.Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        GameObject lightObject = new GameObject("Directional Light");
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.shadows = LightShadows.Soft;
        lightObject.transform.rotation = Quaternion.Euler(50f, -35f, 0f);

        Material groundMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/PaintedGround/Materials/M_PaintedGround.mat");
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Quad);
        ground.name = "Ground";
        Object.DestroyImmediate(ground.GetComponent<Collider>());
        ground.transform.position = new Vector3(50f, 0f, 50f);
        ground.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        ground.transform.localScale = new Vector3(120f, 120f, 1f);
        ground.GetComponent<MeshRenderer>().sharedMaterial = groundMaterial;

        Material wallMaterial = GetOrCreateWallMaterial();
        Material lipMaterial = GetOrCreateLipMaterial();
        Material footMaterial = GetOrCreateFootMaterial();

        GameObject root = new GameObject("TerraceSpike");
        int[,] cellLevels = BuildSpikeCellLevels();
        for (int level = 1; level <= 2; level++)
        {
            List<List<Vector2>> outlines = TerraceSpikeGeometry.TraceLevelOutlines(cellLevels, level, 1f);
            for (int outlineIndex = 0; outlineIndex < outlines.Count; outlineIndex++)
            {
                List<Vector2> rounded = TerraceSpikeGeometry.RoundOutline(outlines[outlineIndex], 0.8f, 4);
                float bottomY = (level - 1) * LevelHeightMetres;
                float topY = level * LevelHeightMetres;
                string suffix = "_L" + level + "_" + outlineIndex;
                AddMeshObject(root, "Wall" + suffix, TerraceSpikeGeometry.BuildWallMesh(rounded, bottomY, topY, 0.12f, level * 31 + outlineIndex), wallMaterial);
                AddMeshObject(root, "Cap" + suffix, TerraceSpikeGeometry.BuildCapMesh(rounded, topY), groundMaterial);
                AddMeshObject(root, "Lip" + suffix, TerraceSpikeGeometry.BuildLipMesh(rounded, topY, 0.6f, 0.9f, 3f), lipMaterial);
                if (level == 1)
                    AddMeshObject(root, "Foot" + suffix, TerraceSpikeGeometry.BuildFootMesh(rounded, 0f, 2.5f), footMaterial);
            }
        }

        GameObject cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.fieldOfView = 35f;
        camera.farClipPlane = 600f;
        PlaceCamera(camera, new Vector3(47f, 3f, 50f), 150f);

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
    }

    static int[,] BuildSpikeCellLevels()
    {
        int[,] cellLevels = new int[100, 100];
        for (int cellX = 0; cellX < 100; cellX++)
        {
            for (int cellZ = 0; cellZ < 100; cellZ++)
            {
                float centreX = cellX + 0.5f;
                float centreZ = cellZ + 0.5f;
                bool inLowerArm = cellX >= 20 && cellX < 75 && cellZ >= 20 && cellZ < 45;
                bool inUpperArm = cellX >= 20 && cellX < 45 && cellZ >= 45 && cellZ < 80;
                bool inCircle = (centreX - 68f) * (centreX - 68f) + (centreZ - 62f) * (centreZ - 62f) <= 144f;
                bool inNotch = cellX >= 45 && cellX < 49 && cellZ >= 20 && cellZ < 27;
                if ((inLowerArm || inUpperArm || inCircle) && !inNotch) cellLevels[cellX, cellZ] = 1;

                bool inRaisedRect = cellX >= 28 && cellX < 42 && cellZ >= 55 && cellZ < 72;
                bool inRaisedCircle = (centreX - 38f) * (centreX - 38f) + (centreZ - 52f) * (centreZ - 52f) <= 36f;
                if (inRaisedRect || inRaisedCircle) cellLevels[cellX, cellZ] = 2;
            }
        }
        return cellLevels;
    }

    static void AddMeshObject(GameObject parent, string objectName, Mesh mesh, Material material)
    {
        GameObject meshObject = new GameObject(objectName);
        meshObject.transform.SetParent(parent.transform, false);
        meshObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = meshObject.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        if (objectName.StartsWith("Lip") || objectName.StartsWith("Foot"))
            renderer.shadowCastingMode = ShadowCastingMode.Off;
    }

    static void EnsureFolder(string parentFolder, string folderName)
    {
        if (!AssetDatabase.IsValidFolder(parentFolder + "/" + folderName))
            AssetDatabase.CreateFolder(parentFolder, folderName);
    }

    static Material GetOrCreateMaterial(string fileName, string shaderName, System.Action<Material> configure)
    {
        string path = SpikeFolder + "/" + fileName;
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;
        Shader shader = Shader.Find(shaderName);
        Material material = new Material(shader);
        configure(material);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    static Material GetOrCreateWallMaterial()
    {
        return GetOrCreateMaterial("M_TerraceWall.mat", "Painted/Ribbon", material =>
        {
            material.SetTexture("_SurfaceTex", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/PaintedGround/Textures/Layers/T_Cliff.png"));
            material.SetFloat("_MetresPerTile", 4f);
            material.SetFloat("_EdgeWearAmount", 0f);
            material.SetFloat("_RutDarkness", 0f);
            material.SetFloat("_RidgeLighten", 0f);
            material.SetFloat("_EdgeDitherSoftness", 0f);
        });
    }

    static Material GetOrCreateLipMaterial()
    {
        return GetOrCreateMaterial("M_TerraceLip.mat", "Universal Render Pipeline/Unlit", material =>
        {
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/PaintedGround/Textures/Edges/T_EdgeFringe.png"));
            material.SetColor("_BaseColor", new Color(0.42f, 0.5f, 0.3f, 1f));
            material.SetFloat("_AlphaClip", 1f);
            material.SetFloat("_Cutoff", 0.4f);
            material.EnableKeyword("_ALPHATEST_ON");
            material.renderQueue = (int)RenderQueue.AlphaTest;
        });
    }

    static Material GetOrCreateFootMaterial()
    {
        return GetOrCreateMaterial("M_TerraceFoot.mat", "Universal Render Pipeline/Particles/Unlit", material =>
        {
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)RenderQueue.Transparent;
        });
    }

    static void PlaceCamera(Camera camera, Vector3 target, float distanceMetres)
    {
        Quaternion rotation = Quaternion.Euler(CameraPitchDegrees, 0f, 0f);
        camera.transform.rotation = rotation;
        camera.transform.position = target - rotation * Vector3.forward * distanceMetres;
    }

    public static void CaptureView(string absolutePngPath, Vector3 cameraPosition, Vector3 cameraEuler, int width = 1920, int height = 1080)
    {
        Camera camera = Camera.main;
        bool previousAsyncCompilation = ShaderUtil.allowAsyncCompilation;
        ShaderUtil.allowAsyncCompilation = false;
        RenderTexture renderTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
        RenderTexture previousActive = RenderTexture.active;
        try
        {
            camera.transform.position = cameraPosition;
            camera.transform.rotation = Quaternion.Euler(cameraEuler);
            camera.targetTexture = renderTexture;
            camera.Render();
            RenderTexture.active = renderTexture;
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            texture.Apply();
            File.WriteAllBytes(absolutePngPath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
        }
        finally
        {
            camera.targetTexture = null;
            RenderTexture.active = previousActive;
            renderTexture.Release();
            Object.DestroyImmediate(renderTexture);
            ShaderUtil.allowAsyncCompilation = previousAsyncCompilation;
        }
    }

    [MenuItem("Stitch Punk/Terrain/Spikes/Capture Terrace Spike")]
    public static void CaptureTerraceSpikeShots()
    {
        if (!File.Exists(ScenePath)) BuildTerraceSpikeScene();
        else EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        string absoluteFolder = Path.GetFullPath(CaptureFolder);
        Directory.CreateDirectory(absoluteFolder);
        Vector3 pitchEuler = new Vector3(CameraPitchDegrees, 0f, 0f);
        CaptureShot(absoluteFolder, "01_overview_two_levels.png", new Vector3(47f, 3f, 50f), 150f, pitchEuler);
        CaptureShot(absoluteFolder, "02_cliff_wall_closeup.png", new Vector3(32f, 1.5f, 20f), 25f, pitchEuler);
        CaptureShot(absoluteFolder, "03_rounded_convex_corner.png", new Vector3(20f, 1.5f, 20f), 25f, pitchEuler);
        CaptureShot(absoluteFolder, "04_concave_notch_corner.png", new Vector3(47f, 1.5f, 26f), 25f, pitchEuler);
        CaptureShot(absoluteFolder, "05_level2_on_level1.png", new Vector3(36f, 4f, 60f), 45f, pitchEuler);
        AssetDatabase.Refresh();
    }

    static void CaptureShot(string absoluteFolder, string fileName, Vector3 target, float distanceMetres, Vector3 euler)
    {
        Vector3 position = target - Quaternion.Euler(euler) * Vector3.forward * distanceMetres;
        CaptureView(Path.Combine(absoluteFolder, fileName), position, euler);
    }
}
