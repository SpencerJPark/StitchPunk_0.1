// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using NUnit.Framework;
using DotsAnimationToolkit.Authoring;
using DotsAnimationToolkit.Editor;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// The Actor Editor's <see cref="ActorPreviewComposer"/> and the runtime's
    /// <c>PlaybackTimeSystem</c>/<c>CommandApplySystem</c> must agree step for step: both call the
    /// same <c>PlaybackTimeMath</c>/<c>PlaybackCommandMath</c> statics, so a preview that finishes a
    /// Once clip a frame early or late is the drift this fixture exists to forbid.
    /// </summary>
    /// <remarks>
    /// Lives here rather than under Tests/PlayMode: the composer is Editor-only, and
    /// PackagingConformanceTests' Conformance_A/B lock the PlayMode test assembly to an unrestricted,
    /// Editor-reference-free asmdef (amendment A25 — restricting it to the Editor platform silently
    /// empties the PlayMode suite instead of narrowing it). The actor entity below is hand-built with
    /// only the components CommandApplySystem/PlaybackTimeSystem read, so no Transforms/Rendering
    /// assembly reference is needed either.
    /// </remarks>
    public sealed class ActorPreviewParityTests
    {
        private const uint LoopingAnimationKey = 11u;
        private const uint OnceAnimationKey = 22u;
        private const ulong LoopingClipId = 100UL;
        private const ulong OnceClipId = 200UL;

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
        private World testWorld;
        private BlobAssetReference<ClipRegistryBlob> registry;
        private BlobAssetReference<ActorProfileBlob> ecsProfileBlob;
        private Entity actor;
        private double elapsedTime;

        private ActorProfileAsset profileAsset;
        private int actionLayerIndex;

        private ActorPreviewComposer composer;
        private FakePosePresenter presenter;

        [SetUp]
        public void SetUp()
        {
            assets = new AuthoringTestAssets();
            testWorld = new World("ActorPreviewParityTests");
            elapsedTime = 0d;

            ClipAsset loopingClipAsset = assets.CreateClip("Looping", LoopingClipId, 2f);
            ClipAsset onceClipAsset = assets.CreateClip("Once", OnceClipId, 1f);

            profileAsset = assets.Create<ActorProfileAsset>("Profile");
            profileAsset.turnDirections = AnimationDirections.Six;
            profileAsset.layers[0].animations.Add(new ActorAnimationDefinition
            {
                animationKey = LoopingAnimationKey,
                hasDirections = false,
                clip = loopingClipAsset,
                loop = LoopMode.Loop,
                speed = 1f
            });

            actionLayerIndex = profileAsset.layers.Count - 1; // insert just before the Override bookend
            profileAsset.layers.Insert(actionLayerIndex, new ActorLayerDefinition { displayName = "Action" });
            profileAsset.layers[actionLayerIndex].animations.Add(new ActorAnimationDefinition
            {
                animationKey = OnceAnimationKey,
                hasDirections = false,
                clip = onceClipAsset,
                loop = LoopMode.Once,
                speed = 1f
            });

            registry = TestBlobFactory.BuildRegistry(
                new[]
                {
                    new TestBlobFactory.ClipSpec { clipId = LoopingClipId, duration = 2f, defaultLoop = LoopMode.Loop },
                    new TestBlobFactory.ClipSpec { clipId = OnceClipId, duration = 1f, defaultLoop = LoopMode.Once }
                },
                Array.Empty<uint>());

            ecsProfileBlob = ActorProfileBuilder.Build(profileAsset, Allocator.Persistent);
            actor = CreateMinimalActor(testWorld, registry, ecsProfileBlob, profileAsset.layers.Count);

            presenter = new FakePosePresenter { RegistryValue = registry };
            composer = new ActorPreviewComposer();
            composer.SetProfile(profileAsset, presenter);
        }

        [TearDown]
        public void TearDown()
        {
            composer.Dispose();

            if (testWorld != null && testWorld.IsCreated)
            {
                testWorld.Dispose();
            }
            testWorld = null;

            if (registry.IsCreated)
            {
                registry.Dispose();
            }
            if (ecsProfileBlob.IsCreated)
            {
                ecsProfileBlob.Dispose();
            }

            assets.DestroyAll();
        }

        [Test]
        public void ComposerAndPlaybackTimeSystem_AgreeOnTimeFlagsClipAndAnimationKey_AfterEachOfNSteps()
        {
            EnqueuePlayAnimation(testWorld, actor, LoopingAnimationKey);
            EnqueuePlayAnimation(testWorld, actor, OnceAnimationKey);
            Assert.IsTrue(composer.PlayAnimation(LoopingAnimationKey));
            Assert.IsTrue(composer.PlayAnimation(OnceAnimationKey));

            const float DeltaTime = 0.3f;
            const int StepCount = 6; // 1.8s total: crosses the Once clip's 1s end and wraps the 2s loop once

            for (int step = 0; step < StepCount; step++)
            {
                AdvanceBoth(DeltaTime, applyEcsCommandsThisStep: step == 0);
                AssertLayerParity(0, "Base/Looping, step " + step);
                AssertLayerParity(actionLayerIndex, "Action/Once, step " + step);
            }
        }

        private void AdvanceBoth(float deltaTime, bool applyEcsCommandsThisStep)
        {
            elapsedTime += deltaTime;
            testWorld.SetTime(new TimeData(elapsedTime, deltaTime));

            if (applyEcsCommandsThisStep)
            {
                SystemHandle commandApplySystem = testWorld.GetOrCreateSystem<CommandApplySystem>();
                commandApplySystem.Update(testWorld.Unmanaged);
            }

            SystemHandle playbackTimeSystem = testWorld.GetOrCreateSystem<PlaybackTimeSystem>();
            playbackTimeSystem.Update(testWorld.Unmanaged);
            testWorld.EntityManager.CompleteAllTrackedJobs();

            composer.Tick(deltaTime, presenter);
        }

        private void AssertLayerParity(int layerIndex, string label)
        {
            PlaybackLayer ecsLayer = testWorld.EntityManager.GetBuffer<PlaybackLayer>(actor)[layerIndex];
            Assert.AreEqual(ecsLayer.time, composer.LayerTime(layerIndex), 1e-5f, label + " - time");
            Assert.AreEqual(ecsLayer.flags, composer.LayerFlags(layerIndex), label + " - flags");
            Assert.AreEqual(ecsLayer.clip, composer.LayerClip(layerIndex), label + " - clip");
            Assert.AreEqual(ecsLayer.animationKey, composer.LayerAnimationKey(layerIndex), label + " - animationKey");
        }

        // -----------------------------------------------------------------------------------
        // Minimal actor: only the components CommandApplySystem/PlaybackTimeSystem read, so this
        // fixture needs no Transforms/Rendering assembly reference.
        // -----------------------------------------------------------------------------------

        private static Entity CreateMinimalActor(
            World world,
            BlobAssetReference<ClipRegistryBlob> clipRegistry,
            BlobAssetReference<ActorProfileBlob> profileBlob,
            int layerCount)
        {
            EntityManager entityManager = world.EntityManager;
            Entity actorEntity = entityManager.CreateEntity();

            entityManager.AddComponentData(actorEntity, new ClipRegistry { Value = clipRegistry });
            entityManager.AddComponentData(actorEntity, new ActorProfile { Value = profileBlob });
            entityManager.AddComponentData(actorEntity, new ActorFacing
            {
                facing = Direction.SouthEast,
                appliedFacing = Direction.SouthEast
            });

            DynamicBuffer<PlaybackLayer> layers = entityManager.AddBuffer<PlaybackLayer>(actorEntity);
            layers.ResizeUninitialized(layerCount);
            for (int layerIndex = 0; layerIndex < layerCount; layerIndex++)
            {
                layers[layerIndex] = new PlaybackLayer
                {
                    clipIndex = -1,
                    previousClipIndex = -1,
                    speed = 1f,
                    previousSpeed = 1f,
                    loop = LoopMode.UseClipDefault,
                    previousLoop = LoopMode.UseClipDefault,
                    flags = PlaybackFlags.None
                };
            }

            entityManager.AddBuffer<AnimationCommand>(actorEntity);
            entityManager.AddComponent<AnimationCommandPending>(actorEntity);
            entityManager.SetComponentEnabled<AnimationCommandPending>(actorEntity, false);

            entityManager.AddBuffer<AnimEventOutput>(actorEntity);
            entityManager.AddComponent<AnimEventsPending>(actorEntity);
            entityManager.SetComponentEnabled<AnimEventsPending>(actorEntity, false);

            entityManager.AddComponent<BoundsDirty>(actorEntity);
            entityManager.SetComponentEnabled<BoundsDirty>(actorEntity, false);

            return actorEntity;
        }

        private static void EnqueuePlayAnimation(World world, Entity actorEntity, uint animationKey)
        {
            EntityManager entityManager = world.EntityManager;
            DynamicBuffer<AnimationCommand> commands = entityManager.GetBuffer<AnimationCommand>(actorEntity);
            commands.Add(new AnimationCommand
            {
                kind = CommandKind.PlayAnimation,
                layerIndex = 0,
                clip = default,
                speed = float.NaN,
                loop = LoopMode.UseClipDefault,
                blendDuration = float.NaN,
                time = 0f,
                animationKey = animationKey
            });
            entityManager.SetComponentEnabled<AnimationCommandPending>(actorEntity, true);
        }
    }
}
