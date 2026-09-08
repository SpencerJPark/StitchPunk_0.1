using NUnit.Framework;
using DotsAnimationToolkit.Editor;
using DotsAnimationToolkit.Authoring;
using UnityEngine;
using System.Collections.Generic;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>Covers VatPreviewMaterial's success binding path and its refusal of a vertex-flavour set.</summary>
    public sealed class VatPreviewMaterialTests
    {
        private readonly List<UnityEngine.Object> spawnedObjects = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (UnityEngine.Object spawnedObject in spawnedObjects)
            {
                if (spawnedObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(spawnedObject);
                }
            }
            spawnedObjects.Clear();
        }

        [Test]
        public void TryCreate_BindsTexelParamsFromTheSet()
        {
            VatTextureSetAsset textureSet = ScriptableObject.CreateInstance<VatTextureSetAsset>();
            spawnedObjects.Add(textureSet);
            textureSet.flavor = VatFlavor.BoneMatrix;
            textureSet.boneTexture = new Texture2D(8, 6);
            spawnedObjects.Add(textureSet.boneTexture);
            textureSet.textureWidth = 8;
            textureSet.rowsPerFrame = 3;
            textureSet.boneCount = 2;
            textureSet.runtimeMesh = new Mesh();
            spawnedObjects.Add(textureSet.runtimeMesh);

            bool succeeded = VatPreviewMaterial.TryCreate(textureSet, null, out VatPreviewMaterial preview, out string failureMessage);

            Assert.IsTrue(succeeded, failureMessage);
            spawnedObjects.Add(preview.Material);

            Vector4 texelParams = preview.Material.GetVector("_VatTexelParams");
            Assert.AreEqual(new Vector4(8, 6, 3, 2), texelParams);
            Assert.AreSame(textureSet.boneTexture, preview.Material.GetTexture("_VatBoneTex"));

            preview.Dispose();
        }

        [Test]
        public void TryCreate_RefusesAVertexFlavourSet_AndNamesTheMissingShader()
        {
            VatTextureSetAsset textureSet = ScriptableObject.CreateInstance<VatTextureSetAsset>();
            spawnedObjects.Add(textureSet);
            textureSet.flavor = VatFlavor.VertexPosition;

            bool succeeded = VatPreviewMaterial.TryCreate(textureSet, null, out VatPreviewMaterial preview, out string failureMessage);

            Assert.IsFalse(succeeded);
            Assert.IsNull(preview);
            StringAssert.Contains("vertex", failureMessage.ToLowerInvariant());
        }
    }
}
