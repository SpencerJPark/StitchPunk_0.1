// Copyright (c) 2026 Stitch Punk. All rights reserved.

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace StitchPunk.AnimationToolkitShaderDemo.Editor
{
    /// <summary>
    /// Builds the billboard verification scene — the §11.4 evidence for C5's "billboard modes
    /// human-verified" that the game itself cannot produce.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Every mode looks correct head-on.</strong> They differ only in how they behave as the
    /// viewer moves, which is why §11.4 asks for verification <em>under camera orbit</em> and why a
    /// screenshot of a single frame proves nothing. The Stitch Punk camera tilts and rotates but
    /// never orbits, so this scratch scene is the only place the evidence exists.
    /// </para>
    /// <para>
    /// The five quads are laid out in a row with a fixed reference post beside each. A billboard is
    /// judged by <em>relative</em> motion — quad against something that is definitely not turning —
    /// and without the posts a whole row of billboards rotating together is indistinguishable from a
    /// camera that has not moved.
    /// </para>
    /// </remarks>
    public static class BillboardDemoBuilder
    {
        private const string ScenePath = "Assets/Scenes/AnimationToolkitBillboardDemo.unity";
        private const string MaterialFolder = "Assets/AnimationToolkitShaderDemo/Generated";
        private const string ShaderPath =
            "Packages/com.stitchpunk.dotsanimationtoolkit/Shaders/HandWritten/ToolkitSpriteUnlit.shader";

        /// <summary>Mode value, label, and the colour that makes it identifiable while spinning.</summary>
        private struct DemoMode
        {
            public float mode;
            public string label;
            public Color color;

            public DemoMode(float mode, string label, Color color)
            {
                this.mode = mode;
                this.label = label;
                this.color = color;
            }
        }

        private static readonly DemoMode[] DemoModes =
        {
            new DemoMode(0f, "Off", new Color(0.55f, 0.55f, 0.58f)),
            new DemoMode(1f, "Full", new Color(0.85f, 0.35f, 0.30f)),
            new DemoMode(2f, "Upright", new Color(0.30f, 0.60f, 0.85f)),
            new DemoMode(3f, "FrozenYaw", new Color(0.85f, 0.70f, 0.25f)),
            new DemoMode(4f, "ScreenAligned", new Color(0.35f, 0.75f, 0.45f))
        };

        [MenuItem("Tools/DOTS Animation Toolkit/Build Billboard Demo Scene")]
        public static void BuildBillboardDemo()
        {
            Shader spriteShader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            if (spriteShader == null)
            {
                Debug.LogError("[BillboardDemoBuilder] Shader not found at " + ShaderPath);
                return;
            }

            EnsureFolder("Assets", "AnimationToolkitShaderDemo");
            EnsureFolder("Assets/AnimationToolkitShaderDemo", "Generated");
            EnsureFolder("Assets", "Scenes");

            UnityEngine.SceneManagement.Scene scene =
                EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            float spacing = 2.2f;
            float rowStart = -(DemoModes.Length - 1) * spacing * 0.5f;

            for (int modeIndex = 0; modeIndex < DemoModes.Length; modeIndex++)
            {
                DemoMode demoMode = DemoModes[modeIndex];
                float x = rowStart + modeIndex * spacing;

                BuildBillboardQuad(spriteShader, demoMode, new Vector3(x, 1f, 0f));
                BuildReferencePost(new Vector3(x, 0.35f, 0.9f), demoMode.color);
            }

            BuildGround();
            ConfigureCamera();

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(
                "[BillboardDemoBuilder] Built " + ScenePath + ". Press Play and watch the camera "
                + "orbit. Off should turn with the camera (it does not billboard); Full should face "
                + "you from every angle; Upright should face you but never tilt; FrozenYaw should "
                + "hold its heading while pitching; ScreenAligned should stay parallel to the screen "
                + "with every quad taking the same rotation. The flat posts are the reference — they "
                + "genuinely do not turn.");
        }

        private static void BuildBillboardQuad(Shader spriteShader, DemoMode demoMode, Vector3 position)
        {
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Billboard_" + demoMode.label;
            Object.DestroyImmediate(quad.GetComponent<Collider>());
            quad.transform.position = position;
            quad.transform.localScale = new Vector3(1.2f, 1.6f, 1f);

            Material material = new Material(spriteShader);
            material.color = demoMode.color;

            // The per-instance rows are set as MATERIAL values here rather than via ECS components.
            // That is the point of the non-instanced fallback in ToolkitInstancing.hlsl: the same
            // shader must work in a plain GameObject scene, and this demo is that scene.
            material.SetVector("_BillboardParams", new Vector4(demoMode.mode, 0.6f, 0f, 0f));
            material.SetFloat("_Cutoff", 0.01f);

            // Atlas mode with the identity rect, so the demo needs no texture array to render.
            material.DisableKeyword("_TOOLKIT_SLICE_MODE");
            material.SetFloat("_SliceMode", 0f);
            material.SetVector("_AtlasFrame", new Vector4(1f, 1f, 0f, 0f));

            CreateOrReplaceAsset(material, MaterialFolder + "/Billboard" + demoMode.label + ".mat");
            quad.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        /// <summary>
        /// A thin, deliberately non-billboarded post beside each quad. Without a fixed reference the
        /// whole row rotating together looks identical to a camera that has not moved.
        /// </summary>
        private static void BuildReferencePost(Vector3 position, Color color)
        {
            GameObject post = GameObject.CreatePrimitive(PrimitiveType.Cube);
            post.name = "ReferencePost";
            Object.DestroyImmediate(post.GetComponent<Collider>());
            post.transform.position = position;
            post.transform.localScale = new Vector3(0.12f, 0.7f, 0.12f);

            Material postMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            postMaterial.color = color * 0.5f;
            post.GetComponent<MeshRenderer>().sharedMaterial = postMaterial;
        }

        private static void BuildGround()
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            Object.DestroyImmediate(ground.GetComponent<Collider>());
            ground.transform.position = Vector3.zero;
            ground.transform.localScale = new Vector3(3f, 1f, 3f);

            Material groundMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            groundMaterial.color = new Color(0.22f, 0.23f, 0.26f);
            ground.GetComponent<MeshRenderer>().sharedMaterial = groundMaterial;
        }

        private static void ConfigureCamera()
        {
            Camera mainCamera = Camera.main;
            if (mainCamera == null)
            {
                return;
            }

            // The binder is what makes screen-aligned mode work at all — without a camera forward
            // global it degrades to spherical, which would make mode 4 look like mode 1 and read as
            // "screen-aligned is broken" rather than "nothing is feeding it".
            mainCamera.gameObject.AddComponent<ToolkitCameraBinder>();

            ToolkitOrbitCamera orbit = mainCamera.gameObject.AddComponent<ToolkitOrbitCamera>();
            orbit.target = new Vector3(0f, 1f, 0f);
            orbit.radius = 8f;
            orbit.height = 2f;
            orbit.degreesPerSecond = 25f;
        }

        private static void EnsureFolder(string parentFolder, string folderName)
        {
            if (!AssetDatabase.IsValidFolder(parentFolder + "/" + folderName))
            {
                AssetDatabase.CreateFolder(parentFolder, folderName);
            }
        }

        private static void CreateOrReplaceAsset(Object asset, string path)
        {
            if (AssetDatabase.LoadAssetAtPath<Object>(path) != null)
            {
                AssetDatabase.DeleteAsset(path);
            }
            AssetDatabase.CreateAsset(asset, path);
        }
    }
}
