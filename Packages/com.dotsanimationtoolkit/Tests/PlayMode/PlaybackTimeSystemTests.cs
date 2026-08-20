// Copyright (c) 2026 Spencer Park. All rights reserved.

using NUnit.Framework;
using Unity.Core;
using Unity.Entities;

namespace DotsAnimationToolkit.Tests.PlayMode
{
    /// <summary>
    /// Covers <c>PlaybackTimeSystem</c> — the advance half of the playback state machine of
    /// architecture section 5.4 (build step C4.3).
    /// </summary>
    /// <remarks>
    /// The world's time is set explicitly before each advance rather than left to the player loop,
    /// so every fixture asserts an exact number instead of a tolerance around whatever frame rate
    /// the test machine happened to manage.
    /// </remarks>
    public sealed class PlaybackTimeSystemTests
    {
        private const ulong WalkClipId = 100;
        private const ulong AttackClipId = 200;

        // Dense indices are positions in ascending-id order.
        private const int WalkClipIndex = 0;
        private const int AttackClipIndex = 1;

        private const float WalkDuration = 2f;
        private const float AttackDuration = 1f;

        private World testWorld;
        private BlobAssetReference<ClipRegistryBlob> registry;
        private Entity actor;
        private double elapsedTime;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("PlaybackTimeSystemTests");
            elapsedTime = 0d;
            registry = PlaybackTestActor.BuildRegistry(new[]
            {
                new PlaybackTestActor.ClipSpec
                {
                    clipId = WalkClipId,
                    duration = WalkDuration,
                    defaultLoop = LoopMode.Loop,
                    defaultBlendIn = 0.25f,
                    defaultBlendOut = 0.5f
                },
                new PlaybackTestActor.ClipSpec
                {
                    clipId = AttackClipId,
                    duration = AttackDuration,
                    defaultLoop = LoopMode.Once,
                    defaultBlendIn = 0.125f,
                    defaultBlendOut = 0f
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
        // Time advance and loop modes
        // -------------------------------------------------------------------------------------

        /// <summary>
        /// Catches: an advance that ignores speed. Playback that always moves at 1× makes every
        /// speed the game sets decorative.
        /// </summary>
        [Test]
        public void AnActiveLayer_AdvancesByDeltaTimesSpeed()
        {
            SeedActiveLayer(0, WalkClipIndex, WalkClipId, time: 0.5f, speed: 2f, loop: LoopMode.Loop);

            Advance(0.25f);

            Assert.AreEqual(1f, GetLayer(0).time, 1e-5f);
        }

        /// <summary>
        /// Catches: an advance that runs on inactive layers. A stopped layer whose time keeps
        /// climbing resumes somewhere unpredictable the next time it is played without a reset.
        /// </summary>
        [Test]
        public void AnInactiveLayer_DoesNotAdvance()
        {
            PlaybackLayer layer = PlaybackTestActor.NewLayer();
            layer.clip = new ClipId(WalkClipId);
            layer.clipIndex = WalkClipIndex;
            layer.time = 0.5f;
            layer.speed = 1f;
            layer.flags = PlaybackFlags.None;
            PlaybackTestActor.SetLayer(testWorld, actor, 0, layer);

            Advance(0.25f);

            Assert.AreEqual(0.5f, GetLayer(0).time, 1e-5f);
        }

        /// <summary>
        /// Catches: wrapping the stored time with fmod. The value looks right either way, and the
        /// sampler wraps it anyway — but the lap count is the only thing
        /// <c>EventWrapMath.CollectCrossings</c> has to tell one lap from the next, so wrapping here
        /// silently drops every marker on any frame long enough to cross the loop point.
        /// </summary>
        [Test]
        public void ALoopingClip_KeepsClimbingPastItsDurationOnTheUnwrappedTimeline()
        {
            SeedActiveLayer(0, WalkClipIndex, WalkClipId, time: 1.9f, speed: 1f, loop: LoopMode.Loop);

            Advance(0.5f);

            Assert.AreEqual(
                2.4f,
                GetLayer(0).time,
                1e-5f,
                "Loop time must stay un-wrapped; ClipSampler.MapTime folds it at sampling time.");
            Assert.IsTrue((GetLayer(0).flags & PlaybackFlags.Finished) == 0, "A looping clip never finishes.");
        }

        /// <summary>
        /// Catches: letting PingPong finish. PingPong reflects forever by definition; a finish would
        /// deactivate the layer at the first end and freeze the pose.
        /// </summary>
        [Test]
        public void APingPongClip_NeverFinishes()
        {
            SeedActiveLayer(0, WalkClipIndex, WalkClipId, time: 1.9f, speed: 1f, loop: LoopMode.PingPong);

            Advance(1f);

            PlaybackLayer layer = GetLayer(0);
            Assert.AreEqual(2.9f, layer.time, 1e-5f);
            Assert.IsTrue((layer.flags & PlaybackFlags.Active) != 0);
            Assert.IsTrue((layer.flags & PlaybackFlags.Finished) == 0);
        }

        /// <summary>
        /// Catches: dropping the clamp or the flags. Without the clamp a Once clip's time runs past
        /// its duration forever; without the flags nothing ever learns the clip ended, which is the
        /// contract combat code is built on.
        /// </summary>
        [Test]
        public void AOnceClip_ClampsAtItsEndAndReportsFinished()
        {
            SeedActiveLayer(0, AttackClipIndex, AttackClipId, time: 0.9f, speed: 1f, loop: LoopMode.Once);

            Advance(0.5f);

            PlaybackLayer layer = GetLayer(0);
            Assert.AreEqual(AttackDuration, layer.time, 1e-5f, "A Once clip clamps at its end.");
            Assert.IsTrue((layer.flags & PlaybackFlags.Finished) != 0);
            Assert.IsTrue((layer.flags & PlaybackFlags.FinishedThisFrame) != 0);
            Assert.IsTrue((layer.flags & PlaybackFlags.Active) == 0, "A finished layer with no queue deactivates.");
        }

        /// <summary>
        /// Catches: a reverse Once clip that only ever checks the forward end. Played backwards it
        /// would run past zero into negative time and never report finishing.
        /// </summary>
        [Test]
        public void AOnceClipPlayedInReverse_ClampsAtZeroAndReportsFinished()
        {
            SeedActiveLayer(0, AttackClipIndex, AttackClipId, time: 0.2f, speed: -1f, loop: LoopMode.Once);

            Advance(0.5f);

            PlaybackLayer layer = GetLayer(0);
            Assert.AreEqual(0f, layer.time, 1e-5f);
            Assert.IsTrue((layer.flags & PlaybackFlags.Finished) != 0);
        }

        /// <summary>
        /// Catches: clearing <c>FinishedThisFrame</c> only on the active path. The completion that
        /// raises the flag also deactivates the layer, so a clear guarded by "is it active" never
        /// runs again — the flag latches on and <c>PlaybackQuery.FinishedThisFrame</c> reports a
        /// completion that happened minutes ago, every frame, forever.
        /// </summary>
        [Test]
        public void FinishedThisFrame_IsAOneFramePulse()
        {
            SeedActiveLayer(0, AttackClipIndex, AttackClipId, time: 0.9f, speed: 1f, loop: LoopMode.Once);

            Advance(0.5f);
            Assert.IsTrue(
                (GetLayer(0).flags & PlaybackFlags.FinishedThisFrame) != 0,
                "Guard: the clip must actually have finished for the clear to be observable.");

            Advance(0.5f);

            PlaybackLayer layer = GetLayer(0);
            Assert.IsTrue((layer.flags & PlaybackFlags.FinishedThisFrame) == 0, "The pulse lasts one frame.");
            Assert.IsTrue((layer.flags & PlaybackFlags.Finished) != 0, "Finished is sticky; only the pulse clears.");
        }

        // -------------------------------------------------------------------------------------
        // Blending
        // -------------------------------------------------------------------------------------

        /// <summary>
        /// Catches: not advancing the crossfade source. A frozen outgoing clip makes every blend
        /// interpolate towards a still frame instead of towards the motion it was performing.
        /// </summary>
        [Test]
        public void ACrossfadeSource_KeepsRunningWhileTheBlendPlays()
        {
            SeedBlendingLayer(0, blendDuration: 1f, blendElapsed: 0f, previousTime: 0.5f, previousSpeed: 1f);

            Advance(0.25f);

            PlaybackLayer layer = GetLayer(0);
            Assert.AreEqual(0.75f, layer.previousTime, 1e-5f, "The outgoing clip advances on its own speed.");
            Assert.AreEqual(0.25f, layer.blendElapsed, 1e-5f);
            Assert.IsTrue((layer.flags & PlaybackFlags.Blending) != 0);
        }

        /// <summary>
        /// Catches: never completing the blend, or completing it without releasing the source slot.
        /// A blend that never ends holds a second clip in the composite forever at full weight.
        /// </summary>
        [Test]
        public void ACompletedBlend_ReleasesTheSourceSlot()
        {
            SeedBlendingLayer(0, blendDuration: 0.5f, blendElapsed: 0.4f, previousTime: 0.5f, previousSpeed: 1f);

            Advance(0.25f);

            PlaybackLayer layer = GetLayer(0);
            Assert.IsTrue((layer.flags & PlaybackFlags.Blending) == 0);
            Assert.AreEqual(-1, layer.previousClipIndex);
            Assert.AreEqual(0f, layer.blendDuration, 1e-5f);
            Assert.AreEqual(AttackClipIndex, layer.clipIndex, "The incoming clip survives its own blend.");
        }

        /// <summary>
        /// Catches: deleting the <c>BoundsDirty</c> write on blend completion. The layer stops
        /// referencing the outgoing clip, so the bounds union shrinks; without the signal the actor
        /// keeps the larger box until something unrelated dirties it — a slow, invisible over-report.
        /// </summary>
        [Test]
        public void ACompletedBlend_DirtiesTheBounds()
        {
            SeedBlendingLayer(0, blendDuration: 0.5f, blendElapsed: 0.4f, previousTime: 0.5f, previousSpeed: 1f);
            testWorld.EntityManager.SetComponentEnabled<BoundsDirty>(actor, false);

            Advance(0.25f);

            Assert.IsTrue(testWorld.EntityManager.IsComponentEnabled<BoundsDirty>(actor));
        }

        /// <summary>
        /// Catches: dirtying the bounds on every advance. Correct output, silently permanent cost —
        /// exactly the failure mode section 5.8 warns a change-version filter would produce.
        /// </summary>
        [Test]
        public void AnOrdinaryAdvance_DoesNotDirtyTheBounds()
        {
            SeedActiveLayer(0, WalkClipIndex, WalkClipId, time: 0.5f, speed: 1f, loop: LoopMode.Loop);
            testWorld.EntityManager.SetComponentEnabled<BoundsDirty>(actor, false);

            Advance(0.25f);

            Assert.IsFalse(
                testWorld.EntityManager.IsComponentEnabled<BoundsDirty>(actor),
                "Nothing changed which clips the layer references, so nothing is dirty.");
        }

        /// <summary>
        /// Catches: leaving a faded-out Stop active. The layer would sit forever at full weight on a
        /// clip index of −1 — active, contributing nothing, and never available to be cleanly
        /// restarted.
        /// </summary>
        [Test]
        public void AStopFade_DeactivatesTheLayerWhenItCompletes()
        {
            PlaybackLayer layer = PlaybackTestActor.NewLayer();
            layer.clipIndex = -1;
            layer.previousClip = new ClipId(WalkClipId);
            layer.previousClipIndex = WalkClipIndex;
            layer.previousTime = 0.5f;
            layer.previousSpeed = 1f;
            layer.previousLoop = LoopMode.Loop;
            layer.blendDuration = 0.5f;
            layer.blendElapsed = 0.4f;
            layer.flags = PlaybackFlags.Active | PlaybackFlags.Blending;
            PlaybackTestActor.SetLayer(testWorld, actor, 0, layer);

            Advance(0.25f);

            Assert.AreEqual(PlaybackFlags.None, GetLayer(0).flags, "The fade ran out and took the layer with it.");
        }

        // -------------------------------------------------------------------------------------
        // Queue promotion
        // -------------------------------------------------------------------------------------

        /// <summary>
        /// <strong>The deferral.</strong> Catches: promoting the queued clip in the same advance
        /// that finished the previous one. <c>EventEmissionSystem</c> runs later in the same group,
        /// so an immediate promotion hands it a layer that no longer names the clip that ended: the
        /// <c>ClipFinished</c> event would name the follow-up, and every marker in the finishing
        /// clip's last segment — where hit frames and footsteps sit — would be collected against the
        /// wrong clip's timeline, i.e. dropped. On the finishing frame the layer must still be the
        /// clip that finished.
        /// </summary>
        [Test]
        public void TheFinishingFrame_StillNamesTheClipThatFinished()
        {
            SeedFinishingLayerWithQueue(queuedBlend: 0f, queuedLoop: LoopMode.Loop);

            Advance(0.5f);

            PlaybackLayer layer = GetLayer(0);
            Assert.AreEqual(AttackClipIndex, layer.clipIndex, "The promotion must not have happened yet.");
            Assert.AreEqual(AttackClipId, layer.clip.Value);
            Assert.AreEqual(AttackDuration, layer.time, 1e-5f, "It holds its final pose.");
            Assert.AreEqual(0.9f, layer.advanceStartTime, 1e-5f, "The finishing clip keeps its own event window.");
            Assert.IsTrue((layer.flags & PlaybackFlags.FinishedThisFrame) != 0);
            Assert.IsTrue((layer.flags & PlaybackFlags.HasQueued) != 0, "Still queued, not yet promoted.");
            Assert.IsTrue((layer.flags & PlaybackFlags.Active) != 0, "A queued layer does not deactivate on finish.");
        }

        /// <summary>
        /// Catches: dropping the promotion. The queue would store a clip that never plays, and the
        /// layer would hold the first clip's final pose forever.
        /// </summary>
        [Test]
        public void AFinishedClipWithAQueuedFollowUp_PromotesItOnTheNextAdvance()
        {
            SeedFinishingLayerWithQueue(queuedBlend: 0f, queuedLoop: LoopMode.Loop);

            Advance(0.5f);
            Advance(0.25f);

            PlaybackLayer layer = GetLayer(0);
            Assert.AreEqual(WalkClipIndex, layer.clipIndex, "The queued clip is now the current clip.");
            Assert.AreEqual(WalkClipId, layer.clip.Value);
            Assert.AreEqual(0.25f, layer.time, 1e-5f, "It starts at zero and takes this frame's advance.");
            Assert.IsTrue((layer.flags & PlaybackFlags.Active) != 0);
            Assert.IsTrue((layer.flags & PlaybackFlags.HasQueued) == 0, "The one-deep slot is now empty.");
            Assert.IsTrue((layer.flags & PlaybackFlags.Finished) == 0, "The layer is playing again, not finished.");
        }

        /// <summary>
        /// Catches: re-raising the completion on every frame the layer sits finished-and-queued.
        /// The pulse drives <c>ClipFinished</c>; repeated, a game that queues a follow-up hears the
        /// previous clip end twice — and a combat consumer that swings on it swings twice.
        /// </summary>
        [Test]
        public void AFinishedQueuedLayer_ReportsItsCompletionOnlyOnce()
        {
            SeedFinishingLayerWithQueue(queuedBlend: 0f, queuedLoop: LoopMode.Loop);

            Advance(0.5f);
            Assert.IsTrue(
                (GetLayer(0).flags & PlaybackFlags.FinishedThisFrame) != 0,
                "Guard: the completion must fire once for the absence of a second one to mean anything.");

            Advance(0.25f);

            Assert.IsTrue((GetLayer(0).flags & PlaybackFlags.FinishedThisFrame) == 0);
        }

        /// <summary>
        /// <strong>The trap, on the promotion path.</strong> Catches: deleting
        /// <c>layer.previousLoop = layer.loop</c> in the promotion, or moving it below
        /// <c>layer.loop = layer.queuedLoop</c>. The outgoing clip must fade under the mode it was
        /// playing (Once), not under the queued entry's mode (Loop) — otherwise the clip that just
        /// ended wraps back to its start while it fades out.
        /// </summary>
        [Test]
        public void APromotionWithABlend_CapturesTheOutgoingLoopMode()
        {
            SeedFinishingLayerWithQueue(queuedBlend: 0.5f, queuedLoop: LoopMode.Loop);

            Advance(0.5f);
            Advance(0.1f);

            PlaybackLayer layer = GetLayer(0);
            Assert.AreEqual(
                LoopMode.Once,
                layer.previousLoop,
                "The finished clip was playing Once and must fade out under Once.");
            Assert.AreEqual(LoopMode.Loop, layer.loop, "The promoted entry owns layer.loop.");
            Assert.AreEqual(AttackClipIndex, layer.previousClipIndex);
            Assert.AreEqual(0.5f, layer.blendDuration, 1e-5f);
            Assert.IsTrue((layer.flags & PlaybackFlags.Blending) != 0);
        }

        /// <summary>
        /// Catches: deleting the <c>BoundsDirty</c> write on promotion. The layer starts referencing
        /// a clip whose silhouette the current bounds were never computed from, so an actor that
        /// grows in its second clip is culled against the first one's box.
        /// </summary>
        [Test]
        public void APromotion_DirtiesTheBounds()
        {
            SeedFinishingLayerWithQueue(queuedBlend: 0f, queuedLoop: LoopMode.Loop);
            Advance(0.5f);
            testWorld.EntityManager.SetComponentEnabled<BoundsDirty>(actor, false);

            Advance(0.25f);

            Assert.IsTrue(testWorld.EntityManager.IsComponentEnabled<BoundsDirty>(actor));
        }

        /// <summary>
        /// Catches: deleting the <c>BoundsDirty</c> write on plain Once completion. A layer that
        /// stops referencing its clip and never says so leaves the actor culled against a box that
        /// includes an animation it is no longer playing.
        /// </summary>
        [Test]
        public void AOnceCompletionWithNoQueue_DirtiesTheBounds()
        {
            SeedActiveLayer(0, AttackClipIndex, AttackClipId, time: 0.9f, speed: 1f, loop: LoopMode.Once);
            testWorld.EntityManager.SetComponentEnabled<BoundsDirty>(actor, false);

            Advance(0.5f);

            Assert.IsTrue(testWorld.EntityManager.IsComponentEnabled<BoundsDirty>(actor));
        }

        // -------------------------------------------------------------------------------------
        // The event window (amendment A27)
        // -------------------------------------------------------------------------------------

        /// <summary>
        /// Catches: deleting the <c>advanceStartTime</c> snapshot. <c>EventEmissionSystem</c> runs
        /// after the advance and has no other record of where the frame began; without it every
        /// marker crossing has to be reconstructed by subtraction, which is wrong on exactly the
        /// frames a clip clamps or a queue promotes.
        /// </summary>
        [Test]
        public void AnAdvance_RecordsWhereTheFrameStarted()
        {
            SeedActiveLayer(0, WalkClipIndex, WalkClipId, time: 0.5f, speed: 1f, loop: LoopMode.Loop);

            Advance(0.25f);

            PlaybackLayer layer = GetLayer(0);
            Assert.AreEqual(0.5f, layer.advanceStartTime, 1e-5f, "The window opens where the frame began.");
            Assert.AreEqual(0.75f, layer.time, 1e-5f, "And closes where it ended.");
        }

        /// <summary>
        /// Catches: leaving <c>advanceStartTime</c> at the finished clip's time through a promotion.
        /// The promoted clip would be credited with a window running from the previous clip's end
        /// time — a sweep across markers it never played, on the frame it starts.
        /// </summary>
        [Test]
        public void APromotion_RestartsTheEventWindowWithThePromotedClip()
        {
            SeedFinishingLayerWithQueue(queuedBlend: 0f, queuedLoop: LoopMode.Loop);

            Advance(0.5f);
            Advance(0.25f);

            PlaybackLayer layer = GetLayer(0);
            Assert.AreEqual(0f, layer.advanceStartTime, 1e-5f, "The window opens where the promoted clip opens.");
            Assert.AreEqual(0.25f, layer.time, 1e-5f);
        }

        // -------------------------------------------------------------------------------------
        // Fixture helpers
        // -------------------------------------------------------------------------------------

        private PlaybackLayer GetLayer(int layerIndex)
        {
            return PlaybackTestActor.GetLayer(testWorld, actor, layerIndex);
        }

        private void SeedActiveLayer(int layerIndex, int clipIndex, ulong clipId, float time, float speed, LoopMode loop)
        {
            PlaybackLayer layer = PlaybackTestActor.NewLayer();
            layer.clip = new ClipId(clipId);
            layer.clipIndex = clipIndex;
            layer.time = time;
            layer.advanceStartTime = time;
            layer.speed = speed;
            layer.loop = loop;
            layer.flags = PlaybackFlags.Active;
            PlaybackTestActor.SetLayer(testWorld, actor, layerIndex, layer);
        }

        /// <summary>Attack crossfading in over Walk, mid-blend.</summary>
        private void SeedBlendingLayer(
            int layerIndex,
            float blendDuration,
            float blendElapsed,
            float previousTime,
            float previousSpeed)
        {
            PlaybackLayer layer = PlaybackTestActor.NewLayer();
            layer.clip = new ClipId(AttackClipId);
            layer.clipIndex = AttackClipIndex;
            layer.time = 0.1f;
            layer.advanceStartTime = 0.1f;
            layer.speed = 1f;
            layer.loop = LoopMode.Loop;
            layer.previousClip = new ClipId(WalkClipId);
            layer.previousClipIndex = WalkClipIndex;
            layer.previousTime = previousTime;
            layer.previousSpeed = previousSpeed;
            layer.previousLoop = LoopMode.Loop;
            layer.blendDuration = blendDuration;
            layer.blendElapsed = blendElapsed;
            layer.flags = PlaybackFlags.Active | PlaybackFlags.Blending;
            PlaybackTestActor.SetLayer(testWorld, actor, layerIndex, layer);
        }

        /// <summary>
        /// Attack playing Once, one advance away from its end, with Walk queued behind it. The
        /// outgoing mode (Once) differs from the queued one (Loop) so the promotion's
        /// <c>previousLoop</c> capture is observable.
        /// </summary>
        private void SeedFinishingLayerWithQueue(float queuedBlend, LoopMode queuedLoop)
        {
            PlaybackLayer layer = PlaybackTestActor.NewLayer();
            layer.clip = new ClipId(AttackClipId);
            layer.clipIndex = AttackClipIndex;
            layer.time = 0.9f;
            layer.advanceStartTime = 0.9f;
            layer.speed = 1f;
            layer.loop = LoopMode.Once;
            layer.queuedClip = new ClipId(WalkClipId);
            layer.queuedSpeed = 1f;
            layer.queuedLoop = queuedLoop;
            layer.queuedBlend = queuedBlend;
            layer.flags = PlaybackFlags.Active | PlaybackFlags.HasQueued;
            PlaybackTestActor.SetLayer(testWorld, actor, 0, layer);
        }

        /// <summary>
        /// Runs one frame of exactly <paramref name="deltaTime"/> seconds and completes its jobs,
        /// so assertions read settled data and exact numbers.
        /// </summary>
        private void Advance(float deltaTime)
        {
            elapsedTime += deltaTime;
            testWorld.SetTime(new TimeData(elapsedTime, deltaTime));

            SystemHandle playbackTimeSystem = testWorld.GetOrCreateSystem<PlaybackTimeSystem>();
            playbackTimeSystem.Update(testWorld.Unmanaged);
            testWorld.EntityManager.CompleteAllTrackedJobs();
        }
    }
}
