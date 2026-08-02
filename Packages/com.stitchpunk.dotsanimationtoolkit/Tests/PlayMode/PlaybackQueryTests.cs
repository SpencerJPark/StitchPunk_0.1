// Copyright (c) 2026 Stitch Punk. All rights reserved.

using NUnit.Framework;
using Unity.Entities;

namespace StitchPunk.AnimationToolkit.Tests.PlayMode
{
    /// <summary>
    /// Covers <c>PlaybackQuery</c> — the read side of the section 5.4 API, including amendment
    /// A26's pinned behaviour (build step C4.3).
    /// </summary>
    /// <remarks>
    /// These are pure functions, but their only input type is a <see cref="DynamicBuffer{T}"/>,
    /// which cannot be constructed outside a World. That is why the fixture lives in PlayMode next
    /// to the systems rather than with the other pure-function suites in EditMode.
    /// </remarks>
    public sealed class PlaybackQueryTests
    {
        private const ulong WalkClipId = 100;
        private const ulong AttackClipId = 200;

        private const int WalkClipIndex = 0;
        private const int AttackClipIndex = 1;

        private const float WalkDuration = 2f;

        private World testWorld;
        private BlobAssetReference<ClipRegistryBlob> registry;
        private Entity actor;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("PlaybackQueryTests");
            registry = PlaybackTestActor.BuildRegistry(new[]
            {
                new PlaybackTestActor.ClipSpec
                {
                    clipId = WalkClipId,
                    duration = WalkDuration,
                    defaultLoop = LoopMode.Loop
                },
                new PlaybackTestActor.ClipSpec
                {
                    clipId = AttackClipId,
                    duration = 1f,
                    defaultLoop = LoopMode.Once
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
        // IsPlaying
        // -------------------------------------------------------------------------------------

        /// <summary>Catches: an IsPlaying that never returns true.</summary>
        [Test]
        public void IsPlaying_IsTrueForTheClipTheLayerIsRunning()
        {
            SeedLayer(WalkClipIndex, WalkClipId, time: 0.5f, loop: LoopMode.Loop, flags: PlaybackFlags.Active);

            Assert.IsTrue(PlaybackQuery.IsPlaying(Layers(), 0, new ClipId(WalkClipId)));
            Assert.IsFalse(PlaybackQuery.IsPlaying(Layers(), 0, new ClipId(AttackClipId)));
        }

        /// <summary>
        /// Catches: dropping the Active check. A layer keeps its clip id after it stops, so an
        /// IsPlaying that only compares ids answers "yes, still attacking" forever — and a game
        /// polling it to decide when a swing is over would never move on.
        /// </summary>
        [Test]
        public void IsPlaying_IsFalseForALayerThatHasStopped()
        {
            SeedLayer(WalkClipIndex, WalkClipId, time: 0.5f, loop: LoopMode.Loop, flags: PlaybackFlags.None);

            Assert.IsFalse(PlaybackQuery.IsPlaying(Layers(), 0, new ClipId(WalkClipId)));
        }

        /// <summary>
        /// Catches: answering from the <c>previous*</c> slot as well. A clip fading out of a
        /// crossfade has already been replaced; reporting it as playing makes every transition look
        /// like two clips are playing at once.
        /// </summary>
        [Test]
        public void IsPlaying_IsFalseForTheClipFadingOutOfACrossfade()
        {
            PlaybackLayer layer = PlaybackTestActor.NewLayer();
            layer.clip = new ClipId(AttackClipId);
            layer.clipIndex = AttackClipIndex;
            layer.previousClip = new ClipId(WalkClipId);
            layer.previousClipIndex = WalkClipIndex;
            layer.blendDuration = 0.5f;
            layer.flags = PlaybackFlags.Active | PlaybackFlags.Blending;
            PlaybackTestActor.SetLayer(testWorld, actor, 0, layer);

            Assert.IsFalse(PlaybackQuery.IsPlaying(Layers(), 0, new ClipId(WalkClipId)));
            Assert.IsTrue(PlaybackQuery.IsPlaying(Layers(), 0, new ClipId(AttackClipId)));
        }

        /// <summary>
        /// Catches: dropping the layer-index bounds check. A stale index left over from a rig with
        /// more layers would read past the buffer instead of answering "no".
        /// </summary>
        [Test]
        public void IsPlaying_IsFalseForALayerTheRigDoesNotHave()
        {
            Assert.IsFalse(PlaybackQuery.IsPlaying(Layers(), 9, new ClipId(WalkClipId)));
        }

        // -------------------------------------------------------------------------------------
        // NormalizedTime (amendment A26)
        // -------------------------------------------------------------------------------------

        /// <summary>Catches: a NormalizedTime that does not divide by duration.</summary>
        [Test]
        public void NormalizedTime_ReportsProgressThroughTheClip()
        {
            SeedLayer(WalkClipIndex, WalkClipId, time: 0.5f, loop: LoopMode.Loop, flags: PlaybackFlags.Active);

            Assert.AreEqual(0.25f, PlaybackQuery.NormalizedTime(Layers(), ref registry.Value, 0), 1e-5f);
        }

        /// <summary>
        /// Catches: dividing the raw un-wrapped time by duration. <see cref="PlaybackLayer.time"/>
        /// climbs past the clip length forever on a loop, so the naive division returns 1.2, 2.4,
        /// 3.7 — numbers outside [0, 1] that a caller driving a progress bar or a blend weight will
        /// happily use.
        /// </summary>
        [Test]
        public void NormalizedTime_FoldsALoopingClipIntoItsCurrentLap()
        {
            SeedLayer(WalkClipIndex, WalkClipId, time: 2.4f, loop: LoopMode.Loop, flags: PlaybackFlags.Active);

            Assert.AreEqual(0.2f, PlaybackQuery.NormalizedTime(Layers(), ref registry.Value, 0), 1e-5f);
        }

        /// <summary>
        /// Catches: reading the clip's authored default instead of resolving the mode the layer is
        /// actually playing under. Walk's default is Loop; played Once past its end it must report 1
        /// (held at the end), not 0.5 (wrapped into a second lap).
        /// </summary>
        [Test]
        public void NormalizedTime_UsesTheModeTheLayerIsPlayingUnder()
        {
            SeedLayer(WalkClipIndex, WalkClipId, time: 3f, loop: LoopMode.Once, flags: PlaybackFlags.Active);

            Assert.AreEqual(
                1f,
                PlaybackQuery.NormalizedTime(Layers(), ref registry.Value, 0),
                1e-5f,
                "A Once-played clip holds at its end; resolving against the clip default would wrap it.");
        }

        /// <summary>
        /// Catches: dropping A26's inactive guard. A stopped layer keeps its last time, so without
        /// the guard the answer is a stale progress value that looks entirely plausible.
        /// </summary>
        [Test]
        public void NormalizedTime_IsZeroForAnInactiveLayer()
        {
            SeedLayer(WalkClipIndex, WalkClipId, time: 1f, loop: LoopMode.Loop, flags: PlaybackFlags.None);

            Assert.AreEqual(0f, PlaybackQuery.NormalizedTime(Layers(), ref registry.Value, 0), 1e-5f);
        }

        /// <summary>
        /// Catches: dropping A26's unresolved-index guard. <c>clipIndex = -1</c> is the normal
        /// resting state of an unplayed layer, and indexing the blob with it reads out of bounds.
        /// </summary>
        [Test]
        public void NormalizedTime_IsZeroForAnUnresolvedClipIndex()
        {
            PlaybackLayer layer = PlaybackTestActor.NewLayer();
            layer.flags = PlaybackFlags.Active;
            layer.time = 1f;
            PlaybackTestActor.SetLayer(testWorld, actor, 0, layer);

            Assert.AreEqual(0f, PlaybackQuery.NormalizedTime(Layers(), ref registry.Value, 0), 1e-5f);
        }

        /// <summary>Catches: dropping the layer-index bounds check.</summary>
        [Test]
        public void NormalizedTime_IsZeroForALayerTheRigDoesNotHave()
        {
            Assert.AreEqual(0f, PlaybackQuery.NormalizedTime(Layers(), ref registry.Value, 9), 1e-5f);
        }

        // -------------------------------------------------------------------------------------
        // FinishedThisFrame
        // -------------------------------------------------------------------------------------

        /// <summary>Catches: reading the sticky Finished flag instead of the one-frame pulse.</summary>
        [Test]
        public void FinishedThisFrame_ReadsThePulseAndNotTheStickyFlag()
        {
            SeedLayer(
                AttackClipIndex,
                AttackClipId,
                time: 1f,
                loop: LoopMode.Once,
                flags: PlaybackFlags.Finished);

            Assert.IsFalse(
                PlaybackQuery.FinishedThisFrame(Layers(), 0),
                "Finished is sticky; only FinishedThisFrame means 'it just happened'.");

            SeedLayer(
                AttackClipIndex,
                AttackClipId,
                time: 1f,
                loop: LoopMode.Once,
                flags: PlaybackFlags.Finished | PlaybackFlags.FinishedThisFrame);

            Assert.IsTrue(PlaybackQuery.FinishedThisFrame(Layers(), 0));
        }

        /// <summary>Catches: dropping the layer-index bounds check.</summary>
        [Test]
        public void FinishedThisFrame_IsFalseForALayerTheRigDoesNotHave()
        {
            Assert.IsFalse(PlaybackQuery.FinishedThisFrame(Layers(), 9));
        }

        private DynamicBuffer<PlaybackLayer> Layers()
        {
            return testWorld.EntityManager.GetBuffer<PlaybackLayer>(actor);
        }

        private void SeedLayer(int clipIndex, ulong clipId, float time, LoopMode loop, PlaybackFlags flags)
        {
            PlaybackLayer layer = PlaybackTestActor.NewLayer();
            layer.clip = new ClipId(clipId);
            layer.clipIndex = clipIndex;
            layer.time = time;
            layer.advanceStartTime = time;
            layer.loop = loop;
            layer.flags = flags;
            PlaybackTestActor.SetLayer(testWorld, actor, 0, layer);
        }
    }
}
