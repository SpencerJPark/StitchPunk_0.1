// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Mathematics;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// The clip-block timing rules a cutscene's clip lane implies. A block's <c>duration</c> does
    /// not stop its clip: playback starts at <c>start</c> and runs until the next block on the lane
    /// starts (a <c>Once</c> clip holds its final pose); duration only derives the crossfade overlap.
    /// </summary>
    public static class CutsceneBlockTiming
    {
        /// <summary>The crossfade window a block inherits from the block before it on the same lane: their overlap. Touching or gapped blocks give 0, a hard cut.</summary>
        public static float SeamBlendDuration(
            float previousBlockStart, float previousBlockDuration, float blockStart)
        {
            return math.max(0f, previousBlockStart + previousBlockDuration - blockStart);
        }

        public static float ElapsedInBlock(float blockStart, float timeSeconds)
        {
            return math.max(0f, timeSeconds - blockStart);
        }

        /// <summary>
        /// Where in its clip a block is at <paramref name="timeSeconds"/>: start offset plus the
        /// elapsed timeline seconds run at the block's own speed. <see cref="ElapsedInBlock"/> is
        /// timeline geometry and is not itself scaled by speed.
        /// </summary>
        public static float ClipTimeInBlock(
            float blockStart, float timeSeconds, float speed, float clipStartOffset)
        {
            return clipStartOffset + ElapsedInBlock(blockStart, timeSeconds) * EffectiveBlockSpeed(speed);
        }

        // 0 means "unset" (a block baked before speed existed), not "frozen" — the authored value
        // cannot go below 0.01, so 0 is never an author asking for a stopped clip; that's
        // CutsceneControl.paused's job.
        public static float EffectiveBlockSpeed(float speed)
        {
            return speed > 0f ? speed : 1f;
        }

        /// <summary>How far a seam crossfade has progressed, 0 at the incoming block's start through 1 at the end of the overlap. A zero window is already fully the incoming block.</summary>
        public static float SeamBlendWeight(float blockStart, float blendDuration, float timeSeconds)
        {
            if (blendDuration <= 0f)
            {
                return 1f;
            }
            return math.saturate(ElapsedInBlock(blockStart, timeSeconds) / blendDuration);
        }

        public static float LoopPhaseNormalized(float clipTimeSeconds, float clipDuration, bool loop)
        {
            return ClipSampler.MapTimeNormalized(
                clipTimeSeconds, clipDuration, loop ? LoopMode.Loop : LoopMode.Once);
        }
    }
}
