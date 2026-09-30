// Painted Ground sample builder.
// Menu: Tools > Painted Ground > Build Sample Island
//
// Builds, in the open scene:
//   - An island mesh (heightfield, ~64 x 64 m) with a sandy beach, a grass meadow, a forest
//     patch, a stone-topped plateau with real cliff faces, and a rocky outcrop, with
//     vertex-colour splat weights (R = grass, G = forest, B = stone, black = base/dirt).
//   - A water plane at y = 0 using Painted/Water.
//   - Materials for both shaders, wired to the placeholder textures in this folder.
//   - A campfire point light, a dim directional "moon" light, and an angled camera.
//
// Everything is placed under a "PaintedGround Sample" root so you can delete it in one go.

#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace PaintedGround
{
    public static class SampleIslandBuilder
    {
        const int   Res      = 129;    // vertices per side
        const float Size     = 64f;    // metres per side
        const float SeaLevel = 0f;

        [MenuItem("Tools/Painted Ground/Build Sample Island")]
        public static void Build()
        {
            string folder = FindFolder();
            if (folder == null)
            {
                EditorUtility.DisplayDialog("Painted Ground",
                    "Could not find the PaintedGround folder (looked for PaintedGround.shader).", "OK");
                return;
            }

            var groundShader = Shader.Find("Painted/Ground");
            var waterShader  = Shader.Find("Painted/Water");
            if (groundShader == null || waterShader == null)
            {
                EditorUtility.DisplayDialog("Painted Ground",
                    "Shaders 'Painted/Ground' and/or 'Painted/Water' not found. Check the console for compile errors.", "OK");
                return;
            }

            // ---- materials --------------------------------------------------------------
            var groundMat = LoadOrCreateMaterial(folder, "M_PaintedGround", groundShader);
            groundMat.SetTexture("_BaseTex",       LoadTex(folder, "T_Dirt.png"));
            groundMat.SetTexture("_LayerRTex",     LoadTex(folder, "T_Grass.png"));
            groundMat.SetTexture("_LayerGTex",     LoadTex(folder, "T_Forest.png"));
            groundMat.SetTexture("_LayerBTex",     LoadTex(folder, "T_Stone.png"));
            groundMat.SetTexture("_CliffTex",      LoadTex(folder, "T_Cliff.png"));
            groundMat.SetTexture("_MacroTex",      LoadTex(folder, "PaintedGround_Macro.png"));
            groundMat.SetTexture("_BlendNoiseTex", LoadTex(folder, "PaintedGround_Brush.png"));
            groundMat.SetTexture("_FringeTex",     LoadTex(folder, "T_EdgeFringe.png"));
            groundMat.EnableKeyword("_SPLAT_VERTEXCOLOR");
            groundMat.DisableKeyword("_SPLAT_WORLDTEXTURE");
            groundMat.EnableKeyword("_SECOND_SAMPLE");
            groundMat.EnableKeyword("_CLIFFS");

            // Same material again, but on the Shader Graph version of the ground (Painted/PaintedGround
            // graph). Swap it onto the Island to compare; both call the same HLSL.
            var graphShader = Shader.Find("Painted/PaintedGround");
            if (graphShader != null)
            {
                var graphMat = LoadOrCreateMaterial(folder, "M_PaintedGround_Graph", graphShader);
                graphMat.CopyMatchingPropertiesFromMaterial(groundMat);
                EditorUtility.SetDirty(graphMat);
            }

            var waterMat = LoadOrCreateMaterial(folder, "M_PaintedWater", waterShader);
            waterMat.SetTexture("_RippleTex", LoadTex(folder, "PaintedGround_Brush.png"));

            // ---- scene objects ----------------------------------------------------------
            var old = GameObject.Find("PaintedGround Sample");
            if (old != null) Undo.DestroyObjectImmediate(old);

            var root = new GameObject("PaintedGround Sample");
            Undo.RegisterCreatedObjectUndo(root, "Build Sample Island");

            var island = new GameObject("Island");
            island.transform.SetParent(root.transform, false);
            var mf = island.AddComponent<MeshFilter>();
            var mr = island.AddComponent<MeshRenderer>();
            mf.sharedMesh = BuildIslandMesh(folder);
            mr.sharedMaterial = groundMat;
            mr.shadowCastingMode = ShadowCastingMode.On;
            island.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;

            // Bake the vertex-colour splats to a world-space texture: the painted borders take
            // their direction from its gradient, which is smooth (vertex colours are per-triangle).
            var splat = SplatBaker.Bake(island, 1024, out var splatBounds);
            if (splat != null)
            {
                groundMat.SetTexture("_SplatMap", splat);
                groundMat.SetVector("_SplatBounds", splatBounds);
                groundMat.SetFloat("_Splat", 1f);
                groundMat.EnableKeyword("_SPLAT_WORLDTEXTURE");
                groundMat.DisableKeyword("_SPLAT_VERTEXCOLOR");
                EditorUtility.SetDirty(groundMat);
                var gm = AssetDatabase.LoadAssetAtPath<Material>(folder + "/Materials/M_PaintedGround_Graph.mat");
                if (gm != null) { gm.SetTexture("_SplatMap", splat); gm.SetVector("_SplatBounds", splatBounds); EditorUtility.SetDirty(gm); }
            }

            var water = GameObject.CreatePrimitive(PrimitiveType.Plane);
            water.name = "Water";
            Object.DestroyImmediate(water.GetComponent<Collider>());
            water.transform.SetParent(root.transform, false);
            water.transform.position   = new Vector3(0, SeaLevel, 0);
            water.transform.localScale = new Vector3(Size / 10f * 1.5f, 1, Size / 10f * 1.5f);
            var wr = water.GetComponent<MeshRenderer>();
            wr.sharedMaterial = waterMat;
            wr.shadowCastingMode = ShadowCastingMode.Off;

            // Campfire on the meadow
            var fire = new GameObject("Campfire Light");
            fire.transform.SetParent(root.transform, false);
            fire.transform.position = new Vector3(-6f, HeightAt(-6f, 4f) + 0.6f, 4f);
            var fl = fire.AddComponent<Light>();
            fl.type = LightType.Point; fl.color = new Color(1f, 0.62f, 0.28f);
            fl.intensity = 6f; fl.range = 11f; fl.shadows = LightShadows.Soft;
            fire.AddComponent<CampfireFlicker>();

            // Dim, cool moon so the fire reads
            var moon = new GameObject("Moon Light");
            moon.transform.SetParent(root.transform, false);
            moon.transform.rotation = Quaternion.Euler(55f, -35f, 0f);
            var ml = moon.AddComponent<Light>();
            ml.type = LightType.Directional; ml.color = new Color(0.55f, 0.62f, 0.78f);
            ml.intensity = 0.55f; ml.shadows = LightShadows.Soft; ml.shadowStrength = 0.6f;

            // Camera: the Klei angle is roughly 45 degrees down, slightly wide
            var cam = Camera.main;
            if (cam == null)
            {
                var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
                camGo.transform.SetParent(root.transform, false);
                cam = camGo.AddComponent<Camera>();
                camGo.AddComponent<AudioListener>();
            }
            cam.transform.position = new Vector3(0f, 22f, -22f);
            cam.transform.rotation = Quaternion.Euler(45f, 0f, 0f);
            cam.fieldOfView = 35f;
            cam.backgroundColor = new Color(0.03f, 0.03f, 0.05f);
            cam.clearFlags = CameraClearFlags.SolidColor;

            RenderSettings.ambientMode  = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.30f, 0.32f, 0.38f);
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 30f; RenderSettings.fogEndDistance = 80f;
            RenderSettings.fogColor = new Color(0.04f, 0.045f, 0.06f);

            // Decals: cracks, stains, leaf litter, puddles... chosen per biome, plus a scorch under the fire
            if (Shader.Find("Painted/Decal") != null)
                DecalScatter.Scatter(island, 220, SeaLevel, fire.transform.position);

            // Ground fog drifting through the forest. (Grass is not an object in this style: it
            // is the painted fringe where the grass turf meets dirt, handled in the ground shader.
            // Tools > Painted Ground > Build Cliff Grass Cards exists if you want tufts as props.)
            if (Shader.Find("Painted/Fog") != null)
                FogCardScatter.Scatter(island, 40, SeaLevel);

            Selection.activeGameObject = root;
            Debug.Log("[Painted Ground] Sample island built. Water, decal and fog shaders need Depth Texture ON in the URP asset. " +
                      "For the paper grain / vignette run Tools > Painted Ground > Add Painted Post Effect to Renderer.");
        }

        // ---- island heightfield --------------------------------------------------------------
        // Layout (metres, origin at centre):
        //   west  side  : long gentle beach into the water
        //   centre      : meadow (grass), with the campfire
        //   north-east  : plateau at +5 m with near-vertical cliffs, stone on top
        //   south       : forest patch
        //   east tip    : rocky outcrop dropping into the sea

        static float HeightAt(float x, float z)
        {
            float nx = x / (Size * 0.5f), nz = z / (Size * 0.5f);
            float r = Mathf.Sqrt(nx * nx + nz * nz);

            // base island: raised disc that falls into the sea, wobbly edge
            float wob = 0.12f * Mathf.Sin(3f * Mathf.Atan2(nz, nx) + 1.3f) + 0.08f * Mathf.Sin(7f * Mathf.Atan2(nz, nx));
            float edge = 0.82f + wob;
            float h = Mathf.SmoothStep(-2.5f, 1.6f, 1f - Mathf.Clamp01((r - edge + 0.35f) / 0.35f));

            // beach: flatten the west side so it slopes slowly into the water
            float beach = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((-nx - 0.15f) / 0.45f));
            h = Mathf.Lerp(h, Mathf.Lerp(-1.2f, 0.9f, Mathf.Clamp01(1f - (r - 0.35f) / 0.6f)), beach * 0.9f);

            // plateau NE with sharp cliff
            float pd = Mathf.Sqrt((nx - 0.32f) * (nx - 0.32f) + (nz - 0.30f) * (nz - 0.30f));
            float pwob = 0.03f * Mathf.Sin(5f * Mathf.Atan2(nz - 0.30f, nx - 0.32f));
            float plateau = 1f - Mathf.Clamp01((pd - (0.30f + pwob)) / 0.035f);   // ~1 m wide cliff band
            plateau = Mathf.SmoothStep(0f, 1f, plateau);
            h = Mathf.Max(h, Mathf.Lerp(h, 5.2f, plateau));

            // rocky outcrop on the east tip, above water so cliffs meet the sea
            float od = Mathf.Sqrt((nx - 0.70f) * (nx - 0.70f) + (nz + 0.15f) * (nz + 0.15f));
            float rock = Mathf.SmoothStep(0f, 1f, 1f - Mathf.Clamp01((od - 0.10f) / 0.05f));
            h = Mathf.Max(h, Mathf.Lerp(h, 2.4f, rock));

            // gentle rolling noise
            h += 0.18f * Mathf.Sin(x * 0.55f + 1.1f) * Mathf.Cos(z * 0.48f) + 0.10f * Mathf.Sin(x * 1.7f - z * 1.3f);
            return h;
        }

        static Color SplatAt(float x, float z, float h)
        {
            float nx = x / (Size * 0.5f), nz = z / (Size * 0.5f);
            float r = Mathf.Sqrt(nx * nx + nz * nz);

            // grass: interior above the beach, fading out toward sand and up the plateau
            float grass = Mathf.Clamp01((h - 0.8f) / 0.6f) * (1f - Mathf.Clamp01((h - 3.2f) / 1.5f));
            grass *= 1f - Mathf.Clamp01((r - 0.62f) / 0.15f);

            // forest: southern patch
            float fd = Mathf.Sqrt((nx + 0.05f) * (nx + 0.05f) + (nz + 0.45f) * (nz + 0.45f));
            float forest = (1f - Mathf.Clamp01((fd - 0.22f) / 0.12f)) * Mathf.Clamp01((h - 0.6f) / 0.5f);

            // stone: plateau top and outcrop
            float stone = Mathf.Clamp01((h - 4.4f) / 0.6f);
            float od = Mathf.Sqrt((nx - 0.70f) * (nx - 0.70f) + (nz + 0.15f) * (nz + 0.15f));
            stone = Mathf.Max(stone, 1f - Mathf.Clamp01((od - 0.06f) / 0.06f));

            // a worn dirt path from the beach to the campfire so the base layer shows
            float pathD = Mathf.Abs(nz - 0.12f - 0.25f * Mathf.Sin(nx * 4f));
            float path = (1f - Mathf.Clamp01((pathD - 0.03f) / 0.03f)) * Mathf.Clamp01((nx + 0.6f) / 0.2f) * Mathf.Clamp01((0.25f - nx) / 0.2f);
            grass *= 1f - path; forest *= 1f - path;

            return new Color(grass, forest * (1f - grass * 0.3f), stone, 1f);
        }

        static Mesh BuildIslandMesh(string folder)
        {
            int n = Res;
            var verts = new Vector3[n * n];
            var cols  = new Color[n * n];
            var uvs   = new Vector2[n * n];
            for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                float x = (i / (float)(n - 1) - 0.5f) * Size;
                float z = (j / (float)(n - 1) - 0.5f) * Size;
                float h = HeightAt(x, z);
                int k = j * n + i;
                verts[k] = new Vector3(x, h, z);
                cols[k]  = SplatAt(x, z, h);
                uvs[k]   = new Vector2(i / (float)(n - 1), j / (float)(n - 1));
            }
            var tris = new int[(n - 1) * (n - 1) * 6];
            int t = 0;
            for (int j = 0; j < n - 1; j++)
            for (int i = 0; i < n - 1; i++)
            {
                int a = j * n + i, b = a + 1, c = a + n, d = c + 1;
                tris[t++] = a; tris[t++] = c; tris[t++] = b;
                tris[t++] = b; tris[t++] = c; tris[t++] = d;
            }
            var mesh = new Mesh { name = "SampleIsland", indexFormat = IndexFormat.UInt32 };
            mesh.vertices = verts; mesh.colors = cols; mesh.uv = uvs; mesh.triangles = tris;
            mesh.RecalculateNormals(); mesh.RecalculateBounds();

            string path = folder + "/Generated/SampleIsland.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        // ---- helpers ----------------------------------------------------------------------------
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

        static Material LoadOrCreateMaterial(string folder, string name, Shader shader)
        {
            string path = folder + "/Materials/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
            else m.shader = shader;
            return m;
        }

        static Texture2D LoadTex(string folder, string file)
        {
            string sub = file.StartsWith("PaintedGround_") ? "Noise" : file == "T_EdgeFringe.png" ? "Edges" : "Layers";
            string path = folder + "/Textures/" + sub + "/" + file;
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp != null)
            {
                bool linear = file.StartsWith("PaintedGround_") || file == "T_EdgeFringe.png";   // data, not colour
                bool dirty = false;
                if (imp.wrapMode != TextureWrapMode.Repeat) { imp.wrapMode = TextureWrapMode.Repeat; dirty = true; }
                if (imp.sRGBTexture == linear) { imp.sRGBTexture = !linear; dirty = true; }
                if (linear && imp.textureCompression != TextureImporterCompression.Uncompressed)
                { imp.textureCompression = TextureImporterCompression.Uncompressed; dirty = true; }
                if (imp.anisoLevel < 4) { imp.anisoLevel = 4; dirty = true; }
                if (file == "T_EdgeFringe.png")
                {
                    // the strip is heavily minified on screen; keep the blades crisp
                    if (imp.anisoLevel < 16) { imp.anisoLevel = 16; dirty = true; }
                    if (imp.mipMapBias > -1f) { imp.mipMapBias = -1f; dirty = true; }
                }
                if (dirty) imp.SaveAndReimport();
            }
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex == null) Debug.LogWarning("[Painted Ground] Missing texture " + path);
            return tex;
        }
    }
}
#endif
