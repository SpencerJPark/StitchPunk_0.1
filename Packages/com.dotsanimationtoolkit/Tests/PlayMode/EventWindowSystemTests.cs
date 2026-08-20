// Copyright (c) 2026 Spencer Park. All rights reserved.

using NUnit.Framework;
using Unity.Entities;

namespace DotsAnimationToolkit.Tests.PlayMode
{
    /// <summary>
    /// Covers <c>EventWindowSystem</c> — architecture section 5.5 as amended by A45.
    /// </summary>
    /// <remarks>
    /// The containment math itself is <c>EventWindowMath</c>'s and is covered exhaustively by
    /// <c>EventWindowMathTests</c> in EditMode. These fixtures are about the parts only a running
    /// system has: which layers it folds together, that it rebuilds rather than accumulates, how it
    /// drives the enabled flag, and that a clip swap drops the previous clip's windows without
    /// anyone cancelling them.
    /// </remarks>
    public sealed class EventWindowSystemTests
    {
        private const ulong WalkClipId = 100;
        private const ulong AttackClipId = 200;

        private const int WalkClipIndex = 0;
        private const int AttackClipIndex = 1;

        private const float WalkDuration = 2f;
        private const float AttackDuration = 1f;

        // Maskable user keys (16–79), so both own a bit.
        private const uint FootstepKey = 20;
        private const uint HitFrameKey = 21;

        // Deliberately above the maskable range: pulse-only, so a window on it must stay inert.
        private const uint UnmaskableKey = AnimEventMaskKeys.LastMaskKey + 1u;

        private const float HalfSecondWindow = 0.5f;

        private World testWorld;
        private BlobAssetReference<ClipRegistryBlob> registry;
        private Entity actor;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("EventWindowSystemTests");
            registry = PlaybackTestActor.BuildRegistry(new[]
            {
                new PlaybackTestActor.ClipSpec
                {
                    clipId = WalkClipId,
                    duration = WalkDuration,
                    defaultLoop = LoopMode.Loop,
                    events = new[]
                    {
                        // At 1.0 s on a 2 s clip, open for half a second.
                        PlaybackTestActor.Marker(0.5f, FootstepKey, windowSeconds: HalfSecondWindow),

                        // A pulse-only marker on the same clip: no window at all.
                        PlaybackTestActor.Marker(0.1f, HitFrameKey)
                    }
                },
                new PlaybackTestActor.ClipSpec
                {
                    clipId = AttackClipId,
                    duration = AttackDuration,
                    defaultLoop = LoopMode.Once,
                    events = new[]
                    {
                        // At 0.5 s on a 1 s clip, open for half a second.
                        PlaybackTestActor.Marker(0.5f, HitFrameKey, windowSeconds: HalfSecondWindow),
                        PlaybackTestActor.Marker(0.5f, UnmaskableKey, windowSeconds: HalfSecondWindow)
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
        // Opening and closing
        // -------------------------------------------------------------------------------------

        /// <summary>
        /// Catches: never setting a bit at all. Without this the whole feature is inert.
        /// </summary>
        [Test]
        public void AMarkerWhoseWindowContainsTheLayerTime_OpensItsBit()
        {
            SeedActiveLayer(0, WalkClipIndex, WalkClipId, time: 1.2f, LoopMode.Loop);

            RunWindows();

            Assert.IsTrue(IsOpen(FootstepKey), "1.2 s is inside the 1.0–1.5 s window.");
            Assert.IsTrue(MaskEnabled(), "A held window must enable the component.");
        }

        /// <summary>
        /// Catches: an off-by-one that leaves the window open one frame past its end, which is how
        /// a hit lands on the recovery frames of an animation.
        /// </summary>
        [Test]
        public void AMarkerWhoseWindowHasElapsed_LeavesItsBitClosed()
        {
            SeedActiveLayer(0, WalkClipIndex, WalkClipId, time: 1.6f, LoopMode.Loop);

            RunWindows();

            Assert.IsFalse(IsOpen(FootstepKey), "1.6 s is past the 1.0–1.5 s window.");
            Assert.IsFalse(MaskEnabled(), "With nothing open the component must be disabled.");
        }

        /// <summary>
        /// Catches: treating every marker as though it held a window. A pulse-only marker with a
        /// zero window must never set a bit, or every sound cue becomes a sustained state.
        /// </summary>
        [Test]
        public void APulseOnlyMarker_NeverOpensABit()
        {
            SeedActiveLayer(0, WalkClipIndex, WalkClipId, time: 0.2f, LoopMode.Loop);

            RunWindows();

            Assert.IsFalse(IsOpen(HitFrameKey), "A zero-length window holds nothing open.");
        }

        /// <summary>
        /// Catches: mapping an out-of-range key onto a bit anyway — <c>1UL &lt;&lt; 64</c> is a
        /// shift overflow that in C# wraps to bit 0, so an unmaskable key would silently open
        /// whatever event owns key 16.
        /// </summary>
        [Test]
        public void AWindowOnAnUnmaskableKey_SetsNoBitAtAll()
        {
            SeedActiveLayer(0, AttackClipIndex, AttackClipId, time: 0.6f, LoopMode.Once);

            RunWindows();

            Assert.IsTrue(IsOpen(HitFrameKey), "The maskable marker on this clip is open.");
            Assert.IsFalse(
                IsOpen(AnimEventMaskKeys.FirstMaskKey),
                "An unmaskable key must not wrap around onto the first bit.");
        }

        // -------------------------------------------------------------------------------------
        // Rebuild semantics
        // -------------------------------------------------------------------------------------

        /// <summary>
        /// <strong>A45's interrupt story, end to end.</strong> Catches: accumulating bits instead of
        /// rebuilding. An interrupted attack whose damage window survives the interrupt is the exact
        /// defect the stateless design exists to prevent, and it is invisible until something
        /// interrupts mid-window.
        /// </summary>
        [Test]
        public void SwappingTheClipMidWindow_ClosesThePreviousClipsWindow()
        {
            SeedActiveLayer(0, AttackClipIndex, AttackClipId, time: 0.6f, LoopMode.Once);
            RunWindows();
            Assert.IsTrue(IsOpen(HitFrameKey), "Precondition: the attack's window is open.");

            // The interrupt: a different clip, at a time holding none of its own windows.
            SeedActiveLayer(0, WalkClipIndex, WalkClipId, time: 0.2f, LoopMode.Loop);
            RunWindows();

            Assert.IsFalse(IsOpen(HitFrameKey), "The interrupted clip's window must not survive.");
            Assert.IsFalse(MaskEnabled());
        }

        /// <summary>
        /// Catches: leaving stale bits set when a layer stops. A stopped layer keeps its clip and
        /// its last time, so a system that skipped the rebuild would hold its final window open
        /// forever.
        /// </summary>
        [Test]
        public void AStoppedLayer_HoldsNoWindow()
        {
            SeedActiveLayer(0, AttackClipIndex, AttackClipId, time: 0.6f, LoopMode.Once);
            RunWindows();
            Assert.IsTrue(IsOpen(HitFrameKey), "Precondition: the window is open while active.");

            PlaybackLayer stoppedLayer = PlaybackTestActor.GetLayer(testWorld, actor, 0);
            stoppedLayer.flags = PlaybackFlags.None;
            PlaybackTestActor.SetLayer(testWorld, actor, 0, stoppedLayer);
            RunWindows();

            Assert.IsFalse(IsOpen(HitFrameKey), "An inactive layer holds nothing open.");
        }

        /// <summary>
        /// Catches: rebuilding from the first layer only, or overwriting rather than OR-ing. A
        /// multi-layer rig is the point of the layer buffer, and an upper-body attack window has to
        /// coexist with a lower-body locomotion window.
        /// </summary>
        [Test]
        public void WindowsOpenOnSeveralLayers_AreFoldedTogether()
        {
            SeedActiveLayer(0, WalkClipIndex, WalkClipId, time: 1.2f, LoopMode.Loop);
            SeedActiveLayer(1, AttackClipIndex, AttackClipId, time: 0.6f, LoopMode.Once);

            RunWindows();

            Assert.IsTrue(IsOpen(FootstepKey), "Layer 0's window.");
            Assert.IsTrue(IsOpen(HitFrameKey), "Layer 1's window.");
        }

        /// <summary>
        /// Catches: an <c>EnabledRefRW</c> parameter enrolling the component as an enabled-only
        /// filter. Baked disabled, an actor's very first window would then never be able to open —
        /// the job would only run for actors that already had one.
        /// </summary>
        [Test]
        public void AnActorStartingDisabled_CanStillOpenItsFirstWindow()
        {
            Assert.IsFalse(MaskEnabled(), "Precondition: the fixture actor bakes disabled.");

            SeedActiveLayer(0, WalkClipIndex, WalkClipId, time: 1.2f, LoopMode.Loop);
            RunWindows();

            Assert.IsTrue(MaskEnabled(), "The first window must be able to enable the component.");
            Assert.IsTrue(IsOpen(FootstepKey));
        }

        // -------------------------------------------------------------------------------------
        // Helpers
        // -------------------------------------------------------------------------------------

        private void SeedActiveLayer(
            int layerIndex,
            int clipIndex,
            ulong clipId,
            float time,
            LoopMode loop)
        {
            PlaybackLayer layer = PlaybackTestActor.NewLayer();
            layer.clip = new ClipId(clipId);
            layer.clipIndex = clipIndex;
            layer.advanceStartTime = time;
            layer.time = time;
            layer.speed = 1f;
            layer.loop = loop;
            layer.flags = PlaybackFlags.Active;
            PlaybackTestActor.SetLayer(testWorld, actor, layerIndex, layer);
        }

        private void RunWindows()
        {
            SystemHandle windowSystem = testWorld.GetOrCreateSystem<EventWindowSystem>();
            windowSystem.Update(testWorld.Unmanaged);
            testWorld.EntityManager.CompleteAllTrackedJobs();
        }

        private bool IsOpen(uint eventKey)
        {
            AnimEventMask mask = testWorld.EntityManager.GetComponentData<AnimEventMask>(actor);
            return AnimEventMaskKeys.IsOpen(mask, eventKey);
        }

        private bool MaskEnabled()
        {
            return testWorld.EntityManager.IsComponentEnabled<AnimEventMask>(actor);
        }
    }
}
