using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using DotsAnimationToolkit;
using DotsAnimationToolkit.Editor;
using DotsAnimationToolkit.Authoring;

namespace DotsAnimationToolkit.Tests.EditMode
{
    [TestFixture]
    public sealed class VatPreviewFrameResolverTests
    {
        [Test]
        public void TryResolveGlobalFrame_UsesTheTargetRangeFirst_ThenTheClipRange_ClampedToTheLastRow()
        {
            BlobBuilder builder = new BlobBuilder(Allocator.Temp);
            ref ClipBlob clipRoot = ref builder.ConstructRoot<ClipBlob>();
            clipRoot.duration = 1f;
            clipRoot.vatFrameStart = 0;
            clipRoot.vatFrameCount = 31;
            clipRoot.vatFps = 30f;

            BlobBuilderArray<VatTrackRangeBlob> ranges = builder.Allocate(ref clipRoot.vatTargetRanges, 1);
            ranges[0] = new VatTrackRangeBlob
            {
                targetIndex = 2,
                frameStart = 31,
                frameCount = 16,
                fps = 15f
            };

            BlobAssetReference<ClipBlob> clipReference = builder.CreateBlobAssetReference<ClipBlob>(Allocator.Persistent);
            builder.Dispose();

            try
            {
                ref ClipBlob clip = ref clipReference.Value;

                bool targetHitResolved = VatPreviewFrameResolver.TryResolveGlobalFrame(ref clip, 2, 0.5f, out float targetHitFrame);
                Assert.IsTrue(targetHitResolved);
                Assert.AreEqual(38.5f, targetHitFrame, 1e-4f);

                bool untargetedZeroResolved = VatPreviewFrameResolver.TryResolveGlobalFrame(ref clip, 0, 0.5f, out float untargetedZeroFrame);
                Assert.IsTrue(untargetedZeroResolved);
                Assert.AreEqual(15f, untargetedZeroFrame, 1e-4f);

                bool untargetedNegativeOneResolved = VatPreviewFrameResolver.TryResolveGlobalFrame(ref clip, -1, 0.5f, out float untargetedNegativeOneFrame);
                Assert.IsTrue(untargetedNegativeOneResolved);
                Assert.AreEqual(15f, untargetedNegativeOneFrame, 1e-4f);

                bool untargetedClampedResolved = VatPreviewFrameResolver.TryResolveGlobalFrame(ref clip, -1, 1f, out float untargetedClampedFrame);
                Assert.IsTrue(untargetedClampedResolved);
                Assert.AreEqual(30f, untargetedClampedFrame, 1e-4f);

                bool targetClampedResolved = VatPreviewFrameResolver.TryResolveGlobalFrame(ref clip, 2, 1f, out float targetClampedFrame);
                Assert.IsTrue(targetClampedResolved);
                Assert.AreEqual(46f, targetClampedFrame, 1e-4f);
            }
            finally
            {
                clipReference.Dispose();
            }

            VatClipRange range = new VatClipRange
            {
                clipId = 0UL,
                targetId = 0U,
                frameStart = 31,
                frameCount = 16,
                fps = 15f
            };
            float rangeFrame = VatPreviewFrameResolver.GlobalFrameForRange(range, 5f);
            Assert.AreEqual(46f, rangeFrame, 1e-4f);
        }
    }
}
