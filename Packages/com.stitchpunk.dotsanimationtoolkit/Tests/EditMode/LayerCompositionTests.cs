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

        private static PlaybackLayer BlendingLayerFromPrevious(
            int currentClipIndex,
            int previousClipIndex,
            float previousTime,
            LoopMode previousLoop,
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
                previousTime = previousTime,
                previousLoop = previousLoop,
                blendElapsed = blendElapsed,
                blendDuration = blendDuration,
                flags = PlaybackFlags.Active | PlaybackFlags.Blending
            };
        }

        private TargetPose Composite(PlaybackLayer[] layerSource)
        {
            return Composite(layerSource, RestPose());
        }

        private TargetPose Composite(PlaybackLayer[] layerSource, TargetRestPose restPose)
        {
            return Composite(layerSource, restPose, snapBlendWeights: false);
        }

        private TargetPose Composite(
            PlaybackLayer[] layerSource, TargetRestPose restPose, bool snapBlendWeights)
        {
            NativeArray<PlaybackLayer> layers = new NativeArray<PlaybackLayer>(layerSource, Allocator.Temp);
            try
            {
                ClipSampler.CompositeLayers(
                    ref registryReference.Value, in layers, 0, restPose, snapBlendWeights, out TargetPose pose);
                return pose;
            }
            finally
            {
                layers.Dispose();
            }
        }

        /// <summary>
        /// <strong>Layer mixing, the fade-in (amendment A32).</strong> An upper layer blending in
        /// with an empty previous slot must lerp <em>from the pose the layers below composited</em>,
        /// not from the rest pose and not from nothing. Catches: treating an empty previous slot as
        /// "no blend" and snapping the upper layer straight to full strength — which is what makes
        /// an attack layer pop in over a walk instead of easing in.
        /// </summary>
        /// <remarks>
        /// Walk (Override, key x = 1) sits underneath. Slide (Override, key x = 4) fades in at
        /// weight 0.5, so the answer is halfway between the two: 1 → 4 gives 2.5. Reading 4 means
        /// the blend was ignored; reading 0 or 2 means it lerped from rest rather than from Walk.
        /// </remarks>
        [Test]
        public void CompositeLayers_UpperLayerFadingInWithNoPreviousClip_LerpsFromTheLayersBelow()
        {
            TargetPose pose = Composite(new PlaybackLayer[]
            {
                ActiveLayer(WalkClipIndex),
                BlendingLayer(SlideClipIndex, -1, 0.5f, 1f)
            });

            Assert.AreEqual(
                2.5f,
                pose.localPosition.x,
                Tolerance,
                "A layer fading in must mix with the layers below it, not replace them outright.");
        }

        /// <summary>
        /// The same fade at weight 0: the upper layer must contribute nothing yet, leaving the pose
        /// exactly as the layers below composited it. Catches an off-by-one in the blend weight
        /// that would make a fade-in start already partly applied.
        /// </summary>
        [Test]
        public void CompositeLayers_UpperLayerAtTheStartOfItsFadeIn_LeavesTheLayersBelowUntouched()
        {
            TargetPose pose = Composite(new PlaybackLayer[]
            {
                ActiveLayer(WalkClipIndex),
                BlendingLayer(SlideClipIndex, -1, 0f, 1f)
            });

            Assert.AreEqual(1f, pose.localPosition.x, Tolerance, "Weight 0 means the walk is untouched.");
        }

        /// <summary>
        /// A rest pose that is offset, rotated and non-uniformly scaled — the fixture every test in
        /// this class should have used from the start.
        /// </summary>
        /// <remarks>
        /// <see cref="RestPose"/> is the identity, and on the identity <c>rest + key</c> and
        /// <c>key</c> are the same number. Every assertion in this file written against it is
        /// therefore blind to amendment A31's distinction, which is exactly how the sampler and the
        /// spec disagreed for three gates without a single test noticing.
        /// </remarks>
        private static TargetRestPose OffsetRestPose()
        {
            return new TargetRestPose
            {
                localPosition = new float3(10f, 20f, 5f),
                rotationZ = 0.25f,
                scale = new float2(3f, 4f),
                restSliceIndex = 0
            };
        }

        /// <summary>
        /// <strong>Amendment A31, the multi-layer half.</strong> Catches: anchoring an Override
        /// track to the incoming composited pose instead of to the rest pose. Both ops treat the key
        /// as a delta; what separates them is the frame they add it to. Walk (Override, key 1,2)
        /// sits under Slide (Override, key 4,0), so the upper layer must land on rest + its own key
        /// — not on the lower layer's result plus its key, and not on its key alone.
        /// </summary>
        /// <remarks>
        /// The y assertion is the sharp one: Slide keys y = 0, so anchoring to rest gives 20 while
        /// anchoring to the composited pose below would give 22 — the two answers differ by exactly
        /// the contribution the Override is supposed to discard.
        /// </remarks>
        [Test]
        public void CompositeLayers_UpperOverride_AnchorsToRestNotToTheLayerBelow()
        {
            TargetPose pose = Composite(
                new PlaybackLayer[] { ActiveLayer(WalkClipIndex), ActiveLayer(SlideClipIndex) },
                OffsetRestPose());

            Assert.AreEqual(
                10f + 4f,
                pose.localPosition.x,
                Tolerance,
                "An Override key offsets from rest (10 + 4), never from the composited layer below.");
            Assert.AreEqual(
                20f,
                pose.localPosition.y,
                Tolerance,
                "Slide keys y = 0, so the result is rest y. Reading 22 means the Override anchored " +
                "to Walk's composited y instead of discarding it.");
        }

        /// <summary>
        /// <strong>Amendment A31, the Additive half.</strong> Catches: giving Additive the rest
        /// anchor too, which would collapse the two ops into one. Bob adds onto Walk's composited
        /// result, so the y is rest + walk + bob, not rest + bob.
        /// </summary>
        [Test]
        public void CompositeLayers_AdditiveUpperLayer_StillAnchorsToTheCompositedResult()
        {
            TargetPose pose = Composite(
                new PlaybackLayer[] { ActiveLayer(WalkClipIndex), ActiveLayer(BobClipIndex) },
                OffsetRestPose());

            Assert.AreEqual(
                20f + 2f + 0.5f,
                pose.localPosition.y,
                Tolerance,
                "Additive adds onto the composited pose (rest 20 + walk 2 + bob 0.5), not onto rest.");
        }

        /// <summary>
        /// Catches: making Override scale replace the rest scale rather than multiply it. Scale
        /// composes multiplicatively — its identity is 1, so a curve authored flat at 1 must leave a
        /// part's authored rest proportions alone rather than resetting them to 1.
        /// </summary>
        [Test]
        public void CompositeLayers_OverrideScale_MultipliesTheRestScale()
        {
            TargetPose pose = Composite(
                new PlaybackLayer[] { ActiveLayer(ScaleBaseClipIndex) },
                OffsetRestPose());

            TargetPose identityRestPose = Composite(new PlaybackLayer[] { ActiveLayer(ScaleBaseClipIndex) });
            Assert.AreEqual(
                3f * identityRestPose.scale.x,
                pose.scale.x,
                Tolerance,
                "Override scale multiplies the rest scale (3 x the value it produces from unit rest).");
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

        /// <summary>
        /// LOD 2's snapped crossfade (architecture section 5.10, amendment A34). Catches: ignoring
        /// the snap flag, which leaves level 2 costing exactly what level 1 does in the sampler;
        /// and snapping the wrong way, which would show the outgoing clip past the halfway point.
        /// A quarter of the way through, the snapped result must be the *outgoing* pose; three
        /// quarters through, the incoming one.
        /// </summary>
        [Test]
        public void CompositeLayers_WithSnappedWeights_HardCutsInsteadOfLerping()
        {
            TargetPose earlyPose = Composite(
                new PlaybackLayer[] { BlendingLayer(SlideClipIndex, WalkClipIndex, 0.25f, 1f) },
                RestPose(),
                snapBlendWeights: true);
            Assert.AreEqual(
                1f, earlyPose.localPosition.x, Tolerance,
                "A quarter through, the snap shows walk alone — the unsnapped lerp would be 1.75.");

            TargetPose latePose = Composite(
                new PlaybackLayer[] { BlendingLayer(SlideClipIndex, WalkClipIndex, 0.75f, 1f) },
                RestPose(),
                snapBlendWeights: true);
            Assert.AreEqual(
                4f, latePose.localPosition.x, Tolerance,
                "Three quarters through, the snap shows slide alone — the unsnapped lerp would be 3.25.");
        }

        /// <summary>
        /// The other half of §5.10's level-2 contract: the snap is a rendering decision only.
        /// Catches: implementing it by writing <c>blendElapsed</c> or <c>blendDuration</c>, which
        /// would make the blend genuinely finish early — so an actor that changed LOD mid-blend
        /// could never rejoin the true weight, the exact property §11.2 tests.
        /// </summary>
        [Test]
        public void SnappingAWeight_LeavesTheUnsnappedResultAvailable()
        {
            PlaybackLayer[] midBlend = new PlaybackLayer[]
            {
                BlendingLayer(SlideClipIndex, WalkClipIndex, 0.5f, 1f)
            };

            TargetPose snapped = Composite(midBlend, RestPose(), snapBlendWeights: true);
            TargetPose unsnapped = Composite(midBlend, RestPose(), snapBlendWeights: false);

            Assert.AreEqual(4f, snapped.localPosition.x, Tolerance);
            Assert.AreEqual(
                2.5f, unsnapped.localPosition.x, Tolerance,
                "The same layer state must still lerp when sampled without the snap.");
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
        public void CompositeLayers_OutgoingClip_KeepsTheLoopModeItWasPlayingUnder()
        {
            // The ramp clip runs x from 0 to 2 across a one-second duration and its authored
            // default is PingPong. Here it is the *outgoing* clip of a crossfade, parked past its
            // end at t = 1.5, with the blend weight at 0 so the composite is the outgoing pose
            // alone. Having been played under an explicit Once override, it must hold at the end.
            TargetPose heldPose = Composite(new PlaybackLayer[]
            {
                BlendingLayerFromPrevious(WalkClipIndex, RampClipIndex, 1.5f, LoopMode.Once, 0f, 1f)
            });
            Assert.AreEqual(2f, heldPose.localPosition.x, Tolerance,
                "An outgoing clip played Once must clamp to its end while it fades. Resolving " +
                "against the clip's PingPong default instead would reflect it to x = 1 — a pop in " +
                "the very transition the crossfade exists to smooth.");

            // Same clip, same time, but no override recorded: the clip default applies and the
            // PingPong reflection is then the correct result.
            TargetPose reflectedPose = Composite(new PlaybackLayer[]
            {
                BlendingLayerFromPrevious(
                    WalkClipIndex, RampClipIndex, 1.5f, LoopMode.UseClipDefault, 0f, 1f)
            });
            Assert.AreEqual(1f, reflectedPose.localPosition.x, Tolerance,
                "UseClipDefault must still fall back to the outgoing clip's own default.");
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
