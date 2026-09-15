// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using NUnit.Framework;
using DotsAnimationToolkit.Authoring;
using DotsAnimationToolkit.Editor;
using UnityEditor;
using UnityEngine;

namespace DotsAnimationToolkit.Tests.EditMode
{
    public sealed class SpriteSheetBakerTests
    {
        // A fresh root-level folder per run: the baker only writes under the project root, and a
        // package file may not name a project folder (Conformance_D), so the path comes back from its GUID.
        private string scratchFolderPath;

        private readonly List<Texture2D> createdTextures = new List<Texture2D>();
        private SpriteSheetAsset createdSheet;

        [SetUp]
        public void SetUp()
        {
            string scratchFolderName = "SpriteSheetBakerScratch_" + System.Guid.NewGuid().ToString("N");
            string createdFolderGuid = AssetDatabase.CreateFolder("Assets", scratchFolderName);
            Assert.IsFalse(
                string.IsNullOrEmpty(createdFolderGuid),
                "Failed to create the scratch folder the baker fixture writes into.");
            scratchFolderPath = AssetDatabase.GUIDToAssetPath(createdFolderGuid);
        }

        [TearDown]
        public void TearDown()
        {
            for (int textureIndex = 0; textureIndex < createdTextures.Count; textureIndex++)
            {
                Object.DestroyImmediate(createdTextures[textureIndex]);
            }
            createdTextures.Clear();

            if (createdSheet != null)
            {
                Object.DestroyImmediate(createdSheet);
                createdSheet = null;
            }

            if (!string.IsNullOrEmpty(scratchFolderPath))
            {
                AssetDatabase.DeleteAsset(scratchFolderPath);
                scratchFolderPath = null;
            }
        }

        private Texture2D CreateSolidTexture(Color32 fillColor)
        {
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            Color32[] pixels = { fillColor, fillColor, fillColor, fillColor };
            texture.SetPixels32(pixels);
            texture.Apply();
            createdTextures.Add(texture);
            return texture;
        }

        [Test]
        public void Bake_LayerOrderIsListOrder()
        {
            Color32 red = new Color32(255, 0, 0, 255);
            Color32 green = new Color32(0, 255, 0, 255);
            Color32 blue = new Color32(0, 0, 255, 255);

            Texture2D redSource = CreateSolidTexture(red);
            Texture2D greenSource = CreateSolidTexture(green);
            Texture2D blueSource = CreateSolidTexture(blue);

            createdSheet = ScriptableObject.CreateInstance<SpriteSheetAsset>();
            createdSheet.frames = new List<SpriteSheetFrame>
            {
                new SpriteSheetFrame { name = "red", source = redSource, index = 7 },
                new SpriteSheetFrame { name = "green", source = greenSource, index = 3 },
                new SpriteSheetFrame { name = "blue", source = blueSource, index = 5 }
            };
            createdSheet.outputPath = scratchFolderPath + "/T_A95Test_Array.asset";

            SpriteSheetBaker baker = new SpriteSheetBaker();
            bool bakeSucceeded = baker.Bake(createdSheet, out string error);

            Assert.IsTrue(bakeSucceeded, error);

            Texture2DArray loadedArray = AssetDatabase.LoadAssetAtPath<Texture2DArray>(createdSheet.outputPath);
            Assert.IsNotNull(loadedArray);
            Assert.AreEqual(3, loadedArray.depth);

            Color32[] sourceColors = { red, green, blue };
            for (int layerIndex = 0; layerIndex < sourceColors.Length; layerIndex++)
            {
                Color32 layerPixel = loadedArray.GetPixels32(layerIndex, 0)[0];
                Color32 expectedColor = sourceColors[layerIndex];
                Assert.AreEqual(expectedColor.r, layerPixel.r);
                Assert.AreEqual(expectedColor.g, layerPixel.g);
                Assert.AreEqual(expectedColor.b, layerPixel.b);
                Assert.AreEqual(expectedColor.a, layerPixel.a);
            }

            for (int frameIndex = 0; frameIndex < createdSheet.frames.Count; frameIndex++)
            {
                Assert.AreEqual(frameIndex, createdSheet.frames[frameIndex].index);
            }

            Assert.AreEqual(loadedArray, createdSheet.texture);
        }
    }
}
