// Copyright (c) 2026 Spencer Park. All rights reserved.

using NUnit.Framework;
using DotsAnimationToolkit.Editor;
using UnityEditor;
using UnityEngine;

namespace DotsAnimationToolkit.Tests.EditMode
{
    public sealed class MaterialAuthoringUtilityTests
    {
        [Test]
        public void TrySetToolkitFeatures_StaticToVat_KeepsBaseColor()
        {
            Shader staticShader = AssetDatabase.LoadAssetAtPath<Shader>(MaterialTemplateUtility.QuadTemplateShaderPath);
            Assert.That(staticShader, Is.Not.Null);
            Material material = new Material(staticShader);
            try
            {
                Color distinctColor = new Color(0.25f, 0.5f, 0.75f, 1f);
                material.SetColor("_BaseColor", distinctColor);

                string resultMessage;
                bool succeeded = MaterialAuthoringUtility.TrySetToolkitFeatures(material, MaterialFeature.Vat, out resultMessage);

                Assert.That(succeeded, Is.True, resultMessage);
                Assert.That(MaterialFeatureResolver.ReadFeatures(material), Is.EqualTo(MaterialFeature.Vat));
                Assert.That(material.GetColor("_BaseColor"), Is.EqualTo(distinctColor));
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }
    }
}
