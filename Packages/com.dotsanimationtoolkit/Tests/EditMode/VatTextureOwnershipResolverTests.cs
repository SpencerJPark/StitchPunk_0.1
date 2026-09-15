// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using DotsAnimationToolkit.Authoring;
using DotsAnimationToolkit.Editor;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>Confirms the VAT ownership resolver keeps a set's own textures but drops any texture another set still references.</summary>
    public sealed class VatTextureOwnershipResolverTests
    {
        private AuthoringTestAssets assets;

        [SetUp]
        public void SetUp()
        {
            assets = new AuthoringTestAssets();
        }

        [TearDown]
        public void TearDown()
        {
            assets.DestroyAll();
        }

        [Test]
        public void SharedTexture_IsNotSafeToTrash()
        {
            VatTextureSetAsset setA = assets.CreateVatTextureSet("SetA", 1UL);
            VatTextureSetAsset setB = assets.CreateVatTextureSet("SetB", 2UL);

            Texture2D ownTexture = new Texture2D(1, 1);
            assets.Track(ownTexture);
            Texture2D sharedTexture = new Texture2D(1, 1);
            assets.Track(sharedTexture);

            setA.parts[0].boneTexture = ownTexture;
            setA.parts[0].positionTexture = sharedTexture;
            setB.parts[0].boneTexture = new Texture2D(1, 1);
            assets.Track(setB.parts[0].boneTexture);
            setB.parts[0].positionTexture = sharedTexture;

            List<VatTextureSetAsset> allSets = new List<VatTextureSetAsset> { setA, setB };

            List<Object> result = VatTextureOwnershipResolver.FindTexturesSafeToTrash(setA, allSets);

            Assert.AreEqual(1, result.Count);
            Assert.AreSame(ownTexture, result[0]);
        }
    }
}
