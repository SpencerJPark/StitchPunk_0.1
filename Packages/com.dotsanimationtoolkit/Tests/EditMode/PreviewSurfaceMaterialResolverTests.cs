// Copyright (c) 2026 Spencer Park. All rights reserved.

using DotsAnimationToolkit.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>Guards preview proxies against the built-in Standard material that URP draws magenta.</summary>
    public sealed class PreviewSurfaceMaterialResolverTests
    {
        private Material createdMaterial;

        [TearDown]
        public void TearDown()
        {
            // The IsPersistent guard matters because a reverted resolver returns a built-in asset, which must never be destroyed.
            if (createdMaterial != null && !EditorUtility.IsPersistent(createdMaterial))
            {
                UnityEngine.Object.DestroyImmediate(createdMaterial);
            }
            createdMaterial = null;
        }

        [Test]
        public void CreateNeutralSurfaceMaterial_UsesAPipelineShader_NotTheBuiltInStandardMaterial()
        {
            createdMaterial = PreviewSurfaceMaterialResolver.CreateNeutralSurfaceMaterial();

            Assert.IsNotNull(createdMaterial);
            Assert.IsNotNull(createdMaterial.shader);

            // Hard-coded on purpose, so a change to the resolver's shader chain cannot also move the test.
            string[] pipelineShaderNames =
            {
                "Universal Render Pipeline/Unlit",
                "Universal Render Pipeline/Lit",
                "Sprites/Default",
            };
            CollectionAssert.Contains(
                pipelineShaderNames,
                createdMaterial.shader.name,
                "Preview proxy resolved a non-pipeline shader; Default-Diffuse (built-in Standard) renders magenta under URP.");

            Assert.AreEqual(HideFlags.HideAndDontSave, createdMaterial.hideFlags);
        }
    }
}
