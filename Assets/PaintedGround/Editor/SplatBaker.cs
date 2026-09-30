// Splat map baker.
// Menu: Tools > Painted Ground > Bake Splat Map from Vertex Colours
//
// Bakes a ground mesh's vertex-colour splat weights into a top-down world-space PNG so the
// ground shader can read a smooth splat map (and its gradient) instead of per-triangle vertex
// colours. Raycasts straight down onto the selected object's MeshCollider, one ray per texel,
// and interpolates the hit triangle's vertex colours by barycentric coordinate.
//
// SDF encoding: after the raster + blur, each of R/G/B is converted independently into a
// signed distance field (in texels, then metres) around its weight > 0.5 boundary and encoded
// as value = saturate(0.5 + signedMetres / (2 * SdfRange)). With this encoding the shader can
// recover true-metre distance as distance = (w - 0.5) / |gradient|, because along this mapping
// the gradient of the encoded value is exactly 1 / (2 * SdfRange) per metre - so a screen-space
// derivative of the channel directly gives a metres-per-texel scale for antialiasing the edge.
// For that identity to hold, the material's Fringe width must stay below SdfRange (the encoding
// saturates beyond +-SdfRange metres from the boundary and no longer carries true distance).

#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PaintedGround
{
    public static class SplatBaker
    {
        public const int DefaultResolution = 1024;

        /// <summary>
        /// Signed-distance range, in metres, encoded either side of the weight > 0.5 boundary.
        /// 0.5 = the border, 1.0 = SdfRange metres or more inside, 0.0 = SdfRange metres or
        /// more outside. Keep the material's Fringe width below this value.
        /// </summary>
        public const float SdfRange = 4f;

        /// <summary>
        /// Fallback world-space size (metres) of the square area a painted mask texture is
        /// assumed to cover, used to convert the mask's texel size into metres for the SDF
        /// encode. Only used to initialise <see cref="MaskWorldSizeMetres"/>.
        /// </summary>
        const float DefaultMaskWorldSize = 256f;

        /// <summary>
        /// World-space size (metres) of the square area the currently selected painted mask
        /// covers. "Encode Painted Splat Mask as SDF" uses this to convert texel distances to
        /// metres. Set this from code before invoking the menu item, or just edit its default
        /// above - it should match sizeX of the World rect (_SplatBounds) on the ground
        /// material that this mask will be assigned to.
        /// </summary>
        public static float MaskWorldSizeMetres = DefaultMaskWorldSize;

        [MenuItem("Tools/Painted Ground/Encode Painted Splat Mask as SDF")]
        public static void EncodeSelectedMaskAsSDF()
        {
            var srcTex = Selection.activeObject as Texture2D;
            if (srcTex == null)
            {
                EditorUtility.DisplayDialog("Painted Ground", "Select a Texture2D asset in the Project window (a painted splat mask).", "OK");
                return;
            }

            string srcPath = AssetDatabase.GetAssetPath(srcTex);
            bool confirmed = EditorUtility.DisplayDialog(
                "Painted Ground",
                $"Encode '{srcTex.name}' as an SDF splat mask?\n\nThis reads the texture as R/G/B channel masks (thresholded at 50% grey), assumes it covers a {MaskWorldSizeMetres}m square (MaskWorldSizeMetres), and writes a new '_SDF' texture next to it.",
                "Encode", "Cancel");
            if (!confirmed) return;

            var importer = AssetImporter.GetAtPath(srcPath) as TextureImporter;
            if (importer == null)
            {
                EditorUtility.DisplayDialog("Painted Ground", "Could not find a TextureImporter for '" + srcPath + "'.", "OK");
                return;
            }

            bool wasReadable = importer.isReadable;
            Color32[] px;
            int w, h;
            try
            {
                if (!wasReadable)
                {
                    importer.isReadable = true;
                    importer.SaveAndReimport();
                }

                px = srcTex.GetPixels32();
                w = srcTex.width;
                h = srcTex.height;
            }
            finally
            {
                if (!wasReadable)
                {
                    importer.isReadable = false;
                    importer.SaveAndReimport();
                }
            }

            // Threshold each channel at 128 (weight > 0.5) before handing it to the same SDF
            // encode path Bake uses - EncodeChannelsAsSDF re-thresholds internally too, but
            // binarising up front means a painted, possibly anti-aliased mask is treated
            // exactly like Bake's raster output.
            for (int i = 0; i < px.Length; i++)
            {
                var c = px[i];
                px[i] = new Color32(
                    (byte)(c.r > 127 ? 255 : 0),
                    (byte)(c.g > 127 ? 255 : 0),
                    (byte)(c.b > 127 ? 255 : 0),
                    255);
            }

            float texelSizeMetres = MaskWorldSizeMetres / w;
            EncodeChannelsAsSDF(px, w, h, texelSizeMetres);

            var outTex = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
            outTex.SetPixels32(px);
            outTex.Apply();

            string dstPath = Path.GetDirectoryName(srcPath).Replace('\\', '/') + "/" + Path.GetFileNameWithoutExtension(srcPath) + "_SDF.png";
            File.WriteAllBytes(dstPath, outTex.EncodeToPNG());
            Object.DestroyImmediate(outTex);

            AssetDatabase.ImportAsset(dstPath);
            if (AssetImporter.GetAtPath(dstPath) is TextureImporter dstTi)
            {
                dstTi.sRGBTexture = false;
                dstTi.wrapMode = TextureWrapMode.Clamp;
                dstTi.filterMode = FilterMode.Bilinear;
                dstTi.mipmapEnabled = false;
                dstTi.textureCompression = TextureImporterCompression.Uncompressed;
                dstTi.SaveAndReimport();
            }

            Debug.Log($"[Painted Ground] Encoded SDF splat mask at '{dstPath}'. Assign it as the ground material's World splat map (_SplatMap).");
        }

        [MenuItem("Tools/Painted Ground/Bake Splat Map from Vertex Colours")]
        public static void BakeFromSelection()
        {
            var go = Selection.activeGameObject;
            if (go == null || go.GetComponent<MeshCollider>() == null)
            {
                EditorUtility.DisplayDialog("Painted Ground", "Select a ground object that has a MeshCollider (the sample Island works).", "OK");
                return;
            }

            var tex = Bake(go, DefaultResolution, out var bounds);
            if (tex == null) return;

            var renderer = go.GetComponent<MeshRenderer>();
            var material = renderer != null ? renderer.sharedMaterial : null;
            if (material != null && material.shader != null && material.shader.name == "Painted/Ground")
            {
                material.SetTexture("_SplatMap", tex);
                material.SetVector("_SplatBounds", bounds);
                material.EnableKeyword("_SPLAT_WORLDTEXTURE");
                material.DisableKeyword("_SPLAT_VERTEXCOLOR");
                material.SetFloat("_Splat", 1f);
                EditorUtility.SetDirty(material);
                Debug.Log($"[Painted Ground] Baked splat map for '{go.name}' and assigned it to material '{material.name}'.");
            }
            else
            {
                Debug.Log($"[Painted Ground] Baked splat map for '{go.name}'.");
            }
        }

        /// <summary>
        /// Bakes <paramref name="ground"/>'s vertex-colour splat weights into a top-down
        /// world-space texture. <paramref name="bounds"/> receives (minX, minZ, sizeX, sizeZ)
        /// of the collider bounds in world space, expanded by 1m on each side.
        /// </summary>
        public static Texture2D Bake(GameObject ground, int resolution, out Vector4 bounds)
        {
            bounds = Vector4.zero;

            string folder = FindFolder();
            if (folder == null) { Debug.LogError("[Painted Ground] folder not found"); return null; }

            var mc = ground.GetComponent<MeshCollider>();
            if (mc == null) { Debug.LogError("[Painted Ground] '" + ground.name + "' has no MeshCollider"); return null; }

            var mesh = mc.sharedMesh;
            Color[] vcols = (mesh != null && mesh.colors != null && mesh.colors.Length == mesh.vertexCount) ? mesh.colors : null;
            int[] tris = mesh != null ? mesh.triangles : null;

            Physics.SyncTransforms();
            var b = mc.bounds;
            float minX = b.min.x - 1f;
            float minZ = b.min.z - 1f;
            float sizeX = b.size.x + 2f;
            float sizeZ = b.size.z + 2f;
            bounds = new Vector4(minX, minZ, sizeX, sizeZ);

            var pixels = new Color32[resolution * resolution];
            float rayY = b.max.y + 10f;
            float rayDist = b.size.y + 20f;

            try
            {
                for (int y = 0; y < resolution; y++)
                {
                    if (y % 32 == 0)
                        EditorUtility.DisplayProgressBar("Painted Ground", $"Baking splat map ({y}/{resolution})", (float)y / resolution);

                    float worldZ = minZ + (y + 0.5f) / resolution * sizeZ;
                    for (int x = 0; x < resolution; x++)
                    {
                        float worldX = minX + (x + 0.5f) / resolution * sizeX;
                        var ray = new Ray(new Vector3(worldX, rayY, worldZ), Vector3.down);

                        Color c = new Color(0f, 0f, 0f, 1f);
                        if (mc.Raycast(ray, out var hit, rayDist))
                        {
                            if (vcols != null && tris != null && hit.triangleIndex >= 0)
                            {
                                int t = hit.triangleIndex * 3;
                                var bc = hit.barycentricCoordinate;
                                Color vc = vcols[tris[t]] * bc.x + vcols[tris[t + 1]] * bc.y + vcols[tris[t + 2]] * bc.z;
                                c = new Color(vc.r, vc.g, vc.b, 1f);
                            }
                        }

                        pixels[y * resolution + x] = c;
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            BoxBlur3x3(pixels, resolution, resolution);

            float texelSizeMetres = sizeX / resolution;
            EncodeChannelsAsSDF(pixels, resolution, resolution, texelSizeMetres);

            var tex = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false, true);
            tex.SetPixels32(pixels);
            tex.Apply();

            string path = folder + "/Generated/" + ground.name + "_Splat.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(path);
            if (AssetImporter.GetAtPath(path) is TextureImporter ti)
            {
                ti.sRGBTexture = false;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.filterMode = FilterMode.Bilinear;
                ti.mipmapEnabled = false;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.maxTextureSize = resolution > 1024 ? 2048 : (resolution < 1024 ? resolution : 1024);
                ti.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ---- helpers ----

        // Edge-clamped 3x3 box blur over RGB (alpha stays 255).
        static void BoxBlur3x3(Color32[] pixels, int w, int h)
        {
            var src = (Color32[])pixels.Clone();
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int rSum = 0, gSum = 0, bSum = 0, n = 0;
                    for (int oy = -1; oy <= 1; oy++)
                    {
                        int sy = Mathf.Clamp(y + oy, 0, h - 1);
                        for (int ox = -1; ox <= 1; ox++)
                        {
                            int sx = Mathf.Clamp(x + ox, 0, w - 1);
                            var c = src[sy * w + sx];
                            rSum += c.r; gSum += c.g; bSum += c.b; n++;
                        }
                    }
                    pixels[y * w + x] = new Color32(
                        (byte)(rSum / n), (byte)(gSum / n), (byte)(bSum / n), 255);
                }
            }
        }

        // For each of R, G, B independently: threshold at weight > 0.5, build the Euclidean
        // distance field (in texels) to the boundary on both sides, convert to metres, and
        // re-encode the channel as value = saturate(0.5 + signedMetres / (2 * SdfRange)).
        // Allocates a handful of resolution-sized buffers, reused across
        // the three channels - no per-texel allocation.
        static void EncodeChannelsAsSDF(Color32[] pixels, int w, int h, float texelSizeMetres)
        {
            int n = w * h;
            var inside = new bool[n];
            var outside = new bool[n];

            for (int channel = 0; channel < 3; channel++)
            {
                for (int i = 0; i < n; i++)
                {
                    var c = pixels[i];
                    byte v = channel == 0 ? c.r : channel == 1 ? c.g : c.b;
                    bool isIn = v > 127; // v/255 > 0.5
                    inside[i] = isIn;
                    outside[i] = !isIn;
                }

                var dOut = DistanceTransform(inside, w, h);  // inside texels -> distance to nearest outside texel
                var dIn = DistanceTransform(outside, w, h);  // outside texels -> distance to nearest inside texel

                for (int i = 0; i < n; i++)
                {
                    float signedTexels = inside[i] ? dOut[i] : -dIn[i];
                    float signedMetres = signedTexels * texelSizeMetres;
                    float value = Mathf.Clamp01(0.5f + signedMetres / (2f * SdfRange));
                    byte encoded = (byte)Mathf.RoundToInt(value * 255f);

                    var c = pixels[i];
                    if (channel == 0) c.r = encoded;
                    else if (channel == 1) c.g = encoded;
                    else c.b = encoded;
                    c.a = 255;
                    pixels[i] = c;
                }
            }
        }

        // Exact 2D Euclidean distance transform: for every texel, the distance (in texels) to
        // the nearest texel where inside == false. Texels that never see a false anywhere in
        // their row/column chain get INF (returned as sqrt(1e10f), i.e. effectively unbounded).
        // Separable: 1D squared-distance transform along rows, then along columns.
        static float[] DistanceTransform(bool[] inside, int w, int h)
        {
            const float INF = 1e10f;
            int m = Mathf.Max(w, h);
            var f = new float[m];
            var d = new float[m];
            var v = new int[m];
            var z = new float[m + 1];

            var g = new float[w * h];
            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                for (int x = 0; x < w; x++)
                    f[x] = inside[row + x] ? INF : 0f;
                EDT(f, w, d, v, z);
                for (int x = 0; x < w; x++)
                    g[row + x] = d[x];
            }

            var result = new float[w * h];
            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                    f[y] = g[y * w + x];
                EDT(f, h, d, v, z);
                for (int y = 0; y < h; y++)
                    result[y * w + x] = d[y];
            }

            for (int i = 0; i < result.Length; i++)
                result[i] = Mathf.Sqrt(result[i]);

            return result;
        }

        // Felzenszwalb & Huttenlocher's 1D lower-envelope-of-parabolas squared-distance
        // transform. f holds the per-sample base cost (0 at "on" samples, INF elsewhere);
        // d receives the squared distance to the nearest 0-cost sample. v and z are scratch
        // buffers of length >= n and n+1 respectively, reused by the caller across calls.
        static void EDT(float[] f, int n, float[] d, int[] v, float[] z)
        {
            const float INF = 1e10f;
            int k = 0;
            v[0] = 0;
            z[0] = -INF;
            z[1] = INF;

            for (int q = 1; q < n; q++)
            {
                float s;
                while (true)
                {
                    int vk = v[k];
                    s = ((f[q] + (float)q * q) - (f[vk] + (float)vk * vk)) / (2f * q - 2f * vk);
                    if (s <= z[k]) k--;
                    else break;
                }
                k++;
                v[k] = q;
                z[k] = s;
                z[k + 1] = INF;
            }

            k = 0;
            for (int q = 0; q < n; q++)
            {
                while (z[k + 1] < q) k++;
                int vk = v[k];
                float dq = q - vk;
                d[q] = dq * dq + f[vk];
            }
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
