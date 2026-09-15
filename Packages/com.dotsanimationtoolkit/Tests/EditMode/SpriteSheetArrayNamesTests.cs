// Copyright (c) 2026 Spencer Park. All rights reserved.

using NUnit.Framework;
using DotsAnimationToolkit.Authoring;
using DotsAnimationToolkit.Editor;
using UnityEditor;
using UnityEngine;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>Covers GetOrCreateSheetForArray naming and reuse over a Texture2DArray.</summary>
    public sealed class SpriteSheetArrayNamesTests
    {
        private string scratchFolderPath;

        [SetUp]
        public void SetUp()
        {
            string scratchFolderName = "SpriteSheetArrayNamesScratch_" + System.Guid.NewGuid().ToString("N");
            string createdFolderGuid = AssetDatabase.CreateFolder("Assets", scratchFolderName);
            Assert.IsFalse(string.IsNullOrEmpty(createdFolderGuid), "Failed to create the scratch folder.");
            scratchFolderPath = AssetDatabase.GUIDToAssetPath(createdFolderGuid);
        }

        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrEmpty(scratchFolderPath))
            {
                AssetDatabase.DeleteAsset(scratchFolderPath);
            }

            scratchFolderPath = null;
        }

        [Test]
        public void GetOrCreateSheetForArray_NamesByIndex_AndReusesTheExistingSheet()
        {
            Texture2DArray array = new Texture2DArray(2, 2, 3, TextureFormat.RGBA32, false);
            AssetDatabase.CreateAsset(array, scratchFolderPath + "/NamesTestArray.asset");

            SpriteSheetAsset first = SpriteSheetAssetUtility.GetOrCreateSheetForArray(array);

            Assert.IsNotNull(first);
            Assert.AreEqual(scratchFolderPath + "/NamesTestArray_Sheet.asset", AssetDatabase.GetAssetPath(first));
            Assert.AreSame(array, first.texture);
            Assert.AreEqual(3, first.frames.Count);

            for (int frameIndex = 0; frameIndex < first.frames.Count; frameIndex++)
            {
                Assert.AreEqual(frameIndex.ToString(), first.frames[frameIndex].name);
                Assert.AreEqual(frameIndex, first.frames[frameIndex].index);
                Assert.IsNull(first.frames[frameIndex].source);
            }

            SpriteSheetAsset second = SpriteSheetAssetUtility.GetOrCreateSheetForArray(array);

            Assert.AreSame(first, second);
            Assert.AreEqual(1, AssetDatabase.FindAssets("t:SpriteSheetAsset", new[] { scratchFolderPath }).Length);
        }
    }
}
