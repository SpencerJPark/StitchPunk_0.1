using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using PlaytestCopilot;
using UnityEngine;

namespace PlaytestCopilot.Tests.Editor
{
    /// Pure-logic coverage for the reference resolver's confidence ranking, the annotation
    /// stroke geometry helpers, and the WAV header writer. No scene, camera rendering, or
    /// physics tick is required by any test kept here.
    [TestFixture]
    public sealed class PlaytestRuntimeLogicTests
    {
        private readonly List<GameObject> spawnedGameObjects = new List<GameObject>();
        private readonly List<string> writtenFilePaths = new List<string>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject spawnedGameObject in spawnedGameObjects)
            {
                if (spawnedGameObject != null)
                {
                    Object.DestroyImmediate(spawnedGameObject);
                }
            }

            spawnedGameObjects.Clear();

            foreach (string writtenFilePath in writtenFilePaths)
            {
                if (File.Exists(writtenFilePath))
                {
                    File.Delete(writtenFilePath);
                }
            }

            writtenFilePaths.Clear();
        }

        [Test]
        public void ConfidenceFor_SixSources_MatchDocumentedBaseValuesAndAreStrictlyDescending()
        {
            PlaytestReferenceSource[] sourcesHighestToLowest =
            {
                PlaytestReferenceSource.CircledRegion,
                PlaytestReferenceSource.PointerUnderCursor,
                PlaytestReferenceSource.EditorSelection,
                PlaytestReferenceSource.TranscriptName,
                PlaytestReferenceSource.NearCameraCenter,
                PlaytestReferenceSource.RecentInteraction
            };

            float[] expectedBaseConfidences = { 0.92f, 0.80f, 0.70f, 0.60f, 0.45f, 0.40f };

            for (int sourceIndex = 0; sourceIndex < sourcesHighestToLowest.Length; sourceIndex++)
            {
                float actualConfidence = PlaytestReferenceResolver.ConfidenceFor(sourcesHighestToLowest[sourceIndex]);
                Assert.AreEqual(expectedBaseConfidences[sourceIndex], actualConfidence, 0.0001f,
                    $"Base confidence for {sourcesHighestToLowest[sourceIndex]} does not match the documented value.");
            }

            for (int sourceIndex = 0; sourceIndex < sourcesHighestToLowest.Length - 1; sourceIndex++)
            {
                float higherConfidence = PlaytestReferenceResolver.ConfidenceFor(sourcesHighestToLowest[sourceIndex]);
                float lowerConfidence = PlaytestReferenceResolver.ConfidenceFor(sourcesHighestToLowest[sourceIndex + 1]);
                Assert.Greater(higherConfidence, lowerConfidence,
                    $"{sourcesHighestToLowest[sourceIndex]} must strictly outrank {sourcesHighestToLowest[sourceIndex + 1]}.");
            }
        }

        [Test]
        public void Resolve_ObjectAgreedByCursorAndSelection_AddsAgreementBonusAndNamesBothSources()
        {
            GameObject agreedGameObject = new GameObject("AgreedTarget");
            spawnedGameObjects.Add(agreedGameObject);

            GameObject cameraGameObject = new GameObject("ResolveTestCamera");
            spawnedGameObjects.Add(cameraGameObject);
            Camera captureCamera = cameraGameObject.AddComponent<Camera>();

            PlaytestReferenceResolver.ResolveRequest resolveRequest = new PlaytestReferenceResolver.ResolveRequest
            {
                CaptureCamera = captureCamera,
                ObjectUnderCursor = agreedGameObject,
                EditorSelection = agreedGameObject
            };

            List<PlaytestObjectReference> resolvedReferences = PlaytestReferenceResolver.Resolve(resolveRequest);

            Assert.AreEqual(1, resolvedReferences.Count);
            float expectedConfidence = PlaytestReferenceResolver.ConfidenceFor(PlaytestReferenceSource.PointerUnderCursor) + 0.05f;
            Assert.AreEqual(expectedConfidence, resolvedReferences[0].Confidence, 0.0001f);
            Assert.AreEqual("PointerUnderCursor+EditorSelection", resolvedReferences[0].ResolvedBy);
        }

        [Test]
        public void Resolve_DefaultConstructedRequest_ReturnsEmptyListWithoutThrowing()
        {
            PlaytestReferenceResolver.ResolveRequest emptyRequest = new PlaytestReferenceResolver.ResolveRequest();

            List<PlaytestObjectReference> resolvedReferences = null;
            Assert.DoesNotThrow(() => resolvedReferences = PlaytestReferenceResolver.Resolve(emptyRequest));
            Assert.IsNotNull(resolvedReferences);
            Assert.AreEqual(0, resolvedReferences.Count);
        }

        [Test]
        public void BoundsOf_NullStrokeAndEmptyPointList_ReturnsRectZero()
        {
            Assert.AreEqual(Rect.zero, PlaytestAnnotationStrokes.BoundsOf(null));

            PlaytestStroke emptyPointStroke = new PlaytestStroke();
            Assert.AreEqual(Rect.zero, PlaytestAnnotationStrokes.BoundsOf(emptyPointStroke));
        }

        [Test]
        public void ToRegions_InflatesSinglePointTapToAtLeastEightByEightPixels()
        {
            // A zero-size rect raycasts through nothing, so a tap must still select something.
            PlaytestStroke tapStroke = new PlaytestStroke
            {
                Tool = PlaytestAnnotationTool.Pen,
                ScreenPoints = new List<Vector2> { new Vector2(50f, 50f) }
            };

            List<PlaytestScreenRegion> regions = PlaytestAnnotationStrokes.ToRegions(
                new List<PlaytestStroke> { tapStroke });

            Assert.AreEqual(1, regions.Count);
            Assert.GreaterOrEqual(regions[0].ScreenRect.width, 8f);
            Assert.GreaterOrEqual(regions[0].ScreenRect.height, 8f);
        }

        [Test]
        public void Write_ProducesValidRiffWaveHeaderWithMatchingSampleRateAndLength()
        {
            string temporaryWavPath = Path.Combine(Path.GetTempPath(), "PlaytestRuntimeLogicTests_Write.wav");
            writtenFilePaths.Add(temporaryWavPath);

            int sampleCount = 100;
            int sampleRate = 24000;
            float[] monoSamples = new float[sampleCount];
            for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
            {
                monoSamples[sampleIndex] = 0f;
            }

            PlaytestWavWriter.Write(temporaryWavPath, monoSamples, sampleCount, sampleRate);

            byte[] wavFileBytes = File.ReadAllBytes(temporaryWavPath);

            Assert.AreEqual('R', (char)wavFileBytes[0]);
            Assert.AreEqual('I', (char)wavFileBytes[1]);
            Assert.AreEqual('F', (char)wavFileBytes[2]);
            Assert.AreEqual('F', (char)wavFileBytes[3]);

            Assert.AreEqual('W', (char)wavFileBytes[8]);
            Assert.AreEqual('A', (char)wavFileBytes[9]);
            Assert.AreEqual('V', (char)wavFileBytes[10]);
            Assert.AreEqual('E', (char)wavFileBytes[11]);

            int sampleRateFromHeader = System.BitConverter.ToInt32(wavFileBytes, 24);
            Assert.AreEqual(sampleRate, sampleRateFromHeader);

            int expectedFileLength = 44 + (sampleCount * 2);
            Assert.AreEqual(expectedFileLength, wavFileBytes.Length);
        }
    }
}
