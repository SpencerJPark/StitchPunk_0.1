// Copyright (c) 2026 Stitch Punk. All rights reserved.

using NUnit.Framework;
using Unity.Entities;

namespace StitchPunk.AnimationToolkit.Tests.PlayMode
{
    /// <summary>
    /// Covers <c>CommandApplySystem</c> — the request half of the playback state machine of
    /// architecture section 5.4 (build step C4.3).
    /// </summary>
    /// <remarks>
    /// Every fixture below names the mutation it catches, per the C4 test-integrity standard. Two
    /// of them exist because the property they pin has already failed silently once in this package
    /// or is documented as the trap that would: the <c>previousLoop</c> capture order, and the
    /// stale-event clear that amendment A28 moved into this system.
    /// </remarks>
    public sealed class CommandApplySystemTests
    {
        private const ulong WalkClipId = 100;
        private const ulong AttackClipId = 200;
        private const ulong UnknownClipId = 999;

        // Dense indices are positions in ascending-id order, so Walk resolves to 0 and Attack to 1.
        private const int WalkClipIndex = 0;
        private const int AttackClipIndex = 1;

        private World testWorld;
        private BlobAssetReference<ClipRegistryBlob> registry;
        private Entity actor;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("CommandApplySystemTests");
            registry = PlaybackTestActor.BuildRegistry(new[]
            {
                new PlaybackTestActor.ClipSpec
                {
                    clipId = WalkClipId,
                    duration = 2f,
                    defaultLoop = LoopMode.Loop,
                    defaultBlendIn = 0.25f,
                    defaultBlendOut = 0.5f
                },
                new PlaybackTestActor.ClipSpec
                {
                    clipId = AttackClipId,
                    duration = 1f,
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
        // Play
        // -------------------------------------------------------------------------------------

        /// <summary>
        /// Catches: dropping the resolve, the <c>clipIndex</c> write, or the Active flag. Without
        /// any of the three the layer looks played and samples nothing.
        /// </summary>
        [Test]
        public void Play_ResolvesTheClipAndActivatesTheLayer()
        {
            PlaybackTestActor.EnqueueCommand(testWorld, actor, PlaybackTestActor.PlayCommand(0, WalkClipId));
            RunCommandApply();

            PlaybackLayer layer = PlaybackTestActor.GetLayer(testWorld, actor, 0);
            Assert.AreEqual(WalkClipIndex, layer.clipIndex, "The clip id did not resolve to its dense index.");
            Assert.AreEqual(WalkClipId, layer.clip.Value);
            Assert.AreEqual(0f, layer.time, "Forward playback starts at the beginning.");
            Assert.IsTrue((layer.flags & PlaybackFlags.Active) != 0, "The layer must be active after a Play.");
        }

        /// <summary>
        /// <strong>The trap.</strong> Catches: deleting <c>layer.previousLoop = layer.loop</c>, or
        /// moving it below <c>layer.loop = command.loop</c>. Walk's authored default is Loop but it
        /// was played Once; unless the mode it was actually playing under is captured before the
        /// incoming request overwrites it, the outgoing clip wraps to zero as it fades instead of
        /// holding on its final pose — a pop in the exact transition the crossfade smooths.
        /// </summary>
        /// <remarks>
        /// The assertion is deliberately not <c>AreNotEqual(UseClipDefault)</c>: a wrong-but-set
        /// value would pass that. Both failure modes produce a specific wrong answer —
        /// <see cref="LoopMode.Loop"/> if the capture moved after the overwrite,
        /// <see cref="LoopMode.UseClipDefault"/> if it was deleted — and neither is Once.
        /// </remarks>
        [Test]
        public void PlayOverACrossfade_CapturesTheModeTheOutgoingClipWasActuallyPlayingUnder()
        {
            PlaybackTestActor.EnqueueCommand(
                testWorld,
                actor,
                PlaybackTestActor.PlayCommand(0, WalkClipId, loop: LoopMode.Once, blendDuration: 0f));
            RunCommandApply();

            Assert.AreEqual(
                LoopMode.Once,
                PlaybackTestActor.GetLayer(testWorld, actor, 0).loop,
                "Guard: the outgoing clip must be playing under a mode that is not its own default, " +
                "or this fixture cannot tell the two failure modes apart.");

            PlaybackTestActor.EnqueueCommand(
                testWorld,
                actor,
                PlaybackTestActor.PlayCommand(0, AttackClipId, loop: LoopMode.Loop, blendDuration: 0.5f));
            RunCommandApply();

            PlaybackLayer layer = PlaybackTestActor.GetLayer(testWorld, actor, 0);
            Assert.AreEqual(
                LoopMode.Once,
                layer.previousLoop,
                "previousLoop must hold the mode the outgoing clip was playing under, captured " +
                "before layer.loop was overwritten with the incoming request.");
            Assert.AreEqual(LoopMode.Loop, layer.loop, "The incoming request owns layer.loop.");
        }

        /// <summary>
        /// Catches: deleting the demotion block. Without it a crossfade has no source pose and the
        /// blend interpolates from whatever the previous slot last held — usually nothing.
        /// </summary>
        [Test]
        public void PlayWithABlend_DemotesTheOutgoingClipIntoThePreviousSlot()
        {
            SeedActiveLayer(0, WalkClipIndex, WalkClipId, time: 0.75f, speed: 1.5f, loop: LoopMode.Loop);

            PlaybackTestActor.EnqueueCommand(
                testWorld,
                actor,
                PlaybackTestActor.PlayCommand(0, AttackClipId, blendDuration: 0.5f));
            RunCommandApply();

            PlaybackLayer layer = PlaybackTestActor.GetLayer(testWorld, actor, 0);
            Assert.AreEqual(WalkClipIndex, layer.previousClipIndex);
            Assert.AreEqual(WalkClipId, layer.previousClip.Value);
            Assert.AreEqual(0.75f, layer.previousTime, 1e-5f, "The outgoing clip keeps its running time.");
            Assert.AreEqual(1.5f, layer.previousSpeed, 1e-5f, "The outgoing clip keeps its running speed.");
            Assert.AreEqual(0.5f, layer.blendDuration, 1e-5f);
            Assert.AreEqual(0f, layer.blendElapsed, 1e-5f);
            Assert.IsTrue((layer.flags & PlaybackFlags.Blending) != 0);
        }

        /// <summary>
        /// Catches: treating a 0 blend as "no opinion" and substituting the clip's default. A hard
        /// cut is a reachable, meaningful request — the old <c>SetLayer</c> pop — and collapsing it
        /// into the default silently makes every cut a quarter-second crossfade.
        /// </summary>
        [Test]
        public void PlayWithAZeroBlend_IsAHardCutWithNoBlendSource()
        {
            SeedActiveLayer(0, WalkClipIndex, WalkClipId, time: 0.75f, speed: 1f, loop: LoopMode.Loop);

            PlaybackTestActor.EnqueueCommand(
                testWorld,
                actor,
                PlaybackTestActor.PlayCommand(0, AttackClipId, blendDuration: 0f));
            RunCommandApply();

            PlaybackLayer layer = PlaybackTestActor.GetLayer(testWorld, actor, 0);
            Assert.AreEqual(-1, layer.previousClipIndex, "A hard cut leaves no crossfade source.");
            Assert.AreEqual(0f, layer.blendDuration, 1e-5f);
            Assert.IsFalse((layer.flags & PlaybackFlags.Blending) != 0);
        }

        /// <summary>
        /// Catches: collapsing a NaN blend to 0, or to some hard-coded constant. NaN means "use the
        /// clip's authored default", and Attack authors 0.125.
        /// </summary>
        [Test]
        public void PlayWithANaNBlend_UsesTheIncomingClipsAuthoredBlendIn()
        {
            SeedActiveLayer(0, WalkClipIndex, WalkClipId, time: 0.5f, speed: 1f, loop: LoopMode.Loop);

            PlaybackTestActor.EnqueueCommand(testWorld, actor, PlaybackTestActor.PlayCommand(0, AttackClipId));
            RunCommandApply();

            PlaybackLayer layer = PlaybackTestActor.GetLayer(testWorld, actor, 0);
            Assert.AreEqual(0.125f, layer.blendDuration, 1e-5f, "NaN must resolve to defaultBlendIn.");
            Assert.IsTrue((layer.flags & PlaybackFlags.Blending) != 0);
        }

        /// <summary>
        /// Catches: always starting at 0. A reverse Play that starts at 0 is instantly at the end of
        /// a Once clip, so it reports finished on its first advance and never shows a frame.
        /// </summary>
        [Test]
        public void PlayInReverse_StartsAtTheEndOfTheClip()
        {
            PlaybackTestActor.EnqueueCommand(
                testWorld,
                actor,
                PlaybackTestActor.PlayCommand(0, WalkClipId, speed: -1f));
            RunCommandApply();

            Assert.AreEqual(
                2f,
                PlaybackTestActor.GetLayer(testWorld, actor, 0).time,
                1e-5f,
                "Reverse playback must start at the clip's duration.");
        }

        /// <summary>
        /// Catches: dropping the resolve guard. Without it an unknown id would be written into the
        /// layer with <c>clipIndex = -1</c>, replacing a clip that was playing perfectly well with
        /// nothing at all, and reporting no error.
        /// </summary>
        [Test]
        public void PlayOfAnUnknownClip_LeavesTheLayerAloneAndReportsIt()
        {
            SeedActiveLayer(0, WalkClipIndex, WalkClipId, time: 0.5f, speed: 1f, loop: LoopMode.Loop);

            PlaybackTestActor.EnqueueCommand(testWorld, actor, PlaybackTestActor.PlayCommand(0, UnknownClipId));
            RunCommandApply();

            PlaybackLayer layer = PlaybackTestActor.GetLayer(testWorld, actor, 0);
            Assert.AreEqual(WalkClipIndex, layer.clipIndex, "The playing clip must survive a failed request.");
            Assert.AreEqual(0.5f, layer.time, 1e-5f, "A failed request must not scrub the layer.");

            DynamicBuffer<AnimEventOutput> animEvents =
                testWorld.EntityManager.GetBuffer<AnimEventOutput>(actor);
            Assert.AreEqual(1, animEvents.Length, "Exactly one failure event.");
            Assert.AreEqual((uint)ReservedEventKeys.ClipResolveFailed, animEvents[0].eventKey);
            Assert.AreEqual(UnknownClipId, animEvents[0].clip.Value, "The event must name the id that failed.");
            Assert.IsTrue(
                testWorld.EntityManager.IsComponentEnabled<AnimEventsPending>(actor),
                "A consumer gated on AnimEventsPending would never see the failure.");
        }

        // -------------------------------------------------------------------------------------
        // Queue and Stop
        // -------------------------------------------------------------------------------------

        /// <summary>
        /// Catches: deleting the queue write or the HasQueued flag. A queue that stores nothing
        /// looks identical to no queue at all until the current clip ends and nothing follows.
        /// </summary>
        [Test]
        public void Queue_StoresTheClipInTheOneDeepSlot()
        {
            SeedActiveLayer(0, AttackClipIndex, AttackClipId, time: 0f, speed: 1f, loop: LoopMode.Once);

            PlaybackTestActor.EnqueueCommand(
                testWorld,
                actor,
                PlaybackTestActor.QueueCommand(0, WalkClipId, speed: 2f, loop: LoopMode.Loop, blendDuration: 0.4f));
            RunCommandApply();

            PlaybackLayer layer = PlaybackTestActor.GetLayer(testWorld, actor, 0);
            Assert.AreEqual(WalkClipId, layer.queuedClip.Value);
            Assert.AreEqual(2f, layer.queuedSpeed, 1e-5f);
            Assert.AreEqual(LoopMode.Loop, layer.queuedLoop);
            Assert.AreEqual(0.4f, layer.queuedBlend, 1e-5f);
            Assert.IsTrue((layer.flags & PlaybackFlags.HasQueued) != 0);
            Assert.AreEqual(AttackClipIndex, layer.clipIndex, "Queueing must not disturb the current clip.");
        }

        /// <summary>
        /// Catches: deleting the queue's own resolve. Without it an unknown queued id sits silently
        /// in the slot and the failure surfaces seconds later, as a promotion that does nothing, at
        /// a moment with no connection to the call that caused it.
        /// </summary>
        [Test]
        public void QueueOfAnUnknownClip_LeavesTheSlotEmptyAndReportsIt()
        {
            PlaybackTestActor.EnqueueCommand(testWorld, actor, PlaybackTestActor.QueueCommand(0, UnknownClipId));
            RunCommandApply();

            PlaybackLayer layer = PlaybackTestActor.GetLayer(testWorld, actor, 0);
            Assert.IsFalse((layer.flags & PlaybackFlags.HasQueued) != 0, "Nothing resolvable was queued.");
            Assert.AreEqual(
                (uint)ReservedEventKeys.ClipResolveFailed,
                testWorld.EntityManager.GetBuffer<AnimEventOutput>(actor)[0].eventKey);
        }

        /// <summary>
        /// Catches: deleting the immediate-deactivate branch, or leaving the clip index set. A
        /// stopped layer that keeps its clip index keeps contributing its pose to compositing.
        /// </summary>
        [Test]
        public void StopWithAZeroBlend_DeactivatesTheLayerImmediately()
        {
            SeedActiveLayer(0, WalkClipIndex, WalkClipId, time: 0.5f, speed: 1f, loop: LoopMode.Loop);

            PlaybackTestActor.EnqueueCommand(testWorld, actor, PlaybackTestActor.StopCommand(0, 0f));
            RunCommandApply();

            PlaybackLayer layer = PlaybackTestActor.GetLayer(testWorld, actor, 0);
            Assert.AreEqual(PlaybackFlags.None, layer.flags);
            Assert.AreEqual(-1, layer.clipIndex);
        }

        /// <summary>
        /// Catches: deactivating on a fading Stop. A layer that goes inactive the instant Stop is
        /// called has nothing left to fade, so the fade-out duration is silently ignored and the
        /// pose snaps — the defect a fade-out exists to prevent.
        /// </summary>
        [Test]
        public void StopWithABlend_KeepsTheLayerActiveWhileTheOldClipFadesOut()
        {
            SeedActiveLayer(0, WalkClipIndex, WalkClipId, time: 0.5f, speed: 1f, loop: LoopMode.Loop);

            PlaybackTestActor.EnqueueCommand(testWorld, actor, PlaybackTestActor.StopCommand(0, 0.5f));
            RunCommandApply();

            PlaybackLayer layer = PlaybackTestActor.GetLayer(testWorld, actor, 0);
            Assert.IsTrue((layer.flags & PlaybackFlags.Active) != 0, "The fade still has to run.");
            Assert.IsTrue((layer.flags & PlaybackFlags.Blending) != 0);
            Assert.AreEqual(-1, layer.clipIndex, "There is no incoming clip — it fades out to nothing.");
            Assert.AreEqual(WalkClipIndex, layer.previousClipIndex, "The stopped clip is the fade source.");
            Assert.AreEqual(0.5f, layer.blendDuration, 1e-5f);
        }

        /// <summary>
        /// Catches: deleting the NaN branch of Stop. Walk authors a 0.5 s fade-out; without the
        /// branch a NaN would land in <c>blendDuration</c> and every comparison against it is false,
        /// so the fade would never complete and the layer would never deactivate.
        /// </summary>
        [Test]
        public void StopWithANaNBlend_UsesTheCurrentClipsAuthoredBlendOut()
        {
            SeedActiveLayer(0, WalkClipIndex, WalkClipId, time: 0.5f, speed: 1f, loop: LoopMode.Loop);

            PlaybackTestActor.EnqueueCommand(testWorld, actor, PlaybackTestActor.StopCommand(0));
            RunCommandApply();

            Assert.AreEqual(
                0.5f,
                PlaybackTestActor.GetLayer(testWorld, actor, 0).blendDuration,
                1e-5f,
                "NaN must resolve to the outgoing clip's defaultBlendOut.");
        }

        /// <summary>
        /// Catches: deleting the queue clear in Stop. A stopped layer holding a queued clip is armed
        /// to restart on its own the next time anything finishes — an animation that comes back from
        /// the dead, with no call that asked for it.
        /// </summary>
        [Test]
        public void Stop_ClearsTheQueue()
        {
            SeedActiveLayer(0, AttackClipIndex, AttackClipId, time: 0f, speed: 1f, loop: LoopMode.Once);
            PlaybackTestActor.EnqueueCommand(testWorld, actor, PlaybackTestActor.QueueCommand(0, WalkClipId));
            RunCommandApply();
            Assert.IsTrue(
                (PlaybackTestActor.GetLayer(testWorld, actor, 0).flags & PlaybackFlags.HasQueued) != 0,
                "Guard: something must be queued for the clear to be observable.");

            PlaybackTestActor.EnqueueCommand(testWorld, actor, PlaybackTestActor.StopCommand(0, 0f));
            RunCommandApply();

            Assert.IsFalse(
                (PlaybackTestActor.GetLayer(testWorld, actor, 0).flags & PlaybackFlags.HasQueued) != 0,
                "A stopped layer must not stay armed to promote a queued clip.");
        }

        // -------------------------------------------------------------------------------------
        // SetSpeed / SetTime / drain / bounds / layer bounds-checking
        // -------------------------------------------------------------------------------------

        /// <summary>
        /// Catches: a SetSpeed that also resets time or clip — the difference between speeding a
        /// walk up and restarting it every time the game adjusts a movement speed.
        /// </summary>
        [Test]
        public void SetSpeed_ChangesSpeedAndNothingElse()
        {
            SeedActiveLayer(0, WalkClipIndex, WalkClipId, time: 0.75f, speed: 1f, loop: LoopMode.Loop);

            PlaybackTestActor.EnqueueCommand(testWorld, actor, new AnimationCommand
            {
                kind = CommandKind.SetSpeed,
                layerIndex = 0,
                speed = -2f,
                loop = LoopMode.UseClipDefault,
                blendDuration = float.NaN
            });
            RunCommandApply();

            PlaybackLayer layer = PlaybackTestActor.GetLayer(testWorld, actor, 0);
            Assert.AreEqual(-2f, layer.speed, 1e-5f);
            Assert.AreEqual(0.75f, layer.time, 1e-5f);
            Assert.AreEqual(WalkClipIndex, layer.clipIndex);
        }

        /// <summary>
        /// Catches: a SetTime that also resets speed. Scrubbing is used to sync an animation to
        /// external state every frame; resetting speed each time would freeze it.
        /// </summary>
        [Test]
        public void SetTime_ChangesTimeAndNothingElse()
        {
            SeedActiveLayer(0, WalkClipIndex, WalkClipId, time: 0.1f, speed: 1.5f, loop: LoopMode.Loop);

            PlaybackTestActor.EnqueueCommand(testWorld, actor, new AnimationCommand
            {
                kind = CommandKind.SetTime,
                layerIndex = 0,
                speed = 0f,
                loop = LoopMode.UseClipDefault,
                blendDuration = float.NaN,
                time = 1.25f
            });
            RunCommandApply();

            PlaybackLayer layer = PlaybackTestActor.GetLayer(testWorld, actor, 0);
            Assert.AreEqual(1.25f, layer.time, 1e-5f);
            Assert.AreEqual(1.5f, layer.speed, 1e-5f);
        }

        /// <summary>
        /// Catches: deleting <c>commands.Clear()</c> or the gate disable. Either one makes every
        /// command re-apply on every subsequent frame — a Play that restarts its own clip forever,
        /// which looks like an animation that is stuck on frame one.
        /// </summary>
        [Test]
        public void AppliedCommands_AreDrainedAndTheGateIsClosed()
        {
            PlaybackTestActor.EnqueueCommand(testWorld, actor, PlaybackTestActor.PlayCommand(0, WalkClipId));
            RunCommandApply();

            Assert.AreEqual(
                0,
                testWorld.EntityManager.GetBuffer<AnimationCommand>(actor).Length,
                "Applied commands must be drained.");
            Assert.IsFalse(
                testWorld.EntityManager.IsComponentEnabled<AnimationCommandPending>(actor),
                "The gate must close, or this system runs on this actor every frame forever.");
        }

        /// <summary>
        /// Catches: setting <c>BoundsDirty</c> unconditionally. The bounds pass is meant to run on
        /// clip changes only; dirtying on every applied command turns it into a per-frame cost, and
        /// the trap is that the output stays correct so nothing else fails.
        /// </summary>
        [Test]
        public void PlayingTheSameClipAgain_DoesNotDirtyTheBounds()
        {
            PlaybackTestActor.EnqueueCommand(testWorld, actor, PlaybackTestActor.PlayCommand(0, WalkClipId));
            RunCommandApply();
            Assert.IsTrue(
                testWorld.EntityManager.IsComponentEnabled<BoundsDirty>(actor),
                "Guard: the first Play changes the clip index and must dirty the bounds.");
            testWorld.EntityManager.SetComponentEnabled<BoundsDirty>(actor, false);

            PlaybackTestActor.EnqueueCommand(
                testWorld,
                actor,
                PlaybackTestActor.PlayCommand(0, WalkClipId, blendDuration: 0f));
            RunCommandApply();

            Assert.IsFalse(
                testWorld.EntityManager.IsComponentEnabled<BoundsDirty>(actor),
                "Replaying the same clip references the same bounds, so nothing is dirty.");
        }

        /// <summary>
        /// Catches: dropping the layer-index bounds check. A stale index from a rig with more layers
        /// would index past the buffer — an out-of-range write into another entity's chunk memory in
        /// a release build, which is worse than the command being ignored.
        /// </summary>
        [Test]
        public void ACommandForALayerThatDoesNotExist_IsDroppedWithoutTouchingAnyLayer()
        {
            PlaybackTestActor.EnqueueCommand(testWorld, actor, PlaybackTestActor.PlayCommand(7, WalkClipId));
            RunCommandApply();

            Assert.AreEqual(-1, PlaybackTestActor.GetLayer(testWorld, actor, 0).clipIndex);
            Assert.AreEqual(-1, PlaybackTestActor.GetLayer(testWorld, actor, 1).clipIndex);
        }

        /// <summary>
        /// Catches: deleting the stale-event clear (amendment A28). Without it every actor's event
        /// buffer grows without bound and consumers re-handle last frame's footstep on every frame
        /// that follows it.
        /// </summary>
        [Test]
        public void LastFramesEvents_AreClearedBeforeThisFramesCommandsApply()
        {
            EntityManager entityManager = testWorld.EntityManager;
            entityManager.GetBuffer<AnimEventOutput>(actor).Add(new AnimEventOutput
            {
                eventKey = (uint)ReservedEventKeys.ClipFinished,
                layerIndex = 0,
                clip = new ClipId(WalkClipId)
            });
            entityManager.SetComponentEnabled<AnimEventsPending>(actor, true);

            PlaybackTestActor.EnqueueCommand(testWorld, actor, PlaybackTestActor.PlayCommand(0, UnknownClipId));
            RunCommandApply();

            DynamicBuffer<AnimEventOutput> animEvents = entityManager.GetBuffer<AnimEventOutput>(actor);
            Assert.AreEqual(1, animEvents.Length, "Last frame's event must not still be in the buffer.");
            Assert.AreEqual(
                (uint)ReservedEventKeys.ClipResolveFailed,
                animEvents[0].eventKey,
                "The surviving event must be this frame's, not last frame's.");
        }

        /// <summary>
        /// Catches: leaving <c>AnimEventsPending</c> enabled after the clear. The flag is what tells
        /// consumers there is something to read; left latched on, every actor that ever emitted one
        /// event advertises events forever.
        /// </summary>
        [Test]
        public void AnActorWithNoNewEvents_HasItsPendingFlagCleared()
        {
            EntityManager entityManager = testWorld.EntityManager;
            entityManager.GetBuffer<AnimEventOutput>(actor).Add(new AnimEventOutput
            {
                eventKey = (uint)ReservedEventKeys.ClipFinished,
                layerIndex = 0,
                clip = new ClipId(WalkClipId)
            });
            entityManager.SetComponentEnabled<AnimEventsPending>(actor, true);

            RunCommandApply();

            Assert.IsFalse(entityManager.IsComponentEnabled<AnimEventsPending>(actor));
            Assert.AreEqual(0, entityManager.GetBuffer<AnimEventOutput>(actor).Length);
        }

        /// <summary>
        /// Seeds a layer as if it had been playing for a while, without routing through the system
        /// under test — so a fixture about the second Play cannot be made to pass by the first one.
        /// </summary>
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

        /// <summary>Runs the system once and completes its jobs, so assertions read settled data.</summary>
        private void RunCommandApply()
        {
            SystemHandle commandApplySystem = testWorld.GetOrCreateSystem<CommandApplySystem>();
            commandApplySystem.Update(testWorld.Unmanaged);
            testWorld.EntityManager.CompleteAllTrackedJobs();
        }
    }
}
