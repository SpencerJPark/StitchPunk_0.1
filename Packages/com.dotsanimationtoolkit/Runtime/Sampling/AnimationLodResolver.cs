// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Burst;
using Unity.Mathematics;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// LOD table as pure functions: what a level does, and which level a squared camera distance
    /// earns. Affects CPU presentation only — never playback timers or events.
    /// </summary>
    [BurstCompile]
    public static class AnimationLodResolver
    {
        public const float UncappedLevel1RateHz = 30f; // rate LOD 1 imposes on an actor that asked for no cap

        public const float UncappedLevel2RateHz = 15f; // rate LOD 2 and 3 impose on an actor that asked for no cap

        /// <param name="lodLevel">0-3; anything above 3 is treated as 3.</param>
        /// <param name="requestedRateHz">The actor's own rate, already resolved against the world default; 0 = every frame.</param>
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
            // Returns the level-2 rate, not 0: freezing is expressed via FreezesPose, not a rate,
            // and 0 here would read as "sample every frame" to ClipSampler.ShouldSample.
            return isUncapped ? UncappedLevel2RateHz : requestedRateHz * 0.25f;
        }

        // Only the blend weight snaps; timers keep advancing at every level, so an actor that
        // changes LOD mid-blend rejoins the correct weight instead of restarting or jumping.
        [BurstCompile]
        public static bool SnapsBlendWeights(byte lodLevel)
        {
            return lodLevel >= 2;
        }

        /// <summary>Whether this level holds the last sampled pose until the actor's clips change.</summary>
        [BurstCompile]
        public static bool FreezesPose(byte lodLevel)
        {
            return lodLevel >= 3;
        }

        /// <returns>0 below the midpoint, 1 at or above it.</returns>
        [BurstCompile]
        public static float SnapBlendWeight(float blendWeight)
        {
            return blendWeight < 0.5f ? 0f : 1f;
        }

        // Tested from the furthest threshold inward, so a non-ascending threshold set degrades to
        // "the furthest one that matches" rather than a level no distance can reach.
        /// <param name="lodDistancesSq">
        /// Ascending squared thresholds; x/y/z promote to level 1/2/3, w reserved. Passed by
        /// <c>in</c> because a <c>[BurstCompile]</c> static is a direct-call entry point and Burst
        /// rejects a vector by value across it (BC1064/BC1067).
        /// </param>
        [BurstCompile]
        public static byte ResolveLevelForDistanceSq(float distanceSq, in float4 lodDistancesSq)
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
