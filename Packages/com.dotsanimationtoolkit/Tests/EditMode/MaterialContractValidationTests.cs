using System.Collections.Generic;
using NUnit.Framework;
using DotsAnimationToolkit.Authoring;
using UnityEngine;

namespace DotsAnimationToolkit.Tests.EditMode
{
    public sealed class MaterialContractValidationTests
    {
        private Material testMaterial;

        [TearDown]
        public void TearDown()
        {
            if (testMaterial != null)
            {
                Object.DestroyImmediate(testMaterial);
                testMaterial = null;
            }
        }

        [Test]
        public void VatMesh_MissingVatFrameA_IsAnError()
        {
            testMaterial = new Material(Shader.Find("Unlit/Color"));
            testMaterial.enableInstancing = true;

            List<ValidationMessage> messages = new List<ValidationMessage>();
            MaterialContractValidation.Validate(testMaterial, TargetKind.VatMesh, "Body", messages);

            bool foundVatFrameAError = false;
            for (int messageIndex = 0; messageIndex < messages.Count; messageIndex++)
            {
                ValidationMessage message = messages[messageIndex];
                if (message.IsError && message.text.Contains("_VatFrameA"))
                {
                    foundVatFrameAError = true;
                }
            }

            Assert.IsTrue(foundVatFrameAError);
        }

        [Test]
        public void Quad_MissingVatFrameA_IsNotReported()
        {
            testMaterial = new Material(Shader.Find("Unlit/Color"));
            testMaterial.enableInstancing = true;

            List<ValidationMessage> messages = new List<ValidationMessage>();
            MaterialContractValidation.Validate(testMaterial, TargetKind.Quad, "Body", messages);

            Assert.IsEmpty(messages);
        }
    }
}
