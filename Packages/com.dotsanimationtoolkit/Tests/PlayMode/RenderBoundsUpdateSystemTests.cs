// Copyright (c) 2026 Spencer Park. All rights reserved.

using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;

namespace DotsAnimationToolkit.Tests.PlayMode
{
    /// <summary>
    /// Covers <c>RenderBoundsUpdateSystem</c> — the bounds half of architecture section 5.8
    /// (build step C4.7).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two of the three defects this system exists to avoid are invisible in play: recomputing every
    /// frame costs performance with correct output, and never clearing the dirty tag does the same.
    /// Only the third — offset space mistaken for actor space (amendment A13) — shows on screen, and
    /// then only for rigs whose parts sit away from the origin, which is why the fixture's rest
    /// bounds are deliberately off-origin.
    /// </para>
    /// <para>
    /// The clip boxes below are asymmetric and contain the origin, which is what the bake produces:
    /// offsets are deltas from a rest pose, so 0 is always inside. That makes the expected actor box
    /// exactly <c>rest ⊕ offset</c> with no slack to hide an arithmetic slip.
    /// </para>
    /// </remarks>
    public sealed class RenderBoundsUpdateSystemTests
    {
        private const float Tolerance = 1e-4f;

        private const ulong StepClipId = 300;
        private const int StepClipIndex = 0;

        private const ulong LeapClipId = 400;
        private const int LeapClipIndex = 1;

        private const int BodyTargetIndex = 0;

        /// <summary>What <c>PlaybackTestActor</c> gives every actor: centre (2, 3, 0), extents (1, 1.5, 0.5).</summary>
        private static readonly float3 RestCentre = new float3(2f, 3f, 0f);
        private static readonly float3 RestExtents = new float3(1f, 1.5f, 0.5f);

        /// <summary>A modest offset box: x ∈ [−1.5, 2.5], y ∈ [−1.25, 0.75], z ∈ [−0.25, 0.25].</summary>
        private static readonly AABB StepOffsets = new AABB
        {
            Center = new float3(0.5f, -0.25f, 0f),
            Extents = new float3(2f, 1f, 0.25f)
        };

        /// <summary>A far larger box, so a union that dropped it is unmistakable.</summary>
        private static readonly AABB LeapOffsets = new AABB
        {
            Center = float3.zero,
            Extents = new float3(5f, 5f, 1f)
        };

        private World testWorld;
        private BlobAssetReference<ClipRegistryBlob> registry;
        private Entity actor;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("RenderBoundsUpdateSystemTests");
            registry = PlaybackTestActor.BuildRegistry(
                new[]
                {
                    new PlaybackTestActor.ClipSpec
                    {
                        clipId = StepClipId,
                        duration = 1f,
                        defaultLoop = LoopMode.Loop,
                        offsetBounds = StepOffsets
                    },
                    new PlaybackTestActor.ClipSpec
                    {
                        clipId = LeapClipId,
                        duration = 1f,
                        defaultLoop = LoopMode.Loop,
                        offsetBounds = LeapOffsets
                    }
                },
                targetCount: 2);
            actor = PlaybackTestActor.CreateActor(testWorld, registry);
        }

        [TearDown]
        public void TearDown()
        {
            if (testWorld != null && testWorld.IsCreated)
            {
                testWorld.Dispose();
            }
            testWorld = null;

            if (registry.IsCreated)
            {
                registry.Dispose();
            }
        }

        /// <summary>
        /// <strong>The amendment A13 test.</strong> Catches: writing the clip's offset box into
        /// <see cref="RenderBounds"/> directly. Offset boxes are origin-centred by construction, so
        /// the mistake gives a rig whose parts sit at (2, 3) a box that does not contain them — and
        /// the actor pops out of existence the moment the camera frames it from the wrong side.
        /// </summary>
        [Test]
        public void ActorBounds_AreTheRestBoxGrownByTheClipOffsets()
        {
            PlayOnLayer(0, StepClipId, StepClipIndex);

            RunSystem();

            AssertBounds(
                RestCentre + StepOffsets.Center,
                RestExtents + StepOffsets.Extents,
                testWorld.EntityManager.GetComponentData<RenderBounds>(actor).Value);
        }

        /// <summary>
        /// Catches: deleting the tag reset, which leaves every actor permanently dirty. Bounds stay
        /// correct and the union is recomputed for every animated entity every frame forever — a
        /// regression only a profiler would otherwise find, and only after someone went looking.
        /// </summary>
        [Test]
        public void TheDirtyTag_IsClearedByTheWrite()
        {
            PlayOnLayer(0, StepClipId, StepClipIndex);

            RunSystem();

            Assert.IsFalse(
                testWorld.EntityManager.IsComponentEnabled<BoundsDirty>(actor),
                "This system is the sole reset path; nothing else disables the tag.");
        }

        /// <summary>
        /// The other half of the same contract, from the query's side. Catches: gating on a change
        /// filter over <see cref="PlaybackLayer"/> instead of the tag — <c>PlaybackTimeSystem</c>
        /// writes <c>time</c> into that buffer every frame, so the filter degenerates to always-true
        /// and this fixture's untouched sentinel would be overwritten with a real box.
        /// </summary>
        [Test]
        public void AFrameThatOnlyAdvancesTime_LeavesBoundsUntouched()
        {
            PlayOnLayer(0, StepClipId, StepClipIndex);
            EntityManager entityManager = testWorld.EntityManager;
            entityManager.SetComponentEnabled<BoundsDirty>(actor, false);

            AABB sentinelBounds = new AABB
            {
                Center = new float3(-99f, -99f, -99f),
                Extents = new float3(0.125f, 0.125f, 0.125f)
            };
            entityManager.SetComponentData(actor, new RenderBounds { Value = sentinelBounds });

            RunSystem();

            AssertBounds(
                sentinelBounds.Center,
                sentinelBounds.Extents,
                entityManager.GetComponentData<RenderBounds>(actor).Value);
        }

        /// <summary>
        /// Catches: unioning only the incoming clip during a crossfade. Both clips are on screen for
        /// the length of the blend, so a leap blending in from a step must already have the leap's
        /// reach — the alternative is the actor culling mid-blend, at the exact frame it moves
        /// furthest.
        /// </summary>
        [Test]
        public void ABlendingLayer_KeepsBothClipsInTheUnion()
        {
            // The *outgoing* clip is the large one, deliberately. With the leap incoming instead,
            // its box swallows the step's on every axis and the expected union is the same number
            // whether or not the outgoing clip was folded in — a fixture that cannot fail.
            PlaybackLayer layer = BuildActiveLayer(StepClipId, StepClipIndex);
            layer.previousClip = new ClipId(LeapClipId);
            layer.previousClipIndex = LeapClipIndex;
            layer.previousTime = 0.5f;
            layer.previousLoop = LoopMode.Loop;
            layer.blendDuration = 0.4f;
            layer.blendElapsed = 0.1f;
            layer.flags |= PlaybackFlags.Blending;
            SetLayerAndDirty(0, layer);

            RunSystem();

            // The leap box contains the step box on every axis, so the union is rest ⊕ leap.
            AssertBounds(
                RestCentre,
                RestExtents + LeapOffsets.Extents,
                testWorld.EntityManager.GetComponentData<RenderBounds>(actor).Value);
        }

        /// <summary>
        /// Catches: computing the actor's box but never writing it to the parts, which leaves every
        /// child renderer culling against whatever the bake left. Parts take the actor's union
        /// rather than a tightened per-part box — per-part tightening is an explicit non-goal of
        /// §5.8 — and that union has to contain the part where it rests.
        /// </summary>
        [Test]
        public void Parts_ReceiveTheActorBox_AndItContainsThemAtRest()
        {
            float3 partRestPosition = new float3(2.5f, 4f, 0f);
            Entity body = PlaybackTestActor.AddPart(
                testWorld, actor, BodyTargetIndex, restPosition: partRestPosition);
            testWorld.EntityManager.SetComponentData(body, new RenderBounds
            {
                Value = new AABB { Center = new float3(-99f, -99f, -99f), Extents = float3.zero }
            });
            PlayOnLayer(0, StepClipId, StepClipIndex);

            RunSystem();

            AABB partBounds = testWorld.EntityManager.GetComponentData<RenderBounds>(body).Value;
            AssertBounds(RestCentre + StepOffsets.Center, RestExtents + StepOffsets.Extents, partBounds);

            float3 boundsMinimum = partBounds.Center - partBounds.Extents;
            float3 boundsMaximum = partBounds.Center + partBounds.Extents;
            Assert.IsTrue(
                math.all(partRestPosition >= boundsMinimum) && math.all(partRestPosition <= boundsMaximum),
                $"The part rests at {partRestPosition}, outside the box [{boundsMinimum}, {boundsMaximum}] — "
                + "which is what mistaking offset space for actor space looks like.");
        }

        /// <summary>
        /// Catches: unioning every layer's clip regardless of state. A stopped layer keeps its last
        /// clip index, so folding it in would leave an idle actor permanently carrying the reach of
        /// whatever it last played. With no layer active the answer is the rest box exactly.
        /// </summary>
        [Test]
        public void AnInactiveLayer_ContributesNothing()
        {
            PlaybackLayer layer = BuildActiveLayer(LeapClipId, LeapClipIndex);
            layer.flags = PlaybackFlags.None;
            SetLayerAndDirty(0, layer);

            RunSystem();

            AssertBounds(
                RestCentre,
                RestExtents,
                testWorld.EntityManager.GetComponentData<RenderBounds>(actor).Value);
        }

        // -------------------------------------------------------------------------------------
        // Fixture helpers
        // -------------------------------------------------------------------------------------

        private static PlaybackLayer BuildActiveLayer(ulong clipId, int clipIndex)
        {
            PlaybackLayer layer = PlaybackTestActor.NewLayer();
            layer.clip = new ClipId(clipId);
            layer.clipIndex = clipIndex;
            layer.time = 0f;
            layer.advanceStartTime = 0f;
            layer.speed = 1f;
            layer.loop = LoopMode.Loop;
            layer.flags = PlaybackFlags.Active;
            return layer;
        }

        private void PlayOnLayer(int layerIndex, ulong clipId, int clipIndex)
        {
            SetLayerAndDirty(layerIndex, BuildActiveLayer(clipId, clipIndex));
        }

        /// <summary>
        /// Seeds a layer and raises the dirty tag — the pairing <c>CommandApplySystem</c> performs
        /// whenever it changes which clip a layer references.
        /// </summary>
        private void SetLayerAndDirty(int layerIndex, PlaybackLayer layer)
        {
            PlaybackTestActor.SetLayer(testWorld, actor, layerIndex, layer);
            testWorld.EntityManager.SetComponentEnabled<BoundsDirty>(actor, true);
        }

        private void RunSystem()
        {
            SystemHandle boundsSystem = testWorld.GetOrCreateSystem<RenderBoundsUpdateSystem>();
            boundsSystem.Update(testWorld.Unmanaged);
            testWorld.EntityManager.CompleteAllTrackedJobs();
        }

        private static void AssertBounds(float3 expectedCentre, float3 expectedExtents, AABB actualBounds)
        {
            Assert.AreEqual(expectedCentre.x, actualBounds.Center.x, Tolerance, "centre.x");
            Assert.AreEqual(expectedCentre.y, actualBounds.Center.y, Tolerance, "centre.y");
            Assert.AreEqual(expectedCentre.z, actualBounds.Center.z, Tolerance, "centre.z");
            Assert.AreEqual(expectedExtents.x, actualBounds.Extents.x, Tolerance, "extents.x");
            Assert.AreEqual(expectedExtents.y, actualBounds.Extents.y, Tolerance, "extents.y");
            Assert.AreEqual(expectedExtents.z, actualBounds.Extents.z, Tolerance, "extents.z");
        }
    }
}
