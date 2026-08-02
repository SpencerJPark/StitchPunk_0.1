// Copyright (c) 2026 Stitch Punk. All rights reserved.

using NUnit.Framework;
using Unity.Core;
using Unity.Entities;

namespace StitchPunk.AnimationToolkit.Tests.PlayMode
{
    /// <summary>
    /// Covers <c>EventEmissionSystem</c> — architecture section 5.5 as amended by A27 and A28
    /// (build step C4.4).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The crossing math itself is <c>EventWrapMath</c>'s and is covered exhaustively by
    /// <c>EventWrapMathTests</c> in EditMode. These fixtures are about the parts only a running
    /// system has: which window it hands the pure function, which layers it visits, what it does
    /// with <see cref="AnimEventsPending"/>, and the two contracts A28 pinned that a reader of this
    /// system alone cannot check.
    /// </para>
    /// <para>
    /// Where a fixture needs the advance and the emission in order, it runs both systems — because
    /// the defect A28 exists to close is invisible to either one on its own.
    /// </para>
    /// </remarks>
    public sealed class EventEmissionSystemTests
    {
        private const ulong WalkClipId = 100;
        private const ulong AttackClipId = 200;

        private const int WalkClipIndex = 0;
        private const int AttackClipIndex = 1;

        private const float WalkDuration = 2f;
        private const float AttackDuration = 1f;

        // User keys start at 16 (validation rule V09); these are deliberately above that.
        private const uint FootstepKey = 20;
        private const uint HitFrameKey = 21;

        private World testWorld;
        private BlobAssetReference<ClipRegistryBlob> registry;
        private Entity actor;
        private double elapsedTime;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("EventEmissionSystemTests");
            elapsedTime = 0d;
            registry = PlaybackTestActor.BuildRegistry(new[]
            {
                new PlaybackTestActor.ClipSpec
                {
                    clipId = WalkClipId,
                    duration = WalkDuration,
                    defaultLoop = LoopMode.Loop,
                    events = new[]
                    {
                        PlaybackTestActor.Marker(0.25f, FootstepKey, intParam: 7, floatParam: 1.5f),
                        PlaybackTestActor.Marker(0.75f, FootstepKey, intParam: 8)
                    }
                },
                new PlaybackTestActor.ClipSpec
                {
                    clipId = AttackClipId,
                    duration = AttackDuration,
                    defaultLoop = LoopMode.Once,
                    events = new[]
                    {
                        PlaybackTestActor.Marker(0.95f, HitFrameKey)
                    }
                }
            });
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

        // -------------------------------------------------------------------------------------
        // Marker crossings
        // -------------------------------------------------------------------------------------

        /// <summary>
        /// Catches: emitting nothing, or emitting without the marker's payload. The payload is how a
        /// game tells one footstep from another; dropping it makes every marker anonymous.
        /// </summary>
        [Test]
        public void AMarkerCrossedThisFrame_IsEmittedWithItsPayload()
        {
            SeedActiveLayer(0, WalkClipIndex, WalkClipId, advanceStartTime: 0.4f, time: 0.6f, loop: LoopMode.Loop);

            RunEmission();

            DynamicBuffer<AnimEventOutput> animEvents = Events();
            Assert.AreEqual(1, animEvents.Length, "Exactly the 0.25-normalized marker (0.5 s) is inside (0.4, 0.6].");
            Assert.AreEqual(FootstepKey, animEvents[0].eventKey);
            Assert.AreEqual(0, animEvents[0].layerIndex);
            Assert.AreEqual(WalkClipId, animEvents[0].clip.Value);
            Assert.AreEqual(7, animEvents[0].intParam);
            Assert.AreEqual(1.5f, animEvents[0].floatParam, 1e-5f);
        }

        /// <summary>
        /// Catches: emitting every marker in the clip regardless of the window. A system that
        /// ignores the window fires every footstep on every frame, which reads as "events work"
        /// until something counts them.
        /// </summary>
        [Test]
        public void AMarkerOutsideThisFramesWindow_IsNotEmitted()
        {
            SeedActiveLayer(0, WalkClipIndex, WalkClipId, advanceStartTime: 0.6f, time: 0.8f, loop: LoopMode.Loop);

            RunEmission();

            Assert.AreEqual(0, Events().Length, "Nothing lies in (0.6, 0.8] on a 2 s clip.");
        }

        /// <summary>
        /// <strong>A27's window, end to end.</strong> Catches: recomputing the opening edge as
        /// <c>time − dt × speed</c> instead of reading <c>advanceStartTime</c>. On the frame a Once
        /// clip clamps, the two differ — the clamp shortens the real interval — and the subtraction
        /// describes a window running past the clip's end, so the marker at 0.95 is missed. That
        /// marker is a hit frame: the difference is an attack that never lands.
        /// </summary>
        [Test]
        public void AMarkerInTheLastSegmentOfAFinishingClip_StillFires()
        {
            SeedActiveLayer(0, AttackClipIndex, AttackClipId, advanceStartTime: 0f, time: 0f, loop: LoopMode.Once);
            PlaybackLayer seeded = GetLayer(0);
            seeded.time = 0.9f;
            seeded.advanceStartTime = 0.9f;
            PlaybackTestActor.SetLayer(testWorld, actor, 0, seeded);

            // A full frame: the advance clamps at the end and raises the completion, then emission
            // reads the window the advance recorded.
            AdvanceAndEmit(0.5f);

            DynamicBuffer<AnimEventOutput> animEvents = Events();
            Assert.AreEqual(2, animEvents.Length, "The hit frame at 0.95, then the completion.");
            Assert.AreEqual(HitFrameKey, animEvents[0].eventKey, "Markers come before the completion they precede.");
            Assert.AreEqual((uint)ReservedEventKeys.ClipFinished, animEvents[1].eventKey);
        }

        /// <summary>
        /// Catches: reading the layer's own index instead of the loop's. Multi-layer rigs are the
        /// point of the buffer, and an event that names the wrong layer routes to the wrong consumer.
        /// </summary>
        [Test]
        public void AnEmittedEvent_NamesTheLayerItCameFrom()
        {
            SeedActiveLayer(1, WalkClipIndex, WalkClipId, advanceStartTime: 0.4f, time: 0.6f, loop: LoopMode.Loop);

            RunEmission();

            Assert.AreEqual(1, Events().Length);
            Assert.AreEqual(1, Events()[0].layerIndex);
        }

        /// <summary>
        /// Catches: visiting inactive layers. A stopped layer keeps its clip and its last times, so
        /// a system that does not check would re-fire its final markers forever.
        /// </summary>
        [Test]
        public void AStoppedLayer_EmitsNothing()
        {
            PlaybackLayer layer = PlaybackTestActor.NewLayer();
            layer.clip = new ClipId(WalkClipId);
            layer.clipIndex = WalkClipIndex;
            layer.advanceStartTime = 0.4f;
            layer.time = 0.6f;
            layer.loop = LoopMode.Loop;
            layer.flags = PlaybackFlags.None;
            PlaybackTestActor.SetLayer(testWorld, actor, 0, layer);

            RunEmission();

            Assert.AreEqual(0, Events().Length);
        }

        // -------------------------------------------------------------------------------------
        // ClipFinished
        // -------------------------------------------------------------------------------------

        /// <summary>
        /// Catches: gating the whole emission on <c>Active</c>. A Once clip that finishes with
        /// nothing queued is deactivated by the same advance that finished it, so an Active-only
        /// gate skips the one layer whose completion has to be reported — and <c>ClipFinished</c>
        /// never fires for the ordinary case of a clip simply ending.
        /// </summary>
        [Test]
        public void AClipThatFinishedAndDeactivated_StillReportsItsCompletion()
        {
            SeedActiveLayer(0, AttackClipIndex, AttackClipId, advanceStartTime: 0.99f, time: 0.99f, loop: LoopMode.Once);

            AdvanceAndEmit(0.5f);

            Assert.IsFalse(
                (GetLayer(0).flags & PlaybackFlags.Active) != 0,
                "Guard: the layer must be inactive, or this fixture proves nothing.");
            Assert.AreEqual(
                (uint)ReservedEventKeys.ClipFinished,
                Events()[Events().Length - 1].eventKey);
        }

        /// <summary>
        /// <strong>The A30 decision, from the consumer's side.</strong> Catches: promoting a queued
        /// clip in the same advance that finished the previous one — <c>ClipFinished</c> would then
        /// name the follow-up, telling a game its next animation ended before it played a frame.
        /// </summary>
        [Test]
        public void ACompletionWithAQueuedFollowUp_NamesTheClipThatActuallyEnded()
        {
            PlaybackLayer layer = PlaybackTestActor.NewLayer();
            layer.clip = new ClipId(AttackClipId);
            layer.clipIndex = AttackClipIndex;
            layer.time = 0.99f;
            layer.advanceStartTime = 0.99f;
            layer.speed = 1f;
            layer.loop = LoopMode.Once;
            layer.queuedClip = new ClipId(WalkClipId);
            layer.queuedSpeed = 1f;
            layer.queuedLoop = LoopMode.Loop;
            layer.flags = PlaybackFlags.Active | PlaybackFlags.HasQueued;
            PlaybackTestActor.SetLayer(testWorld, actor, 0, layer);

            AdvanceAndEmit(0.5f);

            DynamicBuffer<AnimEventOutput> animEvents = Events();
            AnimEventOutput completion = animEvents[animEvents.Length - 1];
            Assert.AreEqual((uint)ReservedEventKeys.ClipFinished, completion.eventKey);
            Assert.AreEqual(
                AttackClipId,
                completion.clip.Value,
                "The completion belongs to the clip that ended, not to the one queued behind it.");
        }

        /// <summary>
        /// Catches: emitting <c>ClipFinished</c> from the sticky <c>Finished</c> flag rather than
        /// the one-frame pulse. A finished layer would report its completion on every subsequent
        /// frame — and a combat consumer that swings on it would swing forever.
        /// </summary>
        [Test]
        public void ACompletion_IsReportedOnceNotOnEveryFrameAfterwards()
        {
            SeedActiveLayer(0, AttackClipIndex, AttackClipId, advanceStartTime: 0.99f, time: 0.99f, loop: LoopMode.Once);

            AdvanceAndEmit(0.5f);
            Assert.AreEqual(
                (uint)ReservedEventKeys.ClipFinished,
                Events()[Events().Length - 1].eventKey,
                "Guard: the completion must fire once for its absence later to mean anything.");

            AdvanceAndEmit(0.5f);

            Assert.AreEqual(0, Events().Length, "The completion is a one-frame pulse.");
        }

        // -------------------------------------------------------------------------------------
        // The AnimEventsPending contract (amendment A28)
        // -------------------------------------------------------------------------------------

        /// <summary>
        /// Catches: never enabling the flag. Consumers query the enableable, so events written into
        /// a buffer whose flag stays off are invisible — the buffer fills and nothing reads it.
        /// </summary>
        [Test]
        public void EmittingAnEvent_EnablesThePendingFlag()
        {
            SeedActiveLayer(0, WalkClipIndex, WalkClipId, advanceStartTime: 0.4f, time: 0.6f, loop: LoopMode.Loop);

            RunEmission();

            Assert.IsTrue(testWorld.EntityManager.IsComponentEnabled<AnimEventsPending>(actor));
        }

        /// <summary>
        /// Catches: enabling the flag unconditionally. It exists so that consumers skip actors with
        /// nothing to say; set on every actor every frame it stops filtering anything.
        /// </summary>
        [Test]
        public void EmittingNothing_LeavesThePendingFlagAlone()
        {
            SeedActiveLayer(0, WalkClipIndex, WalkClipId, advanceStartTime: 0.6f, time: 0.8f, loop: LoopMode.Loop);

            RunEmission();

            Assert.IsFalse(testWorld.EntityManager.IsComponentEnabled<AnimEventsPending>(actor));
        }

        /// <summary>
        /// <strong>A28, the reason it exists.</strong> Catches: adding a buffer clear back into this
        /// system. <c>CommandApplySystem</c> emits <c>ClipResolveFailed</c> earlier in the same
        /// group; a clear here destroys it in the frame it was raised, and the failed request goes
        /// back to being exactly as silent as it was with no event mechanism at all. Neither system
        /// read alone reveals this — only running both, in order, does.
        /// </summary>
        [Test]
        public void AResolveFailureRaisedEarlierInTheFrame_SurvivesEmission()
        {
            SeedActiveLayer(0, WalkClipIndex, WalkClipId, advanceStartTime: 0.4f, time: 0.4f, loop: LoopMode.Loop);
            PlaybackTestActor.EnqueueCommand(
                testWorld,
                actor,
                PlaybackTestActor.PlayCommand(0, 999));

            RunCommandApply();
            Assert.AreEqual(1, Events().Length, "Guard: the failure must have been raised.");

            RunEmission();

            DynamicBuffer<AnimEventOutput> animEvents = Events();
            Assert.AreEqual(1, animEvents.Length, "Emission must not have wiped the buffer.");
            Assert.AreEqual((uint)ReservedEventKeys.ClipResolveFailed, animEvents[0].eventKey);
            Assert.IsTrue(
                testWorld.EntityManager.IsComponentEnabled<AnimEventsPending>(actor),
                "Nor may it disable the flag on an actor that emitted nothing of its own.");
        }

        /// <summary>
        /// Catches: dropping the clear that A28 moved into <c>CommandApplySystem</c>, from the other
        /// end. Last frame's events must be gone by the time this frame's are read, or every
        /// consumer double-handles them.
        /// </summary>
        [Test]
        public void EventsFromTheFrameBefore_AreGoneByTheNextEmission()
        {
            SeedActiveLayer(0, WalkClipIndex, WalkClipId, advanceStartTime: 0.4f, time: 0.4f, loop: LoopMode.Loop);

            AdvanceAndEmit(0.2f);
            Assert.AreEqual(1, Events().Length, "Guard: the 0.5 s marker fires as the layer crosses it.");

            AdvanceAndEmit(0.2f);

            Assert.AreEqual(0, Events().Length, "Last frame's footstep must not still be in the buffer.");
        }

        // -------------------------------------------------------------------------------------
        // Fixture helpers
        // -------------------------------------------------------------------------------------

        private DynamicBuffer<AnimEventOutput> Events()
        {
            return testWorld.EntityManager.GetBuffer<AnimEventOutput>(actor);
        }

        private PlaybackLayer GetLayer(int layerIndex)
        {
            return PlaybackTestActor.GetLayer(testWorld, actor, layerIndex);
        }

        private void SeedActiveLayer(
            int layerIndex,
            int clipIndex,
            ulong clipId,
            float advanceStartTime,
            float time,
            LoopMode loop)
        {
            PlaybackLayer layer = PlaybackTestActor.NewLayer();
            layer.clip = new ClipId(clipId);
            layer.clipIndex = clipIndex;
            layer.advanceStartTime = advanceStartTime;
            layer.time = time;
            layer.speed = 1f;
            layer.loop = loop;
            layer.flags = PlaybackFlags.Active;
            PlaybackTestActor.SetLayer(testWorld, actor, layerIndex, layer);
        }

        /// <summary>Runs emission alone, against a hand-seeded window.</summary>
        private void RunEmission()
        {
            SystemHandle emissionSystem = testWorld.GetOrCreateSystem<EventEmissionSystem>();
            emissionSystem.Update(testWorld.Unmanaged);
            testWorld.EntityManager.CompleteAllTrackedJobs();
        }

        private void RunCommandApply()
        {
            SystemHandle commandApplySystem = testWorld.GetOrCreateSystem<CommandApplySystem>();
            commandApplySystem.Update(testWorld.Unmanaged);
            testWorld.EntityManager.CompleteAllTrackedJobs();
        }

        /// <summary>
        /// Runs a whole logic-group frame in order — clear, advance, emit — which is the only way to
        /// observe the contracts that span the three systems.
        /// </summary>
        private void AdvanceAndEmit(float deltaTime)
        {
            elapsedTime += deltaTime;
            testWorld.SetTime(new TimeData(elapsedTime, deltaTime));

            RunCommandApply();

            SystemHandle playbackTimeSystem = testWorld.GetOrCreateSystem<PlaybackTimeSystem>();
            playbackTimeSystem.Update(testWorld.Unmanaged);
            testWorld.EntityManager.CompleteAllTrackedJobs();

            RunEmission();
        }
    }
}
