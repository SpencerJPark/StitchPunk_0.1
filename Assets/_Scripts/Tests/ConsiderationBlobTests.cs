using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;

namespace StitchPunk.Tests
{
    // ConsiderationBlob.Evaluate is the heart of utility scoring: every awareness decision samples a
    // pre-baked curve through it. A lerp/index bug here silently inverts AI weighting, so endpoints and
    // interpolation midpoints are pinned against a hand-built 3-sample curve.
    [TestFixture]
    public sealed class ConsiderationBlobTests
    {
        private const float Tolerance = 0.0001f;

        // Builds a curve sampled as [0.0, 0.5, 1.0] over t in [0,1] (resolution 3).
        private static BlobAssetReference<ConsiderationBlob> BuildRampCurve()
        {
            BlobBuilder builder = new BlobBuilder(Allocator.Temp);
            ref ConsiderationBlob root = ref builder.ConstructRoot<ConsiderationBlob>();
            root.resolution = 3;
            BlobBuilderArray<float> samples = builder.Allocate(ref root.samples, 3);
            samples[0] = 0f;
            samples[1] = 0.5f;
            samples[2] = 1f;

            BlobAssetReference<ConsiderationBlob> blob =
                builder.CreateBlobAssetReference<ConsiderationBlob>(Allocator.Persistent);
            builder.Dispose();
            return blob;
        }

        [Test]
        public void Evaluate_ReturnsLowerEndpointAtZero()
        {
            BlobAssetReference<ConsiderationBlob> blob = BuildRampCurve();
            try
            {
                Assert.AreEqual(0f, blob.Value.Evaluate(0f), Tolerance);
            }
            finally
            {
                blob.Dispose();
            }
        }

        [Test]
        public void Evaluate_ReturnsUpperEndpointAtOne()
        {
            BlobAssetReference<ConsiderationBlob> blob = BuildRampCurve();
            try
            {
                Assert.AreEqual(1f, blob.Value.Evaluate(1f), Tolerance);
            }
            finally
            {
                blob.Dispose();
            }
        }

        [Test]
        public void Evaluate_HitsExactSampleAtMidpoint()
        {
            BlobAssetReference<ConsiderationBlob> blob = BuildRampCurve();
            try
            {
                // t=0.5 lands exactly on samples[1].
                Assert.AreEqual(0.5f, blob.Value.Evaluate(0.5f), Tolerance);
            }
            finally
            {
                blob.Dispose();
            }
        }

        [Test]
        public void Evaluate_InterpolatesBetweenSamples()
        {
            BlobAssetReference<ConsiderationBlob> blob = BuildRampCurve();
            try
            {
                // t=0.25 -> position 0.5 -> halfway between samples[0]=0 and samples[1]=0.5.
                Assert.AreEqual(0.25f, blob.Value.Evaluate(0.25f), Tolerance);
            }
            finally
            {
                blob.Dispose();
            }
        }

        [Test]
        public void Evaluate_SaturatesInputAboveOne()
        {
            BlobAssetReference<ConsiderationBlob> blob = BuildRampCurve();
            try
            {
                Assert.AreEqual(1f, blob.Value.Evaluate(5f), Tolerance);
            }
            finally
            {
                blob.Dispose();
            }
        }
    }
}
