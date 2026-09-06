// Copyright (c) 2026 Spencer Park. All rights reserved.

using NUnit.Framework;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// Covers <c>CutsceneBlockTiming</c> — the clip-block timing the Scene-view preview and the
    /// runtime player both read (amendment A58, decision A58-D1).
    /// </summary>
    /// <remarks>
    /// The parity this fixture actually protects is structural: there is one implementation, so
    /// the preview cannot disagree with playback. What is left to test is whether that one
    /// implementation follows the two rules the spec names — a looping block keeps cycling rather
    /// than stopping at its authored duration, and a hard cut is fully the incoming clip.
    /// </remarks>
    public sealed class CutsceneBlockTimingTests
    {
        /// <summary>
        /// Catches: treating a block's <c>duration</c> as the clip's end. Phase G's decision G-D8
        /// is explicit that a looping block must keep cycling — clamping here is the "pop back to
        /// frame 0" (or freeze on the last frame) the spec forbids, and it looks like a walk cycle
        /// stopping mid-stride with nothing in the data to explain it.
        /// </summary>
        [Test]
        public void LoopingBlock_PastItsOwnDuration_KeepsCyclingRatherThanHoldingTheLastFrame()
        {
            const float ClipDuration = 2f;

            // A 2s walk on a block that started 5s ago: two full laps plus a quarter.
            float phase = CutsceneBlockTiming.LoopPhaseNormalized(5f, ClipDuration, true);

            Assert.AreEqual(0.5f, phase, 1e-5f,
                "A looping block's phase must wrap through the clip, not stop at the block's duration.");
        }

        /// <summary>
        /// Catches: applying a block's speed to its start offset, or measuring the crossfade in
        /// clip seconds instead of timeline ones (amendment A65 §3.3). "Play the second half of the
        /// swing, slowed" means starting at the offset and then running slowly — scaling the offset
        /// too starts a half-speed block in the middle of the half it was told to play, and scaling
        /// the blend window makes a slowed block's crossfade outlast the overlap it was derived from.
        /// </summary>
        [Test]
        public void BlockSpeedAndOffset_StartAtTheOffset_AndScaleOnlyTheElapsedTime()
        {
            // A block starting at 1s, playing from 0.25s into its clip at half speed. Two seconds of
            // timeline later, the clip has advanced one second.
            Assert.AreEqual(0.25f, CutsceneBlockTiming.ClipTimeInBlock(1f, 1f, 0.5f, 0.25f), 1e-5f,
                "A block starts exactly at its authored offset, whatever its speed.");
            Assert.AreEqual(1.25f, CutsceneBlockTiming.ClipTimeInBlock(1f, 3f, 0.5f, 0.25f), 1e-5f,
                "Only the elapsed time runs at the block's speed.");

            Assert.AreEqual(0.5f, CutsceneBlockTiming.SeamBlendWeight(1f, 1f, 1.5f), 1e-5f,
                "A crossfade is timeline geometry and is not stretched by the block's speed.");

            Assert.AreEqual(1f, CutsceneBlockTiming.EffectiveBlockSpeed(0f), 1e-5f,
                "0 is an older bake with no speed field, never a request for a frozen clip.");
        }

        /// <summary>
        /// Catches: a zero-length blend window reading as weight 0. The weight lerps
        /// <em>from</em> the outgoing clip <em>to</em> the incoming one, so 0 on a hard cut leaves
        /// the previous clip on screen forever — every non-overlapping block after the first would
        /// silently never play.
        /// </summary>
        [Test]
        public void TouchingBlocks_HaveNoBlendWindow_AndReadAsFullyTheIncomingClip()
        {
            float blendDuration = CutsceneBlockTiming.SeamBlendDuration(0f, 2f, 2f);
            Assert.AreEqual(0f, blendDuration, 1e-5f, "Blocks that only touch must not blend.");

            Assert.AreEqual(1f, CutsceneBlockTiming.SeamBlendWeight(2f, blendDuration, 2f), 1e-5f,
                "A hard cut is fully the incoming clip from its first instant.");
        }

        /// <summary>
        /// Catches: measuring the crossfade from the wrong edge. The window opens at the incoming
        /// block's start, so overlapping blocks must reach weight 1 exactly at the outgoing block's
        /// end — measuring from the outgoing block's start instead finishes the fade early and the
        /// seam pops.
        /// </summary>
        [Test]
        public void OverlappingBlocks_ReachFullWeight_AtTheEndOfTheOverlap()
        {
            // Block A spans [0, 2); block B starts at 1.5, so they overlap for 0.5s.
            float blendDuration = CutsceneBlockTiming.SeamBlendDuration(0f, 2f, 1.5f);
            Assert.AreEqual(0.5f, blendDuration, 1e-5f);

            Assert.AreEqual(0f, CutsceneBlockTiming.SeamBlendWeight(1.5f, blendDuration, 1.5f), 1e-5f,
                "The crossfade starts at the incoming block's own start.");
            Assert.AreEqual(1f, CutsceneBlockTiming.SeamBlendWeight(1.5f, blendDuration, 2f), 1e-5f,
                "The crossfade completes where the outgoing block ends.");
        }
    }
}
