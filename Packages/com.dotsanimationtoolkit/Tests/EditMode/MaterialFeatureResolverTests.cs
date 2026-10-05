// Copyright (c) 2026 Spencer Park. All rights reserved.

using NUnit.Framework;
using DotsAnimationToolkit.Editor;
using UnityEditor;
using UnityEngine;

namespace DotsAnimationToolkit.Tests.EditMode
{
    public sealed class MaterialFeatureResolverTests
    {
        [TestCase(MaterialTemplateUtility.QuadTemplateShaderPath, MaterialFeature.None)]
        [TestCase(MaterialTemplateUtility.FlipbookTemplateShaderPath, MaterialFeature.Flipbook)]
        [TestCase(MaterialTemplateUtility.VatTemplateShaderPath, MaterialFeature.Vat)]
        public void ReadFeatures_EachShippedGraph_ReportsItsFeature(string shaderPath, MaterialFeature expectedFeatures)
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
            Assert.That(shader, Is.Not.Null, shaderPath);
            Material material = new Material(shader);
            try
            {
                Assert.That(MaterialFeatureResolver.ReadFeatures(material), Is.EqualTo(expectedFeatures));
                Assert.That(MaterialFeatureResolver.IsToolkitShader(shader), Is.True);
                Assert.That(MaterialFeatureResolver.ResolveToolkitShaderPath(expectedFeatures), Is.EqualTo(shaderPath));
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void ResolveToolkitShaderPath_FlipbookAndVat_IsNull()
        {
            Assert.That(MaterialFeatureResolver.ResolveToolkitShaderPath(MaterialFeature.Flipbook | MaterialFeature.Vat), Is.Null);
        }
    }
}
