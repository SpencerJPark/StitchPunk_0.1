// Cliff grass skirt builder.
// Menu: Tools > Painted Ground > Build Cliff Grass Cards on Selected Ground
//       Tools > Painted Ground > Clear Cliff Grass Cards
//
// Finds the "lip" edges of a ground mesh -- edges shared by a flat triangle and a steep
// (cliff-face) triangle -- and lines them with painted grass card quads that lean out over
// the drop, so cliff tops read as hand-drawn tufts hanging over the edge (Don't Starve style).

#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace PaintedGround
{
    public static class CliffSkirtBuilder
    {
        const float FlatNormalY = 0.72f;
        const float SteepNormalY = 0.55f;
        const float Spacing = 0.9f;
        const float JitterFrac = 0.30f;
        const float OutwardOffset = 0.15f;
        const float TiltDegrees = -25f;
        const int MaxCards = 3000;

        [MenuItem("Tools/Painted Ground/Build Cliff Grass Cards on Selected Ground")]
        public static void BuildOnSelection()
        {
            var go = Selection.activeGameObject;
            var mf = go != null ? go.GetComponent<MeshFilter>() : null;
            if (go == null || mf == null || mf.sharedMesh == null)
            {
                EditorUtility.DisplayDialog("Painted Ground", "Select a ground object that has a MeshFilter with a mesh (the sample Island works).", "OK");
                return;
            }
            Build(go, 0f);
        }

        [MenuItem("Tools/Painted Ground/Clear Cliff Grass Cards")]
        public static void Clear()
        {
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
            {
                if (t.name != "CliffGrass") continue;
                for (int i = t.childCount - 1; i >= 0; i--)
                    Undo.DestroyObjectImmediate(t.GetChild(i).gameObject);
            }
        }

        /// <param name="waterLevel">lip edges below waterLevel + 0.3 are skipped</param>
        public static void Build(GameObject ground, float waterLevel, int seed = 777)
        {
            var mf = ground != null ? ground.GetComponent<MeshFilter>() : null;
            if (mf == null || mf.sharedMesh == null)
            {
                EditorUtility.DisplayDialog("Painted Ground", "Selected object has no MeshFilter/mesh.", "OK");
                return;
            }

            string folder = FindFolder();
            if (folder == null) { Debug.LogError("[Painted Ground] folder not found"); return; }
            var shader = Shader.Find("Painted/Card");
            if (shader == null) { Debug.LogError("[Painted Ground] Painted/Card shader not found"); return; }

            string texPath = folder + "/Textures/Cards/T_GrassTuft.png";
            if (AssetImporter.GetAtPath(texPath) is TextureImporter ti &&
                (ti.wrapMode != TextureWrapMode.Clamp || ti.alphaIsTransparency == false))
            {
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.alphaIsTransparency = true;
                ti.SaveAndReimport();
            }
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            var mat = LoadOrCreateMaterial(folder, "M_GrassCard", shader, tex);
            var quad = LoadOrCreateQuad(folder);

            var mesh = mf.sharedMesh;
            var xform = ground.transform;
            var localVerts = mesh.vertices;
            var worldVerts = new Vector3[localVerts.Length];
            for (int i = 0; i < localVerts.Length; i++)
                worldVerts[i] = xform.TransformPoint(localVerts[i]);

            int[] tris = mesh.triangles;
            int triCount = tris.Length / 3;
            var faceNormals = new Vector3[triCount];
            var centroids = new Vector3[triCount];
            for (int t = 0; t < triCount; t++)
            {
                int a = tris[t * 3], b = tris[t * 3 + 1], c = tris[t * 3 + 2];
                Vector3 pa = worldVerts[a], pb = worldVerts[b], pc = worldVerts[c];
                faceNormals[t] = Vector3.Cross(pb - pa, pc - pa).normalized;
                centroids[t] = (pa + pb + pc) / 3f;
            }

            // edge (ordered, smaller index first) -> triangle indices that share it
            var edgeMap = new Dictionary<(int, int), List<int>>();
            for (int t = 0; t < triCount; t++)
            {
                int a = tris[t * 3], b = tris[t * 3 + 1], c = tris[t * 3 + 2];
                AddEdge(edgeMap, a, b, t);
                AddEdge(edgeMap, b, c, t);
                AddEdge(edgeMap, c, a, t);
            }

            var rng = new System.Random(seed);
            var cliffGrass = ground.transform.Find("CliffGrass");
            if (cliffGrass == null)
            {
                var p = new GameObject("CliffGrass");
                Undo.RegisterCreatedObjectUndo(p, "Build Cliff Grass Cards");
                p.transform.SetParent(ground.transform, false);
                cliffGrass = p.transform;
            }

            int placed = 0;
            foreach (var kvp in edgeMap)
            {
                if (placed >= MaxCards) break;
                var trisOnEdge = kvp.Value;
                if (trisOnEdge.Count != 2) continue;

                int t0 = trisOnEdge[0], t1 = trisOnEdge[1];
                int flatTri, steepTri;
                if (faceNormals[t0].y >= FlatNormalY && faceNormals[t1].y <= SteepNormalY) { flatTri = t0; steepTri = t1; }
                else if (faceNormals[t1].y >= FlatNormalY && faceNormals[t0].y <= SteepNormalY) { flatTri = t1; steepTri = t0; }
                else continue;

                int ia = kvp.Key.Item1, ib = kvp.Key.Item2;
                Vector3 pa = worldVerts[ia], pb = worldVerts[ib];
                Vector3 m = (pa + pb) * 0.5f;
                if (m.y < waterLevel + 0.3f) continue;

                float edgeLen = Vector3.Distance(pa, pb);
                if (edgeLen < 0.001f) continue;
                Vector3 e = (pb - pa) / edgeLen;

                Vector3 o = centroids[steepTri] - centroids[flatTri];
                o.y = 0f;
                if (o.sqrMagnitude < 0.0001f) continue;
                o.Normalize();

                // Walk from vertex a to vertex b along the edge, dropping cards every ~Spacing
                // metres (jittered). "m" here is the current walk point, not the edge midpoint
                // used for the water-level test above.
                float walked = 0f;
                while (walked <= edgeLen && placed < MaxCards)
                {
                    Vector3 walkPoint = pa + e * walked;
                    Vector3 pos = walkPoint + o * OutwardOffset;

                    Quaternion rot = Quaternion.LookRotation(o, Vector3.up) * Quaternion.AngleAxis(TiltDegrees, Vector3.right);

                    float width = Mathf.Lerp(0.9f, 1.5f, (float)rng.NextDouble());
                    float height = Mathf.Lerp(0.6f, 1.1f, (float)rng.NextDouble());
                    bool flip = rng.NextDouble() < 0.5;

                    var card = new GameObject("GrassCard");
                    Undo.RegisterCreatedObjectUndo(card, "Build Cliff Grass Cards");
                    card.transform.SetParent(cliffGrass, false);
                    card.transform.position = pos;
                    card.transform.rotation = rot;
                    card.transform.localScale = new Vector3(flip ? -width : width, height, 1f);

                    var cardMf = card.AddComponent<MeshFilter>();
                    cardMf.sharedMesh = quad;
                    var cardMr = card.AddComponent<MeshRenderer>();
                    cardMr.sharedMaterial = mat;
                    cardMr.shadowCastingMode = ShadowCastingMode.Off;
                    cardMr.receiveShadows = false;
                    cardMr.lightProbeUsage = LightProbeUsage.Off;

                    placed++;

                    float jitter = 1f + ((float)rng.NextDouble() * 2f - 1f) * JitterFrac;
                    walked += Spacing * jitter;
                }
            }

            Debug.Log($"[Painted Ground] Placed {placed} cliff grass cards under '{ground.name}/CliffGrass'.");
        }

        // ---- helpers ----
        static void AddEdge(Dictionary<(int, int), List<int>> map, int a, int b, int triIndex)
        {
            var key = a < b ? (a, b) : (b, a);
            if (!map.TryGetValue(key, out var list)) { list = new List<int>(); map[key] = list; }
            list.Add(triIndex);
        }

        static Mesh LoadOrCreateQuad(string folder)
        {
            string path = folder + "/Generated/GrassCardQuad.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) return existing;

            var mesh = new Mesh { name = "GrassCardQuad" };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, 0f, 0f),
                new Vector3(0.5f, 0f, 0f),
                new Vector3(-0.5f, 1f, 0f),
                new Vector3(0.5f, 1f, 0f),
            };
            mesh.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
            };
            mesh.normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward };
            mesh.triangles = new[] { 0, 1, 2, 1, 3, 2 };
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        static Material LoadOrCreateMaterial(string folder, string name, Shader shader, Texture2D tex)
        {
            string path = folder + "/Materials/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
            m.shader = shader;
            m.SetTexture("_MainTex", tex);
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
