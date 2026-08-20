// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Burst;
using Unity.Mathematics;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// Wrap-correct "is this marker's window open right now" math (architecture section 5.5,
    /// amendment A45) — the state counterpart to <see cref="EventWrapMath"/>'s crossing detection.
    /// Pure, allocation-free, Burst-compatible; shared by <c>EventWindowSystem</c>, tests, and the
    /// editor preview.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Position, not history.</strong> Every predicate here answers from the layer's current
    /// time alone, by asking how long ago the marker was last crossed. Nothing is remembered between
    /// frames, which is what lets an interrupted clip drop its windows without anyone cancelling
    /// them and lets a scrubbed or reversed timeline report exactly what its position implies.
    /// </para>
    /// <para>
    /// <strong>Convention, matched to <see cref="EventWrapMath"/>.</strong> A window is open over
    /// <c>[crossing, crossing + windowSeconds)</c> in the direction of travel — closed at the far
    /// edge, open at the instant of the crossing, so a window and the pulse that shares its marker
    /// agree about the frame the marker fires on. The one deliberate divergence is a marker at
    /// normalized time 0 on a looping clip: its pulse does not fire at play start (only on each
    /// wrap), but its window <em>is</em> open at play start, because "zero seconds past the marker"
    /// is inside the window by the half-open rule above. A window describes where the playhead is,
    /// and at play start the playhead is on the marker.
    /// </para>
    /// </remarks>
    [BurstCompile]
    public static class EventWindowMath
    {
        /// <summary>
        /// Whether a marker's window is open at <paramref name="currentTime"/>.
        /// </summary>
        /// <remarks>
        /// <see cref="LoopMode.UseClipDefault"/> must be resolved by the caller
        /// (<see cref="ClipSampler.ResolveLoopMode"/>); unresolved input answers false.
        /// </remarks>
        /// <param name="markerNormalizedTime">The marker's time as a fraction of the clip, in [0, 1].</param>
        /// <param name="windowSeconds">How long the window stays open; 0 or less is pulse-only.</param>
        /// <param name="currentTime">The layer's playback time in seconds, un-wrapped.</param>
        /// <param name="duration">Clip duration in seconds.</param>
        /// <param name="resolvedLoopMode">The layer's resolved loop mode.</param>
        /// <param name="isReverse">Whether the layer is playing backwards (negative speed).</param>
        /// <returns>True when the playhead is inside the marker's window.</returns>
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

            return elapsed >= 0f && elapsed < windowSeconds;
        }

        /// <summary>
        /// How long ago, in seconds of travel, the playhead last passed this marker — or −1 when it
        /// has not passed it at all (a <see cref="LoopMode.Once"/> clip that has not reached it yet).
        /// </summary>
        /// <remarks>
        /// Exposed because it is the whole of the window decision and is far easier to test directly
        /// than through a boolean: an off-by-a-wrap in the modular arithmetic shows up here as a
        /// number, and in <see cref="IsWindowOpen"/> only as a window that mysteriously never opens.
        /// </remarks>
        /// <param name="markerNormalizedTime">The marker's time as a fraction of the clip, in [0, 1].</param>
        /// <param name="currentTime">The layer's playback time in seconds, un-wrapped.</param>
        /// <param name="duration">Clip duration in seconds.</param>
        /// <param name="resolvedLoopMode">The layer's resolved loop mode.</param>
        /// <param name="isReverse">Whether the layer is playing backwards (negative speed).</param>
        /// <returns>Seconds of travel since the crossing, or −1 when the marker has not been crossed.</returns>
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

        /// <summary>
        /// A Once clip never wraps, so the marker is either behind the playhead or not yet reached.
        /// </summary>
        private static float ElapsedOnce(
            float markerTime,
            float currentTime,
            float duration,
            bool isReverse)
        {
            // Clamped for the same reason the crossing math clamps: a Once layer parks its time at
            // the end of the clip, and an un-clamped overshoot would keep growing the elapsed value
            // long after playback stopped moving — closing a window that should stay open on the
            // final frame, or reopening nothing at all.
            float clampedCurrent = math.clamp(currentTime, 0f, duration);

            return isReverse
                ? markerTime - clampedCurrent
                : clampedCurrent - markerTime;
        }

        /// <summary>
        /// A looping clip crosses the marker once per revolution, so elapsed is modular and a
        /// marker is always "behind" the playhead by some amount.
        /// </summary>
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

        /// <summary>
        /// PingPong reflects, so one marker has two crossing positions per period — its forward-leg
        /// position and its mirrored backward-leg position — and the nearer one behind the playhead
        /// is the one that opened the window.
        /// </summary>
        /// <remarks>
        /// The reasoning runs in wall-clock time rather than in phase. Phase reflects and so has no
        /// single "how long ago" answer, but the layer's un-wrapped time advances monotonically
        /// whatever the phase is doing, so reducing it modulo the full 2×duration period turns the
        /// reflection into two ordinary crossing positions and the rest is the looping case twice.
        /// A marker at exactly 0 or 1 maps both of its positions onto the same point, which is
        /// precisely the "endpoint markers fire once per reflection, never twice" rule
        /// <see cref="EventWrapMath"/> states — here it falls out of the arithmetic rather than
        /// needing a special case.
        /// </remarks>
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

        /// <summary>
        /// <c>math.fmod</c> keeps the sign of the dividend, which is the wrong half of the answer
        /// for every negative difference this file produces.
        /// </summary>
        private static float NonNegativeMod(float value, float modulus)
        {
            float remainder = math.fmod(value, modulus);
            return remainder < 0f ? remainder + modulus : remainder;
        }
    }
}
