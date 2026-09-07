// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using NUnit.Framework;
using Unity.Entities;
using UnityEngine;

namespace DotsAnimationToolkit.Tests.PlayMode
{
    /// <summary>Covers <c>ActorRagdollTriggerSystem</c> — the at-play and at-event ragdoll trigger paths.</summary>
    public sealed class ActorRagdollTriggerTests
    {
        private const ulong ClipStableId = 300;
        private const uint AnimationKey = 1;

        private World testWorld;

        private readonly List<BlobAssetReference<ClipRegistryBlob>> registries =
            new List<BlobAssetReference<ClipRegistryBlob>>();
        private readonly List<BlobAssetReference<ActorProfileBlob>> profileBlobs =
            new List<BlobAssetReference<ActorProfileBlob>>();
        private readonly List<Object> createdAssets = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("ActorRagdollTriggerTests");
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
                Object.DestroyImmediate(createdAssets[assetIndex]);
            }
            createdAssets.Clear();
        }

        [Test]
        public void AnAtPlayStartTrigger_EnablesRagdollActor_AndClearsTheRequest()
        {
            Entity actorEntity = CreateActorWithRagdollEntry(RagdollTrigger.Start, ragdollAtEventKey: 0u);
            AddDisabledRagdollActor(actorEntity);

            PlaybackTestActor.EnqueueCommand(testWorld, actorEntity, PlaybackTestActor.PlayAnimationCommand(AnimationKey));
            RunCommandApply();
            RunTrigger();

            Assert.IsTrue(testWorld.EntityManager.IsComponentEnabled<RagdollActor>(actorEntity));
            Assert.IsFalse(testWorld.EntityManager.IsComponentEnabled<ActorRagdollRequest>(actorEntity));
        }

        [Test]
        public void AnAtEventStartTrigger_EnablesRagdollActor_OnlyForItsOwnEventKey()
        {
            const uint TriggerEventKey = 42;
            Entity actorEntity = CreateActorWithRagdollEntry(RagdollTrigger.Start, ragdollAtEventKey: TriggerEventKey);
            AddDisabledRagdollActor(actorEntity);

            SeedPendingEvent(actorEntity, eventKey: 7u, animationKey: AnimationKey);
            RunTrigger();
            Assert.IsFalse(
                testWorld.EntityManager.IsComponentEnabled<RagdollActor>(actorEntity),
                "A different event key must not fire this entry's trigger.");

            SeedPendingEvent(actorEntity, eventKey: TriggerEventKey, animationKey: AnimationKey);
            RunTrigger();

            Assert.IsTrue(testWorld.EntityManager.IsComponentEnabled<RagdollActor>(actorEntity));
        }

        [Test]
        public void AStopTrigger_DisablesAnEnabledRagdollActor()
        {
            Entity actorEntity = CreateActorWithRagdollEntry(RagdollTrigger.Stop, ragdollAtEventKey: 0u);
            AddDisabledRagdollActor(actorEntity);
            testWorld.EntityManager.SetComponentEnabled<RagdollActor>(actorEntity, true);

            testWorld.EntityManager.SetComponentData(actorEntity, new ActorRagdollRequest { trigger = RagdollTrigger.Stop });
            testWorld.EntityManager.SetComponentEnabled<ActorRagdollRequest>(actorEntity, true);
            RunTrigger();

            Assert.IsFalse(testWorld.EntityManager.IsComponentEnabled<RagdollActor>(actorEntity));
        }

        [Test]
        public void AnActorWithoutRagdollActor_RunsBothPathsWithNoException()
        {
            Entity actorEntity = CreateActorWithRagdollEntry(RagdollTrigger.Start, ragdollAtEventKey: 5u);

            testWorld.EntityManager.SetComponentData(actorEntity, new ActorRagdollRequest { trigger = RagdollTrigger.Start });
            testWorld.EntityManager.SetComponentEnabled<ActorRagdollRequest>(actorEntity, true);
            SeedPendingEvent(actorEntity, eventKey: 5u, animationKey: AnimationKey);

            Assert.DoesNotThrow(() => RunTrigger());
        }

        // -------------------------------------------------------------------------------------
        // Fixture helpers
        // -------------------------------------------------------------------------------------

        private Entity CreateActorWithRagdollEntry(RagdollTrigger ragdollTrigger, uint ragdollAtEventKey)
        {
            BlobAssetReference<ClipRegistryBlob> registry = PlaybackTestActor.BuildRegistry(new[]
            {
                new PlaybackTestActor.ClipSpec { clipId = ClipStableId, duration = 1f, defaultLoop = LoopMode.Loop }
            });
            registries.Add(registry);

            ClipAsset clipAsset = PlaybackTestActor.CreateClipAsset("Clip", ClipStableId);
            createdAssets.Add(clipAsset);

            ActorProfileAsset profileAsset = ScriptableObject.CreateInstance<ActorProfileAsset>();
            createdAssets.Add(profileAsset);
            profileAsset.layers[1].animations.Add(new ActorAnimationDefinition
            {
                animationKey = AnimationKey,
                hasDirections = false,
                clip = clipAsset,
                ragdollTrigger = ragdollTrigger,
                ragdollAtEventKey = ragdollAtEventKey
            });

            Entity actorEntity = PlaybackTestActor.CreateActorWithProfile(
                testWorld, registry, profileAsset, out BlobAssetReference<ActorProfileBlob> profileBlob);
            profileBlobs.Add(profileBlob);

            return actorEntity;
        }

        private void AddDisabledRagdollActor(Entity actorEntity)
        {
            testWorld.EntityManager.AddComponent<RagdollActor>(actorEntity);
            testWorld.EntityManager.SetComponentEnabled<RagdollActor>(actorEntity, false);
        }

        private void SeedPendingEvent(Entity actorEntity, uint eventKey, uint animationKey)
        {
            DynamicBuffer<AnimEventOutput> events = testWorld.EntityManager.GetBuffer<AnimEventOutput>(actorEntity);
            events.Add(new AnimEventOutput { eventKey = eventKey, animationKey = animationKey });
            testWorld.EntityManager.SetComponentEnabled<AnimEventsPending>(actorEntity, true);
        }

        /// <summary>Runs the system once and completes its jobs, so assertions read settled data.</summary>
        private void RunCommandApply()
        {
            SystemHandle commandApplySystem = testWorld.GetOrCreateSystem<CommandApplySystem>();
            commandApplySystem.Update(testWorld.Unmanaged);
            testWorld.EntityManager.CompleteAllTrackedJobs();
        }

        private void RunTrigger()
        {
            SystemHandle triggerSystem = testWorld.GetOrCreateSystem<ActorRagdollTriggerSystem>();
            triggerSystem.Update(testWorld.Unmanaged);
            testWorld.EntityManager.CompleteAllTrackedJobs();
        }
    }
}
