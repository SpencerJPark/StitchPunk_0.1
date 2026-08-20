// Copyright (c) 2026 Stitch Punk. All rights reserved.

using NUnit.Framework;
using DotsAnimationToolkit;
using Unity.Collections;
using Unity.Entities;

namespace DotsAnimationToolkit.Tests.PlayMode
{
    /// <summary>
    /// Covers <c>RigBindingSystem</c> — the spawn-time re-bind of architecture section 5.3
    /// (build step C4.2).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The defect this system exists to prevent is invisible on a baked actor and total on an
    /// instantiated one: instantiate remaps entity references held in components but not those held
    /// inside dynamic buffers, so a copied actor's <see cref="RigPartRef"/> buffer still points at the
    /// <em>prefab's</em> parts. Every fixture below therefore instantiates rather than testing the
    /// source entity, because testing the source would pass no matter what this system did.
    /// </para>
    /// <para>
    /// Entities are hand-built rather than baked. A bake would exercise C3's bakers as well and make a
    /// failure ambiguous; these fixtures are about one system's behaviour given a well-formed actor.
    /// </para>
    /// </remarks>
    public sealed class RigBindingSystemTests
    {
        private World testWorld;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("RigBindingSystemTests");
        }

        [TearDown]
        public void TearDown()
        {
            if (testWorld != null && testWorld.IsCreated)
            {
                testWorld.Dispose();
            }
            testWorld = null;
        }

        /// <summary>
        /// Catches: deleting the <c>RigPartRef</c> rebuild entirely. Without it an instantiated actor
        /// drives the prefab's parts — the whole crowd animates one invisible object.
        /// </summary>
        [Test]
        public void AnInstantiatedActor_BindsToItsOwnParts_NotThePrefabs()
        {
            Entity prefabActor = CreateActorWithParts(2, out Entity firstPrefabPart, out Entity secondPrefabPart);

            Entity instancedActor = testWorld.EntityManager.Instantiate(prefabActor);
            RunBindingSystem();

            DynamicBuffer<RigPartRef> instancedRefs =
                testWorld.EntityManager.GetBuffer<RigPartRef>(instancedActor);

            Assert.AreEqual(2, instancedRefs.Length, "The instance should list exactly its own two parts.");
            for (int refIndex = 0; refIndex < instancedRefs.Length; refIndex++)
            {
                Entity boundPart = instancedRefs[refIndex].part;
                Assert.AreNotEqual(firstPrefabPart, boundPart, "The instance is still bound to a prefab part.");
                Assert.AreNotEqual(secondPrefabPart, boundPart, "The instance is still bound to a prefab part.");
                Assert.AreEqual(
                    instancedActor,
                    testWorld.EntityManager.GetComponentData<RigPartBinding>(boundPart).actorRoot,
                    "A part in the instance's list does not belong to the instance.");
            }
        }

        /// <summary>
        /// Catches: deleting the <c>actorRoot</c> write. The part-to-actor direction is what every
        /// per-part system uses to find its owner; left unwritten it points at the prefab forever.
        /// </summary>
        [Test]
        public void AnInstantiatedActor_RewritesItsPartsBackReference()
        {
            Entity prefabActor = CreateActorWithParts(2, out Entity firstPrefabPart, out _);

            Entity instancedActor = testWorld.EntityManager.Instantiate(prefabActor);
            RunBindingSystem();

            DynamicBuffer<RigPartRef> instancedRefs =
                testWorld.EntityManager.GetBuffer<RigPartRef>(instancedActor);
            RigPartBinding boundPartBinding =
                testWorld.EntityManager.GetComponentData<RigPartBinding>(instancedRefs[0].part);

            Assert.AreEqual(instancedActor, boundPartBinding.actorRoot);
            Assert.AreEqual(
                prefabActor,
                testWorld.EntityManager.GetComponentData<RigPartBinding>(firstPrefabPart).actorRoot,
                "Re-binding an instance must not disturb the prefab it was copied from.");
        }

        /// <summary>
        /// Catches: deleting the tag disable. The system would then re-bind every actor every frame
        /// forever — correct output, silently quadratic cost, and no test would notice without this.
        /// </summary>
        [Test]
        public void AfterBinding_TheUninitializedTagIsDisabled()
        {
            Entity prefabActor = CreateActorWithParts(1, out _, out _);
            Entity instancedActor = testWorld.EntityManager.Instantiate(prefabActor);

            Assert.IsTrue(
                testWorld.EntityManager.IsComponentEnabled<RigBindingUninitialized>(instancedActor),
                "Guard: an instantiated copy must start enabled, or this test proves nothing.");

            RunBindingSystem();

            Assert.IsFalse(
                testWorld.EntityManager.IsComponentEnabled<RigBindingUninitialized>(instancedActor),
                "RigBindingSystem must disable the tag so it never re-binds this actor again.");
        }

        /// <summary>
        /// Catches: starting the <c>LinkedEntityGroup</c> walk at index 0 instead of 1. Element 0 is
        /// the root, so a rig whose root is itself a rig target would bind the actor to itself and
        /// carry a bogus extra entry in every part list.
        /// </summary>
        [Test]
        public void TheActorRoot_IsNotBoundAsOneOfItsOwnParts()
        {
            Entity prefabActor = CreateActorWithParts(2, out _, out _);

            // A root that is also a target is legal — the walk must skip element 0 by position, not
            // by hoping roots never carry a binding.
            testWorld.EntityManager.AddComponentData(
                prefabActor,
                new RigPartBinding { actorRoot = Entity.Null, targetIndex = 99 });

            Entity instancedActor = testWorld.EntityManager.Instantiate(prefabActor);
            RunBindingSystem();

            DynamicBuffer<RigPartRef> instancedRefs =
                testWorld.EntityManager.GetBuffer<RigPartRef>(instancedActor);

            Assert.AreEqual(2, instancedRefs.Length, "Only the two parts belong in the list, not the root.");
            for (int refIndex = 0; refIndex < instancedRefs.Length; refIndex++)
            {
                Assert.AreNotEqual(
                    instancedActor,
                    instancedRefs[refIndex].part,
                    "The actor root bound itself as one of its own parts.");
            }
        }

        /// <summary>
        /// Catches: deleting the <c>partRefs.Clear()</c>. Re-binding an actor whose tag was
        /// re-enabled — which a host respawn or pooling pass does — would append a second copy of
        /// every part and silently double all per-part work.
        /// </summary>
        [Test]
        public void ReBindingAnActorTwice_DoesNotDuplicateItsParts()
        {
            Entity prefabActor = CreateActorWithParts(3, out _, out _);
            Entity instancedActor = testWorld.EntityManager.Instantiate(prefabActor);

            RunBindingSystem();
            Assert.AreEqual(3, testWorld.EntityManager.GetBuffer<RigPartRef>(instancedActor).Length);

            testWorld.EntityManager.SetComponentEnabled<RigBindingUninitialized>(instancedActor, true);
            RunBindingSystem();

            Assert.AreEqual(
                3,
                testWorld.EntityManager.GetBuffer<RigPartRef>(instancedActor).Length,
                "The buffer must be cleared before rebuilding, not appended to.");
        }

        /// <summary>
        /// <strong>The production path.</strong> Catches: deleting the <c>partRefs.Clear()</c> — on
        /// the <em>first</em> bind, which is the only bind a real spawn performs.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Every other fixture in this file starts from an empty <see cref="RigPartRef"/> buffer, a
        /// state production never presents: <c>ActorBaker</c> fills the buffer with the prefab's own
        /// parts, so a freshly spawned actor reaches this system holding a <em>full</em> one. That is
        /// the gap §5.3 recorded as owed after C4.9, and this fixture closes it.
        /// </para>
        /// <para>
        /// <strong>What arrives in that buffer is already correct, per amendment A35.</strong>
        /// <c>Instantiate</c> remaps entity references inside dynamic buffers as well as inside
        /// components whenever the target is a member of the instantiated <c>LinkedEntityGroup</c>,
        /// so the instance's copied refs name the instance's own parts before this system runs. This
        /// fixture therefore does <em>not</em> claim to catch mis-binding — nothing on this path can,
        /// and an earlier fixture that asserted mis-binding as a guard is what produced A35.
        /// </para>
        /// <para>
        /// What it does catch is <strong>duplication on the first bind</strong>: without the clear,
        /// the walk appends a second copy of every part onto the baked ones, so a real spawn starts
        /// life with double-length part list and silently double all per-part work.
        /// <see cref="ReBindingAnActorTwice_DoesNotDuplicateItsParts"/> also fails without the clear,
        /// but only on a second bind that a host has to opt into by re-enabling the tag; this one
        /// fails on the path every spawn takes.
        /// </para>
        /// <para>
        /// The prefab-reference assertions below are kept as a cheap guard rather than as the point:
        /// if a future Entities release stops remapping buffers, they are what says so.
        /// </para>
        /// </remarks>
        [Test]
        public void AnInstantiatedActor_WhoseBakedBufferIsAlreadyPopulated_RebuildsItRatherThanAppending()
        {
            Entity prefabActor = CreateActorWithParts(
                3, out Entity firstPrefabPart, out Entity secondPrefabPart, seedPartRefs: true);

            Assert.AreEqual(
                3,
                testWorld.EntityManager.GetBuffer<RigPartRef>(prefabActor).Length,
                "Guard: the prefab must arrive populated, or this fixture tests the empty-buffer "
                + "path every other fixture already covers.");

            Entity instancedActor = testWorld.EntityManager.Instantiate(prefabActor);
            RunBindingSystem();

            DynamicBuffer<RigPartRef> instancedRefs =
                testWorld.EntityManager.GetBuffer<RigPartRef>(instancedActor);

            Assert.AreEqual(
                3,
                instancedRefs.Length,
                "The copied prefab references must be replaced, not appended to.");

            for (int refIndex = 0; refIndex < instancedRefs.Length; refIndex++)
            {
                Entity boundPart = instancedRefs[refIndex].part;
                Assert.AreNotEqual(
                    firstPrefabPart,
                    boundPart,
                    "Guard (A35): Entities no longer remaps buffer entity references on Instantiate.");
                Assert.AreNotEqual(
                    secondPrefabPart,
                    boundPart,
                    "Guard (A35): Entities no longer remaps buffer entity references on Instantiate.");
                Assert.AreEqual(
                    instancedActor,
                    testWorld.EntityManager.GetComponentData<RigPartBinding>(boundPart).actorRoot,
                    "A part in the instance's list does not belong to the instance.");
            }
        }

        /// <summary>
        /// Catches: deleting the per-instance <c>phase01</c> re-derivation. Every copy of one prefab
        /// carries the same baked phase, so a crowd would sample in lockstep — reintroducing exactly
        /// the same-tick spike the phase exists to spread.
        /// </summary>
        [Test]
        public void TwoInstancesOfOnePrefab_GetDifferentSamplePhases()
        {
            Entity prefabActor = CreateActorWithParts(1, out _, out _);

            Entity firstInstance = testWorld.EntityManager.Instantiate(prefabActor);
            Entity secondInstance = testWorld.EntityManager.Instantiate(prefabActor);
            RunBindingSystem();

            float firstPhase = testWorld.EntityManager.GetComponentData<SampleSettings>(firstInstance).phase01;
            float secondPhase = testWorld.EntityManager.GetComponentData<SampleSettings>(secondInstance).phase01;

            Assert.AreNotEqual(
                firstPhase,
                secondPhase,
                "Both instances kept the prefab's baked phase, so a crowd would sample in lockstep.");
        }

        /// <summary>
        /// Catches: a derivation that can leave [0, 1). Section 5.6 requires the phase to be a
        /// fraction of the sample interval; a value outside the range skews or freezes quantization.
        /// </summary>
        [Test]
        public void EveryDerivedSamplePhase_StaysInTheUnitInterval()
        {
            Entity prefabActor = CreateActorWithParts(1, out _, out _);
            NativeArray<Entity> instances = new NativeArray<Entity>(64, Allocator.Temp);
            for (int instanceIndex = 0; instanceIndex < instances.Length; instanceIndex++)
            {
                instances[instanceIndex] = testWorld.EntityManager.Instantiate(prefabActor);
            }

            RunBindingSystem();

            for (int instanceIndex = 0; instanceIndex < instances.Length; instanceIndex++)
            {
                float phase = testWorld.EntityManager
                    .GetComponentData<SampleSettings>(instances[instanceIndex]).phase01;
                Assert.GreaterOrEqual(phase, 0f, "Phase must be >= 0.");
                Assert.Less(phase, 1f, "Phase must be < 1.");
            }
            instances.Dispose();
        }

        /// <summary>
        /// Runs the system once and completes its jobs, so assertions read settled data.
        /// </summary>
        private void RunBindingSystem()
        {
            SystemHandle bindingSystem = testWorld.GetOrCreateSystem<RigBindingSystem>();
            bindingSystem.Update(testWorld.Unmanaged);
            testWorld.EntityManager.CompleteAllTrackedJobs();
        }

        /// <summary>
        /// Builds a prefab-shaped actor: a root carrying the buffers and settings the binding system
        /// touches, plus <paramref name="partCount"/> parts, all listed in a
        /// <c>LinkedEntityGroup</c> with the root at element 0 (Entities' contract).
        /// </summary>
        /// <param name="partCount">How many parts to create and link.</param>
        /// <param name="firstPart">The part at target index 0, or <c>Entity.Null</c>.</param>
        /// <param name="secondPart">The part at target index 1, or <c>Entity.Null</c>.</param>
        /// <param name="seedPartRefs">
        /// When true the root's <see cref="RigPartRef"/> buffer is filled with its own parts, the
        /// way <c>ActorBaker</c> leaves a baked prefab. Defaults to false only because the fixtures
        /// written before the production path was covered rely on the empty shape; new fixtures
        /// should prefer true.
        /// </param>
        private Entity CreateActorWithParts(
            int partCount, out Entity firstPart, out Entity secondPart, bool seedPartRefs = false)
        {
            EntityManager entityManager = testWorld.EntityManager;

            Entity actorEntity = entityManager.CreateEntity();
            entityManager.AddBuffer<RigPartRef>(actorEntity);
            entityManager.AddComponentData(actorEntity, new SampleSettings { rateHz = 0f, phase01 = 0.25f });
            entityManager.AddComponent<RigBindingUninitialized>(actorEntity);
            entityManager.SetComponentEnabled<RigBindingUninitialized>(actorEntity, true);

            DynamicBuffer<LinkedEntityGroup> linkedEntities = entityManager.AddBuffer<LinkedEntityGroup>(actorEntity);
            linkedEntities.Add(actorEntity);

            firstPart = Entity.Null;
            secondPart = Entity.Null;
            for (int partIndex = 0; partIndex < partCount; partIndex++)
            {
                Entity partEntity = entityManager.CreateEntity();
                entityManager.AddComponentData(
                    partEntity,
                    new RigPartBinding { actorRoot = actorEntity, targetIndex = partIndex });

                // Re-fetch: AddComponentData above is a structural change and invalidates the buffer.
                entityManager.GetBuffer<LinkedEntityGroup>(actorEntity).Add(partEntity);

                if (partIndex == 0)
                {
                    firstPart = partEntity;
                }
                else if (partIndex == 1)
                {
                    secondPart = partEntity;
                }

                if (seedPartRefs)
                {
                    // Re-fetched per iteration for the same reason the LinkedEntityGroup above is:
                    // creating the next part is a structural change that invalidates this buffer.
                    entityManager.GetBuffer<RigPartRef>(actorEntity).Add(new RigPartRef
                    {
                        part = partEntity,
                        targetIndex = partIndex
                    });
                }
            }

            return actorEntity;
        }
    }
}
