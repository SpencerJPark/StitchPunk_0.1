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
        private const string ScratchFolder = "Assets/A95TestScratch";

        private readonly List<Texture2D> createdTextures = new List<Texture2D>();
        private SpriteSheetAsset createdSheet;

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(ScratchFolder))
            {
                AssetDatabase.CreateFolder("Assets", "A95TestScratch");
            }
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

            AssetDatabase.DeleteAsset(ScratchFolder);
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
            createdSheet.outputPath = ScratchFolder + "/T_A95Test_Array.asset";

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
