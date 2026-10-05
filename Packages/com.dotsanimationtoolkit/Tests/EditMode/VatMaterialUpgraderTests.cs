// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using DotsAnimationToolkit.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DotsAnimationToolkit.Tests.EditMode
{
    public sealed class VatMaterialUpgraderTests
    {
        private const string VatShaderPath = "Packages/com.dotsanimationtoolkit/Shaders/ToolkitVatCrowdUnlit.shadergraph";
        private const string SpriteShaderPath = "Packages/com.dotsanimationtoolkit/Shaders/ToolkitSpriteUnlit.shadergraph";

        private readonly List<Object> createdObjects = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int objectIndex = 0; objectIndex < createdObjects.Count; objectIndex++)
            {
                if (createdObjects[objectIndex] != null)
                {
                    Object.DestroyImmediate(createdObjects[objectIndex]);
                }
            }

            createdObjects.Clear();
        }

        [Test]
        public void Upgrade_MaterialDeclaringVatBoneTex_GetsBoneTexture()
        {
            Material material = CreateMaterialOnShader(VatShaderPath);
            VatBakeSource source = CreateSource(1u, "BaseHead", material);
            Texture2D boneTexture = Track(new Texture2D(4, 4));
            VatTextureSetAsset textureSet = CreateTextureSet(boneTexture, 1u);
            List<string> reportLines = new List<string>();

            VatMaterialUpgrader.UpgradeMaterialsForSet(textureSet, new List<VatBakeSource> { source }, reportLines);

            Assert.That(material.GetTexture("_VatBoneTex"), Is.EqualTo(boneTexture), string.Join("\n", reportLines));
        }

        [Test]
        public void Upgrade_MaterialWithoutVatInputs_IsLeftOnItsShader()
        {
            Material material = CreateMaterialOnShader(SpriteShaderPath);
            Shader shaderBefore = material.shader;
            VatBakeSource source = CreateSource(1u, "BaseHead", material);
            Texture2D boneTexture = Track(new Texture2D(4, 4));
            VatTextureSetAsset textureSet = CreateTextureSet(boneTexture, 1u);
            List<string> reportLines = new List<string>();

            VatMaterialUpgrader.UpgradeMaterialsForSet(textureSet, new List<VatBakeSource> { source }, reportLines);

            Assert.That(material.shader, Is.EqualTo(shaderBefore));
            Assert.That(reportLines, Has.Some.Contains("no _VatBoneTex"));
        }

        [Test]
        public void Upgrade_MaterialSharedByTwoParts_IsRefused()
        {
            Material sharedMaterial = CreateMaterialOnShader(VatShaderPath);
            VatBakeSource first = CreateSource(1u, "BaseHead", sharedMaterial);
            VatBakeSource second = CreateSource(2u, "BaseBody", sharedMaterial);
            Texture2D boneTexture = Track(new Texture2D(4, 4));
            VatTextureSetAsset textureSet = CreateTextureSet(boneTexture, 1u, 2u);
            List<string> reportLines = new List<string>();

            VatMaterialUpgrader.UpgradeMaterialsForSet(textureSet, new List<VatBakeSource> { first, second }, reportLines);

            Assert.That(sharedMaterial.GetTexture("_VatBoneTex"), Is.Null);
            Assert.That(reportLines, Has.Some.Contains("refused"));
        }

        private T Track<T>(T createdObject) where T : Object
        {
            createdObjects.Add(createdObject);
            return createdObject;
        }

        private Material CreateMaterialOnShader(string shaderPath)
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
            Assert.That(shader, Is.Not.Null);
            return Track(new Material(shader));
        }

        private VatBakeSource CreateSource(uint targetId, string displayName, Material material)
        {
            GameObject partObject = Track(new GameObject(displayName));
            SkinnedMeshRenderer renderer = partObject.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMaterial = material;
            return new VatBakeSource
            {
                TargetId = targetId,
                DisplayName = displayName,
                SourceNodePath = displayName,
                PrefabRenderer = renderer
            };
        }

        private VatTextureSetAsset CreateTextureSet(Texture2D boneTexture, params uint[] targetIds)
        {
            VatTextureSetAsset textureSet = Track(ScriptableObject.CreateInstance<VatTextureSetAsset>());
            textureSet.flavor = VatFlavor.BoneMatrix;
            for (int targetIndex = 0; targetIndex < targetIds.Length; targetIndex++)
            {
                textureSet.parts.Add(new VatPartTextures
                {
                    targetId = targetIds[targetIndex],
                    displayName = "Part" + targetIds[targetIndex],
                    boneTexture = boneTexture,
                    textureWidth = 4,
                    rowsPerFrame = 1,
                    boneCount = 2
                });
            }

            return textureSet;
        }
    }
}
