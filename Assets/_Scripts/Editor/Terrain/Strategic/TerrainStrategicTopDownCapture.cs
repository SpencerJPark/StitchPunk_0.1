#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Bakes the strategic colour texture from what the real ground looks like: the tile ground, ribbon and water meshes are drawn with their own
// materials by a top-down orthographic camera. Building the colours from the hard-edged paint masks instead gave blocky blobs that never
// matched the smooth splat-map ground the player sees when a tile unloads.
public static class TerrainStrategicTopDownCapture
{
    private const int BlankCheckPrimeStride = 1009;
    private const int MeanSamplePrimeStride = 61;
    private const float MinimumBrightnessGain = 0.5f;
    private const float MaximumBrightnessGain = 6f;
    private const int BackgroundTolerance = 18;
    private const float CameraCeilingMetres = 600f;
    private const float CameraRangeMetres = 1400f;
    private static readonly string[] CapturedShaderNames = { "Painted/Ground", "Painted/Ribbon", "Painted/Water" };
    private static readonly string[] OutlineFeatureTypeNames = { "RobertsCrossRenderFeature", "SilhouetteOutlineFeature", "FullScreenPassRendererFeature" };

    private struct DrawItem
    {
        public Mesh mesh;
        public Matrix4x4 localToWorld;
        public Material material;
        public int subMeshIndex;
    }

    // Null when the open scene has no tile ground to draw (the caller keeps the mask-built texture).
    public static Texture2D TryCapture(Vector2 chunkMin, float chunkSizeMetres, int pixelsPerSide, Color backgroundColour)
    {
        List<DrawItem> drawItems = CollectDrawItems(chunkMin, chunkSizeMetres);
        if (drawItems.Count == 0)
        {
            Debug.LogWarning("[Terrain] Top-down capture found no tile ground overlapping the chunk at " + chunkMin + "; keeping the mask-built colours.");
            return null;
        }

        int captureLayer = FindUnusedDrawnLayer();
        if (captureLayer < 0)
        {
            Debug.LogWarning("[Terrain] No layer is both drawn by the renderer and unused by the scene; keeping the mask-built colours.");
            return null;
        }

        List<ScriptableRendererFeature> disabledFeatures = DisablePostEffectFeatures();
        GameObject cameraObject = new GameObject("StrategicBakeCamera");
        cameraObject.hideFlags = HideFlags.HideAndDontSave;
        RenderTexture renderTexture = null;
        GameObject proxyRoot = null;
        RenderTexture previousActive = RenderTexture.active;
        try
        {
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = chunkSizeMetres * 0.5f;
            camera.aspect = 1f;
            camera.transform.position = new Vector3(chunkMin.x + chunkSizeMetres * 0.5f, CameraCeilingMetres, chunkMin.y + chunkSizeMetres * 0.5f);
            camera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            camera.nearClipPlane = 1f;
            camera.farClipPlane = CameraRangeMetres;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = backgroundColour;
            camera.cullingMask = 1 << captureLayer;
            camera.allowMSAA = false;
            camera.enabled = false;

            UniversalAdditionalCameraData cameraData = cameraObject.AddComponent<UniversalAdditionalCameraData>();
            cameraData.renderPostProcessing = false;
            cameraData.antialiasing = AntialiasingMode.None;

            renderTexture = new RenderTexture(pixelsPerSide, pixelsPerSide, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            camera.targetTexture = renderTexture;

            // Graphics.DrawMesh never reached this manual camera render, so each mesh gets a short-lived hidden proxy object instead.
            proxyRoot = new GameObject("StrategicBakeProxies");
            proxyRoot.hideFlags = HideFlags.HideAndDontSave;
            foreach (DrawItem item in drawItems)
            {
                GameObject proxy = new GameObject("proxy");
                proxy.hideFlags = HideFlags.HideAndDontSave;
                proxy.layer = captureLayer;
                proxy.transform.SetParent(proxyRoot.transform, false);
                proxy.transform.position = item.localToWorld.GetColumn(3);
                proxy.transform.rotation = item.localToWorld.rotation;
                proxy.transform.localScale = item.localToWorld.lossyScale;
                MeshFilter proxyFilter = proxy.AddComponent<MeshFilter>();
                proxyFilter.sharedMesh = item.mesh;
                MeshRenderer proxyRenderer = proxy.AddComponent<MeshRenderer>();
                Material[] proxyMaterials = new Material[Mathf.Max(1, item.subMeshIndex + 1)];
                for (int slot = 0; slot < proxyMaterials.Length; slot++)
                {
                    proxyMaterials[slot] = item.material;
                }
                proxyRenderer.sharedMaterials = proxyMaterials;
                proxyRenderer.shadowCastingMode = ShadowCastingMode.Off;
                proxyRenderer.receiveShadows = false;
            }
            camera.Render();

            RenderTexture.active = renderTexture;
            Texture2D captured = new Texture2D(pixelsPerSide, pixelsPerSide, TextureFormat.RGBA32, false, false);
            captured.ReadPixels(new Rect(0f, 0f, pixelsPerSide, pixelsPerSide), 0, 0);
            captured.Apply(false, false);
            captured.name = "StrategicColour";
            captured.wrapMode = TextureWrapMode.Clamp;
            if (LooksBlank(captured))
            {
                Debug.LogWarning("[Terrain] Top-down ground capture drew nothing for this chunk; keeping the mask-built colours.");
                Object.DestroyImmediate(captured);
                return null;
            }
            return captured;
        }
        finally
        {
            RenderTexture.active = previousActive;
            if (renderTexture != null)
            {
                renderTexture.Release();
                Object.DestroyImmediate(renderTexture);
            }
            Object.DestroyImmediate(cameraObject);
            if (proxyRoot != null)
            {
                Object.DestroyImmediate(proxyRoot);
            }
            foreach (ScriptableRendererFeature feature in disabledFeatures)
            {
                feature.SetActive(true);
            }
        }
    }

    // The renderer only draws the layers in its opaque and transparent masks (this project excludes 28 to 31), and the capture camera must see
    // nothing but its own proxies, so it needs a drawn layer that no scene renderer sits on.
    private static int FindUnusedDrawnLayer()
    {
        UniversalRenderPipelineAsset pipelineAsset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (pipelineAsset == null)
        {
            return -1;
        }
        SerializedObject pipelineObject = new SerializedObject(pipelineAsset);
        SerializedProperty rendererList = pipelineObject.FindProperty("m_RendererDataList");
        SerializedProperty defaultIndex = pipelineObject.FindProperty("m_DefaultRendererIndex");
        if (rendererList == null || defaultIndex == null || rendererList.arraySize == 0)
        {
            return -1;
        }
        UniversalRendererData rendererData = rendererList.GetArrayElementAtIndex(Mathf.Clamp(defaultIndex.intValue, 0, rendererList.arraySize - 1)).objectReferenceValue as UniversalRendererData;
        if (rendererData == null)
        {
            return -1;
        }

        int drawnLayers = rendererData.opaqueLayerMask & rendererData.transparentLayerMask;
        foreach (Renderer sceneRenderer in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include))
        {
            drawnLayers &= ~(1 << sceneRenderer.gameObject.layer);
        }
        for (int layer = 31; layer >= 0; layer--)
        {
            if ((drawnLayers & (1 << layer)) != 0)
            {
                return layer;
            }
        }
        return -1;
    }

    // The edit-mode render is darker than the ground in play (the runtime lighting globals are not set), so the capture is scaled per channel
    // until its mean over the painted area equals the mean of the reference texture, which was calibrated against the in-game ground.
    public static void MatchBrightness(Texture2D captured, Texture2D reference, Color32 backgroundColour)
    {
        Color32[] capturedPixels = captured.GetPixels32();
        Vector3 capturedMean;
        Vector3 referenceMean;
        if (!TryMeanOutsideBackground(capturedPixels, backgroundColour, out capturedMean)
            || !TryMeanOutsideBackground(reference.GetPixels32(), backgroundColour, out referenceMean))
        {
            return;
        }

        Vector3 gain = new Vector3(
            Mathf.Clamp(referenceMean.x / Mathf.Max(capturedMean.x, 1f), MinimumBrightnessGain, MaximumBrightnessGain),
            Mathf.Clamp(referenceMean.y / Mathf.Max(capturedMean.y, 1f), MinimumBrightnessGain, MaximumBrightnessGain),
            Mathf.Clamp(referenceMean.z / Mathf.Max(capturedMean.z, 1f), MinimumBrightnessGain, MaximumBrightnessGain));
        for (int index = 0; index < capturedPixels.Length; index++)
        {
            Color32 pixel = capturedPixels[index];
            capturedPixels[index] = new Color32(
                (byte)Mathf.Min(255f, pixel.r * gain.x), (byte)Mathf.Min(255f, pixel.g * gain.y), (byte)Mathf.Min(255f, pixel.b * gain.z), 255);
        }
        captured.SetPixels32(capturedPixels);
        captured.Apply(false, false);
    }

    private static bool TryMeanOutsideBackground(Color32[] pixels, Color32 backgroundColour, out Vector3 mean)
    {
        mean = Vector3.zero;
        int stride = MeanSamplePrimeStride;
        int counted = 0;
        for (int index = 0; index < pixels.Length; index += stride)
        {
            Color32 pixel = pixels[index];
            if (Mathf.Abs(pixel.r - backgroundColour.r) + Mathf.Abs(pixel.g - backgroundColour.g) + Mathf.Abs(pixel.b - backgroundColour.b) <= BackgroundTolerance)
            {
                continue;
            }
            mean += new Vector3(pixel.r, pixel.g, pixel.b);
            counted++;
        }
        if (counted == 0)
        {
            return false;
        }
        mean /= counted;
        return true;
    }

    // A capture that is one flat colour means nothing was drawn; using it would flatten the whole fallback layer.
    private static bool LooksBlank(Texture2D texture)
    {
        Color32[] pixels = texture.GetPixels32();
        // A prime stride: a power-of-two one lines up with the row length and only ever visits a couple of columns.
        int stride = BlankCheckPrimeStride;
        Color32 first = pixels[0];
        for (int index = 0; index < pixels.Length; index += stride)
        {
            Color32 pixel = pixels[index];
            if (Mathf.Abs(pixel.r - first.r) > 4 || Mathf.Abs(pixel.g - first.g) > 4 || Mathf.Abs(pixel.b - first.b) > 4)
            {
                return false;
            }
        }
        return true;
    }

    private static List<DrawItem> CollectDrawItems(Vector2 chunkMin, float chunkSizeMetres)
    {
        List<DrawItem> items = new List<DrawItem>();
        Rect chunkRect = new Rect(chunkMin.x, chunkMin.y, chunkSizeMetres, chunkSizeMetres);
        foreach (TerrainTile tile in Object.FindObjectsByType<TerrainTile>(FindObjectsInactive.Include))
        {
            if (tile.tileData == null)
            {
                continue;
            }
            Vector2 tileMin = tile.tileData.WorldMinCorner();
            Rect tileRect = new Rect(tileMin.x, tileMin.y, TerrainTileData.TileSizeMetres, TerrainTileData.TileSizeMetres);
            if (!chunkRect.Overlaps(tileRect))
            {
                continue;
            }

            // Disabled renderers are read on purpose: only the mesh and material matter here.
            foreach (MeshRenderer meshRenderer in tile.GetComponentsInChildren<MeshRenderer>(true))
            {
                MeshFilter meshFilter = meshRenderer.GetComponent<MeshFilter>();
                if (meshFilter == null || meshFilter.sharedMesh == null)
                {
                    continue;
                }
                Material[] materials = meshRenderer.sharedMaterials;
                for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                {
                    Material material = materials[materialIndex];
                    if (material == null || material.shader == null || System.Array.IndexOf(CapturedShaderNames, material.shader.name) < 0)
                    {
                        continue;
                    }
                    items.Add(new DrawItem
                    {
                        mesh = meshFilter.sharedMesh,
                        localToWorld = meshRenderer.localToWorldMatrix,
                        material = material,
                        subMeshIndex = Mathf.Min(materialIndex, meshFilter.sharedMesh.subMeshCount - 1),
                    });
                }
            }
        }
        return items;
    }

    // Edge-detect outlines and the zoom full-screen passes would be baked into the ground colours.
    private static List<ScriptableRendererFeature> DisablePostEffectFeatures()
    {
        List<ScriptableRendererFeature> disabled = new List<ScriptableRendererFeature>();
        UniversalRenderPipelineAsset pipelineAsset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (pipelineAsset == null)
        {
            return disabled;
        }
        SerializedObject pipelineObject = new SerializedObject(pipelineAsset);
        SerializedProperty rendererList = pipelineObject.FindProperty("m_RendererDataList");
        for (int index = 0; rendererList != null && index < rendererList.arraySize; index++)
        {
            UniversalRendererData rendererData = rendererList.GetArrayElementAtIndex(index).objectReferenceValue as UniversalRendererData;
            if (rendererData == null)
            {
                continue;
            }
            foreach (ScriptableRendererFeature feature in rendererData.rendererFeatures)
            {
                if (feature != null && feature.isActive && System.Array.IndexOf(OutlineFeatureTypeNames, feature.GetType().Name) >= 0)
                {
                    feature.SetActive(false);
                    disabled.Add(feature);
                }
            }
        }
        return disabled;
    }
}
#endif
