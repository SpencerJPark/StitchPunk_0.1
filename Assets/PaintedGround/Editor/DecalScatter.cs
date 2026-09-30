// Decal scatter tool.
// Menu: Tools > Painted Ground > Scatter Decals on Selected Ground
//       Tools > Painted Ground > Clear Scattered Decals
//
// Raycasts down onto the selected object's collider and drops painted decals, choosing the
// decal type from the ground's vertex-colour splat under each hit (leaves in forest, cracks
// and pebbles on stone, puddles on dirt, moss in shade...). Skips water and steep slopes.
// Decals go under a "Decals" child so you can delete them in one go.

#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PaintedGround
{
    public static class DecalScatter
    {
        // Atlas tiles
        public const int Crack = 0, Crack2 = 1, Stain = 2, Scorch = 3, Puddle = 4, Leaves = 5, Pebbles = 6, Moss = 7;

        struct Rule
        {
            public int tile; public bool multiply; public float minSize, maxSize;
            public float onDirt, onGrass, onForest, onStone;   // relative weights per splat
        }

        static readonly Rule[] Rules =
        {
            new Rule { tile = Crack,   multiply = true,  minSize = 1.6f, maxSize = 3.2f, onDirt = 0.6f, onGrass = 0.05f, onForest = 0.0f, onStone = 1.4f },
            new Rule { tile = Crack2,  multiply = true,  minSize = 1.6f, maxSize = 3.2f, onDirt = 0.6f, onGrass = 0.05f, onForest = 0.0f, onStone = 1.4f },
            new Rule { tile = Stain,   multiply = true,  minSize = 1.5f, maxSize = 3.5f, onDirt = 1.0f, onGrass = 0.35f, onForest = 0.6f, onStone = 0.5f },
            new Rule { tile = Puddle,  multiply = false, minSize = 1.2f, maxSize = 2.4f, onDirt = 0.7f, onGrass = 0.10f, onForest = 0.2f, onStone = 0.2f },
            new Rule { tile = Leaves,  multiply = false, minSize = 1.8f, maxSize = 3.2f, onDirt = 0.2f, onGrass = 0.30f, onForest = 1.6f, onStone = 0.1f },
            new Rule { tile = Pebbles, multiply = false, minSize = 1.2f, maxSize = 2.2f, onDirt = 0.8f, onGrass = 0.15f, onForest = 0.1f, onStone = 1.0f },
            new Rule { tile = Moss,    multiply = false, minSize = 1.4f, maxSize = 2.8f, onDirt = 0.2f, onGrass = 0.50f, onForest = 1.0f, onStone = 0.5f },
        };

        [MenuItem("Tools/Painted Ground/Scatter Decals on Selected Ground")]
        public static void ScatterOnSelection()
        {
            var go = Selection.activeGameObject;
            if (go == null || go.GetComponent<Collider>() == null)
            {
                EditorUtility.DisplayDialog("Painted Ground", "Select a ground object that has a collider (the sample Island works).", "OK");
                return;
            }
            Scatter(go, 220, 0f, null);
        }

        [MenuItem("Tools/Painted Ground/Clear Scattered Decals")]
        public static void Clear()
        {
            foreach (var d in Object.FindObjectsByType<PaintedDecal>(FindObjectsSortMode.None))
                if (d.transform.parent != null && d.transform.parent.name == "Decals")
                    Undo.DestroyObjectImmediate(d.gameObject);
        }

        /// <param name="waterLevel">hits below this height are skipped</param>
        /// <param name="scorchAt">optional world point that gets a scorch mark (a campfire)</param>
        public static void Scatter(GameObject ground, int count, float waterLevel, Vector3? scorchAt, int seed = 12345)
        {
            string folder = FindFolder();
            if (folder == null) { Debug.LogError("[Painted Ground] folder not found"); return; }
            var shader = Shader.Find("Painted/Decal");
            if (shader == null) { Debug.LogError("[Painted Ground] Painted/Decal shader not found"); return; }

            string atlasPath = folder + "/Textures/Decals/T_DecalAtlas.png";
            if (AssetImporter.GetAtPath(atlasPath) is TextureImporter ti &&
                (ti.wrapMode != TextureWrapMode.Clamp || ti.alphaIsTransparency == false || ti.anisoLevel < 4))
            {
                ti.wrapMode = TextureWrapMode.Clamp;      // no bleeding between atlas cells
                ti.alphaIsTransparency = true;            // dilates colour into transparent pixels
                ti.anisoLevel = 4;
                ti.SaveAndReimport();
            }
            var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(atlasPath);
            var matMul = LoadOrCreateMaterial(folder, "M_Decal_Multiply", shader, atlas, true);
            var matNrm = LoadOrCreateMaterial(folder, "M_Decal_Normal",   shader, atlas, false);
            var cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");

            var col = ground.GetComponent<Collider>();
            var mc = col as MeshCollider;
            var mesh = mc != null ? mc.sharedMesh : null;
            Color[] vcols = (mesh != null && mesh.colors != null && mesh.colors.Length == mesh.vertexCount) ? mesh.colors : null;
            int[] tris = mesh != null ? mesh.triangles : null;

            var parent = ground.transform.Find("Decals");
            if (parent == null)
            {
                var p = new GameObject("Decals");
                Undo.RegisterCreatedObjectUndo(p, "Scatter Decals");
                p.transform.SetParent(ground.transform, false);
                parent = p.transform;
            }

            Physics.SyncTransforms();
            var b = col.bounds;
            var rng = new System.Random(seed);
            int placed = 0, tries = 0;
            var placedPositions = new System.Collections.Generic.List<Vector3>();

            while (placed < count && tries < count * 30)
            {
                tries++;
                float x = Mathf.Lerp(b.min.x, b.max.x, (float)rng.NextDouble());
                float z = Mathf.Lerp(b.min.z, b.max.z, (float)rng.NextDouble());
                var ray = new Ray(new Vector3(x, b.max.y + 10f, z), Vector3.down);
                if (!col.Raycast(ray, out var hit, b.size.y + 20f)) continue;
                if (hit.point.y < waterLevel + 0.15f) continue;      // water / shoreline
                if (hit.normal.y < 0.75f) continue;                    // cliffs

                // splat under the hit from vertex colours (barycentric)
                float grass = 0, forest = 0, stone = 0;
                if (vcols != null && tris != null && hit.triangleIndex >= 0)
                {
                    int t = hit.triangleIndex * 3;
                    var bc = hit.barycentricCoordinate;
                    Color c = vcols[tris[t]] * bc.x + vcols[tris[t + 1]] * bc.y + vcols[tris[t + 2]] * bc.z;
                    grass = c.r; forest = c.g; stone = c.b;
                }
                float dirt = Mathf.Clamp01(1f - grass - forest - stone);

                // choose a rule by weighted lottery
                float total = 0f;
                var weights = new float[Rules.Length];
                for (int i = 0; i < Rules.Length; i++)
                {
                    var r = Rules[i];
                    weights[i] = r.onDirt * dirt + r.onGrass * grass + r.onForest * forest + r.onStone * stone;
                    total += weights[i];
                }
                if (total <= 0.001f) continue;
                float pick = (float)rng.NextDouble() * total;
                int idx = 0;
                for (; idx < Rules.Length - 1; idx++) { pick -= weights[idx]; if (pick <= 0f) break; }
                var rule = Rules[idx];

                float size = Mathf.Lerp(rule.minSize, rule.maxSize, (float)rng.NextDouble());

                // keep decals from piling up
                bool tooClose = false;
                foreach (var p in placedPositions)
                    if ((p - hit.point).sqrMagnitude < size * size * 0.35f) { tooClose = true; break; }
                if (tooClose) continue;

                var d = PaintedDecal.Create(rule.multiply ? matMul : matNrm, cube, hit.point,
                    (float)rng.NextDouble() * 360f, size, 1.5f, rule.tile, parent, "Decal_" + rule.tile);
                d.opacity = Mathf.Lerp(0.7f, 1f, (float)rng.NextDouble());
                d.Apply();
                Undo.RegisterCreatedObjectUndo(d.gameObject, "Scatter Decals");
                placedPositions.Add(hit.point);
                placed++;
            }

            if (scorchAt.HasValue)
            {
                var ray = new Ray(scorchAt.Value + Vector3.up * 10f, Vector3.down);
                if (col.Raycast(ray, out var hit, 40f))
                {
                    var s = PaintedDecal.Create(matMul, cube, hit.point, 0f, 3.2f, 1.5f, Scorch, parent, "Decal_Scorch");
                    Undo.RegisterCreatedObjectUndo(s.gameObject, "Scatter Decals");
                }
            }

            Debug.Log($"[Painted Ground] Scattered {placed} decals under '{ground.name}/Decals'.");
        }

        // ---- helpers ----
        static Material LoadOrCreateMaterial(string folder, string name, Shader shader, Texture2D atlas, bool multiply)
        {
            string path = folder + "/Materials/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
            m.shader = shader;
            m.SetTexture("_Atlas", atlas);
            m.SetVector("_AtlasGrid", new Vector4(4, 2, 0, 0));
            if (multiply)
            {
                m.SetFloat("_Mode", 0); m.EnableKeyword("_MODE_MULTIPLY"); m.DisableKeyword("_MODE_NORMAL");
                m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.DstColor);
                m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero);
            }
            else
            {
                m.SetFloat("_Mode", 1); m.EnableKeyword("_MODE_NORMAL"); m.DisableKeyword("_MODE_MULTIPLY");
                m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            }
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
