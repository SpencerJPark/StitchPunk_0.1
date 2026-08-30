// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Mathematics;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// The clip-block timing rules a cutscene's clip lane implies — seam blend duration, blend
    /// weight, and a block's local clip time and loop phase (amendment A58, decision A58-D1).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>One copy, called by both playback paths.</strong> The runtime player derives a Play
    /// command's <c>blendDuration</c> here and lets the existing playback machinery advance the
    /// phase; the editor preview has no playback machinery to lean on and derives the phase and the
    /// weight here too. Two implementations of "what does this block show at this instant" is
    /// exactly the editor/runtime divergence <c>TransformSampleSystem</c>'s remarks call the defect
    /// the single-sampler rule exists to prevent.
    /// </para>
    /// <para>
    /// <strong>A block's <c>duration</c> does not stop its clip.</strong> Playback starts at
    /// <c>start</c> and runs until the next block on the lane starts — a <c>Once</c> clip holds its
    /// final pose, a looping one keeps cycling. The duration's only job is deriving the overlap with
    /// the next block, which is the crossfade window (Phase G §2).
    /// </para>
    /// </remarks>
    public static class CutsceneBlockTiming
    {
        /// <summary>
        /// The crossfade window a block inherits from the block before it on the same lane: their
        /// overlap. Touching or gapped blocks give 0, which is a hard cut.
        /// </summary>
        public static float SeamBlendDuration(
            float previousBlockStart, float previousBlockDuration, float blockStart)
        {
            return math.max(0f, previousBlockStart + previousBlockDuration - blockStart);
        }

        /// <summary>Seconds a block's clip has been playing at <paramref name="timeSeconds"/>.</summary>
        public static float ClipTimeInBlock(float blockStart, float timeSeconds)
        {
            return math.max(0f, timeSeconds - blockStart);
        }

        /// <summary>
        /// How far a seam crossfade has progressed, 0 at the incoming block's start through 1 at the
        /// end of the overlap — <c>blendElapsed / blendDuration</c> saturated, matching
        /// <c>ClipSampler.CompositeLayers</c>. A zero window is already fully the incoming block.
        /// </summary>
        public static float SeamBlendWeight(float blockStart, float blendDuration, float timeSeconds)
        {
            if (blendDuration <= 0f)
            {
                return 1f;
            }
            return math.saturate(ClipTimeInBlock(blockStart, timeSeconds) / blendDuration);
        }

        /// <summary>
        /// A block's sampling phase in [0, 1], folded through the loop mode its <c>loop</c> flag
        /// asks for — the same <c>ClipSampler.MapTimeNormalized</c> the runtime samples through, so
        /// a looping walk cycle sits on the same foot in the preview as in play.
        /// </summary>
        public static float LoopPhaseNormalized(float clipTimeSeconds, float clipDuration, bool loop)
        {
            return ClipSampler.MapTimeNormalized(
                clipTimeSeconds, clipDuration, loop ? LoopMode.Loop : LoopMode.Once);
        }
    }
}
