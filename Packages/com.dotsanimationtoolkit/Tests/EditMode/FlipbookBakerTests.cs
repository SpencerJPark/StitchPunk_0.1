// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using DotsAnimationToolkit.Authoring;
using DotsAnimationToolkit.Editor;
using UnityEditor;
using UnityEngine;

namespace DotsAnimationToolkit.Tests.EditMode
{
    public sealed class FlipbookBakerTests
    {
        // A fresh root-level folder per run: the baker only writes under the project root, and a
        // package file may not name a project folder (Conformance_D), so the path comes back from its GUID.
        private string scratchFolderPath;

        private readonly List<Texture2D> createdTextures = new List<Texture2D>();
        private FlipbookAsset createdFlipbook;

        [SetUp]
        public void SetUp()
        {
            string scratchFolderName = "FlipbookBakerScratch_" + System.Guid.NewGuid().ToString("N");
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

            if (createdFlipbook != null)
            {
                Object.DestroyImmediate(createdFlipbook);
                createdFlipbook = null;
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

        private Texture2DArray CreateReferenceArray()
        {
            Texture2D referenceSource = CreateSolidTexture(new Color32(128, 64, 200, 255));
            byte[] referenceBytes = referenceSource.EncodeToPNG();
            string referencePath = scratchFolderPath + "/ReferenceArray.png";
            File.WriteAllBytes(Path.GetFullPath(referencePath), referenceBytes);
            AssetDatabase.ImportAsset(referencePath, ImportAssetOptions.ForceSynchronousImport);

            TextureImporter referenceImporter = AssetImporter.GetAtPath(referencePath) as TextureImporter;
            Assert.IsNotNull(referenceImporter, "The reference PNG did not import as a texture.");

            TextureImporterSettings referenceSettings = new TextureImporterSettings();
            referenceImporter.ReadTextureSettings(referenceSettings);
            referenceSettings.textureShape = TextureImporterShape.Texture2DArray;
            referenceSettings.flipbookRows = 1;
            referenceSettings.flipbookColumns = 1;
            referenceSettings.filterMode = FilterMode.Trilinear;
            referenceSettings.mipmapEnabled = false;
            referenceImporter.SetTextureSettings(referenceSettings);
            referenceImporter.textureCompression = TextureImporterCompression.CompressedHQ;
            referenceImporter.SaveAndReimport();

            Texture2DArray referenceArray = AssetDatabase.LoadAssetAtPath<Texture2DArray>(referencePath);
            Assert.IsNotNull(referenceArray, "The reference PNG did not import as a Texture2DArray.");
            return referenceArray;
        }

        [Test]
        public void Bake_LayerOrderIsListOrder()
        {
            Texture2DArray referenceArray = CreateReferenceArray();

            Color32 red = new Color32(255, 0, 0, 255);
            Color32 green = new Color32(0, 255, 0, 255);
            Color32 blue = new Color32(0, 0, 255, 255);

            Texture2D redSource = CreateSolidTexture(red);
            Texture2D greenSource = CreateSolidTexture(green);
            Texture2D blueSource = CreateSolidTexture(blue);

            createdFlipbook = ScriptableObject.CreateInstance<FlipbookAsset>();
            createdFlipbook.frames = new List<FlipbookFrame>
            {
                new FlipbookFrame { name = "red", source = redSource, index = 7 },
                new FlipbookFrame { name = "green", source = greenSource, index = 3 },
                new FlipbookFrame { name = "blue", source = blueSource, index = 5 }
            };
            createdFlipbook.outputPath = scratchFolderPath + "/T_A95FTest_Array.png";
            createdFlipbook.importSettingsSource = referenceArray;

            FlipbookBaker baker = new FlipbookBaker();
            bool bakeSucceeded = baker.Bake(createdFlipbook, out string error);

            Assert.IsTrue(bakeSucceeded, error);

            TextureImporter outputImporter = AssetImporter.GetAtPath(createdFlipbook.outputPath) as TextureImporter;
            Assert.IsNotNull(outputImporter);
            Assert.AreEqual(TextureImporterShape.Texture2DArray, outputImporter.textureShape);
            Assert.AreEqual(FilterMode.Trilinear, outputImporter.filterMode);
            Assert.IsFalse(outputImporter.mipmapEnabled);
            Assert.AreEqual(TextureImporterCompression.CompressedHQ, outputImporter.textureCompression);

            // Force uncompressed + readable so the pixel checks below read exact layer colours.
            outputImporter.textureCompression = TextureImporterCompression.Uncompressed;
            outputImporter.isReadable = true;
            outputImporter.SaveAndReimport();

            Texture2DArray loadedArray = AssetDatabase.LoadAssetAtPath<Texture2DArray>(createdFlipbook.outputPath);
            Assert.IsNotNull(loadedArray);
            Assert.AreEqual(4, loadedArray.depth);

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

            Color32 paddingPixel = loadedArray.GetPixels32(3, 0)[0];
            Assert.AreEqual(0, paddingPixel.a);

            for (int frameIndex = 0; frameIndex < createdFlipbook.frames.Count; frameIndex++)
            {
                Assert.AreEqual(frameIndex, createdFlipbook.frames[frameIndex].index);
            }

            Assert.AreEqual(loadedArray, createdFlipbook.texture);
            Assert.AreEqual(createdFlipbook.outputPath, AssetDatabase.GetAssetPath(createdFlipbook.texture));
        }
    }
}
