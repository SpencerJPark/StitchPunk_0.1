// Fog card scatter tool.
// Menu: Tools > Painted Ground > Scatter Fog Cards on Selected Ground
//       Tools > Painted Ground > Clear Fog Cards
//
// Scatters large, flat, horizontal quads just above the ground so a ground-fog shader
// (Painted/Fog) has something to drift across. Cards bias toward forest areas (read from
// the ground's vertex-colour splat) but a few are scattered everywhere; water and steep
// slopes are skipped. Cards go under a "FogCards" child so you can delete them in one go.

#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PaintedGround
{
    public static class FogCardScatter
    {
        [MenuItem("Tools/Painted Ground/Scatter Fog Cards on Selected Ground")]
        public static void ScatterOnSelection()
        {
            var go = Selection.activeGameObject;
            if (go == null || go.GetComponent<Collider>() == null)
            {
                EditorUtility.DisplayDialog("Painted Ground", "Select a ground object that has a collider (the sample Island works).", "OK");
                return;
            }
            Scatter(go, 40, 0f);
        }

        [MenuItem("Tools/Painted Ground/Clear Fog Cards")]
        public static void Clear()
        {
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
            {
                if (t.name != "FogCards") continue;
                for (int i = t.childCount - 1; i >= 0; i--)
                    Undo.DestroyObjectImmediate(t.GetChild(i).gameObject);
            }
        }

        /// <param name="waterLevel">hits below waterLevel + 0.2 are skipped</param>
        public static void Scatter(GameObject ground, int count, float waterLevel, int seed = 4242)
        {
            string folder = FindFolder();
            if (folder == null) { Debug.LogError("[Painted Ground] folder not found"); return; }
            var shader = Shader.Find("Painted/Fog");
            if (shader == null) { Debug.LogError("[Painted Ground] Painted/Fog shader not found"); return; }

            var noise = AssetDatabase.LoadAssetAtPath<Texture2D>(folder + "/Textures/Noise/PaintedGround_Brush.png");
            var mat = LoadOrCreateMaterial(folder, "M_FogCard", shader, noise);
            var quad = LoadOrCreateQuadMesh(folder);

            var col = ground.GetComponent<Collider>();
            if (col == null) { Debug.LogError("[Painted Ground] '" + ground.name + "' has no Collider."); return; }
            var mc = col as MeshCollider;
            var mesh = mc != null ? mc.sharedMesh : null;
            Color[] vcols = (mesh != null && mesh.colors != null && mesh.colors.Length == mesh.vertexCount) ? mesh.colors : null;
            int[] tris = mesh != null ? mesh.triangles : null;

            var parent = ground.transform.Find("FogCards");
            if (parent == null)
            {
                var p = new GameObject("FogCards");
                Undo.RegisterCreatedObjectUndo(p, "Scatter Fog Cards");
                p.transform.SetParent(ground.transform, false);
                parent = p.transform;
            }

            Physics.SyncTransforms();
            var b = col.bounds;
            var rng = new System.Random(seed);
            int placed = 0, tries = 0;
            var placedPositions = new List<Vector3>();
            const float minDist = 3f;

            while (placed < count && tries < count * 40)
            {
                tries++;
                float x = Mathf.Lerp(b.min.x, b.max.x, (float)rng.NextDouble());
                float z = Mathf.Lerp(b.min.z, b.max.z, (float)rng.NextDouble());
                var ray = new Ray(new Vector3(x, b.max.y + 10f, z), Vector3.down);
                if (!col.Raycast(ray, out var hit, b.size.y + 20f)) continue;
                if (hit.point.y < waterLevel + 0.2f) continue;
                if (hit.normal.y < 0.8f) continue;

                // splat under the hit from vertex colours (barycentric)
                float grass = 0, forest = 0;
                if (vcols != null && tris != null && hit.triangleIndex >= 0)
                {
                    int t = hit.triangleIndex * 3;
                    var bc = hit.barycentricCoordinate;
                    Color c = vcols[tris[t]] * bc.x + vcols[tris[t + 1]] * bc.y + vcols[tris[t + 2]] * bc.z;
                    grass = c.r; forest = c.g;
                }

                float weight = 0.25f + 1.5f * forest + 0.4f * grass;
                if ((float)rng.NextDouble() > weight / 1.75f) continue;

                bool tooClose = false;
                foreach (var p in placedPositions)
                    if ((p - hit.point).sqrMagnitude < minDist * minDist) { tooClose = true; break; }
                if (tooClose) continue;

                var go = new GameObject("FogCard");
                Undo.RegisterCreatedObjectUndo(go, "Scatter Fog Cards");
                go.transform.SetParent(parent, false);
                go.transform.position = hit.point + Vector3.up * 0.35f;
                go.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                float sx = Mathf.Lerp(6f, 11f, (float)rng.NextDouble());
                float sz = Mathf.Lerp(6f, 11f, (float)rng.NextDouble());
                go.transform.localScale = new Vector3(sx, 1f, sz);

                var mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = quad;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;

                placedPositions.Add(hit.point);
                placed++;
            }

            Debug.Log($"[Painted Ground] Scattered {placed} fog cards under '{ground.name}/FogCards'.");
        }

        // ---- helpers ----
        static Material LoadOrCreateMaterial(string folder, string name, Shader shader, Texture2D noise)
        {
            string path = folder + "/Materials/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
            m.shader = shader;
            m.SetTexture("_NoiseTex", noise);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Mesh LoadOrCreateQuadMesh(string folder)
        {
            string path = folder + "/Generated/FogCardQuad.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh != null) return mesh;

            mesh = new Mesh { name = "FogCardQuad" };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, 0f, -0.5f),
                new Vector3(0.5f, 0f, -0.5f),
                new Vector3(-0.5f, 0f, 0.5f),
                new Vector3(0.5f, 0f, 0.5f),
            };
            mesh.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
            };
            mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            mesh.RecalculateBounds();

            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
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
