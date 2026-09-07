// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using NUnit.Framework;
using Unity.Entities;
using UnityEngine;

namespace DotsAnimationToolkit.Tests.PlayMode
{
    /// <summary>Covers <c>ActorFacingRepickSystem</c> — the in-place directional re-pick on a changed <see cref="ActorFacing.facing"/> (A70-T7).</summary>
    public sealed class ActorFacingRepickTests
    {
        private World testWorld;

        private readonly List<BlobAssetReference<ClipRegistryBlob>> registries =
            new List<BlobAssetReference<ClipRegistryBlob>>();
        private readonly List<BlobAssetReference<ActorProfileBlob>> profileBlobs =
            new List<BlobAssetReference<ActorProfileBlob>>();
        private readonly List<Object> createdAssets = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("ActorFacingRepickTests");
        }

        [TearDown]
        public void TearDown()
        {
            if (testWorld != null && testWorld.IsCreated)
            {
                testWorld.Dispose();
            }
            testWorld = null;

            for (int registryIndex = 0; registryIndex < registries.Count; registryIndex++)
            {
                if (registries[registryIndex].IsCreated)
                {
                    registries[registryIndex].Dispose();
                }
            }
            registries.Clear();

            for (int profileIndex = 0; profileIndex < profileBlobs.Count; profileIndex++)
            {
                if (profileBlobs[profileIndex].IsCreated)
                {
                    profileBlobs[profileIndex].Dispose();
                }
            }
            profileBlobs.Clear();

            for (int assetIndex = 0; assetIndex < createdAssets.Count; assetIndex++)
            {
                if (createdAssets[assetIndex] != null)
                {
                    Object.DestroyImmediate(createdAssets[assetIndex]);
                }
            }
            createdAssets.Clear();
        }

        /// <summary>
        /// Catches: not folding the new facing through the entry's own coverage, dropping the clip
        /// swap, or resetting <c>time</c> instead of carrying it through the in-place clip change.
        /// </summary>
        [Test]
        public void FacingChange_OnAFourCoverageEntry_SwapsTheClipAndKeepsTime()
        {
            const ulong SouthEastClipId = 300;
            const ulong NorthEastClipId = 400;
            const uint AnimationKey = 1;

            BlobAssetReference<ClipRegistryBlob> registry = PlaybackTestActor.BuildRegistry(new[]
            {
                new PlaybackTestActor.ClipSpec { clipId = SouthEastClipId, duration = 2f, defaultLoop = LoopMode.Loop },
                new PlaybackTestActor.ClipSpec { clipId = NorthEastClipId, duration = 2f, defaultLoop = LoopMode.Loop }
            });
            registries.Add(registry);

            ClipAsset southEastClip = PlaybackTestActor.CreateClipAsset("SouthEastClip", SouthEastClipId);
            ClipAsset northEastClip = PlaybackTestActor.CreateClipAsset("NorthEastClip", NorthEastClipId);
            createdAssets.Add(southEastClip);
            createdAssets.Add(northEastClip);

            ActorProfileAsset profileAsset = ScriptableObject.CreateInstance<ActorProfileAsset>();
            createdAssets.Add(profileAsset);
            // profileAsset.layers is [Base, Override]; the entry lives on Override (index 1).
            profileAsset.layers[1].animations.Add(new ActorAnimationDefinition
            {
                animationKey = AnimationKey,
                hasDirections = true,
                directionSlots = new DirectionSlots { southEast = southEastClip, northEast = northEastClip }
            });

            Entity actor = PlaybackTestActor.CreateActorWithProfile(
                testWorld, registry, profileAsset, out BlobAssetReference<ActorProfileBlob> profileBlob);
            profileBlobs.Add(profileBlob);

            PlaybackTestActor.EnqueueCommand(
                testWorld, actor, PlaybackTestActor.PlayAnimationCommand(AnimationKey));
            RunCommandApply();

            DynamicBuffer<PlaybackLayer> layers = testWorld.EntityManager.GetBuffer<PlaybackLayer>(actor);
            PlaybackLayer playingLayer = layers[1];
            Assert.AreEqual(SouthEastClipId, playingLayer.clip.Value, "Guard: must start on the southEast slot.");
            playingLayer.time = 1.234f;
            layers[1] = playingLayer;

            testWorld.EntityManager.SetComponentData(actor, new ActorFacing
            {
                facing = Direction.NorthEast,
                appliedFacing = Direction.SouthEast
            });

            RunFacingRepick();

            PlaybackLayer repickedLayer = PlaybackTestActor.GetLayer(testWorld, actor, 1);
            Assert.AreEqual(NorthEastClipId, repickedLayer.clip.Value, "NorthEast must resolve to the northEast slot.");
            Assert.AreEqual(1.234f, repickedLayer.time, "The clip swap must not reset playback time.");

            ActorFacing settledFacing = testWorld.EntityManager.GetComponentData<ActorFacing>(actor);
            Assert.AreEqual(Direction.NorthEast, settledFacing.appliedFacing);
        }

        /// <summary>
        /// Catches: folding a facing change through the actor's own turn granularity instead of the
        /// entry's actual coverage — a Two-coverage entry has only one slot filled, so every facing
        /// must land back on it.
        /// </summary>
        [Test]
        public void FacingChange_OnATwoCoverageEntry_NeverSwaps_ButStillUpdatesAppliedFacing()
        {
            const ulong SouthEastClipId = 300;
            const uint AnimationKey = 1;

            BlobAssetReference<ClipRegistryBlob> registry = PlaybackTestActor.BuildRegistry(new[]
            {
                new PlaybackTestActor.ClipSpec { clipId = SouthEastClipId, duration = 2f, defaultLoop = LoopMode.Loop }
            });
            registries.Add(registry);

            ClipAsset southEastClip = PlaybackTestActor.CreateClipAsset("SouthEastClip", SouthEastClipId);
            createdAssets.Add(southEastClip);

            ActorProfileAsset profileAsset = ScriptableObject.CreateInstance<ActorProfileAsset>();
            createdAssets.Add(profileAsset);
            profileAsset.layers[1].animations.Add(new ActorAnimationDefinition
            {
                animationKey = AnimationKey,
                hasDirections = true,
                directionSlots = new DirectionSlots { southEast = southEastClip }
            });

            Entity actor = PlaybackTestActor.CreateActorWithProfile(
                testWorld, registry, profileAsset, out BlobAssetReference<ActorProfileBlob> profileBlob);
            profileBlobs.Add(profileBlob);

            PlaybackTestActor.EnqueueCommand(
                testWorld, actor, PlaybackTestActor.PlayAnimationCommand(AnimationKey));
            RunCommandApply();

            int clipIndexBeforeRepick = PlaybackTestActor.GetLayer(testWorld, actor, 1).clipIndex;

            testWorld.EntityManager.SetComponentData(actor, new ActorFacing
            {
                facing = Direction.NorthEast,
                appliedFacing = Direction.SouthEast
            });

            RunFacingRepick();

            PlaybackLayer repickedLayer = PlaybackTestActor.GetLayer(testWorld, actor, 1);
            Assert.AreEqual(SouthEastClipId, repickedLayer.clip.Value, "A Two-coverage entry has nowhere else to fold to.");
            Assert.AreEqual(clipIndexBeforeRepick, repickedLayer.clipIndex, "The clip index must not churn when the clip did not change.");

            ActorFacing settledFacing = testWorld.EntityManager.GetComponentData<ActorFacing>(actor);
            Assert.AreEqual(
                Direction.NorthEast,
                settledFacing.appliedFacing,
                "appliedFacing must still settle even when no layer's clip changed.");
        }

        /// <summary>
        /// Catches: re-picking a layer that is playing a raw clip rather than a named entry. A raw
        /// Play's <c>animationKey</c> is 0 and has no profile entry to fold against, so it must be
        /// left untouched regardless of how many other layers on the same actor do have entries.
        /// </summary>
        [Test]
        public void FacingChange_OnARawPlayLayer_NeverSwapsTheClip()
        {
            const ulong RawClipId = 500;
            const ulong SouthEastClipId = 300;
            const ulong NorthEastClipId = 400;
            const uint AnimationKey = 1;

            BlobAssetReference<ClipRegistryBlob> registry = PlaybackTestActor.BuildRegistry(new[]
            {
                new PlaybackTestActor.ClipSpec { clipId = RawClipId, duration = 1f, defaultLoop = LoopMode.Loop },
                new PlaybackTestActor.ClipSpec { clipId = SouthEastClipId, duration = 1f, defaultLoop = LoopMode.Loop },
                new PlaybackTestActor.ClipSpec { clipId = NorthEastClipId, duration = 1f, defaultLoop = LoopMode.Loop }
            });
            registries.Add(registry);

            ClipAsset southEastClip = PlaybackTestActor.CreateClipAsset("SouthEastClip", SouthEastClipId);
            ClipAsset northEastClip = PlaybackTestActor.CreateClipAsset("NorthEastClip", NorthEastClipId);
            createdAssets.Add(southEastClip);
            createdAssets.Add(northEastClip);

            ActorProfileAsset profileAsset = ScriptableObject.CreateInstance<ActorProfileAsset>();
            createdAssets.Add(profileAsset);
            // A directional entry exists on the Override layer, so the loop has something to fold
            // against on layer 1; the raw Play below targets layer 0 (Base) directly.
            profileAsset.layers[1].animations.Add(new ActorAnimationDefinition
            {
                animationKey = AnimationKey,
                hasDirections = true,
                directionSlots = new DirectionSlots { southEast = southEastClip, northEast = northEastClip }
            });

            Entity actor = PlaybackTestActor.CreateActorWithProfile(
                testWorld, registry, profileAsset, out BlobAssetReference<ActorProfileBlob> profileBlob);
            profileBlobs.Add(profileBlob);

            PlaybackTestActor.EnqueueCommand(testWorld, actor, PlaybackTestActor.PlayCommand(0, RawClipId));
            RunCommandApply();

            PlaybackLayer rawLayerBeforeRepick = PlaybackTestActor.GetLayer(testWorld, actor, 0);
            Assert.AreEqual(0u, rawLayerBeforeRepick.animationKey, "Guard: a raw Play must never stamp an animation key.");

            testWorld.EntityManager.SetComponentData(actor, new ActorFacing
            {
                facing = Direction.NorthEast,
                appliedFacing = Direction.SouthEast
            });

            RunFacingRepick();

            PlaybackLayer rawLayerAfterRepick = PlaybackTestActor.GetLayer(testWorld, actor, 0);
            Assert.AreEqual(RawClipId, rawLayerAfterRepick.clip.Value, "A raw-Play layer must never be swapped.");
            Assert.AreEqual(rawLayerBeforeRepick.clipIndex, rawLayerAfterRepick.clipIndex);
        }

        private void RunCommandApply()
        {
            SystemHandle commandApplySystem = testWorld.GetOrCreateSystem<CommandApplySystem>();
            commandApplySystem.Update(testWorld.Unmanaged);
            testWorld.EntityManager.CompleteAllTrackedJobs();
        }

        private void RunFacingRepick()
        {
            SystemHandle facingRepickSystem = testWorld.GetOrCreateSystem<ActorFacingRepickSystem>();
            facingRepickSystem.Update(testWorld.Unmanaged);
            testWorld.EntityManager.CompleteAllTrackedJobs();
        }
    }
}
