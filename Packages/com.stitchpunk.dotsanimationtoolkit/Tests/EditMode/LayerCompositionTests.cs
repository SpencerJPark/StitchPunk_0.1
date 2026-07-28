// Copyright (c) 2026 Stitch Punk. All rights reserved.

using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace StitchPunk.AnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// EditMode coverage of bottom-up layer composition (architecture sections 5.6, 8 M3):
    /// Override channel masking, Additive-over-composited-lower-layers (the audit question 3
    /// fixture asserting the documented semantics), blend-weight lerping, sprite snap, multi-track
    /// application, and PingPong reflection through layer time mapping.
    /// </summary>
    public sealed class LayerCompositionTests
    {
        private const float Tolerance = 1e-5f;

        private const int WalkClipIndex = 0;
        private const int BobClipIndex = 1;
        private const int SpinClipIndex = 2;
        private const int SlideClipIndex = 3;
        private const int SliceAClipIndex = 4;
        private const int SliceBClipIndex = 5;
        private const int RampClipIndex = 6;
        private const int MultiTrackClipIndex = 7;
        private const int ScaleBaseClipIndex = 8;
        private const int ScaleAddClipIndex = 9;

        private BlobAssetReference<ClipRegistryBlob> registryReference;

        [SetUp]
        public void CreateRegistryFixture()
        {
            TestBlobFactory.ClipSpec[] clipSpecs =
            {
                new TestBlobFactory.ClipSpec
                {
                    clipId = 1, debugName = "Walk",
                    transformTracks = new TestBlobFactory.TransformTrackSpec[]
                    {
                        new TestBlobFactory.TransformTrackSpec
                        {
                            targetIndex = 0,
                            channels = AnimatedChannels.PositionXY,
                            keys = new TestBlobFactory.TransformKeySpec[] { TestBlobFactory.Key(0f, 1f, 2f) }
                        }
                    }
                },
                new TestBlobFactory.ClipSpec
                {
                    clipId = 2, debugName = "Bob",
                    transformTracks = new TestBlobFactory.TransformTrackSpec[]
                    {
                        new TestBlobFactory.TransformTrackSpec
                        {
                            targetIndex = 0,
                            blendOp = TrackBlendOp.Additive,
                            channels = AnimatedChannels.PositionXY,
                            keys = new TestBlobFactory.TransformKeySpec[] { TestBlobFactory.Key(0f, 0f, 0.5f) }
                        }
                    }
                },
                new TestBlobFactory.ClipSpec
                {
                    clipId = 3, debugName = "Spin",
                    transformTracks = new TestBlobFactory.TransformTrackSpec[]
                    {
                        new TestBlobFactory.TransformTrackSpec
                        {
                            targetIndex = 0,
                            channels = AnimatedChannels.RotationZ,
                            keys = new TestBlobFactory.TransformKeySpec[]
                            {
                                TestBlobFactory.Key(0f, 99f, 99f, Interpolation.Linear, 1.5f, 9f, 9f, 99f)
                            }
                        }
                    }
                },
                new TestBlobFactory.ClipSpec
                {
                    clipId = 4, debugName = "Slide",
                    transformTracks = new TestBlobFactory.TransformTrackSpec[]
                    {
                        new TestBlobFactory.TransformTrackSpec
                        {
                            targetIndex = 0,
                            channels = AnimatedChannels.PositionXY,
                            keys = new TestBlobFactory.TransformKeySpec[] { TestBlobFactory.Key(0f, 4f, 0f) }
                        }
                    }
                },
                new TestBlobFactory.ClipSpec
                {
                    clipId = 5, debugName = "SliceA",
                    spriteTracks = new TestBlobFactory.SpriteTrackSpec[]
                    {
                        new TestBlobFactory.SpriteTrackSpec
                        {
                            targetIndex = 0,
                            mode = SpriteFrameMode.Slice,
                            keys = new TestBlobFactory.SpriteKeySpec[]
                            {
                                new TestBlobFactory.SpriteKeySpec { normalizedTime = 0f, sliceIndex = 2 }
                            }
                        }
                    }
                },
                new TestBlobFactory.ClipSpec
                {
                    clipId = 6, debugName = "SliceB",
                    spriteTracks = new TestBlobFactory.SpriteTrackSpec[]
                    {
                        new TestBlobFactory.SpriteTrackSpec
                        {
                            targetIndex = 0,
                            mode = SpriteFrameMode.Slice,
                            keys = new TestBlobFactory.SpriteKeySpec[]
                            {
                                new TestBlobFactory.SpriteKeySpec { normalizedTime = 0f, sliceIndex = 5 }
                            }
                        }
                    }
                },
                new TestBlobFactory.ClipSpec
                {
                    clipId = 7, debugName = "Ramp",
                    defaultLoop = LoopMode.PingPong,
                    transformTracks = new TestBlobFactory.TransformTrackSpec[]
                    {
                        new TestBlobFactory.TransformTrackSpec
                        {
                            targetIndex = 0,
                            channels = AnimatedChannels.PositionXY,
                            keys = new TestBlobFactory.TransformKeySpec[]
                            {
                                TestBlobFactory.Key(0f, 0f, 0f),
                                TestBlobFactory.Key(1f, 2f, 0f)
                            }
                        }
                    }
                },
                new TestBlobFactory.ClipSpec
                {
                    clipId = 8, debugName = "MultiTrack",
                    transformTracks = new TestBlobFactory.TransformTrackSpec[]
                    {
                        new TestBlobFactory.TransformTrackSpec
                        {
                            targetIndex = 0,
                            channels = AnimatedChannels.PositionXY,
                            keys = new TestBlobFactory.TransformKeySpec[] { TestBlobFactory.Key(0f, 1f, 2f) }
                        },
                        new TestBlobFactory.TransformTrackSpec
                        {
                            targetIndex = 0,
                            blendOp = TrackBlendOp.Additive,
                            channels = AnimatedChannels.PositionXY,
                            keys = new TestBlobFactory.TransformKeySpec[] { TestBlobFactory.Key(0f, 0f, 0.5f) }
                        }
                    }
                },
                new TestBlobFactory.ClipSpec
                {
                    clipId = 9, debugName = "ScaleBase",
                    transformTracks = new TestBlobFactory.TransformTrackSpec[]
                    {
                        new TestBlobFactory.TransformTrackSpec
                        {
                            targetIndex = 0,
                            channels = AnimatedChannels.Scale,
                            keys = new TestBlobFactory.TransformKeySpec[]
                            {
                                TestBlobFactory.Key(0f, 0f, 0f, Interpolation.Linear, 0f, 2f, 2f)
                            }
                        }
                    }
                },
                new TestBlobFactory.ClipSpec
                {
                    clipId = 10, debugName = "ScaleAdd",
                    transformTracks = new TestBlobFactory.TransformTrackSpec[]
                    {
                        new TestBlobFactory.TransformTrackSpec
                        {
                            targetIndex = 0,
                            blendOp = TrackBlendOp.Additive,
                            channels = AnimatedChannels.Scale,
                            keys = new TestBlobFactory.TransformKeySpec[]
                            {
                                TestBlobFactory.Key(0f, 0f, 0f, Interpolation.Linear, 0f, 2f, 3f)
                            }
                        }
                    }
                }
            };
            registryReference = TestBlobFactory.BuildRegistry(clipSpecs, new uint[] { 10 });
        }

        [TearDown]
        public void DisposeRegistryFixture()
        {
            if (registryReference.IsCreated)
            {
                registryReference.Dispose();
            }
            registryReference = default;
        }

        private static TargetRestPose RestPose()
        {
            return new TargetRestPose
            {
                localPosition = float3.zero,
                rotationZ = 0f,
                scale = new float2(1f, 1f),
                restSliceIndex = 0
            };
        }

        private static PlaybackLayer ActiveLayer(int clipIndex, float time = 0f, LoopMode loop = LoopMode.UseClipDefault)
        {
            return new PlaybackLayer
            {
                clipIndex = clipIndex,
                time = time,
                speed = 1f,
                loop = loop,
                previousClipIndex = -1,
                flags = PlaybackFlags.Active
            };
        }

        private static PlaybackLayer BlendingLayer(
            int currentClipIndex,
            int previousClipIndex,
            float blendElapsed,
            float blendDuration)
        {
            return new PlaybackLayer
            {
                clipIndex = currentClipIndex,
                time = 0f,
                speed = 1f,
                loop = LoopMode.UseClipDefault,
                previousClipIndex = previousClipIndex,
                previousTime = 0f,
                blendElapsed = blendElapsed,
                blendDuration = blendDuration,
                flags = PlaybackFlags.Active | PlaybackFlags.Blending
            };
        }

        private TargetPose Composite(PlaybackLayer[] layerSource)
        {
            NativeArray<PlaybackLayer> layers = new NativeArray<PlaybackLayer>(layerSource, Allocator.Temp);
            try
            {
                ClipSampler.CompositeLayers(
                    ref registryReference.Value, in layers, 0, RestPose(), out TargetPose pose);
                return pose;
            }
            finally
            {
                layers.Dispose();
            }
        }

        [Test]
        public void CompositeLayers_NoActiveLayers_ReturnsTheRestPose()
        {
            TargetPose pose = Composite(new PlaybackLayer[] { default, default });
            Assert.AreEqual(0f, pose.localPosition.x, Tolerance);
            Assert.AreEqual(0f, pose.rotationZ, Tolerance);
            Assert.AreEqual(1f, pose.scale.x, Tolerance);
            Assert.AreEqual(0, pose.sliceIndex);
        }

        [Test]
        public void CompositeLayers_SingleOverrideLayer_AppliesTheClipPose()
        {
            TargetPose pose = Composite(new PlaybackLayer[] { ActiveLayer(WalkClipIndex) });
            Assert.AreEqual(1f, pose.localPosition.x, Tolerance);
            Assert.AreEqual(2f, pose.localPosition.y, Tolerance);
        }

        [Test]
        public void CompositeLayers_AdditiveUpperLayer_AddsOntoTheCompositedLowerLayersNotOntoRest()
        {
            TargetPose pose = Composite(new PlaybackLayer[]
            {
                ActiveLayer(WalkClipIndex),
                ActiveLayer(BobClipIndex)
            });
            Assert.AreEqual(1f, pose.localPosition.x, Tolerance);
            Assert.AreEqual(2.5f, pose.localPosition.y, Tolerance,
                "The bob layer must add onto the walk result (2 + 0.5), never onto the rest pose (0 + 0.5).");
        }

        [Test]
        public void CompositeLayers_UpperOverride_ReplacesOnlyItsMaskedChannels()
        {
            TargetPose pose = Composite(new PlaybackLayer[]
            {
                ActiveLayer(WalkClipIndex),
                ActiveLayer(SpinClipIndex)
            });
            Assert.AreEqual(1.5f, pose.rotationZ, Tolerance, "The spin layer's masked rotation applies.");
            Assert.AreEqual(1f, pose.localPosition.x, Tolerance, "The walk position survives the rotation-only upper layer.");
            Assert.AreEqual(2f, pose.localPosition.y, Tolerance);
            Assert.AreEqual(1f, pose.scale.x, Tolerance, "Unmasked scale is untouched despite the key's scale values.");
        }

        [Test]
        public void CompositeLayers_UpperOverride_WinsContestedChannelsBottomUp()
        {
            TargetPose pose = Composite(new PlaybackLayer[]
            {
                ActiveLayer(WalkClipIndex),
                ActiveLayer(SlideClipIndex)
            });
            Assert.AreEqual(4f, pose.localPosition.x, Tolerance);
            Assert.AreEqual(0f, pose.localPosition.y, Tolerance);
        }

        [Test]
        public void CompositeLayers_AdditiveScale_MultipliesOntoTheLowerLayers()
        {
            TargetPose pose = Composite(new PlaybackLayer[]
            {
                ActiveLayer(ScaleBaseClipIndex),
                ActiveLayer(ScaleAddClipIndex)
            });
            Assert.AreEqual(4f, pose.scale.x, Tolerance, "2 (override) × 2 (additive) = 4.");
            Assert.AreEqual(6f, pose.scale.y, Tolerance, "2 (override) × 3 (additive) = 6.");
        }

        [Test]
        public void CompositeLayers_MidBlend_LerpsThePreviousAndCurrentSamples()
        {
            TargetPose pose = Composite(new PlaybackLayer[]
            {
                BlendingLayer(SlideClipIndex, WalkClipIndex, 0.5f, 1f)
            });
            Assert.AreEqual(2.5f, pose.localPosition.x, Tolerance, "lerp(walk 1, slide 4, 0.5).");
            Assert.AreEqual(1f, pose.localPosition.y, Tolerance, "lerp(walk 2, slide 0, 0.5).");
        }

        [Test]
        public void CompositeLayers_BlendEndpoints_MatchThePreviousAndCurrentPoses()
        {
            TargetPose startPose = Composite(new PlaybackLayer[]
            {
                BlendingLayer(SlideClipIndex, WalkClipIndex, 0f, 1f)
            });
            Assert.AreEqual(1f, startPose.localPosition.x, Tolerance, "Weight 0: the previous clip alone.");

            TargetPose endPose = Composite(new PlaybackLayer[]
            {
                BlendingLayer(SlideClipIndex, WalkClipIndex, 1f, 1f)
            });
            Assert.AreEqual(4f, endPose.localPosition.x, Tolerance, "Weight 1: the current clip alone.");
        }

        [Test]
        public void CompositeLayers_FadeOutWithoutCurrentClip_LerpsTowardTheIncomingPose()
        {
            TargetPose pose = Composite(new PlaybackLayer[]
            {
                BlendingLayer(-1, WalkClipIndex, 0.25f, 1f)
            });
            Assert.AreEqual(0.75f, pose.localPosition.x, Tolerance, "lerp(walk 1, rest 0, 0.25).");
            Assert.AreEqual(1.5f, pose.localPosition.y, Tolerance, "lerp(walk 2, rest 0, 0.25).");
        }

        [Test]
        public void CompositeLayers_SpriteBlend_SnapsAtTheBlendMidpoint()
        {
            TargetPose earlyPose = Composite(new PlaybackLayer[]
            {
                BlendingLayer(SliceBClipIndex, SliceAClipIndex, 0.4f, 1f)
            });
            Assert.AreEqual(2, earlyPose.sliceIndex, "Below the midpoint the previous clip's frame wins.");

            TargetPose latePose = Composite(new PlaybackLayer[]
            {
                BlendingLayer(SliceBClipIndex, SliceAClipIndex, 0.6f, 1f)
            });
            Assert.AreEqual(5, latePose.sliceIndex, "Above the midpoint the current clip's frame wins.");
        }

        [Test]
        public void CompositeLayers_MultiTrackSameTarget_AppliesAllTracksInCanonicalOrder()
        {
            TargetPose pose = Composite(new PlaybackLayer[] { ActiveLayer(MultiTrackClipIndex) });
            Assert.AreEqual(1f, pose.localPosition.x, Tolerance);
            Assert.AreEqual(2.5f, pose.localPosition.y, Tolerance,
                "Both tracks apply — override (2) then additive (+0.5) — with no first-match break.");
        }

        [Test]
        public void CompositeLayers_PingPongDefaultLoop_ReflectsTheLayerTime()
        {
            TargetPose reflectedPose = Composite(new PlaybackLayer[]
            {
                ActiveLayer(RampClipIndex, 1.5f)
            });
            Assert.AreEqual(1f, reflectedPose.localPosition.x, Tolerance,
                "UseClipDefault resolves to the clip's PingPong default: t = 1.5 reflects to 0.5 → x = 1.");

            TargetPose clampedPose = Composite(new PlaybackLayer[]
            {
                ActiveLayer(RampClipIndex, 1.5f, LoopMode.Once)
            });
            Assert.AreEqual(2f, clampedPose.localPosition.x, Tolerance,
                "An explicit Once request overrides the clip default and clamps t = 1.5 to the end.");
        }

        [Test]
        public void CompositeLayers_InactiveMiddleLayer_IsSkipped()
        {
            TargetPose pose = Composite(new PlaybackLayer[]
            {
                ActiveLayer(WalkClipIndex),
                default,
                ActiveLayer(BobClipIndex)
            });
            Assert.AreEqual(2.5f, pose.localPosition.y, Tolerance,
                "The inactive middle layer contributes nothing; walk + bob still composite.");
        }
    }
}
