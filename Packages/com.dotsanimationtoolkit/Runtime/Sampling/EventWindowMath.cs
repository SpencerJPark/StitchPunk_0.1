// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Burst;
using Unity.Mathematics;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// Wrap-correct "is this marker's window open now" math — the state counterpart to
    /// <see cref="EventWrapMath"/>'s crossing detection. Every predicate answers from the current
    /// time alone; nothing is remembered between frames, so an interrupted or scrubbed clip needs no cancellation.
    /// </summary>
    [BurstCompile]
    public static class EventWindowMath
    {
        /// <param name="markerNormalizedTime">The marker's time as a fraction of the clip, in [0, 1].</param>
        /// <param name="windowSeconds">How long the window stays open; 0 or less is pulse-only.</param>
        /// <param name="currentTime">The layer's playback time in seconds, un-wrapped.</param>
        /// <param name="isReverse">Whether the layer is playing backwards (negative speed).</param>
        /// <returns>True when the playhead is inside the marker's window. Unresolved <see cref="LoopMode.UseClipDefault"/> answers false — resolve it first via <see cref="ClipSampler.ResolveLoopMode"/>.</returns>
        [BurstCompile]
        public static bool IsWindowOpen(
            float markerNormalizedTime,
            float windowSeconds,
            float currentTime,
            float duration,
            LoopMode resolvedLoopMode,
            bool isReverse)
        {
            if (windowSeconds <= 0f || duration <= 0f)
            {
                return false;
            }

            float elapsed = ElapsedSinceCrossing(
                markerNormalizedTime, currentTime, duration, resolvedLoopMode, isReverse);

            // Half-open [crossing, crossing + windowSeconds): closed at the far edge, open at the
            // crossing itself, so the window agrees with the pulse that shares its marker about
            // which frame it fires on.
            return elapsed >= 0f && elapsed < windowSeconds;
        }

        /// <summary>
        /// How long ago, in seconds of travel, the playhead last passed this marker — exposed
        /// separately because it is far easier to test as a number than as a boolean.
        /// </summary>
        /// <returns>Seconds of travel since the crossing, or -1 when the marker has not been crossed (a <see cref="LoopMode.Once"/> clip that hasn't reached it yet).</returns>
        [BurstCompile]
        public static float ElapsedSinceCrossing(
            float markerNormalizedTime,
            float currentTime,
            float duration,
            LoopMode resolvedLoopMode,
            bool isReverse)
        {
            if (duration <= 0f)
            {
                return -1f;
            }

            float markerTime = markerNormalizedTime * duration;

            switch (resolvedLoopMode)
            {
                case LoopMode.Once:
                    return ElapsedOnce(markerTime, currentTime, duration, isReverse);
                case LoopMode.Loop:
                    return ElapsedLoop(markerTime, currentTime, duration, isReverse);
                case LoopMode.PingPong:
                    return ElapsedPingPong(markerTime, currentTime, duration, isReverse);
                default:
                    return -1f;
            }
        }

        /// <summary>A Once clip never wraps, so the marker is either behind the playhead or not yet reached.</summary>
        private static float ElapsedOnce(
            float markerTime,
            float currentTime,
            float duration,
            bool isReverse)
        {
            // Clamped because a Once layer parks its time at the clip's end; an un-clamped overshoot
            // would keep growing the elapsed value after playback stopped moving, closing a window
            // that should stay open on the final frame.
            float clampedCurrent = math.clamp(currentTime, 0f, duration);

            return isReverse
                ? markerTime - clampedCurrent
                : clampedCurrent - markerTime;
        }

        /// <summary>A looping clip crosses the marker once per revolution, so elapsed is modular and always "behind" the playhead by some amount.</summary>
        private static float ElapsedLoop(
            float markerTime,
            float currentTime,
            float duration,
            bool isReverse)
        {
            return isReverse
                ? NonNegativeMod(markerTime - currentTime, duration)
                : NonNegativeMod(currentTime - markerTime, duration);
        }

        /// <summary>PingPong reflects, so one marker has two crossing positions per period; the nearer one behind the playhead is the one that opened the window.</summary>
        // Runs in wall-clock time, not phase: phase reflects and has no single "how long ago"
        // answer, but un-wrapped time advances monotonically, so reducing modulo the full 2x
        // duration period turns the reflection into two ordinary crossings and this is the looping
        // case twice. A marker at exactly 0 or 1 maps both positions onto the same point, which is
        // how "endpoint markers fire once per reflection" falls out without a special case.
        private static float ElapsedPingPong(
            float markerTime,
            float currentTime,
            float duration,
            bool isReverse)
        {
            float period = 2f * duration;
            float forwardLegCrossing = markerTime;
            float backwardLegCrossing = period - markerTime;

            float elapsedFromForwardLeg = isReverse
                ? NonNegativeMod(forwardLegCrossing - currentTime, period)
                : NonNegativeMod(currentTime - forwardLegCrossing, period);

            float elapsedFromBackwardLeg = isReverse
                ? NonNegativeMod(backwardLegCrossing - currentTime, period)
                : NonNegativeMod(currentTime - backwardLegCrossing, period);

            return math.min(elapsedFromForwardLeg, elapsedFromBackwardLeg);
        }

        // math.fmod keeps the sign of the dividend, the wrong half of the answer for every negative
        // difference this file produces.
        private static float NonNegativeMod(float value, float modulus)
        {
            float remainder = math.fmod(value, modulus);
            return remainder < 0f ? remainder + modulus : remainder;
        }
    }
}
