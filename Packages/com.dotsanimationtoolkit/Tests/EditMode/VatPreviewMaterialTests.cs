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
            VatPartTextures part = new VatPartTextures();
            part.boneTexture = new Texture2D(8, 6);
            spawnedObjects.Add(part.boneTexture);
            part.textureWidth = 8;
            part.rowsPerFrame = 3;
            part.boneCount = 2;
            part.runtimeMesh = new Mesh();
            spawnedObjects.Add(part.runtimeMesh);
            textureSet.parts.Add(part);

            bool succeeded = VatPreviewMaterial.TryCreate(textureSet, part, null, out VatPreviewMaterial preview, out string failureMessage);

            Assert.IsTrue(succeeded, failureMessage);
            spawnedObjects.Add(preview.Material);

            Vector4 texelParams = preview.Material.GetVector("_VatTexelParams");
            Assert.AreEqual(new Vector4(8, 6, 3, 2), texelParams);
            Assert.AreSame(part.boneTexture, preview.Material.GetTexture("_VatBoneTex"));

            preview.Dispose();
        }

        [Test]
        public void TryCreate_RefusesAVertexFlavourSet_AndNamesTheMissingShader()
        {
            VatTextureSetAsset textureSet = ScriptableObject.CreateInstance<VatTextureSetAsset>();
            spawnedObjects.Add(textureSet);
            textureSet.flavor = VatFlavor.VertexPosition;

            bool succeeded = VatPreviewMaterial.TryCreate(textureSet, null, null, out VatPreviewMaterial preview, out string failureMessage);

            Assert.IsFalse(succeeded);
            Assert.IsNull(preview);
            StringAssert.Contains("vertex", failureMessage.ToLowerInvariant());
        }
    }
}
