// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using NUnit.Framework;
using DotsAnimationToolkit.Authoring;
using DotsAnimationToolkit.Editor;
using Unity.Collections;
using Unity.Entities;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// <see cref="ActorPreviewComposer"/>'s own behaviour: a Once entry deactivates on schedule, a
    /// facing change re-picks a directional entry in place, and an at-event or at-play ragdoll
    /// trigger fires exactly once. A stub <see cref="IActorPosePresenter"/> stands in for the real
    /// preview controller.
    /// </summary>
    public sealed class ActorPreviewComposerTests
    {
        private sealed class FakePosePresenter : IActorPosePresenter
        {
            internal BlobAssetReference<ClipRegistryBlob> RegistryValue;

            public BlobAssetReference<ClipRegistryBlob> Registry
            {
                get { return RegistryValue; }
            }

            public void SetClipSets(IReadOnlyList<ClipSetAsset> clipSets)
            {
            }

            public bool SampleCompositedPose(in NativeArray<PlaybackLayer> layers, bool mirrorX)
            {
                return true;
            }
        }

        private AuthoringTestAssets assets;
        private FakePosePresenter presenter;
        private BlobAssetReference<ClipRegistryBlob> registry;
        private ActorPreviewComposer composer;

        [SetUp]
        public void SetUp()
        {
            assets = new AuthoringTestAssets();
            presenter = new FakePosePresenter();
            composer = new ActorPreviewComposer();
        }

        [TearDown]
        public void TearDown()
        {
            composer.Dispose();
            if (registry.IsCreated)
            {
                registry.Dispose();
            }
            assets.DestroyAll();
        }

        [Test]
        public void OnceEntry_DeactivatesAfterItsDuration_AndIsAnimationPlayingTurnsFalse()
        {
            ClipAsset clip = assets.CreateClip("Once", 0x10UL, 1f);
            ActorProfileAsset profile = assets.CreateProfile(null, null, 3);
            profile.layers[1].animations.Add(new ActorAnimationDefinition
            {
                animationKey = 5u,
                hasDirections = false,
                clip = clip,
                loop = LoopMode.Once,
                speed = 1f
            });

            registry = TestBlobFactory.BuildRegistry(
                new[] { new TestBlobFactory.ClipSpec { clipId = clip.Id.Value, duration = 1f, defaultLoop = LoopMode.Once } },
                Array.Empty<uint>());
            presenter.RegistryValue = registry;

            composer.SetProfile(profile, presenter);
            Assert.IsTrue(composer.PlayAnimation(5u));
            Assert.IsTrue(composer.IsAnimationPlaying(5u));

            composer.Tick(1.1f, presenter);

            Assert.IsFalse(composer.IsAnimationPlaying(5u), "A Once clip past its duration with nothing queued deactivates its layer.");
        }

        [Test]
        public void FacingChange_RepicksAFourCoverageDirectionalEntry_KeepingLayerTime()
        {
            ClipAsset southEastClip = assets.CreateClip("SE", 0x21UL, 2f);
            ClipAsset northEastClip = assets.CreateClip("NE", 0x22UL, 2f);
            ActorProfileAsset profile = assets.CreateProfile(null, null, 3);
            profile.turnDirections = AnimationDirections.Six;
            profile.layers[1].animations.Add(new ActorAnimationDefinition
            {
                animationKey = 9u,
                hasDirections = true,
                directionSlots = new DirectionSlots { southEast = southEastClip, northEast = northEastClip },
                loop = LoopMode.Loop,
                speed = 1f
            });

            registry = TestBlobFactory.BuildRegistry(
                new[]
                {
                    new TestBlobFactory.ClipSpec { clipId = southEastClip.Id.Value, duration = 2f, defaultLoop = LoopMode.Loop },
                    new TestBlobFactory.ClipSpec { clipId = northEastClip.Id.Value, duration = 2f, defaultLoop = LoopMode.Loop }
                },
                Array.Empty<uint>());
            presenter.RegistryValue = registry;

            composer.SetProfile(profile, presenter);
            Assert.IsTrue(composer.PlayAnimation(9u));
            composer.SetLayerTime(1, 0.4f);
            Assert.AreEqual(southEastClip.Id, composer.LayerClip(1));

            composer.Facing = Direction.NorthEast;
            composer.Tick(0f, presenter);

            Assert.AreEqual(
                northEastClip.Id, composer.LayerClip(1),
                "A facing change must re-pick the directional entry's clip in place.");
            Assert.AreEqual(0.4f, composer.LayerTime(1), 1e-5f, "An in-place re-pick must not touch layer time.");
        }

        [Test]
        public void AtEventRagdollTrigger_FiresExactlyOnceOnTheCrossingTick_NotAgainAfterward()
        {
            const uint RagdollEventKey = 20u;
            ClipAsset clip = assets.CreateClip("Death", 0x30UL, 1f);
            ActorProfileAsset profile = assets.CreateProfile(null, null, 2);
            profile.layers[0].animations.Add(new ActorAnimationDefinition
            {
                animationKey = 7u,
                hasDirections = false,
                clip = clip,
                loop = LoopMode.Once,
                speed = 1f,
                ragdollTrigger = RagdollTrigger.Start,
                ragdollAtEventKey = RagdollEventKey
            });

            registry = TestBlobFactory.BuildRegistry(
                new[]
                {
                    new TestBlobFactory.ClipSpec
                    {
                        clipId = clip.Id.Value,
                        duration = 1f,
                        defaultLoop = LoopMode.Once,
                        events = new[]
                        {
                            new TestBlobFactory.EventSpec { normalizedTime = 0.5f, eventKey = RagdollEventKey }
                        }
                    }
                },
                Array.Empty<uint>());
            presenter.RegistryValue = registry;

            int startCount = 0;
            composer.RagdollStartRequested += _ => startCount++;

            composer.SetProfile(profile, presenter);
            composer.PlayAnimation(7u);
            Assert.IsFalse(composer.RagdollOn, "ragdollAtEventKey != 0 must not fire at play.");

            composer.Tick(0.6f, presenter);
            Assert.AreEqual(1, startCount, "The marker at t=0.5 was crossed once by this tick.");
            Assert.IsTrue(composer.RagdollOn);
            Assert.AreEqual(7u, composer.RagdollStartedByKey);

            composer.Tick(0.3f, presenter);
            Assert.AreEqual(1, startCount, "The marker is not crossed again on a tick that stays past it.");
        }

        [Test]
        public void AtPlayRagdollTrigger_FiresImmediatelyOnPlayAnimation()
        {
            ClipAsset clip = assets.CreateClip("Death", 0x40UL, 1f);
            ActorProfileAsset profile = assets.CreateProfile(null, null, 2);
            profile.layers[0].animations.Add(new ActorAnimationDefinition
            {
                animationKey = 3u,
                hasDirections = false,
                clip = clip,
                loop = LoopMode.Once,
                speed = 1f,
                ragdollTrigger = RagdollTrigger.Start
                // ragdollAtEventKey left at 0 - "at play, immediately".
            });

            registry = TestBlobFactory.BuildRegistry(
                new[] { new TestBlobFactory.ClipSpec { clipId = clip.Id.Value, duration = 1f, defaultLoop = LoopMode.Once } },
                Array.Empty<uint>());
            presenter.RegistryValue = registry;

            int startCount = 0;
            composer.RagdollStartRequested += _ => startCount++;

            composer.SetProfile(profile, presenter);
            composer.PlayAnimation(3u);

            Assert.AreEqual(1, startCount);
            Assert.IsTrue(composer.RagdollOn);
            Assert.AreEqual(3u, composer.RagdollStartedByKey);
        }
    }
}
