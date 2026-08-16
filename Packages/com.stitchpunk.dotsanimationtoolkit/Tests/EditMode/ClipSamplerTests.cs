// Copyright (c) 2026 Stitch Punk. All rights reserved.

using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;

namespace StitchPunk.AnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// EditMode coverage of single-clip sampling (architecture sections 5.6, 5.7, 8 M3, 11):
    /// key-segment selection, easing application, the single-frame and empty-clip edge cases,
    /// channel masking, and hold-the-last-key sprite sampling with the −1 keep-current convention.
    /// </summary>
    public sealed class ClipSamplerTests
    {
        private const float Tolerance = 1e-5f;

        private BlobAssetReference<ClipBlob> clipReference;

        [TearDown]
        public void DisposeFixture()
        {
            if (clipReference.IsCreated)
            {
                clipReference.Dispose();
            }
            clipReference = default;
        }

        private static TargetRestPose RestPose()
        {
            return new TargetRestPose
            {
                localPosition = new float3(10f, 20f, 3f),
                rotation = new float3(0f, 0f, 0.5f),
                scale = new float3(1f, 1f, 1f),
                restSliceIndex = 7
            };
        }

        [Test]
        public void SampleTransformTrack_SingleKeyClip_ReturnsItsOnlyPoseAtEveryTime()
        {
            clipReference = TestBlobFactory.BuildClip(new TestBlobFactory.ClipSpec
            {
                transformTracks = new TestBlobFactory.TransformTrackSpec[]
                {
                    new TestBlobFactory.TransformTrackSpec
                    {
                        targetIndex = 0,
                        channels = AnimatedChannels.PositionXY | AnimatedChannels.Rotation | AnimatedChannels.Scale,
                        keys = new TestBlobFactory.TransformKeySpec[]
                        {
                            TestBlobFactory.Key(0.5f, 1f, 2f, Interpolation.Linear, 0.25f, 3f, 4f)
                        }
                    }
                }
            });

            float[] sampleTimes = { 0f, 0.25f, 0.5f, 0.75f, 1f };
            foreach (float sampleTime in sampleTimes)
            {
                ClipSampler.SampleTransformTrack(
                    ref clipReference.Value.transformTracks[0],
                    sampleTime,
                    out float3 sampledPosition,
                    out float3 sampledRotationZ,
                    out float3 sampledScale);
                Assert.AreEqual(1f, sampledPosition.x, Tolerance, "t = " + sampleTime);
                Assert.AreEqual(2f, sampledPosition.y, Tolerance, "t = " + sampleTime);
                Assert.AreEqual(0.25f, sampledRotationZ.z, Tolerance, "t = " + sampleTime);
                Assert.AreEqual(3f, sampledScale.x, Tolerance, "t = " + sampleTime);
                Assert.AreEqual(4f, sampledScale.y, Tolerance, "t = " + sampleTime);
            }
        }

        [Test]
        public void SampleTransformTrack_OutsideKeyRange_ClampsToEdgeKeys()
        {
            clipReference = TestBlobFactory.BuildClip(new TestBlobFactory.ClipSpec
            {
                transformTracks = new TestBlobFactory.TransformTrackSpec[]
                {
                    new TestBlobFactory.TransformTrackSpec
                    {
                        targetIndex = 0,
                        keys = new TestBlobFactory.TransformKeySpec[]
                        {
                            TestBlobFactory.Key(0.25f, 1f, 0f),
                            TestBlobFactory.Key(0.75f, 5f, 0f)
                        }
                    }
                }
            });

            ClipSampler.SampleTransformTrack(
                ref clipReference.Value.transformTracks[0], 0f,
                out float3 beforePosition, out float3 beforeRotation, out float3 beforeScale);
            Assert.AreEqual(1f, beforePosition.x, Tolerance, "Before the first key: clamp to it.");

            ClipSampler.SampleTransformTrack(
                ref clipReference.Value.transformTracks[0], 1f,
                out float3 afterPosition, out float3 afterRotation, out float3 afterScale);
            Assert.AreEqual(5f, afterPosition.x, Tolerance, "After the last key: clamp to it.");
        }

        [Test]
        public void SampleTransformTrack_LinearSegment_BlendsBetweenKeys()
        {
            clipReference = TestBlobFactory.BuildClip(new TestBlobFactory.ClipSpec
            {
                transformTracks = new TestBlobFactory.TransformTrackSpec[]
                {
                    new TestBlobFactory.TransformTrackSpec
                    {
                        targetIndex = 0,
                        keys = new TestBlobFactory.TransformKeySpec[]
                        {
                            TestBlobFactory.Key(0f, 0f, 0f),
                            TestBlobFactory.Key(1f, 4f, 8f)
                        }
                    }
                }
            });

            ClipSampler.SampleTransformTrack(
                ref clipReference.Value.transformTracks[0], 0.25f,
                out float3 sampledPosition, out float3 sampledRotationZ, out float3 sampledScale);
            Assert.AreEqual(1f, sampledPosition.x, Tolerance);
            Assert.AreEqual(2f, sampledPosition.y, Tolerance);
        }

        [Test]
        public void SampleTransformTrack_StepKey_HoldsTheLeftKeyUntilTheNextKey()
        {
            clipReference = TestBlobFactory.BuildClip(new TestBlobFactory.ClipSpec
            {
                transformTracks = new TestBlobFactory.TransformTrackSpec[]
                {
                    new TestBlobFactory.TransformTrackSpec
                    {
                        targetIndex = 0,
                        keys = new TestBlobFactory.TransformKeySpec[]
                        {
                            TestBlobFactory.Key(0f, 1f, 0f, Interpolation.Step),
                            TestBlobFactory.Key(1f, 9f, 0f)
                        }
                    }
                }
            });

            ClipSampler.SampleTransformTrack(
                ref clipReference.Value.transformTracks[0], 0.99f,
                out float3 heldPosition, out float3 heldRotation, out float3 heldScale);
            Assert.AreEqual(1f, heldPosition.x, Tolerance, "Step must hold the left key across the segment.");

            ClipSampler.SampleTransformTrack(
                ref clipReference.Value.transformTracks[0], 1f,
                out float3 endPosition, out float3 endRotation, out float3 endScale);
            Assert.AreEqual(9f, endPosition.x, Tolerance, "Exactly at the next key, its value applies.");
        }

        [Test]
        public void SampleTransformTrack_EasedSegment_UsesTheLeftKeysInterpolation()
        {
            clipReference = TestBlobFactory.BuildClip(new TestBlobFactory.ClipSpec
            {
                transformTracks = new TestBlobFactory.TransformTrackSpec[]
                {
                    new TestBlobFactory.TransformTrackSpec
                    {
                        targetIndex = 0,
                        keys = new TestBlobFactory.TransformKeySpec[]
                        {
                            TestBlobFactory.Key(0f, 0f, 0f, Interpolation.EaseIn),
                            TestBlobFactory.Key(1f, 4f, 0f, Interpolation.Linear)
                        }
                    }
                }
            });

            ClipSampler.SampleTransformTrack(
                ref clipReference.Value.transformTracks[0], 0.5f,
                out float3 sampledPosition, out float3 sampledRotationZ, out float3 sampledScale);
            Assert.AreEqual(1f, sampledPosition.x, Tolerance,
                "EaseIn on the left key: weight = 0.5² = 0.25 → position.x = 4 × 0.25.");
        }

        [Test]
        public void SamplePose_EmptyClip_ReturnsTheRestPose()
        {
            clipReference = TestBlobFactory.BuildClip(new TestBlobFactory.ClipSpec());

            ClipSampler.SamplePose(
                ref clipReference.Value, 0, 0.5f, RestPose(), out TargetPose pose);

            Assert.AreEqual(10f, pose.localPosition.x, Tolerance);
            Assert.AreEqual(20f, pose.localPosition.y, Tolerance);
            Assert.AreEqual(3f, pose.localPosition.z, Tolerance);
            Assert.AreEqual(0.5f, pose.rotation.z, Tolerance);
            Assert.AreEqual(1f, pose.scale.x, Tolerance);
            Assert.AreEqual(7, pose.sliceIndex);
            Assert.AreEqual(1f, pose.atlasRect.x, Tolerance);
            Assert.AreEqual(1f, pose.atlasRect.y, Tolerance);
            Assert.AreEqual(0f, pose.atlasRect.z, Tolerance);
            Assert.AreEqual(0f, pose.atlasRect.w, Tolerance);
        }

        /// <summary>
        /// Pins both halves of amendment A31 at once on a <em>non-identity</em> rest pose: the
        /// masked channel is <c>rest + key</c> (0.5 + 1.5), and unmasked channels are left at rest
        /// untouched. A fixture resting at the origin with unit scale could not tell the first half
        /// from "the key replaces rest outright", which is how the two readings coexisted for three
        /// gates.
        /// </summary>
        [Test]
        public void SamplePose_OverrideTrack_OffsetsItsMaskedChannelsFromRest()
        {
            clipReference = TestBlobFactory.BuildClip(new TestBlobFactory.ClipSpec
            {
                transformTracks = new TestBlobFactory.TransformTrackSpec[]
                {
                    new TestBlobFactory.TransformTrackSpec
                    {
                        targetIndex = 0,
                        channels = AnimatedChannels.Rotation,
                        keys = new TestBlobFactory.TransformKeySpec[]
                        {
                            TestBlobFactory.Key(0f, 99f, 99f, Interpolation.Linear, 1.5f, 9f, 9f, 99f)
                        }
                    }
                }
            });

            ClipSampler.SamplePose(
                ref clipReference.Value, 0, 0.5f, RestPose(), out TargetPose pose);

            Assert.AreEqual(
                2f,
                pose.rotation.z,
                Tolerance,
                "An Override key is an offset from rest: 0.5 rest + 1.5 key. Reading 1.5 here means " +
                "the key replaced the rest pose instead of offsetting from it (amendment A31).");
            Assert.AreEqual(10f, pose.localPosition.x, Tolerance, "Unmasked position stays at rest.");
            Assert.AreEqual(20f, pose.localPosition.y, Tolerance, "Unmasked position stays at rest.");
            Assert.AreEqual(3f, pose.localPosition.z, Tolerance, "Unmasked layer z stays at rest.");
            Assert.AreEqual(1f, pose.scale.x, Tolerance, "Unmasked scale stays at rest.");
        }

        [Test]
        public void SamplePose_TrackBoundToAnotherTarget_LeavesTheRestPose()
        {
            clipReference = TestBlobFactory.BuildClip(new TestBlobFactory.ClipSpec
            {
                transformTracks = new TestBlobFactory.TransformTrackSpec[]
                {
                    new TestBlobFactory.TransformTrackSpec
                    {
                        targetIndex = 1,
                        keys = new TestBlobFactory.TransformKeySpec[]
                        {
                            TestBlobFactory.Key(0f, 99f, 99f)
                        }
                    }
                }
            });

            ClipSampler.SamplePose(
                ref clipReference.Value, 0, 0.5f, RestPose(), out TargetPose pose);
            Assert.AreEqual(10f, pose.localPosition.x, Tolerance);
            Assert.AreEqual(20f, pose.localPosition.y, Tolerance);
        }

        /// <summary>
        /// A flipbook index changes on its key, not between keys.
        /// </summary>
        /// <remarks>
        /// The rule this pins down used to be a midpoint crossover, which moved every frame change
        /// half a segment away from the key that caused it — on an evenly spaced flipbook that is
        /// the whole animation playing offset from the timeline it was authored on. A frame index
        /// has no meaningful in-between value, so the key that last fired holds until the next one.
        /// </remarks>
        [Test]
        public void SampleSpriteTrack_HoldsTheLastKey_UntilTheNextKeysTime()
        {
            clipReference = TestBlobFactory.BuildClip(new TestBlobFactory.ClipSpec
            {
                spriteTracks = new TestBlobFactory.SpriteTrackSpec[]
                {
                    new TestBlobFactory.SpriteTrackSpec
                    {
                        targetIndex = 0,
                        mode = SpriteFrameMode.Slice,
                        keys = new TestBlobFactory.SpriteKeySpec[]
                        {
                            new TestBlobFactory.SpriteKeySpec { normalizedTime = 0f, sliceIndex = 1 },
                            new TestBlobFactory.SpriteKeySpec { normalizedTime = 0.5f, sliceIndex = 9 }
                        }
                    }
                }
            });

            int sliceIndex = -1;
            float4 atlasRect = ClipSampler.IdentityAtlasRect;

            ClipSampler.SampleSpriteTrack(ref clipReference.Value.spriteTracks[0], 0.4f, ref sliceIndex, ref atlasRect);
            Assert.AreEqual(1, sliceIndex, "Before the second key the first one still holds.");

            // The midpoint of the segment, which the old rule switched on. It is not a key, so
            // nothing changes here.
            ClipSampler.SampleSpriteTrack(ref clipReference.Value.spriteTracks[0], 0.25f, ref sliceIndex, ref atlasRect);
            Assert.AreEqual(1, sliceIndex, "The segment midpoint is not a key and changes nothing.");

            ClipSampler.SampleSpriteTrack(ref clipReference.Value.spriteTracks[0], 0.5f, ref sliceIndex, ref atlasRect);
            Assert.AreEqual(9, sliceIndex, "The change lands exactly on the second key's time.");

            ClipSampler.SampleSpriteTrack(ref clipReference.Value.spriteTracks[0], 1f, ref sliceIndex, ref atlasRect);
            Assert.AreEqual(9, sliceIndex, "The last key holds to the end of the clip.");
        }

        [Test]
        public void SampleSpriteTrack_NegativeSliceKey_KeepsTheCurrentFrame()
        {
            clipReference = TestBlobFactory.BuildClip(new TestBlobFactory.ClipSpec
            {
                spriteTracks = new TestBlobFactory.SpriteTrackSpec[]
                {
                    new TestBlobFactory.SpriteTrackSpec
                    {
                        targetIndex = 0,
                        mode = SpriteFrameMode.Slice,
                        keys = new TestBlobFactory.SpriteKeySpec[]
                        {
                            new TestBlobFactory.SpriteKeySpec { normalizedTime = 0f, sliceIndex = -1 }
                        }
                    }
                }
            });

            int sliceIndex = 5;
            float4 atlasRect = ClipSampler.IdentityAtlasRect;
            ClipSampler.SampleSpriteTrack(ref clipReference.Value.spriteTracks[0], 0.5f, ref sliceIndex, ref atlasRect);
            Assert.AreEqual(5, sliceIndex, "A −1 slice key means no change (keep the current frame).");
        }

        [Test]
        public void SampleSpriteTrack_AtlasMode_WritesTheChosenKeysRect()
        {
            clipReference = TestBlobFactory.BuildClip(new TestBlobFactory.ClipSpec
            {
                spriteTracks = new TestBlobFactory.SpriteTrackSpec[]
                {
                    new TestBlobFactory.SpriteTrackSpec
                    {
                        targetIndex = 0,
                        mode = SpriteFrameMode.AtlasRect,
                        keys = new TestBlobFactory.SpriteKeySpec[]
                        {
                            new TestBlobFactory.SpriteKeySpec
                            {
                                normalizedTime = 0f,
                                sliceIndex = -1,
                                atlasRect = new float4(0.25f, 0.25f, 0.5f, 0.75f)
                            }
                        }
                    }
                }
            });

            int sliceIndex = -1;
            float4 atlasRect = ClipSampler.IdentityAtlasRect;
            ClipSampler.SampleSpriteTrack(ref clipReference.Value.spriteTracks[0], 0.5f, ref sliceIndex, ref atlasRect);
            Assert.AreEqual(0.25f, atlasRect.x, Tolerance);
            Assert.AreEqual(0.25f, atlasRect.y, Tolerance);
            Assert.AreEqual(0.5f, atlasRect.z, Tolerance);
            Assert.AreEqual(0.75f, atlasRect.w, Tolerance);
        }

        [Test]
        public void LerpPose_MidBlend_LerpsTransformAndSnapsSpriteFrames()
        {
            TargetPose fromPose = new TargetPose
            {
                localPosition = new float3(0f, 0f, 0f),
                rotation = new float3(0f, 0f, 0f),
                scale = new float3(1f, 1f, 1f),
                sliceIndex = 2,
                atlasRect = ClipSampler.IdentityAtlasRect
            };
            TargetPose toPose = new TargetPose
            {
                localPosition = new float3(4f, 2f, 1f),
                rotation = new float3(0f, 0f, 1f),
                scale = new float3(3f, 5f, 1f),
                sliceIndex = 8,
                atlasRect = new float4(0.5f, 0.5f, 0f, 0f)
            };

            ClipSampler.LerpPose(in fromPose, in toPose, 0.25f, out TargetPose earlyBlend);
            Assert.AreEqual(1f, earlyBlend.localPosition.x, Tolerance);
            Assert.AreEqual(0.25f, earlyBlend.rotation.z, Tolerance);
            Assert.AreEqual(1.5f, earlyBlend.scale.x, Tolerance);
            Assert.AreEqual(2, earlyBlend.sliceIndex, "Below the midpoint the from-pose frame wins (snap).");

            ClipSampler.LerpPose(in fromPose, in toPose, 0.75f, out TargetPose lateBlend);
            Assert.AreEqual(3f, lateBlend.localPosition.x, Tolerance);
            Assert.AreEqual(8, lateBlend.sliceIndex, "At or above the midpoint the to-pose frame wins (snap).");
            Assert.AreEqual(0.5f, lateBlend.atlasRect.x, Tolerance);
        }
    }
}
