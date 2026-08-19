// Copyright (c) 2026 Stitch Punk. All rights reserved.

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace StitchPunk.AnimationToolkitShaderDemo.Editor
{
    /// <summary>
    /// Builds the one scene that can settle amendment A44's facing-sign correction: a
    /// non-billboarded reference quad beside the CPU path and the shader path, all wearing the same
    /// deliberately asymmetric glyph.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Why this exists when a billboard demo already does.</strong>
    /// <c>BillboardDemoBuilder</c> answers "do the modes behave differently under orbit", which is a
    /// question about <em>motion</em>. This answers "is the facing sign right", which is a question
    /// about <em>orientation</em> — and a row of quads that are all wrong looks exactly like a row
    /// that is all right. The fixed reference is what makes the answer readable, and the glyph is
    /// what makes a mirrored quad distinguishable from a correct one.
    /// </para>
    /// <para>
    /// <strong>The two failure modes look different, and that is deliberate.</strong>
    /// <c>ToolkitSpriteUnlit</c> declares no <c>Cull</c>, so it culls back faces: a wrong sign turns
    /// the shader quad <em>invisible</em> rather than backwards. The CPU probe drives an ordinary
    /// quad with the same material, so it fails the same way. An observer therefore does not have to
    /// judge a subtle rotation — they have to notice whether something is there.
    /// </para>
    /// </remarks>
    public static class BillboardSignCheckBuilder
    {
        private const string ScenePath = "Assets/Scenes/AnimationToolkitBillboardSignCheck.unity";
        private const string GeneratedFolder = "Assets/AnimationToolkitShaderDemo/Generated";
        private const string TexturePath = GeneratedFolder + "/BillboardSignGlyph.png";
        private const string ShaderPath =
            "Packages/com.stitchpunk.dotsanimationtoolkit/Shaders/ToolkitSpriteUnlit.shadergraph";

        private const int GlyphSize = 128;

        [MenuItem("Tools/DOTS Animation Toolkit/Build Billboard Sign Check Scene")]
        public static void BuildSignCheckScene()
        {
            Shader spriteShader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            if (spriteShader == null)
            {
                Debug.LogError("[BillboardSignCheck] Shader not found at " + ShaderPath);
                return;
            }

            EnsureFolder("Assets", "AnimationToolkitShaderDemo");
            EnsureFolder("Assets/AnimationToolkitShaderDemo", "Generated");
            EnsureFolder("Assets", "Scenes");

            Texture2D glyph = CreateGlyphTexture();

            UnityEngine.SceneManagement.Scene scene =
                EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // Reference first, so the eye reads left to right from "known good" to "under test".
            BuildReferenceQuad(spriteShader, glyph, new Vector3(-2.4f, 1.2f, 0f));
            BuildShaderPathQuad(spriteShader, glyph, new Vector3(0f, 1.2f, 0f));
            BuildCpuPathQuad(spriteShader, glyph, new Vector3(2.4f, 1.2f, 0f));

            BuildGround();
            ConfigureCamera();

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(
                "[BillboardSignCheck] Built " + ScenePath + ".\n"
                + "The camera orbits on its own - no Play mode needed. Watch the Game view.\n"
                + "PASS: the MIDDLE and RIGHT quads stay FLAT and parallel to the screen the whole "
                + "way round - not curving or turning toward the camera point - and read F with the "
                + "GREEN bar on the left.\n"
                + "The LEFT quad does not billboard - it is the reference, and it will turn away "
                + "and become edge-on. That is correct.\n"
                + "FAIL (middle missing): the shader facing sign is inverted.\n"
                + "FAIL (right missing): the CPU facing sign is inverted.\n"
                + "FAIL (F mirrored): the sign is inverted and something is rendering double-sided.");
        }

        // -----------------------------------------------------------------------------------
        // The glyph. Asymmetric on both axes, so a mirror is unmistakable.
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// Draws a blocky "F" with a green bar down the left edge and a red bar down the right.
        /// </summary>
        /// <remarks>
        /// An F because it is asymmetric horizontally <em>and</em> vertically, so neither a mirror
        /// nor an upside-down result can be mistaken for correct. The coloured edge bars are
        /// belt-and-braces: they read at a glance and at any distance, where the glyph's own strokes
        /// may not.
        /// </remarks>
        private static Texture2D CreateGlyphTexture()
        {
            Texture2D glyph = new Texture2D(GlyphSize, GlyphSize, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[GlyphSize * GlyphSize];

            for (int pixelIndex = 0; pixelIndex < pixels.Length; pixelIndex++)
            {
                pixels[pixelIndex] = new Color(0.08f, 0.08f, 0.10f, 1f);
            }

            for (int y = 0; y < GlyphSize; y++)
            {
                for (int x = 0; x < GlyphSize; x++)
                {
                    Color pixel = pixels[y * GlyphSize + x];

                    // Edge bars: green on the left, red on the right. Texture space has +x to the
                    // right and +y up, which is how the quad presents it when facing the viewer.
                    if (x < 10)
                    {
                        pixel = new Color(0.15f, 0.85f, 0.25f, 1f);
                    }
                    else if (x >= GlyphSize - 10)
                    {
                        pixel = new Color(0.90f, 0.20f, 0.15f, 1f);
                    }
                    else if (IsInsideLetterF(x, y))
                    {
                        pixel = Color.white;
                    }

                    pixels[y * GlyphSize + x] = pixel;
                }
            }

            glyph.SetPixels(pixels);
            glyph.Apply();

            System.IO.File.WriteAllBytes(TexturePath, glyph.EncodeToPNG());
            Object.DestroyImmediate(glyph);
            AssetDatabase.ImportAsset(TexturePath, ImportAssetOptions.ForceUpdate);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
        }

        /// <summary>The three strokes of a capital F, in texture pixels.</summary>
        private static bool IsInsideLetterF(int x, int y)
        {
            bool spine = x >= 34 && x <= 52 && y >= 24 && y <= 104;
            bool topArm = y >= 86 && y <= 104 && x >= 34 && x <= 96;
            bool middleArm = y >= 54 && y <= 70 && x >= 34 && x <= 82;
            return spine || topArm || middleArm;
        }

        // -----------------------------------------------------------------------------------
        // The three quads.
        // -----------------------------------------------------------------------------------

        private static Material CreateQuadMaterial(
            Shader spriteShader, Texture2D glyph, string assetName, float billboardMode)
        {
            Material material = new Material(spriteShader);
            material.color = Color.white;
            material.mainTexture = glyph;

            // Atlas mode with the identity rect, so no texture array is needed.
            material.DisableKeyword("_TOOLKIT_SLICE_MODE");
            material.SetFloat("_SliceMode", 0f);
            material.SetVector("_AtlasFrame", new Vector4(1f, 1f, 0f, 0f));
            material.SetFloat("_Cutoff", 0.01f);
            material.SetVector("_BillboardParams", new Vector4(billboardMode, 0f, 0f, 0f));

            CreateOrReplaceAsset(material, GeneratedFolder + "/" + assetName + ".mat");
            return material;
        }

        private static GameObject CreateQuad(string name, Vector3 position)
        {
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = name;
            Object.DestroyImmediate(quad.GetComponent<Collider>());
            quad.transform.position = position;
            quad.transform.localScale = new Vector3(1.4f, 1.4f, 1f);
            return quad;
        }

        /// <summary>
        /// Ground truth: no billboarding of any kind, left at Unity's identity rotation.
        /// </summary>
        /// <remarks>
        /// A Unity <c>Quad</c> at identity is visible from negative Z, which is where a default
        /// scene camera sits — so this quad shows its front face at the start of the orbit and
        /// turns away as the camera moves. Both halves of that matter: the first frame says what
        /// "correct" looks like, and the turning away proves it is genuinely not billboarding.
        /// </remarks>
        private static void BuildReferenceQuad(Shader spriteShader, Texture2D glyph, Vector3 position)
        {
            GameObject quad = CreateQuad("A_Reference_NoBillboard", position);
            quad.GetComponent<MeshRenderer>().sharedMaterial =
                CreateQuadMaterial(spriteShader, glyph, "SignCheckReference", 0f);
        }

        /// <summary>The per-vertex path: <c>ToolkitBillboard.hlsl</c>, screen-aligned.</summary>
        /// <remarks>
        /// <para>
        /// <strong>Screen-aligned, not spherical.</strong> An earlier cut used spherical because it
        /// needs only <c>_WorldSpaceCameraPos</c> and so avoided depending on the host-written
        /// <c>_ToolkitCameraForward</c> global — one unknown at a time. That was the wrong trade: it
        /// produced a demo that curves quads toward the screen edges, which is not what this project
        /// ships and reads as a defect to anyone who knows the target look. Screen-aligned is the
        /// host's behaviour, A44's default, and therefore what a verification scene must show.
        /// </para>
        /// <para>
        /// The global is supplied by the <c>ToolkitCameraBinder</c> this builder puts on the camera.
        /// If that binder is missing the mode silently degrades to spherical, which is exactly the
        /// curve this scene exists to rule out — so its absence would be misread as a failure.
        /// </para>
        /// </remarks>
        private static void BuildShaderPathQuad(Shader spriteShader, Texture2D glyph, Vector3 position)
        {
            GameObject quad = CreateQuad("B_ShaderPath_ScreenAligned", position);
            quad.GetComponent<MeshRenderer>().sharedMaterial =
                CreateQuadMaterial(spriteShader, glyph, "SignCheckShaderPath", 4f);
        }

        /// <summary>The CPU path: <c>BillboardMath.TryResolve</c>, driven by the probe.</summary>
        /// <remarks>
        /// The material's own billboard mode is left at Off. An actor uses one path or the other,
        /// never both — two rotations are no rotation (amendment A41) — and a quad that billboarded
        /// in the shader as well would be testing that trap instead of the sign.
        /// </remarks>
        private static void BuildCpuPathQuad(Shader spriteShader, Texture2D glyph, Vector3 position)
        {
            GameObject quad = CreateQuad("C_CpuPath_ScreenAligned", position);
            quad.GetComponent<MeshRenderer>().sharedMaterial =
                CreateQuadMaterial(spriteShader, glyph, "SignCheckCpuPath", 0f);

            BillboardSignProbe probe = quad.AddComponent<BillboardSignProbe>();
            probe.mode = StitchPunk.AnimationToolkit.BillboardMode.ScreenAligned;
        }

        // -----------------------------------------------------------------------------------
        // Scene furniture.
        // -----------------------------------------------------------------------------------

        private static void BuildGround()
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.position = Vector3.zero;
            ground.transform.localScale = new Vector3(2f, 1f, 2f);
        }

        private static void ConfigureCamera()
        {
            Camera camera = Object.FindAnyObjectByType<Camera>();
            if (camera == null)
            {
                GameObject cameraObject = new GameObject("Main Camera");
                cameraObject.tag = "MainCamera";
                camera = cameraObject.AddComponent<Camera>();
            }

            camera.transform.position = new Vector3(0f, 2.4f, -6f);
            camera.transform.LookAt(new Vector3(0f, 1.2f, 0f), Vector3.up);

            ToolkitOrbitCamera orbit = camera.gameObject.GetComponent<ToolkitOrbitCamera>();
            if (orbit == null)
            {
                orbit = camera.gameObject.AddComponent<ToolkitOrbitCamera>();
            }
            // Screen-aligned billboarding in the SHADER reads a global the host is responsible for
            // writing; the package never touches a Camera. Without this the shader quad falls back
            // to spherical and curves at the screen edges, which is the very thing being ruled out.
            if (camera.gameObject.GetComponent<ToolkitCameraBinder>() == null)
            {
                camera.gameObject.AddComponent<ToolkitCameraBinder>();
            }

            orbit.target = new Vector3(0f, 1.2f, 0f);
            orbit.radius = 6f;
            orbit.height = 1.2f;
            orbit.degreesPerSecond = 25f;
            // So the scene turns the moment it opens, without anyone having to press Play and
            // wonder whether they are looking at the Game view.
            orbit.orbitInEditMode = true;
        }

        // -----------------------------------------------------------------------------------
        // Asset plumbing.
        // -----------------------------------------------------------------------------------

        private static void EnsureFolder(string parentFolder, string folderName)
        {
            if (!AssetDatabase.IsValidFolder(parentFolder + "/" + folderName))
            {
                AssetDatabase.CreateFolder(parentFolder, folderName);
            }
        }

        private static void CreateOrReplaceAsset(Object asset, string assetPath)
        {
            Object existing = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
            if (existing != null)
            {
                AssetDatabase.DeleteAsset(assetPath);
            }
            AssetDatabase.CreateAsset(asset, assetPath);
        }
    }
}
