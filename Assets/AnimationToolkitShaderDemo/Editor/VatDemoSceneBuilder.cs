// Copyright (c) 2026 Stitch Punk. All rights reserved.

using StitchPunk.AnimationToolkit.Authoring;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace StitchPunk.AnimationToolkitShaderDemo.Editor
{
    /// <summary>
    /// Builds a scene that plays the baked tentacle through the VAT shader — the visual proof that
    /// the bake, the texture layout, and the shader addressing agree.
    /// </summary>
    /// <remarks>
    /// A row of tentacles rather than one, each started at a different frame. Three reasons: it
    /// shows the wave travelling rather than just wobbling, it makes a collapsed chain obvious
    /// (they would all be rigid together), and it is the crowd case this shader exists for — one
    /// material, one mesh, many instances, differing only in three floats.
    /// </remarks>
    public static class VatDemoSceneBuilder
    {
        private const string VatFolder = "Assets/AnimationToolkitShaderDemo/Generated/Vat";
        private const string ScenePath = "Assets/Scenes/AnimationToolkitVatDemo.unity";
        private const string ShaderPath =
            "Packages/com.stitchpunk.dotsanimationtoolkit/Shaders/ToolkitVatCrowdUnlit.shadergraph";
        private const int TentacleCount = 7;

        [MenuItem("Tools/DOTS Animation Toolkit/Build VAT Demo Scene")]
        public static void BuildVatDemo()
        {
            VatTextureSetAsset textureSet =
                AssetDatabase.LoadAssetAtPath<VatTextureSetAsset>(VatFolder + "/TentacleVatSet.asset");
            Mesh runtimeMesh = AssetDatabase.LoadAssetAtPath<Mesh>(VatFolder + "/TentacleRuntimeMesh.asset");
            Shader vatShader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);

            if (textureSet == null || runtimeMesh == null || vatShader == null)
            {
                Debug.LogError(
                    "[VatDemoSceneBuilder] Missing inputs. Run Tools > DOTS Animation Toolkit > "
                    + "Bake VAT Tentacle first.");
                return;
            }

            UnityEngine.SceneManagement.Scene scene =
                EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            int frameStart = 0;
            int frameCount = 61;
            float framesPerSecond = 30f;
            if (textureSet.clipRanges != null && textureSet.clipRanges.Count > 0)
            {
                frameStart = textureSet.clipRanges[0].frameStart;
                frameCount = textureSet.clipRanges[0].frameCount;
                framesPerSecond = textureSet.clipRanges[0].fps;
            }

            Material vatMaterial = new Material(vatShader);
            vatMaterial.SetTexture("_VatBoneTex", textureSet.boneTexture);

            // The layout the shader addresses against. Getting any of these four wrong renders the
            // mesh as noise, which is why they come from the bake result rather than being typed in.
            vatMaterial.SetVector("_VatTexelParams", new Vector4(
                textureSet.textureWidth,
                textureSet.boneTexture != null ? textureSet.boneTexture.height : 0,
                textureSet.rowsPerFrame,
                textureSet.boneCount));
            vatMaterial.color = new Color(0.45f, 0.75f, 0.60f);
            CreateOrReplaceAsset(vatMaterial, VatFolder + "/TentacleVatMaterial.mat");

            for (int tentacleIndex = 0; tentacleIndex < TentacleCount; tentacleIndex++)
            {
                GameObject tentacle = new GameObject("Tentacle" + tentacleIndex.ToString());
                tentacle.transform.position = new Vector3((tentacleIndex - (TentacleCount - 1) * 0.5f) * 0.8f, 0f, 0f);

                MeshFilter meshFilter = tentacle.AddComponent<MeshFilter>();
                meshFilter.sharedMesh = runtimeMesh;
                MeshRenderer meshRenderer = tentacle.AddComponent<MeshRenderer>();
                meshRenderer.sharedMaterial = vatMaterial;

                VatPlaybackDriver driver = tentacle.AddComponent<VatPlaybackDriver>();
                driver.frameStart = frameStart;
                driver.frameCount = frameCount;
                driver.framesPerSecond = framesPerSecond;
            }

            BuildGround();

            Camera mainCamera = Camera.main;
            if (mainCamera != null)
            {
                mainCamera.transform.position = new Vector3(0f, 1.6f, -5f);
                mainCamera.transform.rotation = Quaternion.Euler(8f, 0f, 0f);
            }

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(
                "[VatDemoSceneBuilder] Built " + ScenePath + " with " + TentacleCount.ToString()
                + " tentacles on one mesh and one material, frames " + frameStart.ToString()
                + ".." + (frameStart + frameCount - 1).ToString()
                + ". Press Play: they should wave, with the tip moving furthest. A rigid rod means "
                + "the bake collapsed the chain; noise means the texel layout disagrees.");
        }

        private static void BuildGround()
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            Object.DestroyImmediate(ground.GetComponent<Collider>());
            ground.transform.localScale = new Vector3(2f, 1f, 2f);

            Material groundMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            groundMaterial.color = new Color(0.20f, 0.21f, 0.24f);
            ground.GetComponent<MeshRenderer>().sharedMaterial = groundMaterial;
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
