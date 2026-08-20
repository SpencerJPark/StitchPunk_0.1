// Copyright (c) 2026 Stitch Punk. All rights reserved.

using Unity.Burst;
using Unity.Mathematics;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// The whole of architecture section 5.10's LOD table as pure functions: what a level does, and
    /// which level a squared camera distance earns.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Policy lives here rather than inside the systems that obey it</strong>, for the same
    /// reason §5.11 puts sampling in one place. Three systems consume LOD —
    /// <c>TransformSampleSystem</c>, <c>VatMaterialSystem</c>, and whatever a host writes itself —
    /// and a level that means "quarter rate" in one and "quarter rate, or half if the actor
    /// specified a rate" in another is a divergence nobody would ever see, because both look
    /// plausible in motion. As pure functions the table is also EditMode-testable without a World,
    /// which is where its arithmetic belongs.
    /// </para>
    /// <para>
    /// <strong>Levels affect CPU presentation only</strong> — never playback timers, never events.
    /// Nothing in this file is reachable from the logic group, and that is deliberate: gameplay
    /// correctness is LOD-independent (§5.10), so a far-away actor fires its footstep events on
    /// exactly the frames a near one would.
    /// </para>
    /// </remarks>
    [BurstCompile]
    public static class AnimationLodPolicy
    {
        /// <summary>Sample rate LOD 1 imposes on an actor that asked for no cap (rate 0 = every frame).</summary>
        public const float UncappedLevel1RateHz = 30f;

        /// <summary>Sample rate LOD 2 and 3 impose on an actor that asked for no cap.</summary>
        public const float UncappedLevel2RateHz = 15f;

        /// <summary>
        /// The rate an actor actually samples at once its LOD level is applied
        /// (architecture section 5.10).
        /// </summary>
        /// <remarks>
        /// <para>
        /// An actor that asked for no cap has no rate to halve, so the level supplies one outright —
        /// 30 Hz at level 1, 15 Hz at 2 and 3. Without that, LOD would do nothing at all for the
        /// default actor, which is every actor until a host tunes one.
        /// </para>
        /// <para>
        /// Level 3 returns the level-2 rate rather than 0. Freezing is not expressed as a rate: the
        /// transform path stops on <see cref="FreezesPose"/> instead, and VAT keeps publishing at
        /// quarter rate because GPU cost is unaffected by CPU LOD (§5.10). A 0 here would read as
        /// "sample every frame" to <c>ClipSampler.ShouldSample</c> — the exact opposite of the
        /// intent, and silently.
        /// </para>
        /// </remarks>
        /// <param name="lodLevel">The actor's LOD level, 0–3; anything above 3 is treated as 3.</param>
        /// <param name="requestedRateHz">The actor's own rate in Hz, already resolved against the world default; 0 = every frame.</param>
        /// <returns>The effective rate in Hz; 0 only when the level is 0 and the actor asked for no cap.</returns>
        [BurstCompile]
        public static float EffectiveSampleRateHz(byte lodLevel, float requestedRateHz)
        {
            if (lodLevel == 0)
            {
                return requestedRateHz;
            }

            bool isUncapped = requestedRateHz <= 0f;
            if (lodLevel == 1)
            {
                return isUncapped ? UncappedLevel1RateHz : requestedRateHz * 0.5f;
            }
            return isUncapped ? UncappedLevel2RateHz : requestedRateHz * 0.25f;
        }

        /// <summary>
        /// Whether this level renders crossfades as a hard cut (architecture section 5.10, level 2).
        /// </summary>
        /// <remarks>
        /// Only the <em>weight</em> snaps. Blend timers keep advancing at every level, so an actor
        /// that changes LOD mid-blend rejoins the correct weight rather than restarting or jumping —
        /// the property §11.2 tests explicitly.
        /// </remarks>
        [BurstCompile]
        public static bool SnapsBlendWeights(byte lodLevel)
        {
            return lodLevel >= 2;
        }

        /// <summary>
        /// Whether this level holds the last sampled pose until the actor's clips change
        /// (architecture section 5.10, level 3).
        /// </summary>
        [BurstCompile]
        public static bool FreezesPose(byte lodLevel)
        {
            return lodLevel >= 3;
        }

        /// <summary>
        /// Snaps a crossfade weight to the nearer end, which is what level 2 shows instead of a lerp.
        /// </summary>
        /// <param name="blendWeight">The true weight in [0, 1].</param>
        /// <returns>0 below the midpoint, 1 at or above it.</returns>
        [BurstCompile]
        public static float SnapBlendWeight(float blendWeight)
        {
            return blendWeight < 0.5f ? 0f : 1f;
        }

        /// <summary>
        /// The LOD level a squared camera distance earns (architecture section 5.10).
        /// </summary>
        /// <remarks>
        /// <para>
        /// Tested from the furthest threshold inward, so a threshold set that is not ascending
        /// degrades to "the furthest one that matches" instead of producing a level no distance can
        /// reach. <c>ConfigBootstrapSystem</c>'s defaults are ascending and non-zero precisely so a
        /// host that flips <c>distanceLodEnabled</c> without setting distances gets sane behaviour.
        /// </para>
        /// <para>
        /// Squared throughout: the caller compares <c>lengthsq</c> against these, so no square root
        /// runs per actor per frame.
        /// </para>
        /// </remarks>
        /// <param name="distanceSq">Squared distance from the camera to the actor.</param>
        /// <param name="lodDistancesSq">
        /// Ascending squared thresholds; x→1, y→2, z→3. w is reserved. Passed by <c>in</c> because
        /// <c>[BurstCompile]</c> on a static method makes it a direct-call entry point, and Burst
        /// rejects a vector passed by value across that boundary (BC1064/BC1067).
        /// </param>
        /// <returns>The LOD level, 0–3.</returns>
        [BurstCompile]
        public static byte LevelForDistanceSq(float distanceSq, in float4 lodDistancesSq)
        {
            if (distanceSq >= lodDistancesSq.z)
            {
                return 3;
            }
            if (distanceSq >= lodDistancesSq.y)
            {
                return 2;
            }
            if (distanceSq >= lodDistancesSq.x)
            {
                return 1;
            }
            return 0;
        }
    }
}
